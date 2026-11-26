using VoltTray.Core.Models;
using VoltTray.Shared;

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
}
