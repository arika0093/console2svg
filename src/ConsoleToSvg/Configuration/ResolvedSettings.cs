using System;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Configuration;

/// <summary>
/// Configuration values after document layers have been merged. CLI values remain
/// authoritative when the resolved settings are applied to an invocation.
/// </summary>
public sealed class ResolvedSettings
{
    private readonly ConsoleOptions _options;

    private ResolvedSettings(ConsoleOptions options)
    {
        _options = OptionsMerger.Merge(options);
    }

    public static ResolvedSettings Empty { get; } = new(new ConsoleOptions());

    /// <summary>Returns a defensive copy of the merged settings.</summary>
    public ConsoleOptions Options => OptionsMerger.Merge(_options);

    public static ResolvedSettings Resolve(
        ConsoleOptions? global = null,
        ConsoleOptions? local = null,
        ConsoleOptions? explicitConfiguration = null,
        ConsoleOptions? scenario = null,
        ConsoleOptions? launch = null,
        ConsoleOptions? capture = null
    ) => new(OptionsMerger.Merge(global, local, explicitConfiguration, scenario, launch, capture));

    public ResolvedSettings WithScenario(ConsoleOptions? scenario) =>
        Resolve(global: _options, scenario: scenario);

    public ResolvedSettings WithLaunch(ConsoleOptions? launch) =>
        Resolve(scenario: _options, launch: launch);

    public ResolvedSettings WithCapture(ConsoleOptions? capture) =>
        Resolve(scenario: _options, capture: capture);

    /// <summary>
    /// Applies merged document settings to an invocation. Explicit CLI values take
    /// precedence over every document layer.
    /// </summary>
    public void ApplyTo(AppOptions invocation) => OptionsApplicator.Apply(invocation, _options);
}
