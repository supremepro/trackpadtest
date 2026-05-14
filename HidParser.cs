using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TrackpadWindowControl
{
    /// <summary>
    /// Parsed data for a single finger contact from a touchpad HID report.
    /// </summary>
    internal struct TouchContact
    {
        public int   Id;
        public bool  IsTouching;
        public uint  RawX;
        public uint  RawY;
        public uint  LogicalMaxX;
        public uint  LogicalMaxY;
    }

    /// <summary>
    /// Caches per-device HID metadata and parses raw input reports into
    /// TouchContact lists. Thread-safe for concurrent device handles.
    /// </summary>
    internal sealed class HidParser
    {
        private readonly object _lock = new();

        // Per-device cached metadata, keyed on the device handle value.
        private readonly Dictionary<IntPtr, DeviceInfo> _deviceCache = new();

        private sealed class DeviceInfo
        {
            public IntPtr PreparsedData;   // HeapAlloc'd — freed in Dispose
            public NativeMethods.HIDP_CAPS Caps;
            public ushort[] FingerCollections = Array.Empty<ushort>();
            public uint LogicalMaxX;
            public uint LogicalMaxY;
            public bool Valid;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Parses a raw WM_INPUT lParam and returns active touch contacts.
        /// Returns empty list on any error or unsupported device.
        /// </summary>
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

                // Read header fields manually to avoid struct alignment issues.
                uint dwType = (uint)Marshal.ReadInt32(buffer, 0);
                if (dwType != NativeMethods.RIM_TYPEHID) return [];

                IntPtr hDevice = Marshal.ReadIntPtr(buffer, 8); // header offset 8

                var dev = GetOrBuildDeviceInfo(hDevice);
                if (dev == null || !dev.Valid) return [];

                // RAWHID is right after the header (headerSize bytes in).
                uint dwSizeHid = (uint)Marshal.ReadInt32(buffer, (int)headerSize);
                uint dwCount   = (uint)Marshal.ReadInt32(buffer, (int)headerSize + 4);

                if (dwCount == 0 || dwSizeHid == 0) return [];

                // First HID report immediately after RAWHID struct (8 bytes).
                IntPtr reportPtr = buffer + (int)headerSize + 8;
                return ParseReport(dev, reportPtr, dwSizeHid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
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

        // ── Private helpers ──────────────────────────────────────────────────

        private DeviceInfo? GetOrBuildDeviceInfo(IntPtr hDevice)
        {
            lock (_lock)
            {
                if (_deviceCache.TryGetValue(hDevice, out var cached))
                    return cached;

                var dev = BuildDeviceInfo(hDevice);
                _deviceCache[hDevice] = dev;
                return dev;
            }
        }

        private static DeviceInfo BuildDeviceInfo(IntPtr hDevice)
        {
            var dev = new DeviceInfo();

            // Get preparsed data from the raw input device handle.
            uint size = 0;
            NativeMethods.GetRawInputDeviceInfo(
                hDevice, NativeMethods.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);

            if (size == 0) return dev;

            dev.PreparsedData = Marshal.AllocHGlobal((int)size);
            NativeMethods.GetRawInputDeviceInfo(
                hDevice, NativeMethods.RIDI_PREPARSEDDATA, dev.PreparsedData, ref size);

            // Get capabilities.
            int status = NativeMethods.HidP_GetCaps(dev.PreparsedData, out dev.Caps);
            if (status != NativeMethods.HIDP_STATUS_SUCCESS) return dev;

            // Must be a digitizer touchpad.
            if (dev.Caps.UsagePage != NativeMethods.HID_USAGE_PAGE_DIGITIZER ||
                dev.Caps.Usage     != NativeMethods.HID_USAGE_DIGITIZER_TOUCHPAD)
                return dev;

            // Enumerate link collection nodes to find finger collections.
            uint nodeCount = dev.Caps.NumberLinkCollectionNodes;
            if (nodeCount == 0) return dev;

            var nodes = new NativeMethods.HIDP_LINK_COLLECTION_NODE[nodeCount];
            status = NativeMethods.HidP_GetLinkCollectionNodes(
                nodes, ref nodeCount, dev.PreparsedData);

            if (status != NativeMethods.HIDP_STATUS_SUCCESS) return dev;

            var fingerCols = new List<ushort>();
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodes[i].LinkUsagePage == NativeMethods.HID_USAGE_PAGE_DIGITIZER &&
                    nodes[i].LinkUsage     == NativeMethods.HID_USAGE_DIGITIZER_FINGER)
                {
                    fingerCols.Add((ushort)i);
                }
            }

            dev.FingerCollections = fingerCols.ToArray();

            // Probe logical range for X/Y using the first finger collection.
            if (fingerCols.Count > 0)
            {
                ProbeAxisRange(dev.PreparsedData, fingerCols[0],
                    out dev.LogicalMaxX, out dev.LogicalMaxY);
            }

            // If we couldn't auto-detect ranges, use a safe default.
            if (dev.LogicalMaxX == 0) dev.LogicalMaxX = 4095;
            if (dev.LogicalMaxY == 0) dev.LogicalMaxY = 4095;

            dev.Valid = fingerCols.Count > 0;
            return dev;
        }

        // Retrieve logical max for X and Y by reading value caps byte-by-byte.
        // Offset table for HIDP_VALUE_CAPS (72 bytes):
        //   0  UsagePage, 6  LinkCollection, 12 IsRange,
        //  40  LogicalMin, 44 LogicalMax, 56  NotRange.Usage / Range.UsageMin
        private static void ProbeAxisRange(IntPtr preparsedData, ushort linkCollection,
            out uint maxX, out uint maxY)
        {
            maxX = 0; maxY = 0;
            const int CAPS_SIZE = 72;

            foreach (ushort usagePage in new[] {
                NativeMethods.HID_USAGE_PAGE_GENERIC,
                NativeMethods.HID_USAGE_PAGE_DIGITIZER })
            {
                // Allocate buffer for value caps.
                ushort numCaps = 64;
                IntPtr buf = Marshal.AllocHGlobal(numCaps * CAPS_SIZE);
                try
                {
                    // HidP_GetValueCaps signature:
                    // (reportType, ValueCaps, ValueCapsLength, PreparsedData)
                    int st = HidP_GetValueCapsRaw(
                        NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                        buf, ref numCaps, preparsedData);

                    if (st != NativeMethods.HIDP_STATUS_SUCCESS) continue;

                    for (int i = 0; i < numCaps; i++)
                    {
                        IntPtr cap = buf + i * CAPS_SIZE;
                        ushort capPage  = (ushort)Marshal.ReadInt16(cap, 0);
                        ushort capCol   = (ushort)Marshal.ReadInt16(cap, 6);
                        bool   isRange  = Marshal.ReadByte(cap, 12) != 0;
                        int    logMax   = Marshal.ReadInt32(cap, 44);

                        ushort usage = isRange
                            ? (ushort)Marshal.ReadInt16(cap, 56)  // Range.UsageMin
                            : (ushort)Marshal.ReadInt16(cap, 56); // NotRange.Usage (same offset)

                        if (capCol != linkCollection && capCol != 0) continue;
                        if (logMax <= 0) continue;

                        if (capPage == NativeMethods.HID_USAGE_PAGE_GENERIC)
                        {
                            if (usage == NativeMethods.HID_USAGE_GENERIC_X && maxX == 0)
                                maxX = (uint)logMax;
                            if (usage == NativeMethods.HID_USAGE_GENERIC_Y && maxY == 0)
                                maxY = (uint)logMax;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        [DllImport("hid.dll", EntryPoint = "HidP_GetValueCaps")]
        private static extern int HidP_GetValueCapsRaw(
            NativeMethods.HIDP_REPORT_TYPE reportType,
            IntPtr ValueCaps,
            ref ushort ValueCapsLength,
            IntPtr PreparsedData);

        private static List<TouchContact> ParseReport(
            DeviceInfo dev, IntPtr reportPtr, uint reportLen)
        {
            var contacts = new List<TouchContact>(dev.FingerCollections.Length);

            foreach (ushort col in dev.FingerCollections)
            {
                // Tip switch is a button — use HidP_GetUsages.
                uint usageLen = 8;
                var usages = new ushort[usageLen];
                int st = NativeMethods.HidP_GetUsages(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_DIGITIZER,
                    col,
                    usages,
                    ref usageLen,
                    dev.PreparsedData,
                    reportPtr,
                    reportLen);

                bool isTouching = false;
                if (st == NativeMethods.HIDP_STATUS_SUCCESS)
                {
                    for (uint u = 0; u < usageLen; u++)
                        if (usages[u] == NativeMethods.HID_USAGE_DIGITIZER_TIP_SWITCH)
                        {
                            isTouching = true;
                            break;
                        }
                }

                // Also try tip switch as a value (some devices report it that way).
                if (!isTouching)
                {
                    st = NativeMethods.HidP_GetUsageValue(
                        NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                        NativeMethods.HID_USAGE_PAGE_DIGITIZER,
                        col,
                        NativeMethods.HID_USAGE_DIGITIZER_TIP_SWITCH,
                        out uint tipVal,
                        dev.PreparsedData, reportPtr, reportLen);

                    if (st == NativeMethods.HIDP_STATUS_SUCCESS && tipVal != 0)
                        isTouching = true;
                }

                st = NativeMethods.HidP_GetUsageValue(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_DIGITIZER,
                    col,
                    NativeMethods.HID_USAGE_DIGITIZER_CONTACT_ID,
                    out uint contactId,
                    dev.PreparsedData, reportPtr, reportLen);

                if (st != NativeMethods.HIDP_STATUS_SUCCESS &&
                    st != NativeMethods.HIDP_STATUS_INCOMPATIBLE_REPORT_ID)
                    continue;  // Not a valid finger slot in this report.

                NativeMethods.HidP_GetUsageValue(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_GENERIC,
                    col,
                    NativeMethods.HID_USAGE_GENERIC_X,
                    out uint rawX,
                    dev.PreparsedData, reportPtr, reportLen);

                NativeMethods.HidP_GetUsageValue(
                    NativeMethods.HIDP_REPORT_TYPE.HidP_Input,
                    NativeMethods.HID_USAGE_PAGE_GENERIC,
                    col,
                    NativeMethods.HID_USAGE_GENERIC_Y,
                    out uint rawY,
                    dev.PreparsedData, reportPtr, reportLen);

                contacts.Add(new TouchContact
                {
                    Id         = (int)contactId,
                    IsTouching = isTouching,
                    RawX       = rawX,
                    RawY       = rawY,
                    LogicalMaxX = dev.LogicalMaxX,
                    LogicalMaxY = dev.LogicalMaxY,
                });
            }

            return contacts;
        }
    }
}
