using System;
using System.Linq;

namespace ConsoleToSvg.Cli;

internal static class ShellCompletion
{
    private const string Commands = "capture interactive replay convert theme completion";
    private const string Options = "--help --version --out --width --height --theme --window --font --fontsize --background --timeout";

    public static string? GetScript(string? shell) => shell?.ToLowerInvariant() switch
    {
        "bash" => $"_console2svg() {{ COMPREPLY=( $(compgen -W '{Commands} {Options}' -- \"${{COMP_WORDS[COMP_CWORD]}}\") ); }}\ncomplete -F _console2svg console2svg\n",
        "zsh" => $"#compdef console2svg\n_arguments '1:workflow:({Commands})' '*:option:({Options})'\n",
        "fish" => string.Join("\n", Commands.Split(' ').Select(command => $"complete -c console2svg -f -a {command}")) + "\n",
        "powershell" => $"Register-ArgumentCompleter -Native -CommandName console2svg -ScriptBlock {{ param($wordToComplete, $commandAst, $cursorPosition) '{Commands} {Options}'.Split(' ') | Where-Object {{ $_ -like \"$wordToComplete*\" }} | ForEach-Object {{ [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }} }}\n",
        _ => null,
    };
}
