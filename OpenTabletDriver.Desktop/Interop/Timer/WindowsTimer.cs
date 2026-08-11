using System;
using System.Runtime.InteropServices;
using OpenTabletDriver.Native.Windows;
using OpenTabletDriver.Native.Windows.Timers;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Timers;

namespace OpenTabletDriver.Desktop.Interop.Timer
{
    using static OpenTabletDriver.Native.Windows.Windows;

    internal class WindowsTimer : ITimer, IDisposable
    {
        private static readonly Lazy<bool> _highResSupported = new Lazy<bool>(HighResolutionWaitableTimer.IsSupported);

        public WindowsTimer()
        {
            callbackDelegate = Callback;
            callbackHandle = GCHandle.Alloc(callbackDelegate);
        }

        private uint timerId;
        private HighResolutionWaitableTimer highResTimer;
        private FallbackTimer fallbackTimer;
        private readonly TimerCallback callbackDelegate;
        private readonly GCHandle callbackHandle;
        private readonly object stateLock = new object();

        public bool Enabled { private set; get; }
        public float Interval { set; get; } = 1;

        public event Action Elapsed;

        public unsafe void Start()
        {
            lock (stateLock)
            {
                if (!Enabled)
                {
                    if (_highResSupported.Value)
                    {
                        StartHighResolutionTimer();
                    }
                    else if (IsSupportedByMultimediaTimer(Interval))
                    {
                        StartMultimediaTimer();
                    }
                    else
                    {
                        StartFallbackTimer();
                    }
                }
            }
        }

        private void StartHighResolutionTimer()
        {
            highResTimer = new HighResolutionWaitableTimer
            {
                Interval = Interval
            };
            highResTimer.Elapsed += () => Elapsed?.Invoke();
            highResTimer.Start();
            Enabled = true;
        }

        private unsafe void StartMultimediaTimer()
        {
            var caps = new TimeCaps();
            _ = timeGetDevCaps(ref caps, (uint)sizeof(TimeCaps));
            var clampedInterval = Math.Clamp((uint)Interval, caps.wPeriodMin, caps.wPeriodMax);
            _ = timeBeginPeriod(clampedInterval);
            timerId = timeSetEvent(clampedInterval, 1, callbackDelegate, IntPtr.Zero, EventType.TIME_PERIODIC | EventType.TIME_KILL_SYNCHRONOUS);
            Enabled = true;
        }

        private void StartFallbackTimer()
        {
            Log.WriteNotify("Timer", "Unsupported interval detected, will use fallback timer. Expect high CPU usage. Please use 1000hz, 500hz, 250hz or 125hz instead.", LogLevel.Warning);
            fallbackTimer = new FallbackTimer
            {
                Interval = Interval
            };
            fallbackTimer.Elapsed += () => Elapsed?.Invoke();
            fallbackTimer.Start();
            Enabled = true;
        }

        public void Stop()
        {
            lock (stateLock)
            {
                if (Enabled)
                {
                    if (highResTimer != null)
                    {
                        highResTimer.Stop();
                        highResTimer.Dispose();
                        highResTimer = null;
                    }
                    else if (fallbackTimer != null)
                    {
                        fallbackTimer.Stop();
                        fallbackTimer.Dispose();
                        fallbackTimer = null;
                    }
                    else
                    {
                        _ = timeKillEvent(timerId);
                        _ = timeEndPeriod((uint)Interval);
                    }
                    Enabled = false;
                }
            }
        }

        private void Callback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
        {
            Elapsed?.Invoke();
        }

        private static bool IsSupportedByMultimediaTimer(float interval)
        {
            return interval == (int)interval;
        }

        public void Dispose()
        {
            if (Enabled)
                Stop();
            callbackHandle.Free();
            GC.SuppressFinalize(this);
        }
    }
}
