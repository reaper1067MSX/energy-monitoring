using System.Globalization;
using SkiaSharp;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Atoms;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Popover;

internal sealed class SkiaPopoverSurfaceRenderer : IPopoverSurfaceRenderer
{
    private readonly ISvgIconLibrary _svgIconLibrary;
    private readonly SKTypeface _regularTypeface;
    private readonly SKTypeface _semiBoldTypeface;
    private readonly SKTypeface _boldTypeface;
    private bool _disposed;

    public SkiaPopoverSurfaceRenderer(ISvgIconLibrary svgIconLibrary)
    {
        _svgIconLibrary = svgIconLibrary ?? throw new ArgumentNullException(nameof(svgIconLibrary));
        _regularTypeface = ResolveTypeface(SKFontStyleWeight.Normal);
        _semiBoldTypeface = ResolveTypeface(SKFontStyleWeight.SemiBold);
        _boldTypeface = ResolveTypeface(SKFontStyleWeight.Bold);
    }

    public byte[] RenderBgra32(PopoverRenderState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);

        using SKSurface surface = RenderSurface(state);
        int width = Math.Max(1, state.Width);
        int height = Math.Max(1, state.Height);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        byte[] buffer = new byte[width * height * PopoverLayoutConstants.BgraBytesPerPixel];

        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                surface.ReadPixels(imageInfo, (IntPtr)ptr, width * PopoverLayoutConstants.BgraBytesPerPixel, 0, 0);
            }
        }

        return buffer;
    }

    public byte[] RenderPng(PopoverRenderState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);

        using SKSurface surface = RenderSurface(state);
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, PopoverLayoutConstants.PngQuality);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _regularTypeface.Dispose();
        _semiBoldTypeface.Dispose();
        _boldTypeface.Dispose();
    }

    private SKSurface RenderSurface(PopoverRenderState state)
    {
        int width = Math.Max(1, state.Width);
        int height = Math.Max(1, state.Height);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKSurface surface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("Failed to allocate SkiaSharp popover surface.");

        SKCanvas canvas = surface.Canvas;
        ThemePalette palette = DesignTokens.GetPalette(state.Theme);
        PopoverViewModel vm = state.ViewModel;
        bool isLight = state.Theme == UITheme.Light;
        bool isSpanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals(LocalizationConstants.SpanishCulturePrefix, StringComparison.OrdinalIgnoreCase);

        SKColor surfaceFill = ParseColor(palette.Surface);
        if (state.UseAcrylicBackdrop)
        {
            surfaceFill = surfaceFill.WithAlpha(
                isLight ? PopoverLayoutConstants.AcrylicLightSurfaceAlpha : PopoverLayoutConstants.AcrylicDarkSurfaceAlpha);
        }

        canvas.Clear(surfaceFill);
        DrawCardFrame(canvas, width, height, palette, isLight);
        DrawHeader(canvas, width, vm, palette, isLight);

        int contentWidth = width - (PopoverLayoutConstants.OuterMargin * 2);
        int subCardWidth = (contentWidth - PopoverLayoutConstants.ColumnGap) / 2;
        int subCardTop = PopoverLayoutConstants.OuterMargin + PopoverLayoutConstants.HeaderHeight + PopoverLayoutConstants.SectionGap;

        var leftCard = new SKRect(
            PopoverLayoutConstants.OuterMargin,
            subCardTop,
            PopoverLayoutConstants.OuterMargin + subCardWidth,
            subCardTop + PopoverLayoutConstants.SubCardHeight);
        var rightCard = new SKRect(
            leftCard.Right + PopoverLayoutConstants.ColumnGap,
            subCardTop,
            PopoverLayoutConstants.OuterMargin + contentWidth,
            subCardTop + PopoverLayoutConstants.SubCardHeight);

        DrawGridSubCard(canvas, leftCard, vm, palette, isSpanish, state.UseAcrylicBackdrop, isLight);
        DrawBatterySubCard(canvas, rightCard, vm, palette, isSpanish, state.UseAcrylicBackdrop, isLight);

        int loadBandTop = subCardTop + PopoverLayoutConstants.SubCardHeight + PopoverLayoutConstants.SectionGap;
        var loadBandRect = new SKRect(
            PopoverLayoutConstants.OuterMargin,
            loadBandTop,
            PopoverLayoutConstants.OuterMargin + contentWidth,
            loadBandTop + PopoverLayoutConstants.LoadBandHeight);
        DrawLoadBand(canvas, loadBandRect, vm, palette, isSpanish, state.UseAcrylicBackdrop, isLight);

        var layout = new PopoverHitTestLayout(width, height);
        DrawActionButton(
            canvas,
            layout.SelfTestButton,
            SvgIconConstants.MenuBatteryTest,
            vm.SelfTestButtonLabel,
            palette,
            ParseColor(palette.Accent),
            state.HoveredAction == PopoverActionId.SelfTest && !vm.IsTestInProgress,
            state.PressedAction == PopoverActionId.SelfTest && !vm.IsTestInProgress,
            state.FocusedActionIndex == 0,
            vm.IsTestInProgress);

        string beeperIcon = vm.IsBeeperMuted
            ? SvgIconConstants.TelemetryBeeperMute
            : SvgIconConstants.TelemetryBeeper;
        SKColor beeperIconTint = vm.IsBeeperMuted
            ? ParseColor(palette.TextMuted)
            : ParseColor(palette.Ok);
        DrawActionButton(
            canvas,
            layout.ToggleBeeperButton,
            beeperIcon,
            vm.BeeperButtonLabel,
            palette,
            beeperIconTint,
            state.HoveredAction == PopoverActionId.ToggleBeeper,
            state.PressedAction == PopoverActionId.ToggleBeeper,
            state.FocusedActionIndex == 1,
            isDisabled: false);

        return surface;
    }

    private static void DrawCardFrame(SKCanvas canvas, int width, int height, ThemePalette palette, bool isLight)
    {
        using var borderPaint = new SKPaint
        {
            Color = ParseColor(palette.Border),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = PopoverLayoutConstants.StrokeThin,
            IsAntialias = true
        };

        var rect = new SKRect(0.5f, 0.5f, width - 0.5f, height - 0.5f);
        canvas.DrawRoundRect(rect, LayoutTokens.CardRadius, LayoutTokens.CardRadius, borderPaint);

        using var highlightPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(
                isLight ? PopoverLayoutConstants.SpecularHighlightAlphaLight : PopoverLayoutConstants.SpecularHighlightAlphaDark),
            StrokeWidth = PopoverLayoutConstants.StrokeThin,
            IsAntialias = true
        };
        canvas.DrawLine(LayoutTokens.CardRadius, 1.5f, width - LayoutTokens.CardRadius, 1.5f, highlightPaint);
    }

    private void DrawHeader(SKCanvas canvas, int width, PopoverViewModel vm, ThemePalette palette, bool isLight)
    {
        using var titleFont = new SKFont(_boldTypeface, PopoverLayoutConstants.FontTitleSize);
        using var subFont = new SKFont(_regularTypeface, PopoverLayoutConstants.FontSubtitleSize);
        using var chipFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontChipSize);

        (SKColor chipBg, SKColor chipFg, string? chipIcon) = ResolveStatusChipStyle(vm.State, palette, isLight);

        float chipTextWidth = chipFont.MeasureText(vm.StatusLabel);
        float iconSectionWidth = chipIcon != null
            ? PopoverLayoutConstants.IconSizeSm + PopoverLayoutConstants.ChipIconGap
            : 0f;
        float chipWidth = chipTextWidth + iconSectionWidth + (PopoverLayoutConstants.ChipHorizontalPadding * 2);
        float chipRight = width - PopoverLayoutConstants.OuterMargin;
        float chipLeft = chipRight - chipWidth;
        float chipTop = PopoverLayoutConstants.OuterMargin + 4f;
        var chipRect = new SKRect(chipLeft, chipTop, chipRight, chipTop + PopoverLayoutConstants.ChipHeight);

        using var chipFill = new SKPaint { Color = chipBg, IsAntialias = true };
        using var chipBorder = new SKPaint
        {
            Color = chipFg.WithAlpha(PopoverLayoutConstants.DividerAlpha),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = PopoverLayoutConstants.StrokeThin,
            IsAntialias = true
        };
        canvas.DrawRoundRect(chipRect, LayoutTokens.ChipRadius, LayoutTokens.ChipRadius, chipFill);
        canvas.DrawRoundRect(chipRect, LayoutTokens.ChipRadius, LayoutTokens.ChipRadius, chipBorder);

        float chipContentX = chipRect.Left + PopoverLayoutConstants.ChipHorizontalPadding;
        if (chipIcon != null)
        {
            float iconTop = chipRect.MidY - (PopoverLayoutConstants.IconSizeSm / 2f);
            var iconRect = new SKRect(
                chipContentX,
                iconTop,
                chipContentX + PopoverLayoutConstants.IconSizeSm,
                iconTop + PopoverLayoutConstants.IconSizeSm);
            _svgIconLibrary.TryDraw(canvas, chipIcon, iconRect, chipFg);
            chipContentX += PopoverLayoutConstants.IconSizeSm + PopoverLayoutConstants.ChipIconGap;
        }

        using var chipTextPaint = new SKPaint { Color = chipFg, IsAntialias = true };
        DrawTextBaselineCentered(canvas, vm.StatusLabel, chipContentX, chipRect.MidY, chipFont, chipTextPaint);

        using var titlePaint = new SKPaint { Color = ParseColor(palette.Text), IsAntialias = true };
        using var subPaint = new SKPaint { Color = ParseColor(palette.TextMuted), IsAntialias = true };
        float maxTitleWidth = chipLeft - PopoverLayoutConstants.OuterMargin - PopoverLayoutConstants.ColumnGap;
        string title = EllipsizeText(vm.Title, titleFont, maxTitleWidth);
        string subtitle = EllipsizeText(vm.BankSubtitle, subFont, maxTitleWidth);

        canvas.DrawText(title, PopoverLayoutConstants.OuterMargin, PopoverLayoutConstants.OuterMargin + 15f, SKTextAlign.Left, titleFont, titlePaint);
        canvas.DrawText(subtitle, PopoverLayoutConstants.OuterMargin, PopoverLayoutConstants.OuterMargin + 33f, SKTextAlign.Left, subFont, subPaint);

        float dividerY = PopoverLayoutConstants.OuterMargin + PopoverLayoutConstants.HeaderHeight - 2f;
        using var dividerPaint = new SKPaint
        {
            Color = ParseColor(palette.Border).WithAlpha(PopoverLayoutConstants.DividerAlpha),
            StrokeWidth = PopoverLayoutConstants.StrokeThin
        };
        canvas.DrawLine(PopoverLayoutConstants.OuterMargin, dividerY, width - PopoverLayoutConstants.OuterMargin, dividerY, dividerPaint);
    }

    private void DrawGridSubCard(
        SKCanvas canvas,
        SKRect bounds,
        PopoverViewModel vm,
        ThemePalette palette,
        bool isSpanish,
        bool useAcrylicBackdrop,
        bool isLight)
    {
        DrawInsetCard(canvas, bounds, palette, useAcrylicBackdrop, isLight);

        float pad = PopoverLayoutConstants.SubCardPadding;
        float innerLeft = bounds.Left + pad;
        float innerRight = bounds.Right - pad;
        float headerY = bounds.Top + pad;

        using var sectionFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontSectionSize);
        using var microFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontMicroLabelSize);
        using var heroFont = new SKFont(_boldTypeface, PopoverLayoutConstants.FontHeroSize);
        using var metricFont = new SKFont(_regularTypeface, PopoverLayoutConstants.FontMetricSize);
        using var metricStrongFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontMetricSize);
        using var textPaint = new SKPaint { Color = ParseColor(palette.Text), IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = ParseColor(palette.TextMuted), IsAntialias = true };
        using var accentPaint = new SKPaint { Color = ParseColor(palette.Accent), IsAntialias = true };

        var iconRect = new SKRect(innerLeft, headerY, innerLeft + PopoverLayoutConstants.IconSizeSm, headerY + PopoverLayoutConstants.IconSizeSm);
        _svgIconLibrary.TryDraw(canvas, SvgIconConstants.TelemetryGrid, iconRect, ParseColor(palette.Accent));

        string sectionLabel = isSpanish ? LocalizationConstants.GridSectionEs : LocalizationConstants.GridSectionEn;
        DrawTextBaselineCentered(canvas, sectionLabel, iconRect.Right + 5f, iconRect.MidY, sectionFont, textPaint);
        DrawTextRightAligned(canvas, vm.FrequencyText, innerRight, iconRect.MidY, metricStrongFont, accentPaint);

        string inputMicro = isSpanish ? LocalizationConstants.InputAcMicroLabelEs : LocalizationConstants.InputAcMicroLabelEn;
        canvas.DrawText(inputMicro, innerLeft, bounds.Top + 44f, SKTextAlign.Left, microFont, mutedPaint);
        canvas.DrawText(vm.InputVoltageText, innerLeft, bounds.Top + 69f, SKTextAlign.Left, heroFont, textPaint);

        float dividerY = bounds.Top + 86f;
        DrawInnerDivider(canvas, innerLeft, innerRight, dividerY, palette);

        string outputLabel = isSpanish ? LocalizationConstants.OutputLabelEs : LocalizationConstants.OutputLabelEn;
        float row1Y = bounds.Top + 106f;
        canvas.DrawText(outputLabel, innerLeft, row1Y, SKTextAlign.Left, metricFont, mutedPaint);
        using var okPaint = new SKPaint { Color = ParseColor(palette.Ok), IsAntialias = true };
        DrawTextRightAligned(canvas, vm.OutputVoltageText, innerRight, row1Y - 4f, metricStrongFont, okPaint);

        string modeLabel = isSpanish ? LocalizationConstants.ModeLabelEs : LocalizationConstants.ModeLabelEn;
        float row2Y = bounds.Top + 127f;
        canvas.DrawText(modeLabel, innerLeft, row2Y, SKTextAlign.Left, metricFont, mutedPaint);
        using var modePaint = new SKPaint { Color = ResolveBatteryAccentColor(vm.State, palette), IsAntialias = true };
        DrawTextRightAligned(canvas, vm.RegulationModeText, innerRight, row2Y - 4f, metricStrongFont, modePaint);

        float row3Y = bounds.Top + 148f;
        if (!string.IsNullOrWhiteSpace(vm.TemperatureText))
        {
            var tempIconRect = new SKRect(innerLeft, row3Y - 10f, innerLeft + 12f, row3Y + 2f);
            _svgIconLibrary.TryDraw(canvas, SvgIconConstants.TelemetryTemperature, tempIconRect, ParseColor(palette.TextMuted));
            string tempLabel = isSpanish ? LocalizationConstants.TemperatureLabelEs : LocalizationConstants.TemperatureLabelEn;
            canvas.DrawText(tempLabel, tempIconRect.Right + 4f, row3Y, SKTextAlign.Left, metricFont, mutedPaint);
            DrawTextRightAligned(canvas, vm.TemperatureText, innerRight, row3Y - 4f, metricStrongFont, textPaint);
        }
        else if (!string.IsNullOrWhiteSpace(vm.FaultReferenceText))
        {
            string faultLabel = isSpanish ? LocalizationConstants.LastFaultLabelEs : LocalizationConstants.LastFaultLabelEn;
            canvas.DrawText(faultLabel, innerLeft, row3Y, SKTextAlign.Left, metricFont, mutedPaint);
            DrawTextRightAligned(canvas, vm.FaultReferenceText, innerRight, row3Y - 4f, metricFont, mutedPaint);
        }
    }

    private void DrawBatterySubCard(
        SKCanvas canvas,
        SKRect bounds,
        PopoverViewModel vm,
        ThemePalette palette,
        bool isSpanish,
        bool useAcrylicBackdrop,
        bool isLight)
    {
        DrawInsetCard(canvas, bounds, palette, useAcrylicBackdrop, isLight);

        float pad = PopoverLayoutConstants.SubCardPadding;
        float innerLeft = bounds.Left + pad;
        float innerRight = bounds.Right - pad;
        float headerY = bounds.Top + pad;

        using var sectionFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontSectionSize);
        using var microFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontMicroLabelSize);
        using var heroFont = new SKFont(_boldTypeface, PopoverLayoutConstants.FontHeroLargeSize);
        using var metricFont = new SKFont(_regularTypeface, PopoverLayoutConstants.FontMetricSize);
        using var metricStrongFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontMetricSize);
        using var textPaint = new SKPaint { Color = ParseColor(palette.Text), IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = ParseColor(palette.TextMuted), IsAntialias = true };

        SKColor stateColor = ResolveBatteryAccentColor(vm.State, palette);
        using var accentPaint = new SKPaint { Color = stateColor, IsAntialias = true };

        var iconRect = new SKRect(innerLeft, headerY, innerLeft + PopoverLayoutConstants.IconSizeSm, headerY + PopoverLayoutConstants.IconSizeSm);
        _svgIconLibrary.TryDraw(canvas, SvgIconConstants.TelemetryBatteryBank, iconRect, stateColor);

        string sectionLabel = isSpanish ? LocalizationConstants.BatterySectionEs : LocalizationConstants.BatterySectionEn;
        DrawTextBaselineCentered(canvas, sectionLabel, iconRect.Right + 5f, iconRect.MidY, sectionFont, textPaint);
        DrawTextRightAligned(canvas, vm.BatteryVoltageText, innerRight, iconRect.MidY, metricStrongFont, accentPaint);

        string runtimeMicro = isSpanish ? LocalizationConstants.RuntimeMicroLabelEs : LocalizationConstants.RuntimeMicroLabelEn;
        canvas.DrawText(runtimeMicro, innerLeft, bounds.Top + 44f, SKTextAlign.Left, microFont, mutedPaint);

        string heroText = vm.RuntimeText != LocalizationConstants.UnavailablePlaceholder
            ? vm.RuntimeText
            : vm.BatteryPercentageText;
        canvas.DrawText(heroText, innerLeft, bounds.Top + 69f, SKTextAlign.Left, heroFont, accentPaint);

        string capacityLabel = isSpanish ? LocalizationConstants.CapacityLabelEs : LocalizationConstants.CapacityLabelEn;
        canvas.DrawText(capacityLabel, innerLeft, bounds.Top + 84f, SKTextAlign.Left, metricFont, mutedPaint);
        DrawTextRightAligned(canvas, vm.BatteryPercentageText, innerRight, bounds.Top + 80f, metricStrongFont, textPaint);

        float dividerY = bounds.Top + 94f;
        DrawInnerDivider(canvas, innerLeft, innerRight, dividerY, palette);

        if (!string.IsNullOrWhiteSpace(vm.Cell1Text))
        {
            float row1Y = bounds.Top + 112f;
            canvas.DrawText(LocalizationConstants.Cell1Label, innerLeft, row1Y, SKTextAlign.Left, metricFont, mutedPaint);
            DrawTextRightAligned(canvas, vm.Cell1Text, innerRight, row1Y - 4f, metricStrongFont, accentPaint);
        }

        if (!string.IsNullOrWhiteSpace(vm.Cell2Text))
        {
            float row2Y = bounds.Top + 131f;
            canvas.DrawText(LocalizationConstants.Cell2Label, innerLeft, row2Y, SKTextAlign.Left, metricFont, mutedPaint);
            DrawTextRightAligned(canvas, vm.Cell2Text, innerRight, row2Y - 4f, metricStrongFont, accentPaint);
        }

        string loadSuffix = isSpanish ? LocalizationConstants.LoadCapacitySuffixEs : LocalizationConstants.LoadCapacitySuffixEn;
        string footer = vm.BankEnergyWhText is not null
            ? $"{vm.BankEnergyWhText} · {vm.LoadPercentageText} {loadSuffix}"
            : $"{vm.LoadPercentageText} {loadSuffix}";
        canvas.DrawText(EllipsizeText(footer, metricFont, innerRight - innerLeft), innerLeft, bounds.Top + 152f, SKTextAlign.Left, metricFont, mutedPaint);
    }

    private void DrawLoadBand(
        SKCanvas canvas,
        SKRect bounds,
        PopoverViewModel vm,
        ThemePalette palette,
        bool isSpanish,
        bool useAcrylicBackdrop,
        bool isLight)
    {
        DrawInsetCard(canvas, bounds, palette, useAcrylicBackdrop, isLight);

        float pad = PopoverLayoutConstants.LoadBandPadding;
        float innerLeft = bounds.Left + pad;
        float innerRight = bounds.Right - pad;
        float topY = bounds.Top + pad;

        using var sectionFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontButtonSize);
        using var textPaint = new SKPaint { Color = ParseColor(palette.Text), IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = ParseColor(palette.TextMuted), IsAntialias = true };

        SKColor barColor = vm.LoadPercentage switch
        {
            >= PopoverLayoutConstants.LoadDangerThreshold => ParseColor(palette.Danger),
            >= PopoverLayoutConstants.LoadWarningThreshold => ParseColor(palette.Warning),
            _ => ParseColor(palette.Ok)
        };

        var iconRect = new SKRect(innerLeft, topY, innerLeft + PopoverLayoutConstants.IconSizeMd, topY + PopoverLayoutConstants.IconSizeMd);
        _svgIconLibrary.TryDraw(canvas, SvgIconConstants.TelemetryLoad, iconRect, ParseColor(palette.Warning));

        string sectionLabel = isSpanish ? LocalizationConstants.ActiveLoadSectionEs : LocalizationConstants.ActiveLoadSectionEn;
        DrawTextBaselineCentered(canvas, sectionLabel, iconRect.Right + 6f, iconRect.MidY, sectionFont, textPaint);

        using var summaryPaint = new SKPaint { Color = barColor, IsAntialias = true };
        if (!string.IsNullOrWhiteSpace(vm.ActiveLoadCeilingText))
        {
            using var ceilingFont = new SKFont(_regularTypeface, PopoverLayoutConstants.FontMetricSize);
            float ceilingWidth = ceilingFont.MeasureText(vm.ActiveLoadCeilingText);
            DrawTextRightAligned(canvas, vm.ActiveLoadCeilingText, innerRight, iconRect.MidY, ceilingFont, mutedPaint);
            DrawTextRightAligned(canvas, vm.LoadPercentageText, innerRight - ceilingWidth - 5f, iconRect.MidY, sectionFont, summaryPaint);
        }
        else
        {
            DrawTextRightAligned(canvas, vm.LoadPercentageText, innerRight, iconRect.MidY, sectionFont, summaryPaint);
        }

        float barTop = bounds.Top + 39f;
        var trackRect = new SKRect(innerLeft, barTop, innerRight, barTop + PopoverLayoutConstants.BarHeight);
        using var trackPaint = new SKPaint { Color = ParseColor(palette.Border), IsAntialias = true };
        canvas.DrawRoundRect(trackRect, LayoutTokens.BarRadius, LayoutTokens.BarRadius, trackPaint);

        if (vm.LoadPercentage > 0)
        {
            float fillWidth = Math.Max(
                PopoverLayoutConstants.BarHeight,
                trackRect.Width * (vm.LoadPercentage / 100f));
            var fillRect = new SKRect(
                trackRect.Left,
                trackRect.Top,
                Math.Min(trackRect.Right, trackRect.Left + fillWidth),
                trackRect.Bottom);

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(fillRect.Left, fillRect.MidY),
                new SKPoint(fillRect.Right, fillRect.MidY),
                [barColor.WithAlpha(220), barColor],
                SKShaderTileMode.Clamp);
            using var fillPaint = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.DrawRoundRect(fillRect, LayoutTokens.BarRadius, LayoutTokens.BarRadius, fillPaint);
        }
    }

    private void DrawActionButton(
        SKCanvas canvas,
        ViewRect rect,
        string iconName,
        string label,
        ThemePalette palette,
        SKColor iconColor,
        bool isHovered,
        bool isPressed,
        bool isFocused,
        bool isDisabled)
    {
        var skRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

        SKColor fillColor = isPressed
            ? ParseColor(palette.Border)
            : isHovered
                ? ParseColor(palette.Hover)
                : ParseColor(palette.ButtonSecondary);
        SKColor borderColor = isFocused
            ? ParseColor(palette.Accent)
            : isHovered
                ? ParseColor(palette.BorderStrong)
                : ParseColor(palette.Border);
        SKColor contentColor = isDisabled
            ? ParseColor(palette.TextMuted)
            : ParseColor(palette.Text);
        SKColor resolvedIconColor = isDisabled ? ParseColor(palette.TextMuted) : iconColor;

        using var fillPaint = new SKPaint { Color = fillColor, IsAntialias = true };
        using var borderPaint = new SKPaint
        {
            Color = borderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isFocused ? PopoverLayoutConstants.StrokeFocus : PopoverLayoutConstants.StrokeThin,
            IsAntialias = true
        };
        using var textFont = new SKFont(_semiBoldTypeface, PopoverLayoutConstants.FontButtonSize);
        using var textPaint = new SKPaint { Color = contentColor, IsAntialias = true };

        canvas.DrawRoundRect(skRect, LayoutTokens.ButtonRadius, LayoutTokens.ButtonRadius, fillPaint);
        canvas.DrawRoundRect(skRect, LayoutTokens.ButtonRadius, LayoutTokens.ButtonRadius, borderPaint);

        float maxLabelWidth = skRect.Width - PopoverLayoutConstants.IconSizeMd - PopoverLayoutConstants.ButtonIconGap - 16f;
        string displayLabel = EllipsizeText(label, textFont, maxLabelWidth);
        float textWidth = textFont.MeasureText(displayLabel);
        float totalContentWidth = PopoverLayoutConstants.IconSizeMd + PopoverLayoutConstants.ButtonIconGap + textWidth;
        float startX = skRect.MidX - (totalContentWidth / 2f);

        var iconRect = new SKRect(
            startX,
            skRect.MidY - (PopoverLayoutConstants.IconSizeMd / 2f),
            startX + PopoverLayoutConstants.IconSizeMd,
            skRect.MidY + (PopoverLayoutConstants.IconSizeMd / 2f));
        _svgIconLibrary.TryDraw(canvas, iconName, iconRect, resolvedIconColor);
        DrawTextBaselineCentered(
            canvas,
            displayLabel,
            iconRect.Right + PopoverLayoutConstants.ButtonIconGap,
            skRect.MidY,
            textFont,
            textPaint);
    }

    private static void DrawInsetCard(
        SKCanvas canvas,
        SKRect rect,
        ThemePalette palette,
        bool useAcrylicBackdrop,
        bool isLight)
    {
        SKColor insetColor = ParseColor(palette.SurfaceInset);
        if (useAcrylicBackdrop)
        {
            insetColor = insetColor.WithAlpha(
                isLight ? PopoverLayoutConstants.AcrylicLightInsetAlpha : PopoverLayoutConstants.AcrylicDarkInsetAlpha);
        }

        using var fillPaint = new SKPaint { Color = insetColor, IsAntialias = true };
        using var borderPaint = new SKPaint
        {
            Color = ParseColor(palette.Border),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = PopoverLayoutConstants.StrokeThin,
            IsAntialias = true
        };

        canvas.DrawRoundRect(rect, LayoutTokens.CardRadius, LayoutTokens.CardRadius, fillPaint);
        canvas.DrawRoundRect(rect, LayoutTokens.CardRadius, LayoutTokens.CardRadius, borderPaint);
    }

    private static void DrawInnerDivider(SKCanvas canvas, float left, float right, float y, ThemePalette palette)
    {
        using var paint = new SKPaint
        {
            Color = ParseColor(palette.Border).WithAlpha(PopoverLayoutConstants.DividerAlpha),
            StrokeWidth = PopoverLayoutConstants.StrokeThin
        };
        canvas.DrawLine(left, y, right, y, paint);
    }

    private static (SKColor Background, SKColor Foreground, string? IconName) ResolveStatusChipStyle(
        UpsOperationalState state,
        ThemePalette palette,
        bool isLight)
    {
        return state switch
        {
            UpsOperationalState.LineNormal => (
                ParseColor(palette.OkChipBg),
                ParseColor(palette.OkChipText),
                SvgIconConstants.StatusCheck),
            UpsOperationalState.OnBattery or UpsOperationalState.Paused => (
                ParseColor(isLight ? PopoverLayoutConstants.LightAmberChipBg : PopoverLayoutConstants.DarkAmberChipBg),
                ParseColor(palette.Warning),
                null),
            UpsOperationalState.BatteryLow or UpsOperationalState.Fault => (
                ParseColor(isLight ? PopoverLayoutConstants.LightRedChipBg : PopoverLayoutConstants.DarkRedChipBg),
                ParseColor(palette.Danger),
                null),
            UpsOperationalState.ChargingAvr => (
                ParseColor(isLight ? PopoverLayoutConstants.LightCyanChipBg : PopoverLayoutConstants.DarkCyanChipBg),
                ParseColor(isLight ? PopoverLayoutConstants.LightCyanText : PopoverLayoutConstants.DarkCyanText),
                null),
            _ => (
                ParseColor(isLight ? PopoverLayoutConstants.LightNeutralChipBg : PopoverLayoutConstants.DarkNeutralChipBg),
                ParseColor(palette.TextMuted),
                null)
        };
    }

    private static SKColor ResolveBatteryAccentColor(UpsOperationalState state, ThemePalette palette)
    {
        return state switch
        {
            UpsOperationalState.LineNormal => ParseColor(palette.Ok),
            UpsOperationalState.OnBattery or UpsOperationalState.Paused => ParseColor(palette.Warning),
            UpsOperationalState.BatteryLow or UpsOperationalState.Fault => ParseColor(palette.Danger),
            UpsOperationalState.ChargingAvr => ParseColor(palette.Accent),
            _ => ParseColor(palette.TextMuted)
        };
    }

    private static void DrawTextBaselineCentered(
        SKCanvas canvas,
        string text,
        float x,
        float centerY,
        SKFont font,
        SKPaint paint)
    {
        SKFontMetrics metrics = font.Metrics;
        float baseline = centerY - ((metrics.Ascent + metrics.Descent) / 2f);
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
    }

    private static void DrawTextRightAligned(
        SKCanvas canvas,
        string text,
        float rightX,
        float centerY,
        SKFont font,
        SKPaint paint)
    {
        SKFontMetrics metrics = font.Metrics;
        float baseline = centerY - ((metrics.Ascent + metrics.Descent) / 2f);
        canvas.DrawText(text, rightX, baseline, SKTextAlign.Right, font, paint);
    }

    private static string EllipsizeText(string text, SKFont font, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f || font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        int len = text.Length;
        while (len > 1 && font.MeasureText(string.Concat(text.AsSpan(0, len), ellipsis)) > maxWidth)
        {
            len--;
        }

        return string.Concat(text.AsSpan(0, len), ellipsis);
    }

    private static SKTypeface ResolveTypeface(SKFontStyleWeight weight)
    {
        var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        return SKTypeface.FromFamilyName(PopoverLayoutConstants.PrimaryFontFamily, style)
            ?? SKTypeface.FromFamilyName(PopoverLayoutConstants.FallbackFontFamily, style)
            ?? SKTypeface.Default;
    }

    private static SKColor ParseColor(string hex)
    {
        return SKColor.Parse(hex);
    }
}
