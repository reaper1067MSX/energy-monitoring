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
                double batV = _selfTestActive ? 26.94 : 27.41;

                // Status Byte 1:
                // Bit 3 = Line-Interactive (1)
                // Bit 2 = Self-Test (1 if active)
                // Bit 0 = Beeper (1 if active, 0 if muted)
                byte statusByte = 0b00001000;
                if (_beeperActive) statusByte |= 0b00000001;
                if (_selfTestActive) statusByte |= 0b00000100;

                string statusBits = Convert.ToString(statusByte, 2).PadLeft(8, '0');

                // Voltronic Taurus Protocol 'T' (QS-Hex) binary format:
                // # inV_hex in_ratio outV_hex out_ratio load_pct freq1 freq2 bat1 bat2 status1 status2 \r
                byte[] frameBytes = new byte[]
                {
                    0x23,                         // '#'
                    0x73, 0x01, 0x20,             // inV (117.3 V)
                    0x34, 0x20,                   // inRatio (52)
                    0x73, 0x01, 0x20,             // outV (117.3 V)
                    0x34, 0x20,                   // outRatio (52)
                    (byte)loadPct, 0x20,          // load %
                    0x50, 0x0C, 0x20,             // freq part 1
                    0x12, 0xC0, 0x20,             // freq part 2 (60.0 Hz)
                    0xE9, 0x20,                   // bat part 1
                    0x3C, 0x20,                   // bat part 2 (27.41 V)
                    statusByte, 0x20,             // status byte 1
                    0xA1,                         // status byte 2
                    0x0D                          // '\r'
                };

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
