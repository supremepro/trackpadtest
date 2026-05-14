using System;
using System.Runtime.InteropServices;

namespace TrackpadWindowControl
{
    /// <summary>
    /// Creates an invisible Win32 window that receives WM_INPUT messages from
    /// the precision touchpad even when this app is not in the foreground
    /// (RIDEV_INPUTSINK).  Dispatches parsed contact data to TouchTracker.
    /// </summary>
    internal sealed class RawInputWindow : IDisposable
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private readonly NativeMethods.WndProcDelegate _wndProc;
        private readonly HidParser    _parser  = new();
        private readonly TouchTracker _tracker;
        private bool _registered;
        private bool _disposed;

        // Tray application hooks into this to receive menu commands.
        public event Action<uint>? CommandReceived;

        public IntPtr Hwnd => _hwnd;

        public RawInputWindow(TouchTracker tracker)
        {
            _tracker = tracker;
            // Keep delegate alive for the lifetime of this object.
            _wndProc = WndProc;
            CreateWindow();
            RegisterTouchpad();
        }

        private void CreateWindow()
        {
            string className = "TrackpadWindowControl_" + Environment.TickCount64;
            IntPtr hInstance = NativeMethods.GetModuleHandle(null);

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize        = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc   = _wndProc,
                hInstance     = hInstance,
                lpszClassName = className,
            };

            ushort atom = NativeMethods.RegisterClassEx(ref wc);
            if (atom == 0)
                throw new InvalidOperationException(
                    $"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

            // Hidden popup window positioned off-screen.
            _hwnd = NativeMethods.CreateWindowEx(
                0, className, "TrackpadWindowControl",
                0x80000000u, // WS_POPUP
                -32000, -32000, 1, 1,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        private void RegisterTouchpad()
        {
            // Register for Touchpad (0x05) AND Touch Screen (0x04) — ELAN and
            // Synaptics devices on ThinkPads sometimes report as the latter.
            var rids = new[]
            {
                new NativeMethods.RAWINPUTDEVICE
                {
                    usUsagePage = NativeMethods.HID_USAGE_PAGE_DIGITIZER,
                    usUsage     = NativeMethods.HID_USAGE_DIGITIZER_TOUCHPAD,   // 0x05
                    dwFlags     = NativeMethods.RIDEV_INPUTSINK,
                    hwndTarget  = _hwnd,
                },
                new NativeMethods.RAWINPUTDEVICE
                {
                    usUsagePage = NativeMethods.HID_USAGE_PAGE_DIGITIZER,
                    usUsage     = 0x04,   // Touch Screen — some OEM drivers use this
                    dwFlags     = NativeMethods.RIDEV_INPUTSINK,
                    hwndTarget  = _hwnd,
                },
            };

            _registered = NativeMethods.RegisterRawInputDevices(
                rids, (uint)rids.Length,
                (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());

            System.Diagnostics.Debug.WriteLine(_registered
                ? "RegisterRawInputDevices: OK"
                : $"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case NativeMethods.WM_INPUT:
                    HandleRawInput(lParam);
                    return IntPtr.Zero;

                case NativeMethods.WM_TRAYICON:
                    // Right-click → show context menu.
                    // Double-click → forward as-is (handled as toggle in TrayApp).
                    {
                        int notif = (int)lParam;
                        if (notif == NativeMethods.WM_RBUTTONUP ||
                            notif == NativeMethods.WM_LBUTTONDBLCLK)
                            CommandReceived?.Invoke((uint)notif);
                    }
                    return IntPtr.Zero;

                case NativeMethods.WM_COMMAND:
                    CommandReceived?.Invoke((uint)(wParam.ToInt32() & 0xFFFF));
                    return IntPtr.Zero;

                case NativeMethods.WM_DESTROY:
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void HandleRawInput(IntPtr lParam)
        {
            if (!_registered) return;

            var contacts = _parser.Parse(lParam);
            if (contacts.Count > 0)
                _tracker.Update(contacts);
        }

        // ── Message pump (called from main thread) ────────────────────────────

        public void RunMessageLoop()
        {
            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }

        public void PostQuit() =>
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_registered)
            {
                var removeRids = new[]
                {
                    new NativeMethods.RAWINPUTDEVICE { usUsagePage = NativeMethods.HID_USAGE_PAGE_DIGITIZER, usUsage = NativeMethods.HID_USAGE_DIGITIZER_TOUCHPAD, dwFlags = 0x00000001, hwndTarget = IntPtr.Zero },
                    new NativeMethods.RAWINPUTDEVICE { usUsagePage = NativeMethods.HID_USAGE_PAGE_DIGITIZER, usUsage = 0x04,                                        dwFlags = 0x00000001, hwndTarget = IntPtr.Zero },
                };
                NativeMethods.RegisterRawInputDevices(removeRids, (uint)removeRids.Length,
                    (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
            }

            _parser.Dispose();

            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
