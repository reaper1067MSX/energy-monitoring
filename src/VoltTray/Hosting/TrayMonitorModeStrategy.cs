using Microsoft.Extensions.DependencyInjection;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.DependencyInjection;
using VoltTray.Infrastructure.Hid;
using VoltTray.Shared;
using VoltTray.UI.DependencyInjection;
using VoltTray.UI.Dialogs;

namespace VoltTray.Hosting;

/// <summary>
/// Executes the interactive system-tray UPS monitoring lifecycle.
/// </summary>
public sealed class TrayMonitorModeStrategy : IApplicationModeStrategy
{
    private readonly TrayMonitorLaunchOptions _options;

    public TrayMonitorModeStrategy(TrayMonitorLaunchOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public int Execute()
    {
        using Mutex? mutex = SingleInstanceManager.TryAcquire();
        if (mutex == null)
        {
            return ApplicationExitCodes.Success;
        }

        try
        {
            UpsModelProfile modelProfile = ResolveModelProfile(_options);
            using var bootstrapper = new TrayMonitorBootstrapper(_options, () => CreateUpsDevicePort(_options, modelProfile));
            IHostLifecycle lifecycle = bootstrapper.Lifecycle;

            lifecycle.ExitAction = () => Win32MessagePump.PostQuit(ApplicationExitCodes.Success);
            lifecycle.Start(_options.PollingIntervalMs);

            int exitCode = Win32MessagePump.Run();
            lifecycle.Stop();
            return exitCode;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore abandoned or non-owned mutex state on shutdown.
            }
        }
    }

    internal static IUpsDevicePort CreateUpsDevicePort(
        TrayMonitorLaunchOptions options,
        UpsModelProfile? modelProfile = null)
    {
        return options.IsSimulation
            ? new SimulatedUpsDevicePort(modelProfile)
            : new Win32HidUpsPort(modelProfile: modelProfile);
    }

    private static UpsModelProfile ResolveModelProfile(TrayMonitorLaunchOptions options)
    {
        var services = new ServiceCollection();
        services.AddVoltTrayInfrastructure(() => new SimulatedUpsDevicePort());
        services.AddVoltTrayUi(enableTaskbarPill: false);
        using ServiceProvider setupProvider = services.BuildServiceProvider();

        IUserSettingsStore settingsStore = setupProvider.GetRequiredService<IUserSettingsStore>();
        if (options.SelectedModel is UpsModelPreset explicitPreset)
        {
            settingsStore.SaveSelectedModel(explicitPreset);
            return UpsModelCatalog.GetProfile(explicitPreset);
        }

        if (settingsStore.LoadSelectedModel() is UpsModelPreset savedPreset)
        {
            return UpsModelCatalog.GetProfile(savedPreset);
        }

        if (!options.IsSimulation)
        {
            IModelSelectorDialogView selectorDialog = setupProvider.GetRequiredService<IModelSelectorDialogView>();
            if (selectorDialog.Show() is UpsModelPreset chosenPreset)
            {
                settingsStore.SaveSelectedModel(chosenPreset);
                return UpsModelCatalog.GetProfile(chosenPreset);
            }
        }

        return UpsModelCatalog.GetProfile(UpsModelPreset.ForzaNt2401Pro);
    }
}
