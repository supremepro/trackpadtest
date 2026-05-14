using System;
using System.Runtime.InteropServices;

namespace TrackpadWindowControl
{
    internal static class NativeMethods
    {
        // ── Window messages ──────────────────────────────────────────────────
        public const int WM_INPUT          = 0x00FF;
        public const int WM_DESTROY        = 0x0002;
        public const int WM_USER           = 0x0400;
        public const int WM_TRAYICON       = WM_USER + 1;
        public const int WM_NCHITTEST      = 0x0084;

        // ── Raw Input ────────────────────────────────────────────────────────
        public const uint RIDEV_INPUTSINK  = 0x00000100;
        public const uint RID_INPUT        = 0x10000003;
        public const uint RIDI_PREPARSEDDATA = 0x20000005;
        public const uint RIDI_DEVICEINFO  = 0x2000000b;
        public const uint RIM_TYPEHID      = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint   dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint   dwType;
            public uint   dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        // RAWINPUT: header + data union. For HID the data is a variable-length
        // RAWHID record, so we read the full struct into a byte[] and parse manually.
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWHID
        {
            public uint dwSizeHid;   // size of each HID report
            public uint dwCount;     // number of reports in this message
            // followed by dwSizeHid * dwCount bytes of report data
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices,
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize,
            uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize);

        // ── HID ─────────────────────────────────────────────────────────────
        public const ushort HID_USAGE_PAGE_GENERIC   = 0x01;
        public const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;
        public const ushort HID_USAGE_GENERIC_X      = 0x30;
        public const ushort HID_USAGE_GENERIC_Y      = 0x31;
        public const ushort HID_USAGE_DIGITIZER_TOUCHPAD      = 0x05;
        public const ushort HID_USAGE_DIGITIZER_FINGER        = 0x22;
        public const ushort HID_USAGE_DIGITIZER_TIP_SWITCH    = 0x42;
        public const ushort HID_USAGE_DIGITIZER_CONTACT_ID    = 0x51;
        public const ushort HID_USAGE_DIGITIZER_CONTACT_COUNT = 0x54;

        public const int HIDP_STATUS_SUCCESS          = unchecked((int)0x00110000);
        public const int HIDP_STATUS_USAGE_NOT_FOUND  = unchecked((int)0xC0110004);
        public const int HIDP_STATUS_INCOMPATIBLE_REPORT_ID = unchecked((int)0xC0110003);

        public enum HIDP_REPORT_TYPE : int
        {
            HidP_Input   = 0,
            HidP_Output  = 1,
            HidP_Feature = 2
        }

        // HIDP_CAPS — 64 bytes, fully sequential
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        // HIDP_LINK_COLLECTION_NODE — 24 bytes
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_LINK_COLLECTION_NODE
        {
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public ushort Parent;
            public ushort NumberOfChildren;
            public ushort NextSibling;
            public ushort FirstChild;
            // CollectionType (4 bits) | IsAlias (1 bit) | Reserved (27 bits)
            public uint   CollectionTypeAndFlags;
            public IntPtr UserContext;
        }

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(
            IntPtr preparsedData,
            out HIDP_CAPS capabilities);

        [DllImport("hid.dll")]
        public static extern int HidP_GetLinkCollectionNodes(
            [Out] HIDP_LINK_COLLECTION_NODE[] linkCollectionNodes,
            ref uint linkCollectionNodesLength,
            IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsageValue(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            out uint usageValue,
            IntPtr preparsedData,
            IntPtr report,
            uint reportLength);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsages(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            [Out] ushort[] usageList,
            ref uint usageLength,
            IntPtr preparsedData,
            IntPtr report,
            uint reportLength);

        // ── Window management ────────────────────────────────────────────────
        public const uint SWP_NOSIZE        = 0x0001;
        public const uint SWP_NOZORDER      = 0x0004;
        public const uint SWP_NOACTIVATE    = 0x0010;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        public const int GWL_STYLE   = -16;
        public const int GWL_EXSTYLE = -20;
        public const uint WS_MAXIMIZE  = 0x01000000;
        public const uint WS_MINIMIZE  = 0x20000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // ── Display ──────────────────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        // ── System tray ──────────────────────────────────────────────────────
        public const uint NIF_MESSAGE = 0x01;
        public const uint NIF_ICON    = 0x02;
        public const uint NIF_TIP     = 0x04;
        public const uint NIM_ADD     = 0x00;
        public const uint NIM_MODIFY  = 0x01;
        public const uint NIM_DELETE  = 0x02;
        public const int  WM_RBUTTONUP = 0x0205;
        public const int  WM_LBUTTONDBLCLK = 0x0203;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
        public static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);
        public static readonly IntPtr IDI_INFORMATION = new IntPtr(32516);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ── Win32 window creation helpers ────────────────────────────────────
        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint      cbSize;
            public uint      style;
            public WndProcDelegate? lpfnWndProc;
            public int       cbClsExtra;
            public int       cbWndExtra;
            public IntPtr    hInstance;
            public IntPtr    hIcon;
            public IntPtr    hCursor;
            public IntPtr    hbrBackground;
            public string?   lpszMenuName;
            public string    lpszClassName;
            public IntPtr    hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint   message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint   time;
            public POINT  pt;
        }

        // ── Tray menu helpers ────────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool TrackPopupMenu(
            IntPtr hMenu, uint uFlags, int x, int y,
            int nReserved, IntPtr hWnd, IntPtr prcRect);

        public const uint MF_STRING  = 0x00000000;
        public const uint MF_CHECKED = 0x00000008;
        public const uint MF_GRAYED  = 0x00000001;
        public const uint MF_SEPARATOR = 0x00000800;
        public const uint TPM_RIGHTBUTTON = 0x0002;
        public const uint TPM_BOTTOMALIGN = 0x0020;

        public const uint WM_COMMAND = 0x0111;

        // ── Registry auto-start ──────────────────────────────────────────────
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegOpenKeyEx(
            IntPtr hKey, string lpSubKey, uint ulOptions,
            uint samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegSetValueEx(
            IntPtr hKey, string lpValueName, uint Reserved,
            uint dwType, byte[] lpData, uint cbData);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegQueryValueEx(
            IntPtr hKey, string lpValueName, IntPtr lpReserved,
            out uint lpType, byte[]? lpData, ref uint lpcbData);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

        [DllImport("advapi32.dll")]
        public static extern int RegCloseKey(IntPtr hKey);

        public static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        public const uint KEY_READ  = 0x20019;
        public const uint KEY_WRITE = 0x20006;
        public const uint KEY_ALL_ACCESS = 0xF003F;
        public const uint REG_SZ   = 1;
    }
}
