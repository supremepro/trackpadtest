using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TrackpadWindowControl
{
    internal struct TouchContact
    {
        public int   Id;
        public bool  IsTouching;
        public uint  RawX;
        public uint  RawY;
        public uint  LogicalMaxX;
        public uint  LogicalMaxY;
    }

    internal sealed class HidParser
    {
        private readonly object _lock = new();
        private readonly Dictionary<IntPtr, DeviceInfo> _deviceCache = new();

        // Diagnostic log on the Desktop — helps debug unknown touchpad layouts.
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TrackpadDiag.txt");

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
            catch { /* never crash on logging */ }
        }

        static HidParser()
        {
            try { File.WriteAllText(LogPath, $"=== Trackpad Window Control diagnostic ===\n[{DateTime.Now}]\n\n"); }
            catch { }
        }

        private sealed class DeviceInfo
        {
            public IntPtr  PreparsedData;
            public NativeMethods.HIDP_CAPS Caps;
            public ushort[] FingerCollections = Array.Empty<ushort>();
            public uint    LogicalMaxX;
            public uint    LogicalMaxY;
            public bool    Valid;
        }

        // ── Public API ───────────────────────────────────────────────────────

        public List<TouchContact> Parse(IntPtr lParam)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();

            NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT,
                IntPtr.Zero, ref size, headerSize);

            if (size == 0) return [];

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint read = NativeMethods.GetRawInputData(lParam,
                    NativeMethods.RID_INPUT, buffer, ref size, headerSize);

                if (read == uint.MaxValue) return [];

                uint dwType = (uint)Marshal.ReadInt32(buffer, 0);
                if (dwType != NativeMethods.RIM_TYPEHID) return [];

                IntPtr hDevice = Marshal.ReadIntPtr(buffer, 8);

                var dev = GetOrBuildDeviceInfo(hDevice);
                if (dev == null || !dev.Valid) return [];

                uint dwSizeHid = (uint)Marshal.ReadInt32(buffer, (int)headerSize);
                uint dwCount   = (uint)Marshal.ReadInt32(buffer, (int)headerSize + 4);

                if (dwCount == 0 || dwSizeHid == 0) return [];

                IntPtr reportPtr = buffer + (int)headerSize + 8;
                return ParseReport(dev, reportPtr, dwSizeHid);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var dev in _deviceCache.Values)
                    if (dev.PreparsedData != IntPtr.Zero)
                        Marshal.FreeHGlobal(dev.PreparsedData);
                _deviceCache.Clear();
            }
        }

        // ── Device info builder ──────────────────────────────────────────────

        private DeviceInfo? GetOrBuildDeviceInfo(IntPtr hDevice)
        {
            lock (_lock)
            {
                if (_deviceCache.TryGetValue(hDevice, out var cached)) return cached;
                var dev = BuildDeviceInfo(hDevice);
                _deviceCache[hDevice] = dev;
                return dev;
            }
        }

        private static DeviceInfo BuildDeviceInfo(IntPtr hDevice)
        {
            var dev = new DeviceInfo();

            uint size = 0;
            NativeMethods.GetRawInputDeviceInfo(
                hDevice, NativeMethods.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            if (size == 0) { Log($"Device 0x{hDevice:X}: no preparsed data"); return dev; }

            dev.PreparsedData = Marshal.AllocHGlobal((int)size);
            NativeMethods.GetRawInputDeviceInfo(
                hDevice, NativeMethods.RIDI_PREPARSEDDATA, dev.PreparsedData, ref size);

            int status = NativeMethods.HidP_GetCaps(dev.PreparsedData, out dev.Caps);
            if (status != NativeMethods.HIDP_STATUS_SUCCESS)
            {
                Log($"Device 0x{hDevice:X}: HidP_GetCaps failed 0x{status:X}");
                return dev;
            }

            Log($"Device 0x{hDevice:X}: UsagePage=0x{dev.Caps.UsagePage:X} Usage=0x{dev.Caps.Usage:X} " +
                $"ReportLen={dev.Caps.InputReportByteLength} LinkCols={dev.Caps.NumberLinkCollectionNodes}");

            // Accept any HID digitizer device (page 0x0D).
            // Covers: Touchpad (0x05), Touch Screen (0x04), and vendor variants.
            if (dev.Caps.UsagePage != NativeMethods.HID_USAGE_PAGE_DIGITIZER)
            {
                Log($"  → Skipped (not a digitizer)");
                return dev;
            }

            uint nodeCount = dev.Caps.NumberLinkCollectionNodes;
            if (nodeCount == 0) { Log("  → No link collections"); return dev; }

            var nodes = new NativeMethods.HIDP_LINK_COLLECTION_NODE[nodeCount];
            status = NativeMethods.HidP_GetLinkCollectionNodes(nodes, ref nodeCount, dev.PreparsedData);
            if (status != NativeMethods.HIDP_STATUS_SUCCESS)
            {
                Log($"  → GetLinkCollectionNodes failed 0x{status:X}");
                return dev;
            }

            // Log all collections so we can see the device layout.
            for (int i = 0; i < nodeCount; i++)
                Log($"  Col[{i}]: page=0x{nodes[i].LinkUsagePage:X} usage=0x{nodes[i].LinkUsage:X}");

            // Pass 1: look for collections explicitly tagged as Finger (page 0x0D, usage 0x22).
            var fingerCols = new List<ushort>();
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodes[i].LinkUsagePage == NativeMethods.HID_USAGE_PAGE_DIGITIZER &&
                    nodes[i].LinkUsage     == NativeMethods.HID_USAGE_DIGITIZER_FINGER)
                    fingerCols.Add((ushort)i);
            }

            // Pass 2: if none tagged as Finger, fall back to ALL non-root collections.
            // Some ELAN / Synaptics drivers use different link usages or none at all.
            if (fingerCols.Count == 0)
            {
                Log("  → No Finger (0x22) collections found, falling back to all non-root collections");
                for (ushort i = 1; i < nodeCount; i++)
                    fingerCols.Add(i);
            }

            Log($"  → Using {fingerCols.Count} finger collection(s): [{string.Join(",", fingerCols)}]");

            dev.FingerCollections = fingerCols.ToArray();

            // Probe X/Y logical ranges from ANY collection that has them.
            ProbeAxisRange(dev.PreparsedData, fingerCols[0],
                out dev.LogicalMaxX, out dev.LogicalMaxY);

            // If per-collection probe found nothing, scan all collections.
            if (dev.LogicalMaxX == 0 || dev.LogicalMaxY == 0)
                ProbeAxisRangeFallback(dev.PreparsedData,
                    out dev.LogicalMaxX, out dev.LogicalMaxY);

            if (dev.LogicalMaxX == 0) dev.LogicalMaxX = 4095;
            if (dev.LogicalMaxY == 0) dev.LogicalMaxY = 4095;

            Log($"  → LogicalMaxX={dev.LogicalMaxX} LogicalMaxY={dev.LogicalMaxY}");

            dev.Valid = true;
            return dev;
        }

        // ── Axis range probing ────────────────────────────────────────────────

        // HIDP_VALUE_CAPS field offsets (72-byte struct):
        //   0  UsagePage  2  ReportID  3  IsAlias  4  BitField  6  LinkCollection
        //  12  IsRange   40  LogicalMin  44  LogicalMax  56  Usage/UsageMin
        private const int CAPS_SIZE = 72;

        private static void ProbeAxisRange(IntPtr preparsedData, ushort linkCollection,
            out uint maxX, out uint maxY)
        {
            maxX = 0; maxY = 0;
            ushort numCaps = 64;
            IntPtr buf = Marshal.AllocHGlobal(numCaps * CAPS_SIZE);
            try
            {
                if (HidP_GetValueCapsRaw(NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    buf, ref numCaps, preparsedData) != NativeMethods.HIDP_STATUS_SUCCESS)
                    return;

                for (int i = 0; i < numCaps; i++)
                {
                    IntPtr cap   = buf + i * CAPS_SIZE;
                    ushort page  = (ushort)Marshal.ReadInt16(cap, 0);
                    ushort col   = (ushort)Marshal.ReadInt16(cap, 6);
                    int    lgMax = Marshal.ReadInt32(cap, 44);
                    ushort usage = (ushort)Marshal.ReadInt16(cap, 56);

                    if (col != linkCollection) continue;
                    if (lgMax <= 0) continue;
                    if (page != NativeMethods.HID_USAGE_PAGE_GENERIC) continue;

                    if (usage == NativeMethods.HID_USAGE_GENERIC_X && maxX == 0) maxX = (uint)lgMax;
                    if (usage == NativeMethods.HID_USAGE_GENERIC_Y && maxY == 0) maxY = (uint)lgMax;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // Fallback: scan ALL value caps ignoring link collection.
        private static void ProbeAxisRangeFallback(IntPtr preparsedData,
            out uint maxX, out uint maxY)
        {
            maxX = 0; maxY = 0;
            ushort numCaps = 64;
            IntPtr buf = Marshal.AllocHGlobal(numCaps * CAPS_SIZE);
            try
            {
                if (HidP_GetValueCapsRaw(NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    buf, ref numCaps, preparsedData) != NativeMethods.HIDP_STATUS_SUCCESS)
                    return;

                for (int i = 0; i < numCaps; i++)
                {
                    IntPtr cap   = buf + i * CAPS_SIZE;
                    ushort page  = (ushort)Marshal.ReadInt16(cap, 0);
                    int    lgMax = Marshal.ReadInt32(cap, 44);
                    ushort usage = (ushort)Marshal.ReadInt16(cap, 56);

                    if (lgMax <= 0) continue;
                    if (page != NativeMethods.HID_USAGE_PAGE_GENERIC) continue;

                    if (usage == NativeMethods.HID_USAGE_GENERIC_X && maxX == 0) maxX = (uint)lgMax;
                    if (usage == NativeMethods.HID_USAGE_GENERIC_Y && maxY == 0) maxY = (uint)lgMax;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        [DllImport("hid.dll", EntryPoint = "HidP_GetValueCaps")]
        private static extern int HidP_GetValueCapsRaw(
            NativeMethods.HIDP_REPORT_TYPE reportType,
            IntPtr ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        // ── Report parsing ────────────────────────────────────────────────────

        private static int _reportLogCount = 0;

        private static List<TouchContact> ParseReport(
            DeviceInfo dev, IntPtr reportPtr, uint reportLen)
        {
            var contacts = new List<TouchContact>(dev.FingerCollections.Length);

            foreach (ushort col in dev.FingerCollections)
            {
                // Try tip switch as a button first.
                bool isTouching = false;
                uint usageLen = 16;
                var  usages   = new ushort[usageLen];
                int  st = NativeMethods.HidP_GetUsages(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_DIGITIZER, col,
                    usages, ref usageLen,
                    dev.PreparsedData, reportPtr, reportLen);

                if (st == NativeMethods.HIDP_STATUS_SUCCESS)
                    for (uint u = 0; u < usageLen; u++)
                        if (usages[u] == NativeMethods.HID_USAGE_DIGITIZER_TIP_SWITCH)
                        { isTouching = true; break; }

                // Fallback: tip switch as a value.
                if (!isTouching)
                {
                    st = NativeMethods.HidP_GetUsageValue(
                        NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                        NativeMethods.HID_USAGE_PAGE_DIGITIZER, col,
                        NativeMethods.HID_USAGE_DIGITIZER_TIP_SWITCH,
                        out uint tipVal, dev.PreparsedData, reportPtr, reportLen);
                    if (st == NativeMethods.HIDP_STATUS_SUCCESS && tipVal != 0)
                        isTouching = true;
                }

                // Contact ID — use collection index as fallback if not present.
                NativeMethods.HidP_GetUsageValue(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_DIGITIZER, col,
                    NativeMethods.HID_USAGE_DIGITIZER_CONTACT_ID,
                    out uint contactId, dev.PreparsedData, reportPtr, reportLen);
                if (contactId == 0) contactId = col;

                // X and Y — skip this collection if both are zero and not touching.
                NativeMethods.HidP_GetUsageValue(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_GENERIC, col,
                    NativeMethods.HID_USAGE_GENERIC_X,
                    out uint rawX, dev.PreparsedData, reportPtr, reportLen);

                NativeMethods.HidP_GetUsageValue(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_GENERIC, col,
                    NativeMethods.HID_USAGE_GENERIC_Y,
                    out uint rawY, dev.PreparsedData, reportPtr, reportLen);

                if (rawX == 0 && rawY == 0 && !isTouching) continue;

                contacts.Add(new TouchContact
                {
                    Id          = (int)contactId,
                    IsTouching  = isTouching,
                    RawX        = rawX,
                    RawY        = rawY,
                    LogicalMaxX = dev.LogicalMaxX,
                    LogicalMaxY = dev.LogicalMaxY,
                });
            }

            // Log the first 20 reports so we can verify data is flowing.
            if (_reportLogCount < 20 && contacts.Count > 0)
            {
                _reportLogCount++;
                Log($"Report #{_reportLogCount}: {contacts.Count} contact(s) — " +
                    string.Join(" | ", contacts.ConvertAll(c =>
                        $"id={c.Id} touch={c.IsTouching} x={c.RawX} y={c.RawY}")));
            }

            return contacts;
        }
    }
}
