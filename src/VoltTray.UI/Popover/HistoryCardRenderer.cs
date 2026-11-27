using System.Globalization;
using SkiaSharp;
using VoltTray.Core.History;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Popover;

internal static class HistoryCardRenderer
{
    public static void Draw(
        SKCanvas canvas,
        SKRect bounds,
        PopoverHitTestLayout layout,
        HistoryViewTab activeTab,
        TelemetryHistorySnapshot? history,
        ThemePalette palette,
        SKTypeface regularTypeface,
        SKTypeface semiBoldTypeface,
        SKTypeface boldTypeface)
    {
        float pad = PopoverLayoutConstants.HistoryCardPadding;
        float innerLeft = bounds.Left + pad;
        float innerRight = bounds.Right - pad;

        using var sectionFont = new SKFont(semiBoldTypeface, PopoverLayoutConstants.FontSectionSize);
        using var microFont = new SKFont(semiBoldTypeface, PopoverLayoutConstants.FontMicroLabelSize);
        using var metricFont = new SKFont(regularTypeface, PopoverLayoutConstants.FontMetricSize);
        using var kpiValueFont = new SKFont(boldTypeface, PopoverLayoutConstants.FontMetricSize);
        using var textPaint = new SKPaint { Color = SKColor.Parse(palette.Text), IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = SKColor.Parse(palette.TextMuted), IsAntialias = true };

        SKFontMetrics sectionMetrics = sectionFont.Metrics;
        float titleCenterY = bounds.Top + pad + 8f;
        canvas.DrawText(
            LocalizationConstants.HistorySectionTitle,
            innerLeft,
            titleCenterY - ((sectionMetrics.Ascent + sectionMetrics.Descent) / 2f),
            SKTextAlign.Left,
            sectionFont,
            textPaint);

        DrawTabPill(canvas, layout.HistoryTab5Days, LocalizationConstants.HistoryTab5DaysLabel, activeTab == HistoryViewTab.FiveDays, palette, microFont);
        DrawTabPill(canvas, layout.HistoryTabMonth, LocalizationConstants.HistoryTabMonthLabel, activeTab == HistoryViewTab.Month, palette, microFont);
        DrawTabPill(canvas, layout.HistoryTabOutages, LocalizationConstants.HistoryTabOutagesLabel, activeTab == HistoryViewTab.OutageDays, palette, microFont);

        IReadOnlyList<DailyTelemetrySummary> days = history?.RecentDays ?? [];
        IReadOnlyList<PowerOutageEvent> outages = history?.RecentOutages ?? [];

        switch (activeTab)
        {
            case HistoryViewTab.Month:
                DrawMonthCalendar(canvas, bounds, innerLeft, innerRight, days, palette, microFont, mutedPaint);
                DrawPeriodKpis(canvas, bounds, innerLeft, innerRight, days, 30, palette, microFont, kpiValueFont, mutedPaint);
                break;
            case HistoryViewTab.OutageDays:
                DrawOutageList(canvas, bounds, innerLeft, innerRight, outages, palette, metricFont, microFont, textPaint, mutedPaint);
                break;
            default:
                DrawFiveDayBars(canvas, bounds, innerLeft, innerRight, days, palette, microFont, mutedPaint);
                DrawPeriodKpis(canvas, bounds, innerLeft, innerRight, days, 5, palette, microFont, kpiValueFont, mutedPaint);
                break;
        }
    }

    private static void DrawTabPill(
        SKCanvas canvas,
        ViewRect rect,
        string label,
        bool isActive,
        ThemePalette palette,
        SKFont font)
    {
        var skRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        SKColor fillColor = isActive
            ? SKColor.Parse(palette.Accent).WithAlpha(42)
            : SKColor.Parse(palette.Border).WithAlpha(55);
        SKColor textColor = isActive ? SKColor.Parse(palette.Accent) : SKColor.Parse(palette.TextMuted);

        using var fillPaint = new SKPaint { Color = fillColor, IsAntialias = true };
        canvas.DrawRoundRect(skRect, 5f, 5f, fillPaint);

        if (isActive)
        {
            using var borderPaint = new SKPaint
            {
                Color = SKColor.Parse(palette.Accent).WithAlpha(140),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(skRect, 5f, 5f, borderPaint);
        }

        SKFontMetrics metrics = font.Metrics;
        float baseline = skRect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
        using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };
        canvas.DrawText(label, skRect.MidX, baseline, SKTextAlign.Center, font, textPaint);
    }

