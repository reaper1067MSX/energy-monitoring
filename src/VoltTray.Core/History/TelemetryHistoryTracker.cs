using VoltTray.Core.Telemetry;

namespace VoltTray.Core.History;

public sealed class TelemetryHistoryTracker : ITelemetryHistoryTracker
{
    private readonly ITelemetryHistoryStore _store;
    private readonly object _lock = new();
    private readonly List<DailyTelemetrySummary> _recentDays;
    private readonly List<PowerOutageEvent> _recentOutages;

    private DateOnly _currentDate;
    private double _todayEnergyKwh;
    private int _todayPeakWatts;
    private int _sessionPeakWatts;
    private double? _todayMinInputAc;
    private double? _todayMaxInputAc;
    private int _todayAvrCount;
    private int _todayOutageCount;
    private int _todayBatterySeconds;

    private DateTimeOffset? _lastSampleTime;
    private DateTimeOffset _lastFlushTime;
    private bool _wasAvrActive;
    private bool _wasOnBattery;
    private DateTimeOffset? _activeOutageStart;
    private double _activeOutageMinBatteryV;
    private int _preOutageAvrCount;
    private bool _isDirty;

    public TelemetryHistoryTracker(ITelemetryHistoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _recentDays = [.. _store.LoadRecentDays(HistoryDomainConstants.DefaultRecentDaysWindow)];
        _recentOutages = [.. _store.LoadRecentOutages(HistoryDomainConstants.DefaultMaxOutageEvents)];

        _currentDate = DateOnly.FromDateTime(DateTime.Now);
        DailyTelemetrySummary? todaySummary = _recentDays.FirstOrDefault(d => d.Date == _currentDate);
        if (todaySummary != null)
        {
            _todayEnergyKwh = todaySummary.EnergyKwh;
            _todayPeakWatts = todaySummary.PeakWatts;
            _todayMinInputAc = todaySummary.MinInputVoltageAc;
            _todayMaxInputAc = todaySummary.MaxInputVoltageAc;
            _todayAvrCount = todaySummary.AvrEventCount;
            _todayOutageCount = todaySummary.OutageCount;
            _todayBatterySeconds = todaySummary.BatterySeconds;
        }
    }

    public TelemetryHistorySnapshot RecordSample(UpsTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        lock (_lock)
        {
            DateTimeOffset sampleTime = telemetry.Timestamp.ToLocalTime();
            DateOnly sampleDate = DateOnly.FromDateTime(sampleTime.DateTime);
            if (sampleDate != _currentDate)
            {
                RolloverDayUnsafe(sampleDate);
            }

            if (_lastSampleTime is DateTimeOffset previousTime)
            {
                double dtSeconds = (sampleTime - previousTime).TotalSeconds;
                if (dtSeconds > 0 && dtSeconds <= HistoryDomainConstants.MaxIntegrationStepSeconds)
                {
                    if (telemetry.ActiveLoadWatts is int watts && watts > 0)
                    {
                        _todayEnergyKwh += (watts / HistoryDomainConstants.WattsPerKilowatt) *
                                          (dtSeconds / HistoryDomainConstants.SecondsPerHour);
                        _isDirty = true;
                    }

                    if (telemetry.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow)
                    {
                        _todayBatterySeconds += (int)Math.Round(dtSeconds);
                        _isDirty = true;
                    }
                }
            }

            _lastSampleTime = sampleTime;

            if (telemetry.ActiveLoadWatts is int activeWatts)
            {
                if (activeWatts > _sessionPeakWatts)
                {
                    _sessionPeakWatts = activeWatts;
                }

                if (activeWatts > _todayPeakWatts)
                {
                    _todayPeakWatts = activeWatts;
                    _isDirty = true;
                }
            }

            if (telemetry.InputVoltageAc > HistoryDomainConstants.ValidMainsVoltageFloorAc)
            {
                if (_todayMinInputAc is null || telemetry.InputVoltageAc < _todayMinInputAc)
                {
                    _todayMinInputAc = telemetry.InputVoltageAc;
                    _isDirty = true;
                }

                if (_todayMaxInputAc is null || telemetry.InputVoltageAc > _todayMaxInputAc)
                {
                    _todayMaxInputAc = telemetry.InputVoltageAc;
                    _isDirty = true;
                }
            }

            bool isAvr = telemetry.State == UpsOperationalState.ChargingAvr ||
                         (telemetry.PowerStatus?.IsAvrActive ?? false);
            if (isAvr && !_wasAvrActive)
            {
                _todayAvrCount++;
                _isDirty = true;
            }
            _wasAvrActive = isAvr;

            bool isOnBattery = telemetry.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow ||
                               (telemetry.PowerStatus?.IsOnBattery ?? false);
            bool outageEnded = TrackOutageEdgeUnsafe(isOnBattery, telemetry.BatteryVoltageDc, sampleTime);

            if (outageEnded ||
                (_isDirty && (sampleTime - _lastFlushTime).TotalSeconds >= HistoryDomainConstants.DefaultFlushIntervalSeconds))
            {
                FlushUnsafe(sampleTime);
            }

            return BuildSnapshotUnsafe();
        }
    }

