using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;
using VoltTray.Tests.ObjectMothers;

namespace VoltTray.Tests;

public class StrategyAndRulePatternsTests
{
    private sealed class FakeShutdownPort : ISafetyShutdownPort
    {
        public int InitiateCalls { get; private set; }
        public string? LastReason { get; private set; }

        public bool InitiateShutdown(bool dryRun, string reason)
        {
            InitiateCalls++;
            LastReason = reason;
            return true;
        }

        public bool AbortShutdown() => true;
    }

    private sealed class FakeNotificationPort : INotificationPort
    {
        public void ShowNotification(string title, string message, NotificationSeverity severity)
        {
        }
    }

    private sealed class CustomAlwaysTripRule : IShutdownTriggerRule
    {
        public ShutdownTriggerEvaluation Evaluate(ShutdownEvaluationContext context)
        {
            return new ShutdownTriggerEvaluation(
                Applies: true,
                IsTriggered: true,
                RequiresDebounce: false,
                TriggerReason: "Custom rule triggered");
        }
    }

    private sealed class CustomUiDriftPhysicsRule : ITelemetryPhysicsRule
    {
        public TelemetryAnomalyKind? Evaluate(UpsTelemetry candidate, UpsTelemetry? lastKnownGood)
        {
            return candidate.LoadPercentage > 95
                ? TelemetryAnomalyKind.PhysicsImplausible
                : null;
        }
    }

    private sealed class CustomPreferredDialectProbe(string dialectId) : IProtocolDialectProbe
    {
        public NegotiatedUpsProtocol? TryProbe(
            Func<string, byte[]?> queryRaw,
            UpsCapabilities baseCapabilities,
            IBatteryAutonomyEstimator estimator)
        {
            return new NegotiatedUpsProtocol(
                new VoltronicQsHexProtocol(dialectId, estimator),
                baseCapabilities with { ModelName = $"Custom-{dialectId}", DialectVerified = true });
        }
    }

    [Fact]
    public void BatterySoCCurvePoint_And_PeukertEstimator_InterpolateExactBoundariesAndMidpoints()
    {
        var point = new BatterySoCCurvePoint(12.5, 70);
        Assert.Equal(12.5, point.BlockVoltageDc);
        Assert.Equal(70, point.Percentage);

        var estimator = new PeukertEstimator();
        Assert.Equal(100, estimator.CalculateSoCPercentage(27.0, seriesBatteryCount: 2));
        Assert.Equal(95, estimator.CalculateSoCPercentage(26.5, seriesBatteryCount: 2));
        Assert.Equal(23, estimator.CalculateSoCPercentage(22.5, seriesBatteryCount: 2));
        Assert.Equal(1, estimator.CalculateSoCPercentage(20.42, seriesBatteryCount: 2));
        Assert.Equal(0, estimator.CalculateSoCPercentage(20.4, seriesBatteryCount: 2));
    }

    [Fact]
    public void BatteryVoltageBand_And_BatteryBankInference_RejectGapVoltagesAndClassifyBands()
    {
        var band = new BatteryVoltageBand(1, 9.5, 15.5);
        Assert.Equal(1, band.SeriesCount);
        Assert.Equal(9.5, band.MinVoltageDc);
        Assert.Equal(15.5, band.MaxVoltageDc);

        Assert.Equal(1, BatteryBankInference.ClassifySeriesCount(13.7));
        Assert.Null(BatteryBankInference.ClassifySeriesCount(17.0));
        Assert.Equal(2, BatteryBankInference.ClassifySeriesCount(24.0));
        Assert.Equal(3, BatteryBankInference.ClassifySeriesCount(36.0));
        Assert.Equal(4, BatteryBankInference.ClassifySeriesCount(54.0));
        Assert.Null(BatteryBankInference.ClassifySeriesCount(65.0));
    }

    [Fact]
    public void ShutdownCoordinator_EvaluatesInjectedCustomTriggerRulesInOrder()
    {
        var shutdownPort = new FakeShutdownPort();
        var coordinator = new ShutdownCoordinator(
            shutdownPort,
            new FakeNotificationPort(),
            rules: [new CustomAlwaysTripRule()])
        {
            DryRun = false
        };

        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 30, isTestInProgress: false);
        Assert.Equal(0, shutdownPort.InitiateCalls);

        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 30, isTestInProgress: false);
        Assert.Equal(1, shutdownPort.InitiateCalls);
        Assert.Equal("Custom rule triggered", shutdownPort.LastReason);
    }

    [Fact]
    public void TelemetryPlausibilityGuard_EvaluatesCustomPhysicsPipeline()
    {
        UpsTelemetry baseline = TelemetryMother.CreateLineNormal(loadPct: 98);

        TelemetryAnomalyKind? defaultOutcome = TelemetryPlausibilityGuard.EvaluatePhysics(baseline, lastKnownGood: null);
        Assert.Null(defaultOutcome);

        TelemetryAnomalyKind? customOutcome = TelemetryPlausibilityGuard.EvaluatePhysics(
            baseline,
            lastKnownGood: null,
            rules: [new CustomUiDriftPhysicsRule()]);

        Assert.Equal(TelemetryAnomalyKind.PhysicsImplausible, customOutcome);
    }

    [Fact]
    public void ProtocolNegotiator_ExecutesOrderedDialectProbesUntilFirstMatch()
    {
        var negotiator = new ProtocolNegotiator(
            probes: [new CustomPreferredDialectProbe(VoltronicProtocolConstants.DialectP)]);

        NegotiatedUpsProtocol negotiated = negotiator.Negotiate(_ => "T\r"u8.ToArray());

        Assert.Equal("P", negotiated.Strategy.DialectId);
        Assert.Equal("Custom-P", negotiated.Capabilities.ModelName);
        Assert.True(negotiated.Capabilities.DialectVerified);
    }

    [Theory]
    [InlineData("--simulate", true, false)]
    [InlineData("--sim", true, false)]
    [InlineData("--mock", true, false)]
    [InlineData("--dry-run", false, true)]
    public void ApplicationOptionsParser_FlagRuleRegistry_MapsAliasesAndValidatesScope(
        string flag,
        bool expectedSimulation,
        bool expectedDryRun)
    {
        ApplicationLaunchOptions valid = ApplicationOptionsParser.Parse([flag]);
        TrayMonitorLaunchOptions tray = Assert.IsType<TrayMonitorLaunchOptions>(valid);
        Assert.Equal(expectedSimulation, tray.IsSimulation);
        Assert.Equal(expectedDryRun, tray.IsDryRun);
        Assert.Equal(ApplicationDefaults.DefaultPollingIntervalMs, tray.PollingIntervalMs);

        ApplicationLaunchOptions conflict = ApplicationOptionsParser.Parse(["--inspect", flag]);
        Assert.IsType<InvalidLaunchOptions>(conflict);
    }
}
