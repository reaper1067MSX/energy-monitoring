using VoltTray.Core.Models;
using VoltTray.Shared;

namespace VoltTray.Core.Ports;

/// <summary>
/// Domain port for communicating with the UPS hardware device.
/// Expresses domain intent without leaking wire protocols or OS transport details.
/// </summary>
public interface IUpsDevicePort : IDisposable
{
    /// <summary>
    /// Gets whether the underlying UPS device connection is active and responsive.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the active hardware or simulated device path if connected, or null.
    /// </summary>
    string? ActiveDevicePath { get; }

    /// <summary>
    /// Attempts to locate and open the physical UPS device.
    /// Returns true if connected successfully, false otherwise.
    /// </summary>
    bool TryConnect(string? customPath = null);

    /// <summary>
    /// Asynchronously connects to the UPS device with bounded retry on transient open failures
    /// and rich diagnostic errors on contention or missing hardware.
    /// </summary>
    Task<Result<string>> ConnectAsync(string? customPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately releases the device connection within 100 ms.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Asynchronously reads and decodes the latest operational telemetry snapshot from the UPS.
    /// </summary>
    Task<UpsTelemetry?> ReadTelemetryAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously toggles the audible beeper/alarm on the UPS.
    /// </summary>
    Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously initiates a battery self-test on the UPS.
    /// </summary>
    Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default);
}
