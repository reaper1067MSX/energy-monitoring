using System.Globalization;

namespace VoltTray.UI.Tokens;

public static class ColorUtilities
{
    public static uint ParseBgrColor(string hex)
    {
        string clean = hex.TrimStart('#');
        byte r = byte.Parse(clean[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (uint)(r | (g << 8) | (b << 16));
    }

    public static double CalculateContrastRatio(string foregroundHex, string backgroundHex)
    {
        double lumFg = CalculateLuminance(foregroundHex);
        double lumBg = CalculateLuminance(backgroundHex);
        double l1 = Math.Max(lumFg, lumBg);
        double l2 = Math.Min(lumFg, lumBg);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    private static double CalculateLuminance(string hex)
    {
        string clean = hex.TrimStart('#');
        double r = byte.Parse(clean[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        double g = byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        double b = byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

        static double Adjust(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Adjust(r) + 0.7152 * Adjust(g) + 0.0722 * Adjust(b);
    }
}
