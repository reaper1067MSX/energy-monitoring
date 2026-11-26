using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Hid;

/// <summary>USB HID descriptor metadata discovered via SetupDi and hid.dll.</summary>
public sealed record HidDeviceDescriptor(string DevicePath, ushort VendorId, ushort ProductId, ushort VersionNumber, string Manufacturer, string Product, string SerialNumber, ushort InputReportByteLength, ushort OutputReportByteLength);

/// <summary>Forensic read-only capture output for a single UPS HID inquiry command.</summary>
public sealed record UpsInspectionReport(HidDeviceDescriptor Device, string Command, IReadOnlyList<byte[]> RawReports, byte[] RawPayload, byte[] DeescapedPayload, string RawHex, string AsciiPreview, IReadOnlyList<string> TokenHexBreakdown, UpsStatusByte2Ratings? StatusByte2Ratings, double? FrequencyHz, string SafeCaptureNotice);

/// <summary>Read-only diagnostic inspector for dynamic SetupDi HID discovery and 0x00-preserving frame capture.</summary>
public static class UpsHidInspector
{
    public const string SafeCaptureGuidance = "Safe capture (--stream): wall-powered PC, uninterrupted PE, upstream Line (L) cut, 25-60W resistive load, <=10s outage window.";
    public static readonly IReadOnlySet<string> AllowedInquiryCommands = new HashSet<string>(StringComparer.Ordinal) { "M\r", "QS\r", "Q1\r", "QPI\r", "QID\r", "QRI\r", "QMD\r", "QVFW\r", "QBV\r", "QGS\r", "QMOD\r", "QFLAG\r", "F\r", "I\r" };
    [StructLayout(LayoutKind.Sequential)] private struct SpDevIntData { public int cbSize; public Guid Guid; public int Flags; public IntPtr Res; }
    [StructLayout(LayoutKind.Sequential)] private struct HiddAttrs { public int Size; public ushort Vid, Pid, Ver; }
    [StructLayout(LayoutKind.Sequential)] private struct HidpCaps { public ushort Usage, UsagePage, InLen, OutLen, FeatLen; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Res; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HiddAttrs a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] private static extern uint HidP_GetCaps(IntPtr p, out HidpCaps c);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetManufacturerString(SafeFileHandle h, IntPtr b, int l);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(SafeFileHandle h, IntPtr b, int l);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle h, IntPtr b, int l);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid c, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr i, ref Guid c, uint idx, ref SpDevIntData d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref SpDevIntData d, IntPtr det, int sz, out int req, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

    /// <summary>Returns true when the command is in the read-only inquiry allowlist.</summary>
    public static bool IsAllowedInquiryCommand(string? command) => !string.IsNullOrEmpty(command) && AllowedInquiryCommands.Contains(command);

    /// <summary>Validates a command against the read-only inquiry allowlist before any USB write.</summary>
    public static string EnsureAllowedInquiryCommand(string? command) =>
        IsAllowedInquiryCommand(command) ? command! : throw new InvalidOperationException($"Command '{command?.Replace("\r", "\\r")}' blocked by read-only allowlist.");

    /// <summary>Assembles 9-byte HID input reports into a raw payload while preserving every interior 0x00 byte up to 0x0D.</summary>
    public static byte[] AssemblePayloadPreservingNulls(IEnumerable<byte[]> nineByteReports, bool requireLeadingHeader = false)
    {
        List<byte> payload = new();
        foreach (byte[] r in nineByteReports)
            for (int b = 1; b < r.Length; b++)
            {
                byte v = r[b];
                if (payload.Count == 0 && ((requireLeadingHeader && v != VoltronicProtocolConstants.FrameHeaderHash && v != VoltronicProtocolConstants.FrameHeaderParen) || v == VoltronicProtocolConstants.NullByte)) continue;
                payload.Add(v);
                if (v == VoltronicProtocolConstants.CarriageReturn) return payload.ToArray();
            }
        return payload.ToArray();
    }

    /// <summary>Discovers attached HID interfaces dynamically via SetupDiGetClassDevsW and hid.dll.</summary>
    public static IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHid = false)
    {
        List<HidDeviceDescriptor> devices = new();
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr devInfo = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return devices;
        try
        {
            var ifData = new SpDevIntData { cbSize = Marshal.SizeOf<SpDevIntData>() };
            for (uint idx = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, idx, ref ifData); idx++)
            {
                SetupDiGetDeviceInterfaceDetailW(devInfo, ref ifData, IntPtr.Zero, 0, out int req, IntPtr.Zero);
                if (req <= 4) continue;
                IntPtr det = Marshal.AllocHGlobal(req);
                try { Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6); if (SetupDiGetDeviceInterfaceDetailW(devInfo, ref ifData, det, req, out _, IntPtr.Zero) && Marshal.PtrToStringUni(IntPtr.Add(det, 4)) is { Length: > 0 } path && TryReadDescriptor(path) is { } d && (includeAllHid || (d.VendorId == HidConstants.VendorId && d.ProductId == HidConstants.ProductId))) devices.Add(d); }
                finally { Marshal.FreeHGlobal(det); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devInfo); }
        return devices;
    }

    private static HidDeviceDescriptor? TryReadDescriptor(string path)
    {
        using var h = Win32HidUpsPort.OpenOverlapped(path, desiredAccess: 0, flags: 0);
        var attrs = new HiddAttrs { Size = Marshal.SizeOf<HiddAttrs>() };
        if (h.IsInvalid || !HidD_GetAttributes(h, ref attrs)) return null;
        ushort inLen = 0, outLen = 0;
        if (HidD_GetPreparsedData(h, out IntPtr pp)) try { if (HidP_GetCaps(pp, out var c) == 0x00110000) { inLen = c.InLen; outLen = c.OutLen; } } finally { HidD_FreePreparsedData(pp); }
        IntPtr buf = Marshal.AllocHGlobal(256);
        try { string Str(Func<SafeFileHandle, IntPtr, int, bool> fn) { Marshal.WriteInt16(buf, 0); return fn(h, buf, 256) ? (Marshal.PtrToStringUni(buf)?.Trim() ?? string.Empty) : string.Empty; } return new(path, attrs.Vid, attrs.Pid, attrs.Ver, Str(HidD_GetManufacturerString), Str(HidD_GetProductString), Str(HidD_GetSerialNumberString), inLen, outLen); }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Builds a forensic inspection report from captured raw reports and assembled payload.</summary>
    public static UpsInspectionReport BuildReport(HidDeviceDescriptor device, string command, IReadOnlyList<byte[]> rawReports, byte[] rawPayload)
    {
        List<byte[]> tokens = new();
        if (rawPayload.Length > 0 && rawPayload[0] == VoltronicProtocolConstants.FrameHeaderHash)
        {
            List<byte> cur = new();
            for (int i = 1; i < rawPayload.Length && rawPayload[i] != VoltronicProtocolConstants.CarriageReturn; i++)
            {
                if (rawPayload[i] == VoltronicProtocolConstants.SpaceDelimiter) { if (cur.Count > 0) { tokens.Add(VoltronicParser.Deescape(cur.ToArray())); cur.Clear(); } }
                else { cur.Add(rawPayload[i]); if (rawPayload[i] == VoltronicProtocolConstants.FrameHeaderParen && i + 1 < rawPayload.Length) cur.Add(rawPayload[++i]); }
            }
            if (cur.Count > 0) tokens.Add(VoltronicParser.Deescape(cur.ToArray()));
        }
        return new UpsInspectionReport(device, command, rawReports, rawPayload, VoltronicParser.Deescape(rawPayload),
            BitConverter.ToString(rawPayload).Replace('-', ' '), new(rawPayload.Select(b => b is >= 0x20 and <= 0x7E ? (char)b : '.').ToArray()),
            tokens.Select((t, idx) => $"T{idx}: {BitConverter.ToString(t).Replace('-', ' ')}").ToArray(),
            tokens.Count >= 11 && tokens[10].Length > 0 ? VoltronicParser.DecodeStatusByte2Ratings(tokens[10][0]) : null,
            tokens.Count >= 7 ? VoltronicParser.DecodeFrequencyHz(tokens[5], tokens[6]) : null, SafeCaptureGuidance);
    }

    /// <summary>Executes one allowlisted read-only inquiry against an open HID handle.</summary>
    public static UpsInspectionReport? QueryReadOnly(SafeFileHandle handle, HidDeviceDescriptor device, string command)
    {
        EnsureAllowedInquiryCommand(command);
        byte[] drain = new byte[HidConstants.ReportLength];
        while (Win32HidUpsPort.ReadOverlapped(handle, drain, HidConstants.DefaultDrainTimeoutMs) > 0) { }
        if (!Win32HidUpsPort.WriteCommandChunksInternal(handle, command)) return null;
        List<byte[]> reports = new();
        var sw = Stopwatch.StartNew();
        byte[] assembled = Array.Empty<byte>();
        while (sw.ElapsedMilliseconds < HidConstants.DefaultQueryTimeoutMs)
        {
            byte[] buf = new byte[HidConstants.ReportLength];
            if (Win32HidUpsPort.ReadOverlapped(handle, buf, HidConstants.DefaultReadTimeoutMs) <= 0) continue;
            reports.Add(buf);
            assembled = AssemblePayloadPreservingNulls(reports);
            if (assembled.Length > 0 && assembled[^1] == VoltronicProtocolConstants.CarriageReturn) break;
        }
        return reports.Count == 0 ? null : BuildReport(device, command, reports, assembled);
    }

    /// <summary>Discovers the first matching UPS HID device and executes one allowlisted read-only inquiry.</summary>
    public static UpsInspectionReport? InspectFirstMatchingDevice(string command = "QS\r", bool includeAllHid = false)
    {
        EnsureAllowedInquiryCommand(command);
        if (DiscoverDevices(includeAllHid).FirstOrDefault(d => d.VendorId == HidConstants.VendorId && d.ProductId == HidConstants.ProductId) is not { } target) return null;
        using var handle = Win32HidUpsPort.OpenOverlapped(target.DevicePath);
        return handle.IsInvalid ? null : QueryReadOnly(handle, target, command);
    }

    /// <summary>Serializes an inspection report as indented JSON preserving hex null bytes and decoded ratings.</summary>
    public static string SerializeCaptureJson(UpsInspectionReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Executes the read-only `--inspect` CLI flow (`--all-hid`, `--export`, `--cmd`).</summary>
    public static int RunCli(string[] args, TextWriter output)
    {
        bool allHid = args.Any(a => a.Equals("--all-hid", StringComparison.OrdinalIgnoreCase));
        string cmd = "QS\r"; string? exportPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--export", StringComparison.OrdinalIgnoreCase)) { if (i + 1 >= args.Length) { output.WriteLine("Missing value for --export."); return 1; } exportPath = args[++i]; }
            else if (args[i].Equals("--cmd", StringComparison.OrdinalIgnoreCase)) { if (i + 1 >= args.Length) { output.WriteLine("Missing value for --cmd."); return 1; } cmd = args[++i]; if (!cmd.EndsWith('\r')) cmd += "\r"; }
        }
        try { EnsureAllowedInquiryCommand(cmd); } catch (InvalidOperationException ex) { output.WriteLine(ex.Message); return 1; }
        output.WriteLine(SafeCaptureGuidance);
        foreach (var d in DiscoverDevices(allHid)) output.WriteLine($"VID_{d.VendorId:X4}&PID_{d.ProductId:X4} v{d.VersionNumber:X4} | {d.Manufacturer} {d.Product} | Serial: {(string.IsNullOrEmpty(d.SerialNumber) ? "(none)" : d.SerialNumber)} | In:{d.InputReportByteLength} Out:{d.OutputReportByteLength}");
        if (InspectFirstMatchingDevice(cmd, allHid) is not { } report) { output.WriteLine("No matching UPS HID device/response."); return 1; }
        for (int i = 0; i < report.RawReports.Count; i++) output.WriteLine($"R#{i}: {BitConverter.ToString(report.RawReports[i]).Replace('-', ' ')}");
        output.WriteLine($"HEX: {report.RawHex}\nDeescaped: {BitConverter.ToString(report.DeescapedPayload).Replace('-', ' ')}\nASCII: {report.AsciiPreview}\n{string.Join('\n', report.TokenHexBreakdown)}");
        if (report.FrequencyHz is { } hz) output.WriteLine($"Frequency: {hz:F2} Hz");
        if (report.StatusByte2Ratings is { } r) output.WriteLine($"Status2: {r.NominalFrequencyHz} Hz, {r.NominalBatteryVoltageDc} V ({r.BatteryBankSeriesCount}x12V), {r.NominalOutputVoltageAc?.ToString() ?? "Unknown"} V");
        if (!string.IsNullOrEmpty(exportPath)) File.WriteAllText(exportPath, SerializeCaptureJson(report), Encoding.UTF8);
        return 0;
    }
}
