using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Configuration.Writable;
using Configuration.Writable.FormatProvider;
using Microsoft.Extensions.DependencyInjection;
using VYaml.Annotations;
using VYaml.Serialization;

namespace ConsoleToSvg.Configuration;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower,
    WriteIndented = true
)]
[JsonSerializable(typeof(ConfigDocument))]
[JsonSerializable(typeof(ScenarioDocument))]
internal partial class DocumentJsonContext : JsonSerializerContext;

public static class DocumentStore
{
    private const string ApplicationId = "console2svg";
#pragma warning disable S1075 // This is the stable public location used by published config schemas.
    private const string SchemaBaseUri =
        "https://raw.githubusercontent.com/arika0093/console2svg/main/schema/";
#pragma warning restore S1075

    static DocumentStore()
    {
        ConfigDocument.__RegisterVYamlFormatter();
        ScenarioDocument.__RegisterVYamlFormatter();
    }

    public static ConsoleOptions LoadConfigOptions(
        string? explicitPath = null,
        string? currentDirectory = null
    ) => LoadResolvedSettings(explicitPath, currentDirectory).Options;

    public static ResolvedSettings LoadResolvedSettings(
        string? explicitPath = null,
        string? currentDirectory = null
    )
    {
        var workingDirectory = Path.GetFullPath(currentDirectory ?? Environment.CurrentDirectory);
        var explicitFilePath = explicitPath is null ? null : Path.GetFullPath(explicitPath);
        if (explicitFilePath is not null)
        {
            _ = CreateProvider(explicitFilePath);
        }
        if (explicitFilePath is not null && !File.Exists(GetExpectedFilePath(explicitFilePath)))
        {
            throw new FileNotFoundException(
                $"The specified configuration file '{GetExpectedFilePath(explicitFilePath)}' does not exist.",
                GetExpectedFilePath(explicitFilePath)
            );
        }

        var services = new ServiceCollection();
        services.AddWritableOptions(builder =>
        {
            builder.SchemaBaseUri = SchemaBaseUri;
            builder.Add<ConfigDocument>(
                "global",
                options =>
                {
                    options.FormatProvider = CreateYamlProvider();
                    options.UseStandardSaveDirectory(ApplicationId).AddFilePath("config");
                }
            );
            builder.Add<ConfigDocument>(
                "local",
                options =>
                {
                    options.FormatProvider = CreateYamlProvider();
                    options.UseFile(Path.Combine(workingDirectory, "console2svg.config.yaml"));
                }
            );
            if (explicitFilePath is not null)
            {
                builder.Add<ConfigDocument>(
                    "explicit",
                    options =>
                    {
                        options.FormatProvider = CreateProvider(explicitFilePath);
                        options.UseFile(explicitFilePath);
                    }
                );
            }
        });

        using var serviceProvider = services.BuildServiceProvider();
        var localPath = Path.Combine(workingDirectory, "console2svg.config.yaml");
        var sourceDescription = explicitFilePath is null
            ? $"global configuration or '{localPath}'"
            : $"global configuration, '{localPath}', or '{explicitFilePath}'";
        var namedOptions = ReadDocument(
            () => serviceProvider.GetRequiredService<IReadOnlyNamedOptions<ConfigDocument>>(),
            sourceDescription
        );
        var global = ReadDocument(() => namedOptions.Get("global"), "global configuration");
        var local = ReadDocument(() => namedOptions.Get("local"), localPath);
        var explicitOptions = explicitFilePath is null
            ? null
            : ReadDocument(() => namedOptions.Get("explicit"), explicitFilePath).Options;

        ValidateConfig(global, "global");
        ValidateConfig(local, "local");
        if (explicitFilePath is not null && explicitOptions is not null)
        {
            ValidateConfig(new ConfigDocument { Options = explicitOptions }, explicitFilePath);
        }

        var resolved = ResolvedSettings.Resolve(
            global: global.Options,
            local: local.Options,
            explicitConfiguration: explicitOptions
        );
        ValidateConfig(new ConfigDocument { Options = resolved.Options }, "merged configuration");
        return resolved;
    }

