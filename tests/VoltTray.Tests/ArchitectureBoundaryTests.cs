using VoltTray.Core.Models;

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

        Assert.DoesNotContain("VoltTray.UI", referencedNames);
        Assert.DoesNotContain("VoltTray.Infrastructure", referencedNames);
        Assert.DoesNotContain("VoltTray", referencedNames);
    }
}
