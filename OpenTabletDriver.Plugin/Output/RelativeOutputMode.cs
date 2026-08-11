using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;

#nullable enable

namespace OpenTabletDriver.Plugin.Output
{
    /// <summary>
    /// A relatively positioned output mode.
    /// </summary>
    [PluginIgnore]
    public abstract class RelativeOutputMode : OutputMode
    {
        private HPETDeltaStopwatch stopwatch = new HPETDeltaStopwatch();
        private Vector2? lastTransformedPos;
        private Vector2 lastReadPos;
        private bool outOfRange;
        private float maxPressureReciprocal;

        // for handling detection of low resetTimes
        private uint _resets;
        private bool _warnedBadResets;

        /// <summary>
        /// The class in which the final relative positioned output is handled.
        /// </summary>
        public abstract IRelativePointer Pointer { set; get; }

        private Vector2 sensitivity;

        /// <summary>
        /// The sensitivity vector in which input will be transformed.
        /// <remarks>
        /// This sensitivity is in mm/px.
        /// </remarks>
        /// </summary>
        public Vector2 Sensitivity
        {
            set
            {
                this.sensitivity = value;
                this.TransformationMatrix = CreateTransformationMatrix();
            }
            get => this.sensitivity;
        }

        private float rotation;
        private TimeSpan _resetTime;

        /// <summary>
        /// The angle of rotation to be applied to the input.
        /// </summary>
        public float Rotation
        {
            set
            {
                this.rotation = value;
                this.TransformationMatrix = CreateTransformationMatrix();
            }
            get => this.rotation;
        }

        /// <summary>
        /// The delay in which to reset the last known position in relative positioning.
        /// </summary>
        public TimeSpan ResetTime
        {
            set
            {
                _resetTime = value;
                _resets = 0;
                _warnedBadResets = false;
            }
            get => _resetTime;
        }

        protected override Matrix3x2 CreateTransformationMatrix()
        {
            var pen = Tablet?.Properties?.Specifications?.Pen;
            maxPressureReciprocal = pen != null && pen.MaxPressure > 0 ? 1.0f / pen.MaxPressure : 0f;

            var transform = Matrix3x2.CreateRotation(
                (float)(-Rotation * System.Math.PI / 180));

            var digitizer = Tablet?.Properties.Specifications.Digitizer;
            return transform *= Matrix3x2.CreateScale(
                sensitivity.X * ((digitizer?.Width / digitizer?.MaxX) ?? 0.01f),
                sensitivity.Y * ((digitizer?.Height / digitizer?.MaxY) ?? 0.01f));
        }

        public override void Read(IDeviceReport deviceReport)
        {
            // intercept positional reports
            if (deviceReport is IAbsolutePositionReport report)
            {
                var deltaTime = stopwatch.Restart();

                // reset origin when exceeding the reset delay
                if (deltaTime > ResetTime)
                {
                    outOfRange = true;
                    lastTransformedPos = null;
                    _resets++;
                }
                else _resets = 0;

                if (!_warnedBadResets &&
                    (_warnedBadResets =
                        _resets > 10))
                    Log.WriteNotify("RelativeOutputMode",
                        $"Position reset spam detected - the configured reset time ({ResetTime.TotalMilliseconds} ms) is likely too low",
                        LogLevel.Warning);

                // skip duplicate reports sent by tablets right after going into
                // range from an out of range state.
                if (outOfRange && report.Position == lastReadPos)
                    return;

                outOfRange = false;
                lastReadPos = report.Position;
            }
            else if (deviceReport is OutOfRangeReport)
            {
                outOfRange = true;
            }

            base.Read(deviceReport);
        }

        protected override IAbsolutePositionReport Transform(IAbsolutePositionReport report)
        {
            var pos = Vector2.Transform(report.Position, TransformationMatrix);
            var delta = pos - lastTransformedPos;

            lastTransformedPos = pos;
            report.Position = delta.GetValueOrDefault();

            return report;
        }

        protected override void OnOutput(IDeviceReport report)
        {
            var caps = ResolveCapabilities(report);

            // this should be ordered from least to most chance of having a
            // dependency to another pointer property.
            if ((caps & ReportCapabilities.Proximity) != 0 && Pointer is IHoverDistanceHandler hoverDistanceHandler)
                hoverDistanceHandler.SetHoverDistance(((IProximityReport)report).HoverDistance);
            if ((caps & ReportCapabilities.Eraser) != 0 && Pointer is IEraserHandler eraserHandler)
                eraserHandler.SetEraser(((IEraserReport)report).Eraser);
            if ((caps & ReportCapabilities.Tilt) != 0 && Pointer is ITiltHandler tiltHandler && !DisableTilt)
                tiltHandler.SetTilt(((ITiltReport)report).Tilt);
            if ((caps & ReportCapabilities.Tablet) != 0 && Pointer is IPressureHandler pressureHandler &&
                !DisablePressure && maxPressureReciprocal != 0f)
                pressureHandler.SetPressure(((ITabletReport)report).Pressure * maxPressureReciprocal);

            // make sure to set the position last
            if ((caps & ReportCapabilities.AbsolutePosition) != 0)
                Pointer.SetPosition(((IAbsolutePositionReport)report).Position);
            if (Pointer is ISynchronousPointer synchronousPointer)
            {
                if ((caps & ReportCapabilities.OutOfRange) != 0)
                    synchronousPointer.Reset();
                synchronousPointer.Flush();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Pointer is ISynchronousPointer synchronousPointer)
            {
                synchronousPointer.Reset();
                synchronousPointer.Flush();
            }

            base.Dispose(disposing);
        }
    }
}
