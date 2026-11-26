using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;
using System.Text;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;

namespace VoltTray.Tests;

public class ProtocolNegotiatorTests
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Negotiate_ProtocolIdentityT_SelectsVoltronicQsHexDialectT_AndDecodesStatusByte2AndPowerStatus()
    {
        var negotiator = new ProtocolNegotiator();
        NegotiatedUpsProtocol negotiated = negotiator.Negotiate(command => command switch
        {
            VoltronicProtocolConstants.ProtocolIdentityCommand => "T\r"u8.ToArray(),
            _ => null
        });

        Assert.Equal("T", negotiated.Strategy.DialectId);
        Assert.Equal(VoltronicProtocolConstants.QueryStatusCommand, negotiated.Strategy.StatusQueryCommand);
        Assert.True(negotiated.Capabilities.DialectVerified);

        UpsTelemetry? telemetry = negotiated.Strategy.Parse(
            VoltronicFrameMother.Nt2401ProLiveFrame,
            negotiated.Capabilities,
            previous: null,
            now: FixedTimestamp);

        Assert.NotNull(telemetry);
        Assert.Equal(59.97, telemetry.FrequencyHz);
        Assert.Equal(24.0, telemetry.BankSpec.NominalVoltageDc);
        Assert.Equal(2, telemetry.BankSpec.SeriesBatteryCount);
        Assert.Null(telemetry.TemperatureCelsius);
        Assert.NotNull(telemetry.PowerStatus);
        Assert.False(telemetry.PowerStatus.Value.IsOnBattery);
        Assert.True(telemetry.PowerStatus.Value.IsBeeperActive);
    }

    [Fact]
    public void Negotiate_ProtocolIdentityP_SelectsVoltronicQsHexDialectP_AndParsesTenTokenFrame()
    {
        byte[] tenTokenFrame =
        [
            0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20,
            0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20,
            0x08, 0x0D
        ];

        var negotiator = new ProtocolNegotiator();
        NegotiatedUpsProtocol negotiated = negotiator.Negotiate(command => command switch
        {
            VoltronicProtocolConstants.ProtocolIdentityCommand => "P\r"u8.ToArray(),
            _ => null
        });

        Assert.Equal("P", negotiated.Strategy.DialectId);
        Assert.Equal(VoltronicProtocolConstants.QueryStatusCommand, negotiated.Strategy.StatusQueryCommand);
        Assert.True(negotiated.Capabilities.DialectVerified);

        UpsTelemetry? telemetry = negotiated.Strategy.Parse(
            tenTokenFrame,
            negotiated.Capabilities,
            previous: null,
            now: FixedTimestamp);

        Assert.NotNull(telemetry);
        Assert.Equal(UpsOperationalState.LineNormal, telemetry.State);
        Assert.Null(telemetry.TemperatureCelsius);
    }

    [Fact]
    public void Negotiate_ProtocolIdentityV_SelectsMegatecAsciiDialectV_AndProbesRatingsAndVendor()
    {
        var queriedCommands = new List<string>();
        var negotiator = new ProtocolNegotiator();

        NegotiatedUpsProtocol negotiated = negotiator.Negotiate(command =>
        {
            queriedCommands.Add(command);
            return command switch
            {
                VoltronicProtocolConstants.ProtocolIdentityCommand => "V\r"u8.ToArray(),
                VoltronicProtocolConstants.NominalRatingsCommand => "#220.0 004 24.00 50.0\r"u8.ToArray(),
                VoltronicProtocolConstants.VendorInformationCommand => "#FORZA   FX2200   V1.2\r"u8.ToArray(),
                _ => null
            };
        });

        Assert.Equal("V", negotiated.Strategy.DialectId);
        Assert.Equal(VoltronicProtocolConstants.QueryStatusCommand, negotiated.Strategy.StatusQueryCommand);
        Assert.True(negotiated.Capabilities.DialectVerified);
        Assert.Equal(220.0, negotiated.Capabilities.NominalVoltageAc);
        Assert.Equal(50.0, negotiated.Capabilities.NominalFrequencyHz);
        Assert.Equal(24.0, negotiated.Capabilities.Bank.NominalVoltageDc);
        Assert.Equal(2, negotiated.Capabilities.Bank.SeriesBatteryCount);
        Assert.Equal("FORZA FX2200 V1.2", negotiated.Capabilities.ModelName);
        Assert.Contains(VoltronicProtocolConstants.NominalRatingsCommand, queriedCommands);
        Assert.Contains(VoltronicProtocolConstants.VendorInformationCommand, queriedCommands);

        byte[] megatecFrame = Encoding.ASCII.GetBytes("(220.5 220.0 220.5 025 50.0 27.20 26.5 00001001\r");
        UpsTelemetry? telemetry = negotiated.Strategy.Parse(
            megatecFrame,
            negotiated.Capabilities,
            previous: null,
            now: FixedTimestamp);

        Assert.NotNull(telemetry);
        Assert.Equal(220.5, telemetry.InputVoltageAc);
        Assert.Equal(220.0, telemetry.FaultReferenceVoltageAc);
        Assert.Equal(220.5, telemetry.OutputVoltageAc);
        Assert.Equal(25, telemetry.LoadPercentage);
        Assert.Equal(50.0, telemetry.FrequencyHz);
        Assert.Equal(27.20, telemetry.BatteryVoltageDc);
        Assert.Equal(26.5, telemetry.TemperatureCelsius);
        Assert.Equal(UpsOperationalState.LineNormal, telemetry.State);
        Assert.False(telemetry.IsBeeperMuted);
    }

    [Fact]
    public void Negotiate_Q1Fallback_SelectsMegatecAsciiDialectQ1_AndAppliesQxMultiplyBattvolt()
    {
        byte[] q1PerCellFrame = Encoding.ASCII.GetBytes("(220.0 220.0 220.0 025 50.0 2.27 28.5 10001000\r");
        var negotiator = new ProtocolNegotiator();

        NegotiatedUpsProtocol negotiated = negotiator.Negotiate(command => command switch
        {
            VoltronicProtocolConstants.ProtocolIdentityCommand => "N\r"u8.ToArray(),
            VoltronicProtocolConstants.StandardAsciiStatusCommand => q1PerCellFrame,
            VoltronicProtocolConstants.NominalRatingsCommand => "#220.0 004 24.00 50.0\r"u8.ToArray(),
            VoltronicProtocolConstants.VendorInformationCommand => "N\r"u8.ToArray(),
            _ => null
        });

        Assert.Equal("Q1", negotiated.Strategy.DialectId);
        Assert.Equal(VoltronicProtocolConstants.StandardAsciiStatusCommand, negotiated.Strategy.StatusQueryCommand);
        Assert.True(negotiated.Capabilities.DialectVerified);

        UpsTelemetry? telemetry = negotiated.Strategy.Parse(
            q1PerCellFrame,
            negotiated.Capabilities,
            previous: null,
            now: FixedTimestamp);

        Assert.NotNull(telemetry);
        Assert.Equal(27.24, telemetry.BatteryVoltageDc);
        Assert.Equal(13.62, telemetry.Cell1VoltageDc);
        Assert.Equal(28.5, telemetry.TemperatureCelsius);
        Assert.Equal(UpsOperationalState.OnBattery, telemetry.State);
        Assert.NotNull(telemetry.PowerStatus);
        Assert.True(telemetry.PowerStatus.Value.IsOnBattery);
        Assert.True(telemetry.IsBeeperMuted);
    }

    [Fact]
    public void Negotiate_WhenAllProbesFailOrNak_FallsBackToUnverifiedDialectT()
    {
        var negotiator = new ProtocolNegotiator();
        NegotiatedUpsProtocol negotiated = negotiator.Negotiate(_ => "N\r"u8.ToArray());

        Assert.Equal("T", negotiated.Strategy.DialectId);
        Assert.Equal(VoltronicProtocolConstants.QueryStatusCommand, negotiated.Strategy.StatusQueryCommand);
        Assert.False(negotiated.Capabilities.DialectVerified);
    }

    [Fact]
    public void MegatecAsciiProtocol_RejectsMalformedOrNakFrames_AndHandlesMissingTemperatureToken()
    {
        var protocol = new MegatecAsciiProtocol("Q1", VoltronicProtocolConstants.StandardAsciiStatusCommand);
        var capabilities = new UpsCapabilities(null, 220.0, 50.0, null, new BatteryBankSpec(), true);
        UpsTelemetry baseline = UpsTelemetry.CreateDefault(FixedTimestamp);

        Assert.Same(baseline, protocol.Parse("N\r"u8, capabilities, baseline, FixedTimestamp));
        Assert.Same(baseline, protocol.Parse("220.0 220.0 220.0 025 50.0 27.2 25.0 00001001\r"u8, capabilities, baseline, FixedTimestamp));
        Assert.Same(baseline, protocol.Parse("(220.0 220.0 220.0 025 50.0 27.2 25.0 00001001"u8, capabilities, baseline, FixedTimestamp));
        Assert.Same(baseline, protocol.Parse("(220.0 220.0 220.0 025\r"u8, capabilities, baseline, FixedTimestamp));

        UpsTelemetry? noTempTelemetry = protocol.Parse(
            "(220.0 220.0 220.0 025 50.0 27.20 --.- 00001001\r"u8,
            capabilities,
            baseline,
            FixedTimestamp);

        Assert.NotNull(noTempTelemetry);
        Assert.Null(noTempTelemetry.TemperatureCelsius);
        Assert.Equal(27.20, noTempTelemetry.BatteryVoltageDc);
    }

    [Fact]
    public async Task Win32HidUpsPort_WithNegotiateOnConnectEnabled_NegotiatesMegatecAndReadsScaledTelemetry()
    {
        var responses = new Dictionary<string, byte[]>
        {
            [VoltronicProtocolConstants.ProtocolIdentityCommand] = "N\r"u8.ToArray(),
            [VoltronicProtocolConstants.StandardAsciiStatusCommand] =
                Encoding.ASCII.GetBytes("(220.0 220.0 220.0 020 50.0 2.25 27.0 00001001\r"),
            [VoltronicProtocolConstants.NominalRatingsCommand] = "#220.0 004 24.00 50.0\r"u8.ToArray(),
            [VoltronicProtocolConstants.VendorInformationCommand] = "#FORZA NT-2401PRO V1.0\r"u8.ToArray()
        };

        var connection = new ScriptedHidConnection(responses);
        var transport = new SingleConnectionTransport(connection);
        var descriptor = new HidDeviceDescriptor(
            @"\\?\HID#VID_0665&PID_5161#megatec",
            HidConstants.VendorId,
            HidConstants.ProductId,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "(none)",
            HidConstants.ReportLength,
            HidConstants.ReportLength);
        var scanner = new SingleDeviceScanner(descriptor);

        using var port = new Win32HidUpsPort(
            preferredDevicePath: null,
            allowAutoDiscovery: true,
            estimator: null,
            deviceScanner: scanner,
            transport: transport,
            negotiateOnConnect: true);

        Assert.True(port.TryConnect());
        Assert.Equal("Q1", port.NegotiatedProtocol.Strategy.DialectId);
        Assert.Equal("FORZA NT-2401PRO V1.0", port.NegotiatedProtocol.Capabilities.ModelName);

        UpsTelemetry? telemetry = await port.ReadTelemetryAsync();
        Assert.NotNull(telemetry);
        Assert.Equal(27.0, telemetry.BatteryVoltageDc);
        Assert.Equal(27.0, telemetry.TemperatureCelsius);
    }

    private sealed class SingleDeviceScanner(HidDeviceDescriptor descriptor) : IHidDeviceScanner
    {
        public IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false) => [descriptor];
    }

    private sealed class SingleConnectionTransport(IHidConnection connection) : IHidTransport
    {
        public IHidConnection? TryOpen(string devicePath) => connection;

        public VoltTray.Shared.Result<IHidConnection> Open(string devicePath) =>
            VoltTray.Shared.Result<IHidConnection>.Success(connection);
    }

    private sealed class ScriptedHidConnection(IReadOnlyDictionary<string, byte[]> responses) : IHidConnection
    {
        private byte[] _activeResponse = [];
        private int _offset;

        public bool IsOpen { get; private set; } = true;
        public void CancelPendingIo() { }
        public void DrainInputBuffer(int drainTimeoutMs) { }

        public bool WriteCommand(string command)
        {
            _offset = 0;
            _activeResponse = responses.TryGetValue(command, out byte[]? payload) ? payload : [];
            return IsOpen;
        }

        public int ReadReport(byte[] buffer, int timeoutMs)
        {
            if (_offset >= _activeResponse.Length)
            {
                return 0;
            }

            Array.Clear(buffer, 0, buffer.Length);
            buffer[0] = HidConstants.DefaultReportId;
            int chunkLength = Math.Min(HidConstants.PayloadChunkLength, _activeResponse.Length - _offset);
            Array.Copy(_activeResponse, _offset, buffer, HidConstants.ReportPayloadStartIndex, chunkLength);
            _offset += chunkLength;
            return HidConstants.ReportLength;
        }

        public void Dispose() => IsOpen = false;
    }
}
