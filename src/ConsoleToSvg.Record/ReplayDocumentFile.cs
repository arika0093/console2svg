using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Configuration.Writable;
using Configuration.Writable.Configure;
using Configuration.Writable.FormatProvider;
using VYaml.Serialization;

namespace ConsoleToSvg.Recording;

/// <summary>Reads and writes canonical Replay v2 session documents.</summary>
public static class ReplayDocumentFile
{
    // WritableOptions is process-global by design. Serializing setup and use
    // makes explicit replay paths safe for callers that load documents in
    // parallel (notably test runners) without retaining configuration state.
    private static readonly SemaphoreSlim s_writableGate = new(1, 1);

    /// <summary>
    /// Loads a YAML v2 document or a JSON v1 document. A JSON v1 source is
    /// automatically migrated and materialized as a sibling <c>.yaml</c> file.
    /// </summary>
    public static async Task<ReplayDocumentV2> ReadAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        RejectUnsupportedJsonV2(path);

        await s_writableGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var logicalPath = GetLogicalPath(path);
            WritableOptions.Initialize(builder =>
            {
                builder.FormatProvider = new ReplayYamlFormatProvider();
                builder.AddFallbackFormatProvider(
                    new JsonAotFormatProvider(ReplayV1JsonContext.Default)
                    {
                        SchemaVersionFallbackProperties = [],
                    }
                );
                builder.Add<ReplayDocumentV2>(config =>
                {
                    config.UseFile(logicalPath);
                    config.UseCustomCloneStrategy(document => document.DeepClone());
                });
            });

            var document = WritableOptions.GetOptions<ReplayDocumentV2>().CurrentValue;
            ReplayDocumentValidation.Validate(document);
            return document;
        }
        finally
        {
            s_writableGate.Release();
        }
    }

    /// <summary>Writes a replay document in canonical YAML form.</summary>
    public static async Task WriteAsync(
        string path,
        ReplayDocumentV2 document,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        ReplayDocumentValidation.Validate(document);

        await s_writableGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var logicalPath = GetLogicalPath(path);
            WritableOptions.Initialize(builder =>
            {
                builder.FormatProvider = new ReplayYamlFormatProvider();
                builder.AddFallbackFormatProvider(
                    new JsonAotFormatProvider(ReplayV1JsonContext.Default)
                    {
                        SchemaVersionFallbackProperties = [],
                    }
                );
                builder.Add<ReplayDocumentV2>(config =>
                {
                    config.UseFile(logicalPath);
                    config.UseCustomCloneStrategy(value => value.DeepClone());
                });
            });

            await WritableOptions
                .GetOptions<ReplayDocumentV2>()
                .SaveAsync(document, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            s_writableGate.Release();
        }
    }

    /// <summary>Gets the YAML file that represents a supplied replay path.</summary>
    public static string GetCanonicalPath(string path)
    {
        return GetLogicalPath(path) + ".yaml";
    }

    /// <summary>
    /// Converts a YAML or JSON path into the extensionless replay identity used
    /// by Configuration.Writable to resolve canonical and fallback candidates.
    /// </summary>
    public static string GetLogicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path);
        return
            string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(
                Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path)
            )
            : path;
    }

    private static void RejectUnsupportedJsonV2(string path)
    {
        if (
            !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path)
        )
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (
                json.RootElement.TryGetProperty("$version", out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var number)
                && number >= 2
            )
            {
                throw new FormatException(
                    "Replay v2 uses YAML. Convert this JSON document to a .yaml replay file."
                );
            }
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Replay JSON '{path}' is invalid.", ex);
        }
    }
}

