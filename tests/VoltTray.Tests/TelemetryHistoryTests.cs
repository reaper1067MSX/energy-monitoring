using VoltTray.Core.History;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.History;
using VoltTray.Infrastructure.Settings;
using Xunit;

namespace VoltTray.Tests;

public class TelemetryHistoryTests
{
    [Fact]
    public void SqliteTelemetryHistoryStore_RoundTripsDailySummariesWithGaps_AndOutageEvents()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"volttray-hist-{Guid.NewGuid():N}");
        string dbPath = Path.Combine(tempDir, "telemetry.db");

        try
        {
            var store = new SqliteTelemetryHistoryStore(dbPath);
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            DateOnly twoDaysAgo = today.AddDays(-2);

            store.UpsertDailySummary(new DailyTelemetrySummary(twoDaysAgo, 1.45, 420, 114.2, 121.0, 2, 0, 0));
            store.UpsertDailySummary(new DailyTelemetrySummary(today, 2.15, 510, 115.0, 120.5, 1, 1, 240));
            store.UpsertDailySummary(new DailyTelemetrySummary(today, 2.35, 540, 114.8, 120.5, 2, 1, 240));

            var outage = new PowerOutageEvent(DateTimeOffset.Now.AddMinutes(-10), 240, 24.35, 2);
            store.RecordOutageEvent(outage);

            IReadOnlyList<DailyTelemetrySummary> days = store.LoadRecentDays(5);
            Assert.Equal(2, days.Count);
            Assert.Equal(twoDaysAgo, days[0].Date);
            Assert.Equal(today, days[1].Date);
            Assert.Equal(2.35, days[1].EnergyKwh, 2);
            Assert.Equal(540, days[1].PeakWatts);

            IReadOnlyList<PowerOutageEvent> outages = store.LoadRecentOutages(10);
            Assert.Single(outages);
            Assert.Equal(240, outages[0].DurationSeconds);
            Assert.Equal(24.35, outages[0].MinBatteryVoltageDc, 2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void TelemetryHistoryTracker_IntegratesKwh_IgnoresSleepGaps_AndRecordsOutages()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"volttray-track-{Guid.NewGuid():N}");
        string dbPath = Path.Combine(tempDir, "telemetry.db");

        try
        {
            var store = new SqliteTelemetryHistoryStore(dbPath);
            var tracker = new TelemetryHistoryTracker(store);
            DateTimeOffset t0 = DateTimeOffset.Now;

            UpsTelemetry s1 = UpsTelemetry.CreateDefault(t0) with { InputVoltageAc = 118.0, ActiveLoadWatts = 600 };
            UpsTelemetry s2 = UpsTelemetry.CreateDefault(t0.AddSeconds(10)) with { InputVoltageAc = 116.0, ActiveLoadWatts = 600 };
            UpsTelemetry sleepGap = UpsTelemetry.CreateDefault(t0.AddMinutes(10)) with { InputVoltageAc = 121.0, ActiveLoadWatts = 600 };
            UpsTelemetry avrSample = UpsTelemetry.CreateDefault(t0.AddMinutes(10).AddSeconds(2)) with
            {
                InputVoltageAc = 99.0,
                ActiveLoadWatts = 600,
                State = UpsOperationalState.ChargingAvr
            };
            UpsTelemetry blackout = UpsTelemetry.CreateDefault(t0.AddMinutes(10).AddSeconds(4)) with
            {
                InputVoltageAc = 0.0,
                BatteryVoltageDc = 24.8,
                ActiveLoadWatts = 600,
                State = UpsOperationalState.OnBattery
            };
            UpsTelemetry recovered = UpsTelemetry.CreateDefault(t0.AddMinutes(10).AddSeconds(14)) with
            {
                InputVoltageAc = 119.0,
                ActiveLoadWatts = 300,
                State = UpsOperationalState.LineNormal
            };

            tracker.RecordSample(s1);
            tracker.RecordSample(s2);
            tracker.RecordSample(sleepGap);
            tracker.RecordSample(avrSample);
            tracker.RecordSample(blackout);
            TelemetryHistorySnapshot snap = tracker.RecordSample(recovered);

            Assert.True(snap.TodayEnergyKwh > 0.003 && snap.TodayEnergyKwh < 0.01);
            Assert.Equal(600, snap.SessionPeakWatts);
            Assert.Equal(99.0, snap.TodayMinInputAc);
            Assert.Equal(121.0, snap.TodayMaxInputAc);
            Assert.Equal(1, snap.TodayAvrCount);
            Assert.Equal(1, snap.TodayOutageCount);
            Assert.Single(snap.RecentOutages);
            Assert.Equal(10, snap.RecentOutages[0].DurationSeconds);
            Assert.Equal(24.8, snap.RecentOutages[0].MinBatteryVoltageDc, 1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonUserSettingsStore_PersistsThemePreference_WithoutClobberingModelOrIconMode()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"volttray-theme-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            var store = new JsonUserSettingsStore(settingsPath);
            Assert.Equal(ThemePreference.Auto, store.LoadThemePreference());

            store.SaveSelectedModel(UpsModelPreset.ForzaNt2401Pro);
            store.SaveIconMode(TrayIconMode.Large);
            store.SaveThemePreference(ThemePreference.Dark);

            Assert.Equal(ThemePreference.Dark, store.LoadThemePreference());
            Assert.Equal(UpsModelPreset.ForzaNt2401Pro, store.LoadSelectedModel());
            Assert.Equal(TrayIconMode.Large, store.LoadIconMode());

            store.SaveThemePreference(ThemePreference.Light);
            Assert.Equal(ThemePreference.Light, store.LoadThemePreference());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
