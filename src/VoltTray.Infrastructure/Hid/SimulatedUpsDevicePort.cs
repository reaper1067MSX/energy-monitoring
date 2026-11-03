using System.Text;
using VoltTray.Core.Ports;
using VoltTray.Core.Shared.Constants;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// In-memory simulated UPS device port implementing Voltronic Protocol 'T' (QS-Hex).
/// Provides realistic telemetry frames with micro-jitter for local UI testing without physical hardware.
/// </summary>
public sealed class SimulatedUpsDevicePort : IUpsDevicePort
{
    private readonly Random _random = new(42);
    private readonly object _lock = new();
    private bool _isConnected = true;
    private bool _beeperActive = true;
    private bool _selfTestActive = false;
    private DateTime _selfTestExpires = DateTime.MinValue;

    public bool IsConnected
    {
        get { lock (_lock) return _isConnected; }
    }

    public string? ActiveDevicePath => HidConstants.SimulatedDevicePath;

    public bool TryConnect(string? customPath = null)
    {
        lock (_lock)
        {
            _isConnected = true;
            return true;
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _isConnected = false;
        }
    }

    public Task<string?> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isConnected) return Task.FromResult<string?>(null);

            if (command.StartsWith(VoltronicProtocolConstants.QueryStatusCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (_selfTestActive && DateTime.UtcNow >= _selfTestExpires)
                {
                    _selfTestActive = false;
                }

                int loadPct = 14 + _random.Next(-1, 2);

                byte statusByte = VoltronicProtocolConstants.StatusLineNormal;
                if (_beeperActive) statusByte |= VoltronicProtocolConstants.StatusBeeperActiveMask;
                if (_selfTestActive) statusByte |= VoltronicProtocolConstants.StatusSelfTestActiveMask;

                byte[] frameBytes = VoltronicProtocolConstants.BuildBinaryFrame(
                    statusByte1: statusByte,
                    loadPercentage: (byte)loadPct);

                return Task.FromResult<string?>(Encoding.Latin1.GetString(frameBytes));
            }

            return Task.FromResult<string?>(null);
        }
    }

    public Task<bool> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isConnected) return Task.FromResult(false);

            if (command.StartsWith(VoltronicProtocolConstants.QueryCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _beeperActive = !_beeperActive;
                return Task.FromResult(true);
            }

            if (command.StartsWith(VoltronicProtocolConstants.SelfTestCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _selfTestActive = true;
                _selfTestExpires = DateTime.UtcNow.AddSeconds(HidConstants.SelfTestDurationSeconds);
                return Task.FromResult(true);
            }

            return Task.FromResult(true);
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
