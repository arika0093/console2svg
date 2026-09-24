using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Cli;

internal static class LlmSkill
{
    private const string ResourceName = "console2svg.skills.console2svg.SKILL.md";

    public static async Task<int> WriteAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            await error.WriteLineAsync(
                $"Bundled Agent Skill resource '{ResourceName}' was not found.".AsMemory(),
                cancellationToken
            );
            return 1;
        }

        using (resource)
        using (var reader = new StreamReader(resource))
        {
            var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }
}
