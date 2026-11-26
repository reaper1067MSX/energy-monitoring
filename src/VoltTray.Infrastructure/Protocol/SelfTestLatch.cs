using VoltTray.Shared;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Tracks the rising edge of the hardware self-test flag and enforces the 15-second ceiling
/// so a stuck Bit 2 flag cannot lock out safety shutdown indefinitely.
/// </summary>
internal sealed class SelfTestLatch
{
    private bool _wasBitActive;
    private DateTimeOffset? _startedAt;

    public bool Update(bool isSelfTestBitActive, DateTimeOffset timestamp)
    {
        if (!isSelfTestBitActive)
        {
            _wasBitActive = false;
            _startedAt = null;
            return false;
        }

        if (!_wasBitActive)
        {
            _startedAt = timestamp;
            _wasBitActive = true;
            return true;
        }

        return _startedAt.HasValue &&
               (timestamp - _startedAt.Value).TotalSeconds < ElectricalConstants.MaxSelfTestDurationSeconds;
    }

    public void Reset()
    {
        _wasBitActive = false;
        _startedAt = null;
    }
}
