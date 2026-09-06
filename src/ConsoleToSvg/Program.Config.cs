using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static async Task<int> RunConfigAsync(string[] args, AppOptions options)
    {
        var values = new Dictionary<string, List<string>?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal)) continue;
            var split = token.IndexOf('=');
            var key = split > 0 ? token[..split] : token;
            if (key is "--file" or "--help" or "--version")
            {
                if (key == "--file" && split < 0) i++;
                continue;
            }

            List<string>? supplied = null;
            if (split > 0)
            {
                supplied = [token[(split + 1)..]];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                supplied = [args[++i]];
                if (key == "--background" && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)) supplied.Add(args[++i]);
                if (key == "--mask") while (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)) supplied.Add(args[++i]);
            }
            values[key.TrimStart('-')] = supplied;
        }
        if (values.Count == 0)
        {
            await Console.Error.WriteLineAsync("config requires at least one option to save.");
            return 1;
        }

        var path = options.ConfigPath ?? "console2svg.config.json";
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var output = File.Create(path);
        await using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (var pair in values)
        {
            if (pair.Value is null) writer.WriteBoolean(pair.Key, true);
            else if (pair.Value.Count == 1) writer.WriteString(pair.Key, pair.Value[0]);
            else
            {
                writer.WriteStartArray(pair.Key);
                foreach (var value in pair.Value) writer.WriteStringValue(value);
                writer.WriteEndArray();
            }
        }
        writer.WriteEndObject();
        await writer.FlushAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Saved configuration: {path}");
        return 0;
    }
}