/// <summary>
/// YAML provider for the replay document's small action union. It keeps
/// Configuration.Writable's version migration and fallback handling while
/// accepting both <c>waitFor: prompt</c> and <c>waitFor: { regex: ... }</c>.
/// </summary>
internal sealed class ReplayYamlFormatProvider : YamlFormatProvider
{
    public override async ValueTask<object> LoadConfigurationAsync(
        Type type,
        PipeReader reader,
        List<string> sectionNameParts,
        CancellationToken cancellationToken = default
    )
    {
        if (type != typeof(ReplayDocumentV2) || sectionNameParts.Count != 0)
        {
            return await base.LoadConfigurationAsync(
                    type,
                    reader,
                    sectionNameParts,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        using var stream = reader.AsStream(leaveOpen: false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (buffer.Length == 0)
        {
            return new ReplayDocumentV2();
        }

        try
        {
            var node = YamlSerializer.Deserialize<object?>(buffer.ToArray(), SerializerOptions);
            return ReplayYamlCodec.ReadDocument(node);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormatException("Replay YAML could not be parsed.", ex);
        }
    }

    public override async Task SaveAsync<T>(
        T config,
        IWritableOptionsConfiguration options,
        CancellationToken cancellationToken = default
    )
    {
        if (config is not ReplayDocumentV2 document || options.SectionNameParts.Count != 0)
        {
            await base.SaveAsync(config, options, cancellationToken).ConfigureAwait(false);
            return;
        }

        var yaml = YamlSerializer.Serialize<object?>(
            ReplayYamlCodec.WriteDocument(document),
            SerializerOptions
        );
        await options
            .FileProvider.SaveToFileAsync(
                options.ConfigFilePath,
                yaml,
                options.Logger,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}

internal static class ReplayYamlCodec
{
    internal static ReplayDocumentV2 ReadDocument(object? value)
    {
        var root = RequireMap(value, "replay document");
        RejectUnknown(
            root,
            "replay document",
            "$version",
            "options",
            "command",
            "defaults",
            "timeout",
            "steps",
            "appVersion",
            "createdAt"
        );

        var document = new ReplayDocumentV2
        {
            Options = ReadSessionOptions(
                GetMap(root, "options") ?? new Dictionary<string, object?>()
            ),
            Command = GetString(root, "command"),
            Defaults = ReadDefaults(GetMap(root, "defaults") ?? new Dictionary<string, object?>()),
            Timeout = GetString(root, "timeout"),
            AppVersion = GetString(root, "appVersion"),
            CreatedAt = GetDateTimeOffset(root, "createdAt"),
        };
        _ = GetInt(root, "$version", required: true);

        var steps = GetList(root, "steps") ?? [];
        for (var index = 0; index < steps.Count; index++)
        {
            document.Steps.Add(ReadStep(steps[index], index));
        }

        return document;
    }

    internal static Dictionary<string, object?> WriteDocument(ReplayDocumentV2 document)
    {
        var root = new Dictionary<string, object?>
        {
            ["$version"] = 2,
            ["options"] = WriteSessionOptions(document.Options),
        };
        Add(root, "command", document.Command);
        Add(root, "timeout", document.Timeout);
        if (document.Defaults.Timeout is not null)
        {
            root["defaults"] = new Dictionary<string, object?>
            {
                ["timeout"] = document.Defaults.Timeout,
            };
        }

        root["steps"] = document.Steps.Select(WriteStep).Cast<object?>().ToList();
        Add(root, "appVersion", document.AppVersion);
        if (document.CreatedAt is DateTimeOffset createdAt)
        {
            root["createdAt"] = createdAt.ToString("O", CultureInfo.InvariantCulture);
        }

        return root;
    }

    private static ReplayDefaults ReadDefaults(IReadOnlyDictionary<string, object?> map)
    {
        RejectUnknown(map, "defaults", "timeout");
        return new ReplayDefaults { Timeout = GetString(map, "timeout") };
    }

    private static ReplayStep ReadStep(object? value, int index)
    {
        var map = RequireMap(value, $"steps[{index}]");
        RejectUnknown(
            map,
            $"steps[{index}]",
            "waitFor",
            "input",
            "key",
            "modifiers",
            "sleep",
            "raw",
            "timeout",
            "interval"
        );
        var step = new ReplayStep
        {
            Input = GetString(map, "input"),
            Key = GetString(map, "key"),
            Modifiers = GetStringList(map, "modifiers"),
            Sleep = GetString(map, "sleep"),
            Raw = GetString(map, "raw"),
            Timeout = GetString(map, "timeout"),
            Interval = GetString(map, "interval"),
        };

        if (map.TryGetValue("waitFor", out var waitFor))
        {
            step.WaitFor = ReadWaitFor(waitFor, index, ref step);
        }

        return step;
    }

    private static ReplayWaitFor ReadWaitFor(object? value, int index, ref ReplayStep step)
    {
        if (value is string text)
        {
            return new ReplayWaitFor { Text = text };
        }

        var map = RequireMap(value, $"steps[{index}].waitFor");
        RejectUnknown(
            map,
            $"steps[{index}].waitFor",
            "text",
            "regex",
            "newOutput",
            "scope",
            "timeout"
        );
        if (step.Timeout is not null && map.ContainsKey("timeout"))
        {
            throw new FormatException(
                $"steps[{index}] cannot specify timeout both on the step and waitFor."
            );
        }

        step.Timeout ??= GetString(map, "timeout");
        return new ReplayWaitFor
        {
            Text = GetString(map, "text"),
            Regex = GetString(map, "regex"),
            NewOutput = GetBool(map, "newOutput") ?? false,
            Scope = GetString(map, "scope") ?? "visible",
        };
    }

    private static Dictionary<string, object?> WriteStep(ReplayStep step)
    {
        var map = new Dictionary<string, object?>();
        if (step.WaitFor is ReplayWaitFor waitFor)
        {
            if (
                waitFor.Text is not null
                && waitFor.Regex is null
                && !waitFor.NewOutput
                && string.Equals(waitFor.Scope, "visible", StringComparison.OrdinalIgnoreCase)
                && step.Timeout is null
            )
            {
                map["waitFor"] = waitFor.Text;
            }
            else
            {
                var condition = new Dictionary<string, object?>();
                Add(condition, "text", waitFor.Text);
                Add(condition, "regex", waitFor.Regex);
                if (waitFor.NewOutput)
                {
                    condition["newOutput"] = true;
                }
                if (!string.Equals(waitFor.Scope, "visible", StringComparison.OrdinalIgnoreCase))
                {
                    condition["scope"] = waitFor.Scope;
                }
                Add(condition, "timeout", step.Timeout);
                map["waitFor"] = condition;
            }
        }
        else if (step.Input is not null)
        {
            map["input"] = step.Input;
            Add(map, "interval", step.Interval);
            Add(map, "timeout", step.Timeout);
        }
        else if (step.Key is not null)
        {
            map["key"] = step.Key;
            if (step.Modifiers is { Count: > 0 })
            {
                map["modifiers"] = step.Modifiers.Cast<object?>().ToList();
            }
            Add(map, "timeout", step.Timeout);
        }
        else if (step.Sleep is not null)
        {
            map["sleep"] = step.Sleep;
            Add(map, "timeout", step.Timeout);
        }
        else if (step.Raw is not null)
        {
            map["raw"] = step.Raw;
            Add(map, "timeout", step.Timeout);
        }

        return map;
    }

    private static SessionOptions ReadSessionOptions(IReadOnlyDictionary<string, object?> map)
    {
        RejectUnknown(map, "options", "terminal", "appearance", "render");
        var terminal = GetMap(map, "terminal") ?? new Dictionary<string, object?>();
        var appearance = GetMap(map, "appearance") ?? new Dictionary<string, object?>();
        var render = GetMap(map, "render") ?? new Dictionary<string, object?>();
        RejectUnknown(terminal, "options.terminal", "width", "height");
        RejectUnknown(
            appearance,
            "options.appearance",
            "theme",
            "font",
            "fontSize",
            "window",
            "margin",
            "padding",
            "foreColor",
            "backColor",
            "background"
        );
        RejectUnknown(
            render,
            "options.render",
            "mode",
            "fps",
            "timing",
            "loop",
            "sleep",
            "fadeOut",
            "outputCoalesceMs"
        );
        var font = ReadFont(appearance);
        return new SessionOptions
        {
            Terminal = new TerminalSessionOptions
            {
                Width = GetNullableInt(terminal, "width"),
                Height = GetNullableInt(terminal, "height"),
            },
            Appearance = new AppearanceSessionOptions
            {
                Theme = GetString(appearance, "theme"),
                Font = font.Family,
                FontSize = font.Size,
                Window = GetString(appearance, "window"),
                Margin = GetNullableDouble(appearance, "margin"),
                Padding = GetNullableDouble(appearance, "padding"),
                ForeColor = GetString(appearance, "foreColor"),
                BackColor = GetString(appearance, "backColor"),
                Background = GetStringList(appearance, "background"),
            },
            Render = new RenderSessionOptions
            {
                Mode = GetString(render, "mode"),
                Fps = GetNullableDouble(render, "fps"),
                Timing = GetString(render, "timing"),
                Loop = GetBool(render, "loop"),
                Sleep = GetNullableDouble(render, "sleep"),
                FadeOut = GetNullableDouble(render, "fadeOut"),
                OutputCoalesceMs = GetNullableDouble(render, "outputCoalesceMs"),
            },
        };
    }

    private static Dictionary<string, object?> WriteSessionOptions(SessionOptions options)
    {
        var terminal = new Dictionary<string, object?>();
        Add(terminal, "width", options.Terminal.Width);
        Add(terminal, "height", options.Terminal.Height);
        var appearance = new Dictionary<string, object?>();
        Add(appearance, "theme", options.Appearance.Theme);
        if (options.Appearance.Font is not null || options.Appearance.FontSize is not null)
        {
            var font = new Dictionary<string, object?>();
            Add(font, "family", options.Appearance.Font);
            Add(font, "size", options.Appearance.FontSize);
            appearance["font"] = font;
        }
        Add(appearance, "window", options.Appearance.Window);
        Add(appearance, "margin", options.Appearance.Margin);
        Add(appearance, "padding", options.Appearance.Padding);
        Add(appearance, "foreColor", options.Appearance.ForeColor);
        Add(appearance, "backColor", options.Appearance.BackColor);
        if (options.Appearance.Background is { Count: > 0 })
        {
            appearance["background"] = options.Appearance.Background.Cast<object?>().ToList();
        }
        var render = new Dictionary<string, object?>();
        Add(render, "mode", options.Render.Mode);
        Add(render, "fps", options.Render.Fps);
        Add(render, "timing", options.Render.Timing);
        Add(render, "loop", options.Render.Loop);
        Add(render, "sleep", options.Render.Sleep);
        Add(render, "fadeOut", options.Render.FadeOut);
        Add(render, "outputCoalesceMs", options.Render.OutputCoalesceMs);

        var result = new Dictionary<string, object?>();
        if (terminal.Count > 0)
        {
            result["terminal"] = terminal;
        }
        if (appearance.Count > 0)
        {
            result["appearance"] = appearance;
        }
        if (render.Count > 0)
        {
            result["render"] = render;
        }
        return result;
    }

    private static (string? Family, double? Size) ReadFont(
        IReadOnlyDictionary<string, object?> appearance
    )
    {
        if (!appearance.TryGetValue("font", out var value) || value is null)
        {
            return (null, GetNullableDouble(appearance, "fontSize"));
        }
        if (value is string family)
        {
            return (family, GetNullableDouble(appearance, "fontSize"));
        }

        var font = RequireMap(value, "options.appearance.font");
        RejectUnknown(font, "options.appearance.font", "family", "size");
        if (appearance.ContainsKey("fontSize"))
        {
            throw new FormatException(
                "options.appearance.fontSize cannot be used when font is a mapping."
            );
        }
        return (GetString(font, "family"), GetNullableDouble(font, "size"));
    }

    private static IReadOnlyDictionary<string, object?> RequireMap(object? value, string path) =>
        ToMap(value) ?? throw new FormatException($"{path} must be a YAML mapping.");

    private static IReadOnlyDictionary<string, object?>? GetMap(
        IReadOnlyDictionary<string, object?> map,
        string name
    )
    {
        if (!map.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        return RequireMap(value, name);
    }

    private static IReadOnlyDictionary<string, object?>? ToMap(object? value)
    {
        if (value is not IDictionary dictionary)
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                throw new FormatException("YAML mapping keys must be strings.");
            }
            result[key] = entry.Value;
        }
        return result;
    }

    private static List<object?>? GetList(IReadOnlyDictionary<string, object?> map, string name)
    {
        if (!map.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        if (value is string || value is not IEnumerable values)
        {
            throw new FormatException($"{name} must be a YAML sequence.");
        }

        var result = new List<object?>();
        foreach (var item in values)
        {
            result.Add(item);
        }
        return result;
    }

    private static List<string>? GetStringList(
        IReadOnlyDictionary<string, object?> map,
        string name
    )
    {
        var values = GetList(map, name);
        if (values is null)
        {
            return null;
        }

        var result = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not string value)
            {
                throw new FormatException($"{name}[{i}] must be a string.");
            }
            result.Add(value);
        }
        return result;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> map, string name)
    {
        if (!map.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        return value as string ?? throw new FormatException($"{name} must be a string.");
    }

    private static bool? GetBool(IReadOnlyDictionary<string, object?> map, string name)
    {
        if (!map.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        return value is bool result
            ? result
            : throw new FormatException($"{name} must be true or false.");
    }

    private static int GetInt(
        IReadOnlyDictionary<string, object?> map,
        string name,
        bool required = false
    )
    {
        var result = GetNullableInt(map, name);
        if (result.HasValue)
        {
            return result.Value;
        }
        if (required)
        {
            throw new FormatException($"{name} is required.");
        }
        return 0;
    }

    private static int? GetNullableInt(IReadOnlyDictionary<string, object?> map, string name)
    {
        if (!map.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        try
        {
            return value switch
            {
                int integer => integer,
                long integer when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
                _ => throw new InvalidCastException(),
            };
        }
        catch (InvalidCastException)
        {
            throw new FormatException($"{name} must be an integer.");
        }
    }

    private static double? GetNullableDouble(IReadOnlyDictionary<string, object?> map, string name)
    {
        if (!map.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        var result = value switch
        {
            int integer => integer,
            long integer => integer,
            float number => number,
            double number => number,
            _ => double.NaN,
        };
        if (!double.IsFinite(result))
        {
            throw new FormatException($"{name} must be a finite number.");
        }
        return result;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        IReadOnlyDictionary<string, object?> map,
        string name
    )
    {
        var value = GetString(map, name);
        if (value is null)
        {
            return null;
        }
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var date
        )
            ? date
            : throw new FormatException($"{name} must be an ISO 8601 timestamp.");
    }

    private static void RejectUnknown(
        IReadOnlyDictionary<string, object?> map,
        string path,
        params string[] known
    )
    {
        var unknown = map.Keys.FirstOrDefault(key => !known.Contains(key, StringComparer.Ordinal));
        if (unknown is not null)
        {
            throw new FormatException($"{path} contains unknown property '{unknown}'.");
        }
    }

    private static void Add<T>(IDictionary<string, object?> map, string name, T? value)
    {
        if (value is not null)
        {
            map[name] = value;
        }
    }
}

internal static class ReplayDocumentValidation
{
    private static readonly HashSet<string> s_modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "shift",
        "alt",
        "ctrl",
        "meta",
    };

    internal static void Validate(ReplayDocumentV2 document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDuration(document.Defaults.Timeout, "defaults.timeout");
        ValidateDuration(document.Timeout, "timeout");
        ValidatePositive(document.Options.Terminal.Width, "options.terminal.width");
        ValidatePositive(document.Options.Terminal.Height, "options.terminal.height");
        ValidateFinite(document.Options.Appearance.FontSize, "options.appearance.fontSize");
        ValidateFinite(document.Options.Appearance.Margin, "options.appearance.margin");
        ValidateFinite(document.Options.Appearance.Padding, "options.appearance.padding");
        ValidateFinite(document.Options.Render.Fps, "options.render.fps");
        ValidateFinite(document.Options.Render.Sleep, "options.render.sleep");
        ValidateFinite(document.Options.Render.FadeOut, "options.render.fadeOut");
        ValidateFinite(document.Options.Render.OutputCoalesceMs, "options.render.outputCoalesceMs");
        if (
            document.Options.Render.Mode is string mode
            && !string.Equals(mode, "image", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new FormatException("options.render.mode must be 'image' or 'video'.");
        }
        if (
            document.Options.Render.Timing is string timing
            && !string.Equals(timing, "deterministic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(timing, "realtime", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new FormatException(
                "options.render.timing must be 'deterministic' or 'realtime'."
            );
        }
        if (document.Options.Appearance.Background is { Count: > 2 })
        {
            throw new FormatException(
                "options.appearance.background can contain at most two values."
            );
        }

        for (var index = 0; index < document.Steps.Count; index++)
        {
            ValidateStep(document.Steps[index], index);
        }
    }

    private static void ValidateStep(ReplayStep step, int index)
    {
        ArgumentNullException.ThrowIfNull(step);
        var actions =
            (step.WaitFor is null ? 0 : 1)
            + (step.Input is null ? 0 : 1)
            + (step.Key is null ? 0 : 1)
            + (step.Sleep is null ? 0 : 1)
            + (step.Raw is null ? 0 : 1);
        if (actions != 1)
        {
            throw new FormatException($"steps[{index}] must contain exactly one action.");
        }

        ValidateDuration(step.Timeout, $"steps[{index}].timeout");
        if (step.Interval is not null)
        {
            if (step.Input is null)
            {
                throw new FormatException($"steps[{index}].interval is only valid with input.");
            }
            ValidateDuration(step.Interval, $"steps[{index}].interval");
        }

        if (step.Sleep is not null)
        {
            ValidateDuration(step.Sleep, $"steps[{index}].sleep");
        }

        if (step.Key is not null)
        {
            if (string.IsNullOrEmpty(step.Key))
            {
                throw new FormatException($"steps[{index}].key must not be empty.");
            }
            ValidateModifiers(step.Modifiers, index, step.Key);
        }
        else if (step.Modifiers is { Count: > 0 })
        {
            throw new FormatException($"steps[{index}].modifiers is only valid with key.");
        }

        if (step.WaitFor is ReplayWaitFor waitFor)
        {
            if ((waitFor.Text is null) == (waitFor.Regex is null))
            {
                throw new FormatException(
                    $"steps[{index}].waitFor must contain exactly one of text or regex."
                );
            }
            if (waitFor.Text is { Length: 0 } || waitFor.Regex is { Length: 0 })
            {
                throw new FormatException($"steps[{index}].waitFor pattern must not be empty.");
            }
            if (
                !string.Equals(waitFor.Scope, "visible", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(waitFor.Scope, "scrollback", StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new FormatException(
                    $"steps[{index}].waitFor.scope must be 'visible' or 'scrollback'."
                );
            }
            if (waitFor.Regex is not null)
            {
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(
                        waitFor.Regex,
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1)
                    );
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException(
                        $"steps[{index}].waitFor.regex is invalid: {ex.Message}",
                        ex
                    );
                }
            }
        }
    }

    private static void ValidateModifiers(List<string>? modifiers, int index, string key)
    {
        var allModifiers = modifiers is null ? new List<string>() : [.. modifiers];
        var plusIndex = key.LastIndexOf('+');
        if (plusIndex > 0 && plusIndex < key.Length - 1)
        {
            var parts = key.Split('+', StringSplitOptions.TrimEntries);
            if (parts.Any(part => part.Length == 0))
            {
                throw new FormatException($"steps[{index}].key chord is invalid.");
            }
            allModifiers.AddRange(parts[..^1]);
        }

        if (allModifiers.Count == 0)
        {
            return;
        }
        var unsupportedModifier = allModifiers.FirstOrDefault(modifier =>
            !s_modifiers.Contains(modifier)
        );
        if (unsupportedModifier is not null)
        {
            throw new FormatException(
                $"steps[{index}].modifiers contains unsupported modifier '{unsupportedModifier}'."
            );
        }
        if (allModifiers.Count != allModifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new FormatException($"steps[{index}].modifiers must not contain duplicates.");
        }
    }

    private static void ValidateDuration(string? value, string fieldName)
    {
        if (value is not null)
        {
            _ = ReplayDuration.Parse(value, fieldName);
        }
    }

    private static void ValidatePositive(int? value, string fieldName)
    {
        if (value is <= 0)
        {
            throw new FormatException($"{fieldName} must be greater than zero.");
        }
    }

    private static void ValidateFinite(double? value, string fieldName)
    {
        if (value is double number && (!double.IsFinite(number) || number < 0d))
        {
            throw new FormatException($"{fieldName} must be a finite non-negative number.");
        }
    }
}
