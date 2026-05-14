using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TrackpadWindowControl
{
    /// <summary>
    /// Manages the HKCU Run key so the app can be toggled to start with Windows.
    /// </summary>
    internal static class AutoStartManager
    {
        private const string KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string VALUE_NAME = "TrackpadWindowControl";

        public static bool IsEnabled()
        {
            IntPtr hKey;
            int err = NativeMethods.RegOpenKeyEx(
                NativeMethods.HKEY_CURRENT_USER, KEY_PATH, 0,
                NativeMethods.KEY_READ, out hKey);

            if (err != 0) return false;

            try
            {
                uint type = 0;
                uint size = 0;
                int qErr = NativeMethods.RegQueryValueEx(
                    hKey, VALUE_NAME, IntPtr.Zero, out type, null, ref size);

                return qErr == 0 && size > 0;
            }
            finally { NativeMethods.RegCloseKey(hKey); }
        }

        public static void Enable()
        {
            string? exePath = GetExePath();
            if (exePath == null) return;

            IntPtr hKey;
            int err = NativeMethods.RegOpenKeyEx(
                NativeMethods.HKEY_CURRENT_USER, KEY_PATH, 0,
                NativeMethods.KEY_WRITE, out hKey);

            if (err != 0) return;

            try
            {
                byte[] data = Encoding.Unicode.GetBytes(exePath + "\0");
                NativeMethods.RegSetValueEx(
                    hKey, VALUE_NAME, 0, NativeMethods.REG_SZ, data, (uint)data.Length);
            }
            finally { NativeMethods.RegCloseKey(hKey); }
        }

        public static void Disable()
        {
            IntPtr hKey;
            int err = NativeMethods.RegOpenKeyEx(
                NativeMethods.HKEY_CURRENT_USER, KEY_PATH, 0,
                NativeMethods.KEY_WRITE, out hKey);

            if (err != 0) return;

            try { NativeMethods.RegDeleteValue(hKey, VALUE_NAME); }
            finally { NativeMethods.RegCloseKey(hKey); }
        }

        private static string? GetExePath() =>
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }
}