    public static ScenarioDocument LoadScenario(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var filePath = Path.GetFullPath(path);
        var formatProvider = CreateProvider(filePath);
        var expectedFilePath = GetExpectedFilePath(filePath);
        if (!File.Exists(expectedFilePath))
        {
            throw new FileNotFoundException(
                $"The Scenario document '{expectedFilePath}' does not exist.",
                expectedFilePath
            );
        }

        var services = new ServiceCollection();
        services.AddWritableOptions(builder =>
        {
            builder.SchemaBaseUri = SchemaBaseUri;
            builder.Add<ScenarioDocument>(
                "scenario",
                options =>
                {
                    options.FormatProvider = formatProvider;
                    options.UseFile(filePath);
                }
            );
        });

        using var serviceProvider = services.BuildServiceProvider();
        var document = ReadDocument(
            () =>
                serviceProvider
                    .GetRequiredService<IReadOnlyNamedOptions<ScenarioDocument>>()
                    .Get("scenario"),
            expectedFilePath
        );
        var errors = DocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid Scenario document '{expectedFilePath}': {string.Join(" ", errors)}"
            );
        }

        return document;
    }

    public static async Task SaveScenarioAsync(
        string path,
        ScenarioDocument document,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        var filePath = Path.GetFullPath(path);
        var formatProvider = CreateProvider(filePath);
        var expectedFilePath = GetExpectedFilePath(filePath);
        var errors = DocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid Scenario document '{expectedFilePath}': {string.Join(" ", errors)}"
            );
        }

        var services = new ServiceCollection();
        services.AddWritableOptions(builder =>
        {
            builder.SchemaBaseUri = SchemaBaseUri;
            builder.Add<ScenarioDocument>(
                "scenario",
                options =>
                {
                    options.FormatProvider = formatProvider;
                    options.UseFile(filePath);
                }
            );
        });

        using var serviceProvider = services.BuildServiceProvider();
        await serviceProvider
            .GetRequiredService<IWritableNamedOptions<ScenarioDocument>>()
            .SaveAsync("scenario", document, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void GenerateSchemas()
    {
        WritableOptions.Initialize(builder =>
        {
            builder.EnableJsonSchemaGeneration(DocumentJsonContext.Default);
            builder.SchemaBaseUri = SchemaBaseUri;
            builder.Add<ConfigDocument>("config-schema");
            builder.Add<ScenarioDocument>("scenario-schema");
        });

        throw new InvalidOperationException(
            "Configuration.Writable did not handle --cw-generate-json-schema."
        );
    }

    private static void ValidateConfig(ConfigDocument document, string source)
    {
        var errors = DocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid configuration file '{source}': {string.Join(" ", errors)}"
            );
        }
    }

    private static T ReadDocument<T>(Func<T> read, string source)
    {
        try
        {
            return read();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid document '{source}': {exception.Message}",
                exception
            );
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"Invalid document '{source}': {exception.Message}",
                exception
            );
        }
    }

    private static IWritableFormatProvider CreateProvider(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".yaml" or ".yml" or "" => CreateYamlProvider(),
            ".json" => new JsonAotFormatProvider(DocumentJsonContext.Default),
            var extension => throw new InvalidDataException(
                $"Unsupported document extension '{extension}'. Use .yaml, .yml, or .json."
            ),
        };

    private static YamlFormatProvider CreateYamlProvider() =>
        new()
        {
            SerializerOptions = new YamlSerializerOptions
            {
                NamingConvention = NamingConvention.KebabCase,
            },
        };

    private static string GetExpectedFilePath(string path) =>
        Path.GetFileName(path).Contains('.') ? path : path + ".yaml";
}
