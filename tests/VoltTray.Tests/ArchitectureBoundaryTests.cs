using VoltTray.UI.Tray;
using VoltTray.UI.Popover;
using VoltTray.UI.Dialogs;
using VoltTray.Core.Telemetry;
using System.Reflection;
using System.Text.Json.Serialization;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;
using VoltTray.UI.Tokens;

namespace VoltTray.Tests;

public class ArchitectureBoundaryTests
{
    private static readonly string[] ForbiddenUiNamespaces =
    [
        ".Tokens",
        ".Views",
        ".Presenters"
    ];

    private static readonly string[] ForbiddenUiTypeNames =
    [
        "UITheme",
        "TaskbarEdge",
        "ExitDialogResult",
        "ViewRect",
        "DesignTokens",
        "SplitCockpitPresenter",
        "ITrayIconView",
        "IPopoverView",
        "IExitDialogView",
        "IVoltTrayView"
    ];

    [Fact]
    public void VoltTrayCoreAssembly_ContainsNoUiNamespacesOrTypes()
    {
        var coreAssembly = typeof(UpsTelemetry).Assembly;
        Type[] coreTypes = coreAssembly.GetTypes();

        var violatingNamespaces = coreTypes
            .Where(type => type.Namespace != null && ForbiddenUiNamespaces.Any(ns => type.Namespace.EndsWith(ns, StringComparison.Ordinal) || type.Namespace.Contains($"{ns}.", StringComparison.Ordinal)))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        var violatingTypeNames = coreTypes
            .Where(type => ForbiddenUiTypeNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.Empty(violatingNamespaces);
        Assert.Empty(violatingTypeNames);
    }

    [Fact]
    public void VoltTrayCoreAssembly_DoesNotReferenceUiOrInfrastructureAssemblies()
    {
        var coreAssembly = typeof(UpsTelemetry).Assembly;
        string[] referencedNames = coreAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        string[] referencedVoltTrayAssemblies = referencedNames
            .Where(name => name.StartsWith("VoltTray.", StringComparison.Ordinal) || name == "VoltTray")
            .ToArray();

        Assert.DoesNotContain("VoltTray.UI", referencedNames);
        Assert.DoesNotContain("VoltTray.Infrastructure", referencedNames);
        Assert.DoesNotContain("VoltTray", referencedNames);
        Assert.All(referencedVoltTrayAssemblies, name => Assert.Equal("VoltTray.Shared", name));
    }

    [Fact]
    public void VoltTraySharedAssembly_HasZeroVoltTrayProjectReferencesAndNoPlatformOrUiLeakage()
    {
        var sharedAssembly = typeof(ElectricalConstants).Assembly;
        string[] referencedVoltTrayAssemblies = sharedAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .Where(name => name.StartsWith("VoltTray.", StringComparison.Ordinal) || name == "VoltTray")
            .ToArray();

        Assert.Empty(referencedVoltTrayAssemblies);

        Type[] sharedTypes = sharedAssembly.GetTypes();
        Assert.DoesNotContain(
            sharedTypes,
            type => type.Namespace != null &&
                    (type.Namespace.Contains("Hid", StringComparison.OrdinalIgnoreCase) ||
                     type.Namespace.Contains("UI", StringComparison.OrdinalIgnoreCase) ||
                     type.Namespace.Contains("Protocol", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BehavioralClasses_DoNotDeclareLooseConstants_OutsideDedicatedCatalogsOrInterop()
    {
        Assembly[] productionAssemblies =
        [
            typeof(ElectricalConstants).Assembly,
            typeof(UpsTelemetry).Assembly,
            typeof(VoltronicParser).Assembly,
            typeof(DesignTokens).Assembly,
            typeof(HostLifecycle).Assembly
        ];

        string[] allowedSuffixes =
        [
            "Constants",
            "Defaults",
            "ExitCodes",
            "Tokens",
            "NativeMethods",
            "GdiNative"
        ];

        var violations = new List<string>();

        foreach (Assembly assembly in productionAssemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsEnum ||
                    type.Name.StartsWith("<", StringComparison.Ordinal) ||
                    (type.Namespace != null && type.Namespace.StartsWith("Windows.Win32", StringComparison.Ordinal)) ||
                    typeof(JsonSerializerContext).IsAssignableFrom(type))
                {
                    continue;
                }

                bool isAllowedCatalog =
                    allowedSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)) ||
                    (type.DeclaringType != null && type.DeclaringType.Name == nameof(DesignTokens));

                if (isAllowedCatalog)
                {
                    continue;
                }

                FieldInfo[] literalFields = type
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(field => field.IsLiteral && !field.IsInitOnly)
                    .ToArray();

                if (literalFields.Length > 0)
                {
                    string fieldNames = string.Join(", ", literalFields.Select(field => field.Name));
                    violations.Add($"{type.FullName}: [{fieldNames}]");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void LayerServiceImplementations_RemainInternal_ExceptExplicitCompositionEntryPoints()
    {
        Assembly[] layerAssemblies =
        [
            typeof(DesignTokens).Assembly,
            typeof(VoltronicParser).Assembly
        ];

        HashSet<string> allowedPublicEntryPoints = new(StringComparer.Ordinal)
        {
            "TrayRenderer",
            "PopoverPainter",
            "ExitDialog",
            "Win32ScreenPlacementProvider",
            "InProcessTelemetryProbe",
            "SafeExecutionMiddleware",
            "WindowsShutdownPort",
            "WindowsNotificationPort",
            "SimulatedUpsDevicePort",
            "Win32HidUpsPort",
            "ReadOnlyUpsProbe"
        };

        string[] unexpectedPublicServices = layerAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type.IsPublic &&
                type.GetInterfaces().Any(iface =>
                    iface.Namespace != null &&
                    iface.Namespace.StartsWith("VoltTray.", StringComparison.Ordinal)) &&
                !allowedPublicEntryPoints.Contains(type.Name))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedPublicServices);
    }
}
