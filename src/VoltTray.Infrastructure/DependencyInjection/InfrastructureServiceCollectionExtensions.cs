using Microsoft.Extensions.DependencyInjection;
using VoltTray.Core.Diagnostics;
using VoltTray.Core.Safety;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.OS;
using VoltTray.Infrastructure.Settings;

namespace VoltTray.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddVoltTrayInfrastructure(
        this IServiceCollection services,
        Func<IUpsDevicePort> rawPortFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rawPortFactory);

        services.AddSingleton<InProcessTelemetryProbe>();
        services.AddSingleton<ITelemetryDiagnosticSink>(sp => sp.GetRequiredService<InProcessTelemetryProbe>());
        services.AddSingleton<IExecutionBoundary>(sp => new SafeExecutionMiddleware(sp.GetRequiredService<InProcessTelemetryProbe>()));
        services.AddSingleton<IUpsDevicePort>(sp => new DiagnosticUpsPortDecorator(
            rawPortFactory(),
            sp.GetRequiredService<InProcessTelemetryProbe>()));
        services.AddSingleton<ISafetyShutdownPort, WindowsShutdownPort>();
        services.AddSingleton<INotificationPort, WindowsNotificationPort>();
        services.AddSingleton<IUserSettingsStore>(_ => new JsonUserSettingsStore());

        return services;
    }
}
