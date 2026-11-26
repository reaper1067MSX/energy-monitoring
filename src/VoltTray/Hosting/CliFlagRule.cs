namespace VoltTray.Hosting;

internal sealed record CliFlagRule(
    IReadOnlyList<string> Aliases,
    CliOptionScope Scope,
    bool RequiresValue,
    Func<CliParseState, string?, string?> Apply);
