using System;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed class CliHelpFormatterTests
{
    [Test]
    public void ColorizesGeneratedHeaderSectionsCommandsAndOptions()
    {
        const string help =
            """
            console2svg [Ver: 1.2.3]

            Usage: capture [options...] [-h|--help]

            Options:
              -o, --out <string?>    Output path. [Default: null]

            Commands:
              capture    Capture command output.
            """;

        var colored = CliHelpFormatter.Colorize(help);

        colored.ShouldContain("\x1b[1m\x1b[32mconsole2svg [Ver: 1.2.3]");
        colored.ShouldContain("\x1b[1m\x1b[33mOptions:");
        colored.ShouldContain("\x1b[36m-o\x1b[0m");
        colored.ShouldContain("\x1b[36m--out\x1b[0m");
        colored.ShouldContain("\x1b[35m<string?>\x1b[0m");
        colored.ShouldContain("\x1b[1m\x1b[36mcapture\x1b[0m");
    }

    [Test]
    public void MovesOptionsWithShorthandBeforeLongOnlyOptions()
    {
        const string help =
            """
            Options:
              --format <string>    Output format.
              -o, --out <string>   Output path.
              --theme <string>     Theme.
              -w, --width <int>    Width.
            """;

        var reordered = CliHelpFormatter.ReorderOptions(help);

        reordered.IndexOf("-o, --out", StringComparison.Ordinal)
            .ShouldBeLessThan(reordered.IndexOf("--format", StringComparison.Ordinal));
        reordered.IndexOf("-w, --width", StringComparison.Ordinal)
            .ShouldBeLessThan(reordered.IndexOf("--theme", StringComparison.Ordinal));
    }
}
