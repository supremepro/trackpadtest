using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TrackpadWindowControl
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Single-instance guard.
            using var mutex = new Mutex(true, "TrackpadWindowControl_SingleInstance",
                out bool createdNew);

            if (!createdNew)
            {
                MessageBoxW(IntPtr.Zero,
                    "Trackpad Window Control is already running.\n" +
                    "Look for its icon in the system tray.",
                    "Already Running", 0x40 /* MB_ICONINFORMATION */);
                return;
            }

            try
            {
                using var app = new TrayApplication();
                app.Run();
            }
            catch (Exception ex)
            {
                MessageBoxW(IntPtr.Zero,
                    $"Fatal error:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Trackpad Window Control", 0x10 /* MB_ICONERROR */);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
