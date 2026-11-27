using Microsoft.Extensions.DependencyInjection;
using VoltTray.UI.Atoms;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Popover;
using VoltTray.UI.TaskbarPill;
using VoltTray.UI.Tray;

namespace VoltTray.UI.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    public static IServiceCollection AddVoltTrayUi(
        this IServiceCollection services,
        bool enableTaskbarPill,
        bool? showTrayIcon = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISvgIconLibrary, SkiaSvgIconLibrary>();
        services.AddSingleton<ITrayIconRasterizer>(sp => new TrayIconRasterizer(sp.GetRequiredService<ISvgIconLibrary>()));
        services.AddSingleton<INativeIconFactory, Win32NativeIconFactory>();
        services.AddSingleton<IPopoverSurfaceRenderer>(sp => new SkiaPopoverSurfaceRenderer(sp.GetRequiredService<ISvgIconLibrary>()));
        services.AddSingleton<IScreenPlacementProvider, Win32ScreenPlacementProvider>();

        services.AddSingleton<ITrayIconView>(sp => new TrayRenderer(
            sp.GetRequiredService<ITrayIconRasterizer>(),
            sp.GetRequiredService<INativeIconFactory>(),
            WindowsThemeDetector.IsLightTaskbar,
            showTrayIcon: showTrayIcon ?? !enableTaskbarPill));
        services.AddSingleton<IPopoverView>(sp => new PopoverPainter(
            enableNativeWindow: true,
            surfaceRenderer: sp.GetRequiredService<IPopoverSurfaceRenderer>()));
        services.AddSingleton<IContextMenuView>(_ => new Win32ContextMenu(enableNativeMenu: true));
        services.AddSingleton<IExitDialogView>(_ => new ExitDialog(enableNativeDialog: true));

        if (enableTaskbarPill)
        {
            services.AddSingleton<ITaskbarPillPlacementCalculator, TaskbarPillPlacementCalculator>();
            services.AddSingleton<ITaskbarPillSurfaceRenderer>(sp => new TaskbarPillSurfaceRenderer(
                sp.GetRequiredService<ITrayIconRasterizer>(),
                sp.GetRequiredService<ISvgIconLibrary>()));
            services.AddSingleton<IFullscreenStateProvider, Win32FullscreenStateProvider>();
            services.AddSingleton<ITaskbarPillView>(sp => new TaskbarPillWindow(
                sp.GetRequiredService<ITaskbarPillPlacementCalculator>(),
                sp.GetRequiredService<ITaskbarPillSurfaceRenderer>(),
                sp.GetRequiredService<IFullscreenStateProvider>(),
                WindowsThemeDetector.IsLightTaskbar,
                enableNativeWindow: true));
        }

        return services;
    }
}
