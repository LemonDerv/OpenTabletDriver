using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenTabletDriver.Plugin.Tablet
{
    public sealed class TabletReport : ITabletReport
    {
        [ThreadStatic] private static bool[]? s_cachedButtons;

        public TabletReport(byte[] report)
        {
            Raw = report;

            if (report.Length < 8)
            {
                Position = Vector2.Zero;
                Pressure = 0;
                PenButtons = Array.Empty<bool>();
                return;
            }

            Position = new Vector2
            {
                X = Unsafe.ReadUnaligned<ushort>(ref report[2]),
                Y = Unsafe.ReadUnaligned<ushort>(ref report[4])
            };
            Pressure = Unsafe.ReadUnaligned<ushort>(ref report[6]);

            var buttons = s_cachedButtons ??= new bool[3];
            buttons[0] = report[1].IsBitSet(1);
            buttons[1] = report[1].IsBitSet(2);
            buttons[2] = report[1].IsBitSet(3);
            PenButtons = buttons;
        }

        public byte[] Raw { set; get; }
        public Vector2 Position { set; get; }
        public uint Pressure { set; get; }
        public bool[] PenButtons { set; get; }
    }
}
