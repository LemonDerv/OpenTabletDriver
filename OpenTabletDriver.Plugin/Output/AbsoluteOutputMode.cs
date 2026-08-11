using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Plugin.Output
{
    /// <summary>
    /// An absolutely positioned output mode.
    /// </summary>
    [PluginIgnore]
    public abstract class AbsoluteOutputMode : OutputMode
    {
        private Vector2 min, max;
        private Area outputArea, inputArea;
        private float maxPressureReciprocal;

        /// <summary>
        /// The area in which the tablet's input is transformed to.
        /// </summary>
        public Area Input
        {
            set
            {
                this.inputArea = value;
                this.TransformationMatrix = CreateTransformationMatrix();
            }
            get => this.inputArea;
        }

        /// <summary>
        /// The area in which the final processed output is transformed to.
        /// </summary>
        public Area Output
        {
            set
            {
                this.outputArea = value;
                this.TransformationMatrix = CreateTransformationMatrix();
            }
            get => this.outputArea;
        }

        /// <summary>
        /// The class in which the final absolute positioned output is handled.
        /// </summary>
        public abstract IAbsolutePointer Pointer { set; get; }

        /// <summary>
        /// Whether to clip all tablet inputs to the assigned areas.
        /// </summary>
        /// <remarks>
        /// If false, input outside the area can escape the assigned areas, but still will be transformed.
        /// If true, input outside the area will be clipped to the edges of the assigned areas.
        /// </remarks>
        public bool AreaClipping { set; get; }

        /// <summary>
        /// Whether to stop accepting input outside the assigned areas.
        /// </summary>
        /// <remarks>
        /// If true, <see cref="AreaClipping"/> is automatically implied true.
        /// </remarks>
        public bool AreaLimiting { set; get; }

        protected override Matrix3x2 CreateTransformationMatrix()
        {
            if (Input != null && Output != null && Tablet != null)
            {
                var pen = Tablet?.Properties?.Specifications?.Pen;
                maxPressureReciprocal = pen != null && pen.MaxPressure > 0 ? 1.0f / pen.MaxPressure : 0f;

                var transform = CalculateTransformation(Input, Output, Tablet.Properties.Specifications.Digitizer);

                var halfDisplayWidth = Output?.Width / 2 ?? 0;
                var halfDisplayHeight = Output?.Height / 2 ?? 0;

                var minX = Output?.Position.X - halfDisplayWidth ?? 0;
                var maxX = Output?.Position.X + Output?.Width - halfDisplayWidth ?? 0;
                var minY = Output?.Position.Y - halfDisplayHeight ?? 0;
                var maxY = Output?.Position.Y + Output?.Height - halfDisplayHeight ?? 0;

                this.min = new Vector2(minX, minY);
                this.max = new Vector2(maxX, maxY);

                return transform;
            }
            else
            {
                return Matrix3x2.Identity;
            }
        }

        protected static Matrix3x2 CalculateTransformation(Area input, Area output, DigitizerSpecifications digitizer)
        {
            // Convert raw tablet data to millimeters
            var res = Matrix3x2.CreateScale(
                digitizer.Width / digitizer.MaxX,
                digitizer.Height / digitizer.MaxY);

            // Translate to the center of input area
            res *= Matrix3x2.CreateTranslation(
                -input.Position.X, -input.Position.Y);

            // Apply rotation
            res *= Matrix3x2.CreateRotation(
                (float)(-input.Rotation * System.Math.PI / 180));

            // Scale millimeters to pixels
            res *= Matrix3x2.CreateScale(
                output.Width / input.Width, output.Height / input.Height);

            // Translate output to virtual screen coordinates
            res *= Matrix3x2.CreateTranslation(
                output.Position.X, output.Position.Y);

            return res;
        }

        /// <summary>
        /// Transposes, transforms, and performs all absolute positioning calculations to a <see cref="IAbsolutePositionReport"/>.
        /// </summary>
        /// <param name="report">The <see cref="IAbsolutePositionReport"/> in which to transform.</param>
        protected override IAbsolutePositionReport Transform(IAbsolutePositionReport report)
        {
            // Apply transformation
            var pos = Vector2.Transform(report.Position, this.TransformationMatrix);

            // Clipping to display bounds
            var clippedPoint = Vector2.Clamp(pos, this.min, this.max);
            if (AreaLimiting && clippedPoint != pos)
                return null;

            if (AreaClipping)
                pos = clippedPoint;

            report.Position = pos;

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
