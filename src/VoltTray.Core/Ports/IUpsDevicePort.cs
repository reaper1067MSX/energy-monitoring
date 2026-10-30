namespace VoltTray.Core.Ports;

/// <summary>
/// Domain port for communicating with the UPS hardware device over USB HID.
/// Isolated from OS-specific drivers and Win32 P/Invoke implementations.
/// </summary>
public interface IUpsDevicePort : IDisposable
{
    /// <summary>
    /// Gets whether the underlying USB HID device handle is actively open and responsive.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the hardware device path if connected, or null.
    /// </summary>
    string? ActiveDevicePath { get; }

    /// <summary>
    /// Attempts to locate and open the physical HID device.
    /// Returns true if connected successfully, false otherwise.
    /// </summary>
    bool TryConnect(string? customPath = null);

    /// <summary>
    /// Immediately releases the device handle and frees the USB interface within 100 ms.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Asynchronously transmits a command and reads the terminated response frame.
    /// </summary>
    Task<string?> QueryAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously transmits a control command (such as Q\r, T\r, CT\r) without expecting a multi-byte telemetry response.
    /// </summary>
    Task<bool> SendCommandAsync(string command, CancellationToken cancellationToken = default);
}
