using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Resolves target HID device paths, opens transport handles, and runs protocol negotiation
/// on newly opened UPS sessions.
/// </summary>
internal sealed class HidDeviceSessionOpener
{
    private readonly string? _preferredDevicePath;
    private readonly bool _allowAutoDiscovery;
    private readonly IHidDeviceScanner _deviceScanner;
    private readonly IHidTransport _transport;
    private readonly IBatteryAutonomyEstimator _estimator;
    private readonly ProtocolNegotiator _protocolNegotiator = new();
    private readonly bool _negotiateOnConnect;
    private readonly UpsModelProfile? _modelProfile;

    public HidDeviceSessionOpener(
        string? preferredDevicePath,
        bool allowAutoDiscovery,
        IHidDeviceScanner deviceScanner,
        IHidTransport transport,
        IBatteryAutonomyEstimator estimator,
        bool negotiateOnConnect,
        UpsModelProfile? modelProfile = null)
    {
        _preferredDevicePath = preferredDevicePath;
        _allowAutoDiscovery = allowAutoDiscovery;
        _deviceScanner = deviceScanner;
        _transport = transport;
        _estimator = estimator;
        _negotiateOnConnect = negotiateOnConnect;
        _modelProfile = modelProfile;
    }

    public NegotiatedUpsProtocol CreateDefaultNegotiatedProtocol()
    {
        UpsCapabilities capabilities = _modelProfile?.ToCapabilities() ?? new UpsCapabilities(
            ModelName: null,
            NominalVoltageAc: null,
            NominalFrequencyHz: null,
            RatedWatts: null,
            Bank: new BatteryBankSpec(),
            DialectVerified: false);

        return new NegotiatedUpsProtocol(
            new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectT, _estimator),
            capabilities);
    }

    public Result<(string DevicePath, IHidConnection Connection, NegotiatedUpsProtocol Protocol)> OpenSession(
        string? customPath)
    {
        string? targetPath = ResolveTargetPath(customPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            return Result<(string, IHidConnection, NegotiatedUpsProtocol)>.Failure(new DomainError(
                SharedErrorConstants.DeviceNotFoundCode,
                "No supported Voltronic/Forza USB HID UPS device was found.",
                ErrorCategory.DeviceNotFound,
                IsTransient: false));
        }

        Result<IHidConnection> openResult = _transport.Open(targetPath);
        if (openResult.IsFailure || openResult.Value is null)
        {
            return Result<(string, IHidConnection, NegotiatedUpsProtocol)>.Failure(
                openResult.Error ?? new DomainError(
                    SharedErrorConstants.DeviceNotFoundCode,
                    $"Failed to open HID device path '{targetPath}'.",
                    ErrorCategory.DeviceNotFound,
                    IsTransient: false));
        }

        IHidConnection openedConnection = openResult.Value;
        openedConnection.DrainInputBuffer(HidConstants.DefaultDrainTimeoutMs);

        NegotiatedUpsProtocol negotiatedProtocol = _negotiateOnConnect
            ? _protocolNegotiator.Negotiate(
                command => HidCommandExecutor.QuerySynchronously(openedConnection, command),
                _estimator,
                _modelProfile)
            : CreateDefaultNegotiatedProtocol();

        return Result<(string, IHidConnection, NegotiatedUpsProtocol)>.Success(
            (targetPath, openedConnection, negotiatedProtocol));
    }

    private string? ResolveTargetPath(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            return customPath;
        }

        if (!string.IsNullOrEmpty(_preferredDevicePath))
        {
            return _preferredDevicePath;
        }

        if (!_allowAutoDiscovery)
        {
            return null;
        }

        HidDeviceDescriptor? discoveredDevice = _deviceScanner
            .DiscoverDevices(includeAllHidDevices: false)
            .FirstOrDefault(descriptor => descriptor.IsSupportedVoltronicDevice);

        return discoveredDevice?.DevicePath;
    }

    public static Result<string>? CheckShortCircuit(
        bool isDisposed,
        bool isConnected,
        string? activeDevicePath,
        CancellationToken cancellationToken)
    {
        if (isDisposed)
        {
            return Result<string>.Failure(CreateDisposedError());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Failure(CreateCancelledError());
        }

        if (isConnected && activeDevicePath != null)
        {
            return Result<string>.Success(activeDevicePath);
        }

        return null;
    }

    public static void CancelSafely(CancellationTokenSource? cts, bool dispose = false)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (dispose)
        {
            cts.Dispose();
        }
    }

    public static DomainError CreateDisposedError()
    {
        return new DomainError(
            SharedErrorConstants.DeviceNotFoundCode,
            "Win32 HID UPS port has been disposed.",
            ErrorCategory.DeviceNotFound,
            IsTransient: false);
    }

    public static DomainError CreateCancelledError()
    {
        return new DomainError(
            SharedErrorConstants.TransportTimeoutCode,
            "HID connection attempt was cancelled.",
            ErrorCategory.TransportTimeout,
            IsTransient: false);
    }
}
