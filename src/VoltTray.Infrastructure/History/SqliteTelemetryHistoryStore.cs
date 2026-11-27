using System.Globalization;
using Microsoft.Data.Sqlite;
using VoltTray.Core.History;
using VoltTray.Infrastructure.Settings;

namespace VoltTray.Infrastructure.History;

internal sealed class SqliteTelemetryHistoryStore : ITelemetryHistoryStore
{
    private readonly string _connectionString;
    private readonly object _dbLock = new();
    private bool _schemaInitialized;

    public SqliteTelemetryHistoryStore(string? databaseFilePath = null)
    {
        string resolvedPath = string.IsNullOrWhiteSpace(databaseFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SettingsConstants.AppFolderName,
                HistoryConstants.DatabaseFileName)
            : databaseFilePath;

        string? directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        _connectionString = builder.ConnectionString;
    }

    public void UpsertDailySummary(DailyTelemetrySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenInitializedConnection();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = HistoryConstants.UpsertDailySql;
            cmd.Parameters.AddWithValue("$date_iso", summary.Date.ToString(HistoryConstants.IsoDateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$energy_kwh", summary.EnergyKwh);
            cmd.Parameters.AddWithValue("$peak_watts", summary.PeakWatts);
            cmd.Parameters.AddWithValue("$min_input_ac", summary.MinInputVoltageAc.HasValue ? summary.MinInputVoltageAc.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$max_input_ac", summary.MaxInputVoltageAc.HasValue ? summary.MaxInputVoltageAc.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$avr_events", summary.AvrEventCount);
            cmd.Parameters.AddWithValue("$outage_count", summary.OutageCount);
            cmd.Parameters.AddWithValue("$battery_seconds", summary.BatterySeconds);
            cmd.ExecuteNonQuery();
        }
    }

    public void RecordOutageEvent(PowerOutageEvent outageEvent)
    {
        ArgumentNullException.ThrowIfNull(outageEvent);

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenInitializedConnection();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = HistoryConstants.InsertOutageSql;
            cmd.Parameters.AddWithValue("$started_at", outageEvent.StartedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$duration_seconds", outageEvent.DurationSeconds);
            cmd.Parameters.AddWithValue("$min_battery_v", outageEvent.MinBatteryVoltageDc);
            cmd.Parameters.AddWithValue("$pre_event_avr", outageEvent.PreEventAvrCount);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<DailyTelemetrySummary> LoadRecentDays(int days)
    {
        int clampedDays = Math.Max(1, days);
        DateOnly cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-(clampedDays - 1));

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenInitializedConnection();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = HistoryConstants.SelectRecentDaysSql;
            cmd.Parameters.AddWithValue("$cutoff_date", cutoff.ToString(HistoryConstants.IsoDateFormat, CultureInfo.InvariantCulture));

            var results = new List<DailyTelemetrySummary>();
            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!DateOnly.TryParseExact(
                        reader.GetString(0),
                        HistoryConstants.IsoDateFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly date))
                {
                    continue;
                }

                results.Add(new DailyTelemetrySummary(
                    Date: date,
                    EnergyKwh: reader.GetDouble(1),
                    PeakWatts: reader.GetInt32(2),
                    MinInputVoltageAc: reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    MaxInputVoltageAc: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    AvrEventCount: reader.GetInt32(5),
                    OutageCount: reader.GetInt32(6),
                    BatterySeconds: reader.GetInt32(7)));
            }

            return results;
        }
    }

    public IReadOnlyList<PowerOutageEvent> LoadRecentOutages(int maxCount)
    {
        int clampedLimit = Math.Max(1, maxCount);

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenInitializedConnection();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = HistoryConstants.SelectRecentOutagesSql;
            cmd.Parameters.AddWithValue("$limit", clampedLimit);

            var results = new List<PowerOutageEvent>();
            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!DateTimeOffset.TryParse(
                        reader.GetString(0),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset startedAt))
                {
                    continue;
                }

                results.Add(new PowerOutageEvent(
                    StartedAt: startedAt,
                    DurationSeconds: reader.GetInt32(1),
                    MinBatteryVoltageDc: reader.GetDouble(2),
                    PreEventAvrCount: reader.GetInt32(3)));
            }

            return results;
        }
    }

    private SqliteConnection OpenInitializedConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        if (!_schemaInitialized)
        {
            using SqliteCommand pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = HistoryConstants.PragmaSql;
            pragmaCmd.ExecuteNonQuery();

            using SqliteCommand schemaCmd = connection.CreateCommand();
            schemaCmd.CommandText = HistoryConstants.SchemaSql;
            schemaCmd.ExecuteNonQuery();

            _schemaInitialized = true;
        }

        return connection;
    }
}
