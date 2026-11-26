namespace VoltTray.Core.Services;

/// <summary>
/// Tracks bounded 15-second battery self-test immunity with cooldown enforcement
/// so flapping self-test flags cannot indefinitely re-arm shutdown suppression.
/// </summary>
internal sealed class SelfTestImmunityTracker
{
    private bool _lastTestState;
    private DateTimeOffset? _selfTestStartTime;
    private DateTimeOffset? _selfTestCooldownUntil;

    public void Update(bool isTestInProgress, DateTimeOffset timestamp)
    {
        if (isTestInProgress)
        {
            if (!_lastTestState)
            {
                bool inCooldown = _selfTestCooldownUntil.HasValue && timestamp < _selfTestCooldownUntil.Value;
                if (!inCooldown)
                {
                    _selfTestStartTime = timestamp;
                    _lastTestState = true;
                }
            }

            return;
        }

        if (_lastTestState && _selfTestStartTime.HasValue)
        {
            double elapsedSeconds = (timestamp - _selfTestStartTime.Value).TotalSeconds;
            if (elapsedSeconds >= ShutdownCoordinator.SelfTestImmunitySeconds)
            {
                _selfTestCooldownUntil = timestamp.AddSeconds(ShutdownCoordinator.SelfTestCooldownSeconds);
            }
        }

        _lastTestState = false;
        _selfTestStartTime = null;
    }

    public bool IsImmunityActive(bool isTestInProgress, DateTimeOffset timestamp)
    {
        return isTestInProgress &&
               _selfTestStartTime.HasValue &&
               (timestamp - _selfTestStartTime.Value).TotalSeconds < ShutdownCoordinator.SelfTestImmunitySeconds;
    }
}
