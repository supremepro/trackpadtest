using System;
using System.Runtime.InteropServices;

namespace TrackpadWindowControl
{
    /// <summary>
    /// Listens to gesture events and moves the target window accordingly.
    /// The target is the top-level window under the cursor when the gesture starts.
    /// </summary>
    internal sealed class WindowMover
    {
        // Sensitivity multiplier: logical HID units → screen pixels.
        // Higher = faster movement.  Users can tune this.
        public float Sensitivity { get; set; } = 3.0f;

        private IntPtr _targetWindow = IntPtr.Zero;
        private int    _winX, _winY;

        // Accumulated sub-pixel remainder to avoid drift from integer rounding.
        private float  _remX, _remY;

        private readonly TouchTracker _tracker;

        public WindowMover(TouchTracker tracker)
        {
            _tracker = tracker;
            _tracker.GestureStarted += OnGestureStarted;
            _tracker.GestureMoved   += OnGestureMoved;
            _tracker.GestureEnded   += OnGestureEnded;
        }

        private void OnGestureStarted()
        {
            _remX = 0; _remY = 0;

            // Find window under cursor.
            NativeMethods.GetCursorPos(out var pt);
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);

            if (hwnd == IntPtr.Zero) return;

            // Walk up to the root (top-level) window.
            hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);

            if (hwnd == IntPtr.Zero) return;

            // Skip maximized, minimized, or invisible windows.
            if (!NativeMethods.IsWindowVisible(hwnd)) return;
            if (NativeMethods.IsIconic(hwnd))  return;
            if (NativeMethods.IsZoomed(hwnd))  return;

            _targetWindow = hwnd;

            NativeMethods.GetWindowRect(hwnd, out var rect);
            _winX = rect.Left;
            _winY = rect.Top;
        }

        private void OnGestureMoved(float dx, float dy)
        {
            if (_targetWindow == IntPtr.Zero) return;

            // Re-validate: window might have been maximised/closed mid-gesture.
            if (!NativeMethods.IsWindowVisible(_targetWindow) ||
                NativeMethods.IsZoomed(_targetWindow))
            {
                _targetWindow = IntPtr.Zero;
                return;
            }

            float scaledDx = dx * Sensitivity + _remX;
            float scaledDy = dy * Sensitivity + _remY;

            int pixDx = (int)scaledDx;
            int pixDy = (int)scaledDy;

            _remX = scaledDx - pixDx;
            _remY = scaledDy - pixDy;

            _winX += pixDx;
            _winY += pixDy;

            NativeMethods.SetWindowPos(
                _targetWindow, IntPtr.Zero,
                _winX, _winY, 0, 0,
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_ASYNCWINDOWPOS);
        }

        private void OnGestureEnded()
        {
            _targetWindow = IntPtr.Zero;
        }
    }
}