    public TelemetryHistorySnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return BuildSnapshotUnsafe();
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_isDirty)
            {
                FlushUnsafe(DateTimeOffset.Now);
            }
        }
    }

    private bool TrackOutageEdgeUnsafe(bool isOnBattery, double batteryVoltageDc, DateTimeOffset sampleTime)
    {
        if (isOnBattery)
        {
            if (!_wasOnBattery)
            {
                _todayOutageCount++;
                _activeOutageStart = sampleTime;
                _activeOutageMinBatteryV = batteryVoltageDc;
                _preOutageAvrCount = _todayAvrCount;
                _isDirty = true;
            }
            else if (batteryVoltageDc < _activeOutageMinBatteryV)
            {
                _activeOutageMinBatteryV = batteryVoltageDc;
            }

            _wasOnBattery = true;
            return false;
        }

        if (_wasOnBattery && _activeOutageStart is DateTimeOffset started)
        {
            int durationSeconds = Math.Max(1, (int)Math.Round((sampleTime - started).TotalSeconds));
            var outageEvent = new PowerOutageEvent(started, durationSeconds, _activeOutageMinBatteryV, _preOutageAvrCount);
            _store.RecordOutageEvent(outageEvent);
            _recentOutages.Insert(0, outageEvent);
            if (_recentOutages.Count > HistoryDomainConstants.DefaultMaxOutageEvents)
            {
                _recentOutages.RemoveAt(_recentOutages.Count - 1);
            }

            _activeOutageStart = null;
            _wasOnBattery = false;
            _isDirty = true;
            return true;
        }

        _wasOnBattery = false;
        return false;
    }

    private void RolloverDayUnsafe(DateOnly nextDate)
    {
        if (_isDirty)
        {
            FlushUnsafe(_lastSampleTime ?? DateTimeOffset.Now);
        }

        _currentDate = nextDate;
        _todayEnergyKwh = 0;
        _todayPeakWatts = 0;
        _todayMinInputAc = null;
        _todayMaxInputAc = null;
        _todayAvrCount = 0;
        _todayOutageCount = 0;
        _todayBatterySeconds = 0;
        _isDirty = false;
    }

    private void FlushUnsafe(DateTimeOffset timestamp)
    {
        DailyTelemetrySummary summary = BuildTodaySummaryUnsafe();
        _store.UpsertDailySummary(summary);
        UpsertRecentDayUnsafe(summary);
        _lastFlushTime = timestamp;
        _isDirty = false;
    }

    private DailyTelemetrySummary BuildTodaySummaryUnsafe() => new(
        Date: _currentDate,
        EnergyKwh: Math.Round(_todayEnergyKwh, 4),
        PeakWatts: _todayPeakWatts,
        MinInputVoltageAc: _todayMinInputAc,
        MaxInputVoltageAc: _todayMaxInputAc,
        AvrEventCount: _todayAvrCount,
        OutageCount: _todayOutageCount,
        BatterySeconds: _todayBatterySeconds);

    private void UpsertRecentDayUnsafe(DailyTelemetrySummary summary)
    {
        int existingIndex = _recentDays.FindIndex(d => d.Date == summary.Date);
        if (existingIndex >= 0)
        {
            _recentDays[existingIndex] = summary;
            return;
        }

        _recentDays.Add(summary);
        if (_recentDays.Count > HistoryDomainConstants.DefaultRecentDaysWindow)
        {
            _recentDays.RemoveAt(0);
        }
    }

    private TelemetryHistorySnapshot BuildSnapshotUnsafe()
    {
        DailyTelemetrySummary todaySummary = BuildTodaySummaryUnsafe();
        var recentCopy = new List<DailyTelemetrySummary>(_recentDays);
        int idx = recentCopy.FindIndex(d => d.Date == _currentDate);
        if (idx >= 0)
        {
            recentCopy[idx] = todaySummary;
        }
        else if (_todayEnergyKwh > 0 || _todayPeakWatts > 0 || _todayAvrCount > 0 || _todayOutageCount > 0 || _todayMinInputAc.HasValue)
        {
            recentCopy.Add(todaySummary);
        }

        return new TelemetryHistorySnapshot(
            TodayEnergyKwh: _todayEnergyKwh,
            SessionPeakWatts: _sessionPeakWatts,
            TodayMinInputAc: _todayMinInputAc,
            TodayMaxInputAc: _todayMaxInputAc,
            TodayAvrCount: _todayAvrCount,
            TodayOutageCount: _todayOutageCount,
            RecentDays: recentCopy,
            RecentOutages: [.. _recentOutages]);
    }
}
