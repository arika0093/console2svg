using System;
using System.Linq;

namespace ConsoleToSvg.Cli;

internal static class ShellCompletion
{
    private const string Commands = "capture interactive replay convert theme completion";

    // Keep this list in sync with OptionParser.Options.cs. Both long options
    // and their documented short aliases are offered by every shell.
    private const string Options =
        "--help --version -o --out -w --width -h --height -m --mode -v --video -i --interactive -c --with-command --in --mask --verbose --frame --time --crop-top --crop-right --crop-bottom --crop-left --theme --forecolor -d --window --padding --no-loop --no-colorenv --no-delete-envs --fps --timing --sleep --fadeout --coalesce-ms --opacity --adjust --background --timeout --font --fontsize --save-cast --embed-cast --embed-logs --embed-replay --embed-debug --replay-save --replay --header --prompt --pcmode --pc-padding --backcolor --stdout --save-frames --size --svg-converter";

    public static string? GetScript(string? shell) =>
        shell?.ToLowerInvariant() switch
        {
            "bash" =>
                $"_console2svg() {{ COMPREPLY=( $(compgen -W '{Commands} {Options}' -- \"${{COMP_WORDS[COMP_CWORD]}}\") ); }}\ncomplete -F _console2svg console2svg\n",
            "zsh" =>
                $"#compdef console2svg\n_arguments '1:workflow:({Commands})' '*:option:({Options})'\n",
            "fish" => string.Join(
                "\n",
                Commands.Split(' ').Select(command => $"complete -c console2svg -f -a {command}")
            ) + $"\ncomplete -c console2svg -f -a '{Options}'\n",
            "powershell" =>
                $"Register-ArgumentCompleter -Native -CommandName console2svg -ScriptBlock {{ param($wordToComplete, $commandAst, $cursorPosition) '{Commands} {Options}'.Split(' ') | Where-Object {{ $_ -like \"$wordToComplete*\" }} | ForEach-Object {{ [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }} }}\n",
            _ => null,
        };
}
