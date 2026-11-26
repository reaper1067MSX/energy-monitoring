namespace VoltTray.UI.Presenters;

public static class RuntimeDisplayFormatter
{
    private const string SpanishCulturePrefix = "es";

    public static string? FormatRuntimeString(int? minutes)
    {
        return minutes is int value
            ? $"≈ {value} min"
            : null;
    }

    public static string? FormatRuntimeCaption(int? minutes, int loadPercentage, string cultureName = "en")
    {
        if (minutes is null)
        {
            return null;
        }

        if (cultureName.StartsWith(SpanishCulturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"Autonomía a {loadPercentage} % de carga";
        }

        return $"Runtime at {loadPercentage}% load";
    }
}
