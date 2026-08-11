using System;

namespace OpenTabletDriver.Plugin.Tablet
{
    /// <summary>
    /// Cached capability flags for fast report type dispatch in output modes.
    /// Eliminates repeated interface 'is' checks on every report at 1000Hz.
    /// </summary>
    [Flags]
    public enum ReportCapabilities : byte
    {
        None = 0,
        AbsolutePosition = 1 << 0,
        Tablet = 1 << 1,
        Proximity = 1 << 2,
        Eraser = 1 << 3,
        Tilt = 1 << 4,
        OutOfRange = 1 << 5,
    }
}
