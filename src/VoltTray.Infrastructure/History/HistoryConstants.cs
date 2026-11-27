namespace VoltTray.Infrastructure.History;

internal static class HistoryConstants
{
    public const string DatabaseFileName = "telemetry.db";
    public const string IsoDateFormat = "yyyy-MM-dd";

    public const string PragmaSql = """
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        """;

    public const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS daily_telemetry_summary (
            date_iso TEXT PRIMARY KEY,
            energy_kwh REAL NOT NULL,
            peak_watts INTEGER NOT NULL,
            min_input_ac REAL NULL,
            max_input_ac REAL NULL,
            avr_events INTEGER NOT NULL,
            outage_count INTEGER NOT NULL,
            battery_seconds INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS power_outage_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at TEXT NOT NULL,
            duration_seconds INTEGER NOT NULL,
            min_battery_v REAL NOT NULL,
            pre_event_avr INTEGER NOT NULL
        );
        """;

    public const string UpsertDailySql = """
        INSERT INTO daily_telemetry_summary (
            date_iso, energy_kwh, peak_watts, min_input_ac, max_input_ac, avr_events, outage_count, battery_seconds
        ) VALUES (
            $date_iso, $energy_kwh, $peak_watts, $min_input_ac, $max_input_ac, $avr_events, $outage_count, $battery_seconds
        )
        ON CONFLICT(date_iso) DO UPDATE SET
            energy_kwh = excluded.energy_kwh,
            peak_watts = excluded.peak_watts,
            min_input_ac = excluded.min_input_ac,
            max_input_ac = excluded.max_input_ac,
            avr_events = excluded.avr_events,
            outage_count = excluded.outage_count,
            battery_seconds = excluded.battery_seconds;
        """;

    public const string InsertOutageSql = """
        INSERT INTO power_outage_events (
            started_at, duration_seconds, min_battery_v, pre_event_avr
        ) VALUES (
            $started_at, $duration_seconds, $min_battery_v, $pre_event_avr
        );
        """;

    public const string SelectRecentDaysSql = """
        SELECT date_iso, energy_kwh, peak_watts, min_input_ac, max_input_ac, avr_events, outage_count, battery_seconds
        FROM daily_telemetry_summary
        WHERE date_iso >= $cutoff_date
        ORDER BY date_iso ASC;
        """;

    public const string SelectRecentOutagesSql = """
        SELECT started_at, duration_seconds, min_battery_v, pre_event_avr
        FROM power_outage_events
        ORDER BY id DESC
        LIMIT $limit;
        """;
}
