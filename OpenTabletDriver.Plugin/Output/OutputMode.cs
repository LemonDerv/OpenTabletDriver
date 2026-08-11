using System;
using System.Collections.Generic;
using System.Numerics;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Plugin.Output
{
    public abstract class OutputMode : PipelineManager<IDeviceReport>, IOutputMode
    {
        public OutputMode()
        {
            Passthrough = true;
        }

        private bool passthrough;
        private TabletReference tablet;
        private IList<IPositionedPipelineElement<IDeviceReport>> elements;
        private IPipelineElement<IDeviceReport> entryElement;
        private List<IPipelineElement<IDeviceReport>> cachedPipeline;

        public event Action<IDeviceReport>? Emit;

        protected bool Passthrough
        {
            private set
            {
                Action<IDeviceReport> output = this.OnOutput;
                if (value && !passthrough)
                {
                    this.entryElement = this;
                    Link(this, output);
                    this.passthrough = true;
                }
                else if (!value && passthrough)
                {
                    this.entryElement = null;
                    Unlink(this, output);
                    this.passthrough = false;
                }
            }
            get => this.passthrough;
        }

        protected IList<IPositionedPipelineElement<IDeviceReport>> PreTransformElements { private set; get; } =
            Array.Empty<IPositionedPipelineElement<IDeviceReport>>();

        protected IList<IPositionedPipelineElement<IDeviceReport>> PostTransformElements { private set; get; } =
            Array.Empty<IPositionedPipelineElement<IDeviceReport>>();

        public bool DisablePressure { set; get; }

        public bool DisableTilt { set; get; }

        public Matrix3x2 TransformationMatrix { protected set; get; }

        public IList<IPositionedPipelineElement<IDeviceReport>> Elements
        {
            set
            {
                this.elements = value;

                Passthrough = false;
                DestroyInternalLinks();

                if (Elements != null && Elements.Count > 0)
                {
                    PreTransformElements = GroupElements(Elements, PipelinePosition.PreTransform);
                    PostTransformElements = GroupElements(Elements, PipelinePosition.PostTransform);

                    Action<IDeviceReport> output = this.OnOutput;

                    var links = BuildPipeline();
                    cachedPipeline = links;

                    entryElement = links[0];

                    LinkAll(links, output);
                }
                else
                {
                    Passthrough = true;
                    PreTransformElements = Array.Empty<IPositionedPipelineElement<IDeviceReport>>();
                    PostTransformElements = Array.Empty<IPositionedPipelineElement<IDeviceReport>>();
                }
            }
            get => this.elements;
        }

        private List<IPipelineElement<IDeviceReport>> BuildPipeline()
        {
            var links = new List<IPipelineElement<IDeviceReport>>(
                PreTransformElements.Count + 1 + PostTransformElements.Count);

            links.AddRange(PreTransformElements);
            links.Add(this);
            links.AddRange(PostTransformElements);

            return links;
        }

        public virtual TabletReference Tablet
        {
            set
            {
                this.tablet = value;
                this.TransformationMatrix = CreateTransformationMatrix();
            }
            get => this.tablet;
        }

        public virtual void Consume(IDeviceReport report)
        {
            if (report is IAbsolutePositionReport tabletReport)
                report = Transform(tabletReport);
            if (report != null)
                Emit?.Invoke(report);
        }

        public virtual void Read(IDeviceReport deviceReport) => entryElement?.Consume(deviceReport);

        protected abstract Matrix3x2 CreateTransformationMatrix();
        protected abstract IAbsolutePositionReport Transform(IAbsolutePositionReport tabletReport);
        protected abstract void OnOutput(IDeviceReport report);

        private void DestroyInternalLinks()
        {
            if (cachedPipeline == null)
                return;

            Action<IDeviceReport> output = this.OnOutput;

            UnlinkAll(cachedPipeline, output);
            cachedPipeline = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Cached report capability resolution — eliminates per-report interface 'is' checks
        private Type? _lastReportType;
        private ReportCapabilities _cachedReportCaps;

        /// <summary>
        /// Resolves report capabilities using cached type lookup.
        /// Since the same report type flows through the pipeline for the lifetime of a tablet connection,
        /// the interface 'is' checks are performed only once per type change.
        /// </summary>
        protected ReportCapabilities ResolveCapabilities(IDeviceReport report)
        {
            var type = report.GetType();
            if (type == _lastReportType) return _cachedReportCaps;

            var caps = ReportCapabilities.None;
            if (report is IAbsolutePositionReport) caps |= ReportCapabilities.AbsolutePosition;
            if (report is ITabletReport) caps |= ReportCapabilities.Tablet;
            if (report is IProximityReport) caps |= ReportCapabilities.Proximity;
            if (report is IEraserReport) caps |= ReportCapabilities.Eraser;
            if (report is ITiltReport) caps |= ReportCapabilities.Tilt;
            if (report is OutOfRangeReport) caps |= ReportCapabilities.OutOfRange;

            _lastReportType = type;
            _cachedReportCaps = caps;
            return caps;
        }

        private bool _isDisposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                entryElement = null;

                foreach (var obj in Elements ?? [])
                    if (obj is IDisposable disposable)
                        disposable.Dispose();
            }

            _isDisposed = true;
        }
    }
}
