using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TrackpadWindowControl
{
    /// <summary>
    /// Manages the system tray icon and right-click context menu.
    /// Owns all subsystems (RawInputWindow, TouchTracker, WindowMover).
    /// </summary>
    internal sealed class TrayApplication : IDisposable
    {
        // Menu command IDs.
        private const uint CMD_TOGGLE     = 101;
        private const uint CMD_AUTOSTART  = 102;
        private const uint CMD_ABOUT      = 103;
        private const uint CMD_EXIT       = 104;

        private readonly TouchTracker    _tracker;
        private readonly WindowMover     _mover;
        private readonly RawInputWindow  _window;

        private bool _enabled = true;
        private bool _disposed;

        // Tray icon handle — we create a simple programmatic icon.
        private IntPtr _hIcon = IntPtr.Zero;
        private NativeMethods.NOTIFYICONDATA _nid;

        public TrayApplication()
        {
            _tracker = new TouchTracker();
            _mover   = new WindowMover(_tracker);
            _window  = new RawInputWindow(_tracker);
            _window.CommandReceived += OnCommand;

            AddTrayIcon();
        }

        public void Run() => _window.RunMessageLoop();

        // ── Tray icon ────────────────────────────────────────────────────────

        private void AddTrayIcon()
        {
            _hIcon = CreateSimpleIcon();

            _nid = new NativeMethods.NOTIFYICONDATA
            {
                cbSize          = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd            = _window.Hwnd,
                uID             = 1,
                uFlags          = NativeMethods.NIF_MESSAGE |
                                  NativeMethods.NIF_ICON    |
                                  NativeMethods.NIF_TIP,
                uCallbackMessage = NativeMethods.WM_TRAYICON,
                hIcon           = _hIcon,
                szTip           = "Trackpad Window Control — enabled",
            };

            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
        }

        private void UpdateTrayTooltip()
        {
            _nid.szTip = _enabled
                ? "Trackpad Window Control — enabled"
                : "Trackpad Window Control — disabled";
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _nid);
        }

        private void RemoveTrayIcon()
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
            if (_hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }

        // ── Context menu ─────────────────────────────────────────────────────

        private void ShowContextMenu()
        {
            NativeMethods.GetCursorPos(out var pt);

            IntPtr hMenu = NativeMethods.CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            try
            {
                bool autoStart = AutoStartManager.IsEnabled();

                // "Enable / Disable" toggle
                string toggleLabel = _enabled ? "Disable" : "Enable";
                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING,
                    new IntPtr(CMD_TOGGLE), toggleLabel);

                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR,
                    IntPtr.Zero, null);

                // Auto-start toggle (checkmark when active)
                uint autoStartFlags = NativeMethods.MF_STRING |
                    (autoStart ? NativeMethods.MF_CHECKED : 0);
                NativeMethods.AppendMenu(hMenu, autoStartFlags,
                    new IntPtr(CMD_AUTOSTART), "Start with Windows");

                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR,
                    IntPtr.Zero, null);

                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING,
                    new IntPtr(CMD_ABOUT), "About…");

                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING,
                    new IntPtr(CMD_EXIT), "Exit");

                // Required before TrackPopupMenu so the menu disappears on click-away.
                NativeMethods.SetForegroundWindow(_window.Hwnd);

                NativeMethods.TrackPopupMenu(
                    hMenu,
                    NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_BOTTOMALIGN,
                    pt.X, pt.Y, 0, _window.Hwnd, IntPtr.Zero);
            }
            finally { NativeMethods.DestroyMenu(hMenu); }
        }

        // ── Command handling ─────────────────────────────────────────────────

        private void OnCommand(uint command)
        {
            if (command == NativeMethods.WM_RBUTTONUP)
            {
                ShowContextMenu();
                return;
            }

            if (command == NativeMethods.WM_LBUTTONDBLCLK)
            {
                // Double-click on tray icon → toggle enable/disable.
                _enabled = !_enabled;
                _tracker.Enabled = _enabled;
                if (!_enabled) _tracker.ClearAll();
                UpdateTrayTooltip();
                return;
            }

            switch (command)
            {
                case CMD_TOGGLE:
                    _enabled = !_enabled;
                    _tracker.Enabled = _enabled;
                    if (!_enabled) _tracker.ClearAll();
                    UpdateTrayTooltip();
                    break;

                case CMD_AUTOSTART:
                    if (AutoStartManager.IsEnabled())
                        AutoStartManager.Disable();
                    else
                        AutoStartManager.Enable();
                    break;

                case CMD_ABOUT:
                    ShowAbout();
                    break;

                case CMD_EXIT:
                    RemoveTrayIcon();
                    _window.PostQuit();
                    break;
            }
        }

        private static void ShowAbout()
        {
            // Use MessageBox via user32 directly to avoid WinForms dependency here.
            MessageBoxW(IntPtr.Zero,
                "Trackpad Window Control v1.0\n\n" +
                "Move windows with a 3-finger drag on your precision touchpad.\n\n" +
                "Tip: For best results, disable Windows 3-finger gestures in\n" +
                "Settings → Bluetooth & devices → Touchpad → Three-finger gestures\n" +
                "and set the gesture to 'Nothing'.",
                "About Trackpad Window Control",
                0x40 /* MB_ICONINFORMATION */);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        // ── Icon creation ────────────────────────────────────────────────────

        // Creates a simple 16×16 icon programmatically so we need no icon file.
        private static unsafe IntPtr CreateSimpleIcon()
        {
            // BITMAPV4HEADER + XOR mask (16×16 × 4 bytes BGRA) + AND mask (16×16 / 8 bytes)
            const int W = 16, H = 16;
            const int colorBytes  = W * H * 4;
            const int maskBytes   = (W * H) / 8;

            byte[] colorBits = new byte[colorBytes];
            byte[] maskBits  = new byte[maskBytes];   // all zero = fully opaque

            // Draw a simple hand-pointer icon (3 fingers + palm).
            // Rows are bottom-up in DIB format.
            for (int row = 0; row < H; row++)
            {
                // positive biHeight → bottom-up DIB: row 0 = bottom of image.
                // Flip so y=0 means top of the visual icon.
                int y = (H - 1) - row;
                for (int x = 0; x < W; x++)
                {
                    bool draw = IsIconPixel(x, y);
                    int idx = (row * W + x) * 4;
                    if (draw)
                    {
                        colorBits[idx + 0] = 0x00; // B
                        colorBits[idx + 1] = 0x70; // G
                        colorBits[idx + 2] = 0xC0; // R  → steel-blue
                        colorBits[idx + 3] = 0xFF; // A
                    }
                    // else transparent (alpha=0)
                }
            }

            // Build ICONINFO with CreateDIBSection-based bitmaps.
            IntPtr hdc = GetDC(IntPtr.Zero);
            IntPtr hbmColor = IntPtr.Zero, hbmMask = IntPtr.Zero;

            try
            {
                // Color bitmap (32-bit BGRA, bottom-up = positive height).
                byte[] bmiColor = BuildBMIH(W, H, 32);
                fixed (byte* pBmi = bmiColor)
                fixed (byte* pBits = colorBits)
                {
                    IntPtr pvBits;
                    hbmColor = CreateDIBSection(hdc, (IntPtr)pBmi, 0 /*DIB_RGB_COLORS*/,
                        out pvBits, IntPtr.Zero, 0);
                    if (hbmColor != IntPtr.Zero && pvBits != IntPtr.Zero)
                        Buffer.MemoryCopy(pBits, (void*)pvBits, colorBytes, colorBytes);
                }

                // Mask bitmap (1-bit, all black = opaque).
                byte[] bmiMask = BuildBMIH(W, H, 1);
                fixed (byte* pBmi = bmiMask)
                fixed (byte* pBits = maskBits)
                {
                    IntPtr pvBits;
                    hbmMask = CreateDIBSection(hdc, (IntPtr)pBmi, 0,
                        out pvBits, IntPtr.Zero, 0);
                    if (hbmMask != IntPtr.Zero && pvBits != IntPtr.Zero)
                        Buffer.MemoryCopy(pBits, (void*)pvBits, maskBytes, maskBytes);
                }

                var iconInfo = new ICONINFO
                {
                    fIcon    = 1,
                    xHotspot = 0,
                    yHotspot = 0,
                    hbmMask  = hbmMask,
                    hbmColor = hbmColor,
                };

                return CreateIconIndirect(ref iconInfo);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
                if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
                if (hbmMask  != IntPtr.Zero) DeleteObject(hbmMask);
            }
        }

        // Simple icon: three finger bars at top, palm base at bottom.
        // y=0 is the top of the 16×16 icon.
        private static bool IsIconPixel(int x, int y)
        {
            // Three 2-px wide finger bars
            if (y >= 0 && y <= 10)
            {
                if (x >= 1 && x <= 3)  return true;   // left finger
                if (x >= 6 && x <= 8)  return true;   // middle finger
                if (x >= 11 && x <= 13) return true;  // right finger
            }
            // Palm base
            if (y >= 11 && y <= 14 && x >= 1 && x <= 13) return true;
            return false;
        }

        private static byte[] BuildBMIH(int w, int h, ushort bpp)
        {
            // BITMAPINFOHEADER = 40 bytes. Positive biHeight → bottom-up DIB.
            byte[] b = new byte[40];
            BitConverter.TryWriteBytes(b.AsSpan(0),  40);
            BitConverter.TryWriteBytes(b.AsSpan(4),  w);
            BitConverter.TryWriteBytes(b.AsSpan(8),  h);   // positive = bottom-up
            BitConverter.TryWriteBytes(b.AsSpan(12), (ushort)1);
            BitConverter.TryWriteBytes(b.AsSpan(14), bpp);
            return b;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int    fIcon;
            public int    xHotspot;
            public int    yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("gdi32.dll")]  private static extern IntPtr CreateDIBSection(IntPtr hdc, IntPtr pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr ho);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RemoveTrayIcon();
            _window.Dispose();
        }
    }
}