    private static void DrawFiveDayBars(
        SKCanvas canvas,
        SKRect bounds,
        float innerLeft,
        float innerRight,
        IReadOnlyList<DailyTelemetrySummary> days,
        ThemePalette palette,
        SKFont microFont,
        SKPaint mutedPaint)
    {
        canvas.DrawText(LocalizationConstants.DailyEnergyLabel, innerLeft, bounds.Top + 41f, SKTextAlign.Left, microFont, mutedPaint);
        canvas.DrawText(LocalizationConstants.PcOffLegendLabel, innerRight, bounds.Top + 41f, SKTextAlign.Right, microFont, mutedPaint);

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        var byDate = days.ToDictionary(d => d.Date);
        var slots = new (DateOnly Date, DailyTelemetrySummary? Summary)[5];
        double maxKwh = 0.5;
        for (int i = 0; i < 5; i++)
        {
            DateOnly date = today.AddDays(-(4 - i));
            byDate.TryGetValue(date, out DailyTelemetrySummary? summary);
            slots[i] = (date, summary);
            if (summary != null && summary.EnergyKwh > maxKwh)
            {
                maxKwh = summary.EnergyKwh;
            }
        }

        float totalW = innerRight - innerLeft;
        float colW = totalW / 5f;
        float barBaselineY = bounds.Top + 82f;
        float maxBarH = 26f;

        for (int i = 0; i < 5; i++)
        {
            (DateOnly date, DailyTelemetrySummary? summary) = slots[i];
            float centerX = innerLeft + (colW * i) + (colW / 2f);
            float barW = Math.Min(34f, colW - 10f);
            string dayLabel = i == 4
                ? "Today"
                : date.ToString("ddd", CultureInfo.InvariantCulture);

            if (summary is null || summary.EnergyKwh <= 0.001)
            {
                var offRect = new SKRect(centerX - (barW / 2f), barBaselineY - 4f, centerX + (barW / 2f), barBaselineY);
                using var offPaint = new SKPaint
                {
                    Color = SKColor.Parse(palette.Border).WithAlpha(120),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f,
                    PathEffect = SKPathEffect.CreateDash([3f, 2f], 0f),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(offRect, 2f, 2f, offPaint);
                canvas.DrawText("—", centerX, barBaselineY - 8f, SKTextAlign.Center, microFont, mutedPaint);
            }
            else
            {
                float barH = Math.Max(5f, (float)(summary.EnergyKwh / maxKwh) * maxBarH);
                SKColor barColor = ResolveKwhColor(summary.EnergyKwh, palette);
                var barRect = new SKRect(centerX - (barW / 2f), barBaselineY - barH, centerX + (barW / 2f), barBaselineY);
                using var barPaint = new SKPaint { Color = barColor, IsAntialias = true };
                canvas.DrawRoundRect(barRect, 3f, 3f, barPaint);

                using var valPaint = new SKPaint { Color = barColor, IsAntialias = true };
                canvas.DrawText(
                    summary.EnergyKwh.ToString("0.0", CultureInfo.InvariantCulture),
                    centerX,
                    barRect.Top - 3f,
                    SKTextAlign.Center,
                    microFont,
                    valPaint);
            }

            canvas.DrawText(dayLabel, centerX, bounds.Top + 93f, SKTextAlign.Center, microFont, mutedPaint);
        }
    }

    private static void DrawMonthCalendar(
        SKCanvas canvas,
        SKRect bounds,
        float innerLeft,
        float innerRight,
        IReadOnlyList<DailyTelemetrySummary> days,
        ThemePalette palette,
        SKFont microFont,
        SKPaint mutedPaint)
    {
        canvas.DrawText(LocalizationConstants.MonthlyCalendarLabel, innerLeft, bounds.Top + 41f, SKTextAlign.Left, microFont, mutedPaint);
        canvas.DrawText("30d", innerRight, bounds.Top + 41f, SKTextAlign.Right, microFont, mutedPaint);

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        var byDate = days.ToDictionary(d => d.Date);
        float totalW = innerRight - innerLeft;
        const int cols = 10;
        float gap = 3f;
        float cellW = (totalW - (gap * (cols - 1))) / cols;
        float cellH = 13f;
        float startY = bounds.Top + 48f;

        for (int index = 0; index < 30; index++)
        {
            int row = index / cols;
            int col = index % cols;
            float left = innerLeft + (col * (cellW + gap));
            float top = startY + (row * (cellH + gap));
            var cellRect = new SKRect(left, top, left + cellW, top + cellH);

            DateOnly date = today.AddDays(-(29 - index));
            if (!byDate.TryGetValue(date, out DailyTelemetrySummary? summary) || summary.EnergyKwh <= 0.001)
            {
                using var offPaint = new SKPaint
                {
                    Color = SKColor.Parse(palette.Border).WithAlpha(110),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f,
                    PathEffect = SKPathEffect.CreateDash([2f, 2f], 0f),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(cellRect, 2.5f, 2.5f, offPaint);
            }
            else
            {
                SKColor tone = ResolveKwhColor(summary.EnergyKwh, palette);
                using var fillPaint = new SKPaint { Color = tone.WithAlpha(175), IsAntialias = true };
                canvas.DrawRoundRect(cellRect, 2.5f, 2.5f, fillPaint);

                if (summary.OutageCount > 0)
                {
                    using var dotPaint = new SKPaint { Color = SKColor.Parse(palette.Danger), IsAntialias = true };
                    canvas.DrawCircle(cellRect.Right - 3f, cellRect.Top + 3f, 1.7f, dotPaint);
                }
            }
        }
    }

    private static void DrawOutageList(
        SKCanvas canvas,
        SKRect bounds,
        float innerLeft,
        float innerRight,
        IReadOnlyList<PowerOutageEvent> outages,
        ThemePalette palette,
        SKFont metricFont,
        SKFont microFont,
        SKPaint textPaint,
        SKPaint mutedPaint)
    {
        if (outages.Count == 0)
        {
            canvas.DrawText(LocalizationConstants.NoOutagesRecordedLabel, innerLeft, bounds.Top + 66f, SKTextAlign.Left, metricFont, mutedPaint);
            return;
        }

        float rowY = bounds.Top + 50f;
        int count = Math.Min(3, outages.Count);
        using var warnPaint = new SKPaint { Color = SKColor.Parse(palette.Warning), IsAntialias = true };
        for (int i = 0; i < count; i++)
        {
            PowerOutageEvent o = outages[i];
            string dateText = o.StartedAt.ToLocalTime().ToString("MMM dd · HH:mm", CultureInfo.InvariantCulture);
            string durText = o.DurationSeconds >= 60
                ? string.Create(CultureInfo.InvariantCulture, $"{o.DurationSeconds / 60}m on battery")
                : string.Create(CultureInfo.InvariantCulture, $"{o.DurationSeconds}s on battery");
            string minVText = string.Create(CultureInfo.InvariantCulture, $"Min {o.MinBatteryVoltageDc:F1}V");

            canvas.DrawText($"{dateText} · {durText}", innerLeft, rowY, SKTextAlign.Left, microFont, textPaint);
            canvas.DrawText(minVText, innerRight, rowY, SKTextAlign.Right, microFont, warnPaint);
            rowY += 22f;
        }
    }

    private static void DrawPeriodKpis(
        SKCanvas canvas,
        SKRect bounds,
        float innerLeft,
        float innerRight,
        IReadOnlyList<DailyTelemetrySummary> days,
        int windowDays,
        ThemePalette palette,
        SKFont microFont,
        SKFont valueFont,
        SKPaint mutedPaint)
    {
        float dividerY = bounds.Top + 98f;
        using var divPaint = new SKPaint
        {
            Color = SKColor.Parse(palette.Border).WithAlpha(PopoverLayoutConstants.DividerAlpha),
            StrokeWidth = PopoverLayoutConstants.StrokeThin
        };
        canvas.DrawLine(innerLeft, dividerY, innerRight, dividerY, divPaint);

        DateOnly cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-(windowDays - 1));
        List<DailyTelemetrySummary> window = days.Where(d => d.Date >= cutoff).ToList();

        double totalKwh = window.Sum(d => d.EnergyKwh);
        int activeDays = window.Count(d => d.EnergyKwh > 0.001);
        double avgKwh = activeDays > 0 ? totalKwh / activeDays : 0.0;
        int totalAvr = window.Sum(d => d.AvrEventCount);
        int totalOutages = window.Sum(d => d.OutageCount);

        float colW = (innerRight - innerLeft) / 4f;
        DrawKpi(canvas, innerLeft, bounds.Top, LocalizationConstants.TotalKwhKpiLabel, FormattableString.Invariant($"{totalKwh:F1} kWh"), ResolveKwhColor(avgKwh, palette), microFont, valueFont, mutedPaint);
        DrawKpi(canvas, innerLeft + colW, bounds.Top, LocalizationConstants.AvgPerDayKpiLabel, FormattableString.Invariant($"{avgKwh:F1} kWh"), ResolveKwhColor(avgKwh, palette), microFont, valueFont, mutedPaint);
        DrawKpi(canvas, innerLeft + (colW * 2f), bounds.Top, LocalizationConstants.AvrFixesKpiLabel, totalAvr.ToString(CultureInfo.InvariantCulture), totalAvr > 0 ? SKColor.Parse(palette.Warning) : SKColor.Parse(palette.Ok), microFont, valueFont, mutedPaint);
        DrawKpi(canvas, innerLeft + (colW * 3f), bounds.Top, LocalizationConstants.OutagesKpiLabel, totalOutages.ToString(CultureInfo.InvariantCulture), totalOutages >= 3 ? SKColor.Parse(palette.Danger) : (totalOutages > 0 ? SKColor.Parse(palette.Warning) : SKColor.Parse(palette.Ok)), microFont, valueFont, mutedPaint);
    }

    private static void DrawKpi(
        SKCanvas canvas,
        float x,
        float cardTop,
        string label,
        string value,
        SKColor valueColor,
        SKFont microFont,
        SKFont valueFont,
        SKPaint mutedPaint)
    {
        canvas.DrawText(label, x, cardTop + 111f, SKTextAlign.Left, microFont, mutedPaint);
        using var valPaint = new SKPaint { Color = valueColor, IsAntialias = true };
        canvas.DrawText(value, x, cardTop + 126f, SKTextAlign.Left, valueFont, valPaint);
    }

    private static SKColor ResolveKwhColor(double kwh, ThemePalette palette) => kwh switch
    {
        >= 4.5 => SKColor.Parse(palette.Danger),
        >= 2.8 => SKColor.Parse(palette.Warning),
        _ => SKColor.Parse(palette.Ok)
    };
}
