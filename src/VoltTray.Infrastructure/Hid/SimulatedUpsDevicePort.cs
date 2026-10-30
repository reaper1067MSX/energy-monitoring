using System.Text;
using VoltTray.Core.Ports;

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

    public string? ActiveDevicePath => @"\\?\SIMULATED_UPS_FORZA_FX2200";

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

            if (command.StartsWith("QS", StringComparison.OrdinalIgnoreCase))
            {
                if (_selfTestActive && DateTime.UtcNow >= _selfTestExpires)
                {
                    _selfTestActive = false;
                }

                // Micro-jitter: +/- 0.3V on AC, +/- 0.05 Hz on freq
                double jitterV = (_random.NextDouble() - 0.5) * 0.6;
                double inV = Math.Round(116.2 + jitterV, 1);
                double outV = Math.Round(116.2 + jitterV, 1);
                int loadPct = 14 + _random.Next(-1, 2);
                double freq = 60.0;
                double batV = _selfTestActive ? 26.94 : 27.41;
                double temp = 25.0;

                // Status Byte 1:
                // Bit 3 = Line-Interactive (1)
                // Bit 2 = Self-Test (1 if active)
                // Bit 0 = Beeper (1 if active, 0 if muted)
                byte statusByte = 0b00001000;
                if (_beeperActive) statusByte |= 0b00000001;
                if (_selfTestActive) statusByte |= 0b00000100;

                string statusBits = Convert.ToString(statusByte, 2).PadLeft(8, '0');

                // Voltronic format: #(MMM.M QQQ.Q SSS.S RRR.R KKK.K III.I b7b6b5b4b3b2b1b0\r
                string frame = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"#{inV:F1} {outV:F1} {loadPct:D3} {freq:F1} {batV:F2} {temp:F1} {statusBits}\r");

                return Task.FromResult<string?>(frame);
            }

            return Task.FromResult<string?>(null);
        }
    }

    public Task<bool> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isConnected) return Task.FromResult(false);

            if (command.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
            {
                _beeperActive = !_beeperActive;
                return Task.FromResult(true);
            }

            if (command.StartsWith("T", StringComparison.OrdinalIgnoreCase))
            {
                _selfTestActive = true;
                _selfTestExpires = DateTime.UtcNow.AddSeconds(10);
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
