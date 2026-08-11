using System;
using System.Runtime.InteropServices;
using System.Threading;
using ITimer = OpenTabletDriver.Plugin.Timers.ITimer;

using static OpenTabletDriver.Native.Windows.Windows;

namespace OpenTabletDriver.Desktop.Interop.Timer
{
    /// <summary>
    /// High-resolution periodic timer using CreateWaitableTimerExW with
    /// CREATE_WAITABLE_TIMER_HIGH_RESOLUTION. Available on Windows 10 1803+
    /// and provides reliable sub-millisecond timing on Windows 11 where
    /// the legacy multimedia timers (timeSetEvent) have degraded precision.
    /// </summary>
    internal class HighResolutionWaitableTimer : ITimer, IDisposable
    {
        private IntPtr timerHandle;
        private Thread timerThread;
        private volatile bool running;

        public bool Enabled { get; private set; }
        public float Interval { get; set; } = 1;

        public event Action Elapsed;

        public void Start()
        {
            if (Enabled)
                return;

            timerHandle = CreateWaitableTimerExW(
                IntPtr.Zero,
                IntPtr.Zero,
                CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                TIMER_ALL_ACCESS);

            if (timerHandle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateWaitableTimerExW failed: {Marshal.GetLastWin32Error()}");

            // Negative value = relative time in 100-nanosecond intervals
            long dueTime = -(long)(Interval * 10_000);
            int periodMs = (int)Interval;

            // For sub-millisecond accuracy, use period=0 and re-arm manually
            // For integer-ms intervals, use built-in periodic mode
            bool usePeriodicMode = Interval >= 1.0f && Interval == (int)Interval;

            if (usePeriodicMode)
            {
                if (!SetWaitableTimer(timerHandle, ref dueTime, periodMs, IntPtr.Zero, IntPtr.Zero, false))
                {
                    CloseHandle(timerHandle);
                    timerHandle = IntPtr.Zero;
                    throw new InvalidOperationException(
                        $"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
                }
            }
            else
            {
                if (!SetWaitableTimer(timerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    CloseHandle(timerHandle);
                    timerHandle = IntPtr.Zero;
                    throw new InvalidOperationException(
                        $"SetWaitableTimer failed: {Marshal.GetLastWin32Error()}");
                }
            }

            running = true;
            Enabled = true;

            timerThread = new Thread(() => TimerLoop(usePeriodicMode))
            {
                Name = "OTD High-Resolution Timer",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            timerThread.Start();
        }

        public void Stop()
        {
            if (!Enabled)
                return;

            running = false;

            if (timerHandle != IntPtr.Zero)
            {
                CancelWaitableTimer(timerHandle);
                // Unblock the waiting thread
            }

            timerThread?.Join();
            timerThread = null;

            if (timerHandle != IntPtr.Zero)
            {
                CloseHandle(timerHandle);
                timerHandle = IntPtr.Zero;
            }

            Enabled = false;
        }

        ~HighResolutionWaitableTimer()
        {
            if (timerHandle != IntPtr.Zero)
                CloseHandle(timerHandle);
        }

        private void TimerLoop(bool periodicMode)
        {
            while (running)
            {
                uint result = WaitForSingleObject(timerHandle, INFINITE);
                if (result != WAIT_OBJECT_0 || !running)
                    break;

                Elapsed?.Invoke();

                if (!periodicMode && running)
                {
                    // Re-arm for non-periodic (fractional ms) intervals
                    long dueTime = -(long)(Interval * 10_000);
                    SetWaitableTimer(timerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false);
                }
            }
        }

        /// <summary>
        /// Tests whether the high-resolution waitable timer is available on this OS.
        /// </summary>
        public static bool IsSupported()
        {
            IntPtr handle = CreateWaitableTimerExW(
                IntPtr.Zero,
                IntPtr.Zero,
                CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                TIMER_ALL_ACCESS);

            if (handle == IntPtr.Zero)
                return false;

            CloseHandle(handle);
            return true;
        }

        public void Dispose()
        {
            if (Enabled)
                Stop();

            GC.SuppressFinalize(this);
        }
    }
}
