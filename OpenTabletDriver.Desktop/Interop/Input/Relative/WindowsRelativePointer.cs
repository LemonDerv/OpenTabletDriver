using System.Numerics;
using OpenTabletDriver.Native.Windows.Input;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input.Relative
{
    public class WindowsRelativePointer : WindowsVirtualMouse, IRelativePointer
    {
        private float errorX;
        private float errorY;

        public void SetPosition(Vector2 delta)
        {
            SetDirty();

            var x = delta.X + errorX;
            var y = delta.Y + errorY;
            var dx = (int)x;
            var dy = (int)y;

            errorX = x - dx;
            errorY = y - dy;

            inputs[0].U.mi.dwFlags |= MOUSEEVENTF.MOVE;
            inputs[0].U.mi.dx = dx;
            inputs[0].U.mi.dy = dy;
        }
    }
}
