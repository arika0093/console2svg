using System;
using System.Linq;

namespace ConsoleToSvg.Cli;

internal static class ShellCompletion
{
    private const string Commands =
        "capture interactive replay convert theme completion live-server";
    private const string Shells = "bash zsh fish powershell";

    // Keep these lists in sync with the workflows accepted by OptionParser.
    // They intentionally offer only options that make sense for each verb; validation
    // remains the source of truth for combinations of otherwise valid options.
    private const string CommonOptions =
        "--help --version -o --out -w --width -h --height -m --mode -v --video -c --with-command --mask --verbose --frame --time --crop-top --crop-right --crop-bottom --crop-left --theme --forecolor -d --window --padding --no-loop --no-colorenv --no-delete-envs --fps --timing --sleep --fadeout --coalesce-ms --opacity --adjust --background --timeout --font --fontsize --embed-logs --header --prompt --pcmode --pc-padding --backcolor --save-frames --size --svg-converter";
    private const string CaptureOptions =
        CommonOptions
        + " --in --save-cast --embed-cast --embed-replay --embed-debug --replay-save --replay --stdout";
    private const string InteractiveOptions = CommonOptions;
    private const string ReplayOptions =
        CommonOptions + " --save-cast --embed-cast --embed-replay --embed-debug --stdout";
    private const string ConvertOptions = CommonOptions + " --stdout";
    private const string LiveServerOptions =
        "--help --version -w --width -h --height -c --with-command --mask --verbose --theme --forecolor -d --window --padding --no-colorenv --no-delete-envs --opacity --adjust --background --font --fontsize --header --prompt --pcmode --pc-padding --backcolor --listen";

    public static string? GetScript(string? shell) =>
        shell?.ToLowerInvariant() switch
        {
            "bash" => GetBashScript(),
            "zsh" => GetZshScript(),
            "fish" => GetFishScript(),
            "powershell" => GetPowerShellScript(),
            _ => null,
        };

    private static string GetBashScript() =>
        $$"""
                _console2svg() {
              local current="${COMP_WORDS[COMP_CWORD]}" workflow="${COMP_WORDS[1]}" candidates
              local commands='{{Commands}}' shells='{{Shells}}'
              local capture='{{CaptureOptions}}' interactive='{{InteractiveOptions}}' replay='{{ReplayOptions}}' convert='{{ConvertOptions}}' live_server='{{LiveServerOptions}}'
              local words_before="${COMP_WORDS[*]:1:COMP_CWORD}"
              [[ " $words_before " == *" -- "* ]] && return 0
              case "${COMP_WORDS[COMP_CWORD-1]}" in
                -o|--out|--in|--save-cast|--replay-save|--replay|--save-frames)
                  compopt -o filenames; return 0 ;;
              esac
                  if [[ $COMP_CWORD -eq 1 ]]; then candidates="$commands";
              elif [[ "$workflow" == completion && $COMP_CWORD -eq 2 ]]; then candidates="$shells";
                  else
                    case "$workflow" in
                      capture) candidates="$capture" ;; interactive) candidates="$interactive" ;;
                      replay) candidates="$replay" ;; convert) candidates="$convert" ;;
                      live-server) candidates="$live_server" ;; theme) candidates='--help --version' ;;
                      *) candidates="$commands" ;;
                    esac
                  fi
                  COMPREPLY=( $(compgen -W "$candidates" -- "$current") )
                }
                complete -F _console2svg console2svg
            """ + "\n";

    private static string GetZshScript() =>
        $$"""
                #compdef console2svg
                _console2svg() {
              local -a candidates
              local workflow=${words[2]}
              (( ${words[(I)--]} > 0 && ${words[(I)--]} <= CURRENT )) && return 0
              case ${words[CURRENT-1]} in
                -o|--out|--in|--save-cast|--replay-save|--replay|--save-frames)
                  _files; return ;;
              esac
                  if (( CURRENT == 2 )); then candidates=({{Commands}})
              elif [[ $workflow == completion && CURRENT -eq 3 ]]; then candidates=({{Shells}})
                  else
                    case $workflow in
                      capture) candidates=({{CaptureOptions}}) ;; interactive) candidates=({{InteractiveOptions}}) ;;
                      replay) candidates=({{ReplayOptions}}) ;; convert) candidates=({{ConvertOptions}}) ;;
                      live-server) candidates=({{LiveServerOptions}}) ;; theme) candidates=(--help --version) ;;
                      *) candidates=({{Commands}}) ;;
                    esac
                  fi
                  _describe -t values 'console2svg arguments' candidates
                }
                _console2svg "$@"
            """ + "\n";

    private static string GetFishScript()
    {
        var commandLines = string.Join(
            "\n",
            Commands
                .Split(' ')
                .Select(command =>
                    $"complete -c console2svg -f -n '__fish_use_subcommand' -a {command}"
                )
        );
        return $$"""
                        {{commandLines}}
                        complete -c console2svg -f -n '__fish_seen_subcommand_from completion' -a '{{Shells}}'
                        complete -c console2svg -f -n '__fish_seen_subcommand_from capture' -a '{{CaptureOptions}}'
                        complete -c console2svg -f -n '__fish_seen_subcommand_from interactive' -a '{{InteractiveOptions}}'
                        complete -c console2svg -f -n '__fish_seen_subcommand_from replay' -a '{{ReplayOptions}}'
                        complete -c console2svg -f -n '__fish_seen_subcommand_from convert' -a '{{ConvertOptions}}'
                        complete -c console2svg -f -n '__fish_seen_subcommand_from live-server' -a '{{LiveServerOptions}}'
                    complete -c console2svg -f -n '__fish_seen_subcommand_from theme' -a '--help --version'
                    complete -c console2svg -l out -r
                    complete -c console2svg -s o -r
                    complete -c console2svg -l in -r
                    complete -c console2svg -l save-cast -r
                    complete -c console2svg -l replay-save -r
                    complete -c console2svg -l replay -r
                    complete -c console2svg -l save-frames -r
                """ + "\n";
    }

    private static string GetPowerShellScript() =>
        $$"""
                Register-ArgumentCompleter -Native -CommandName console2svg -ScriptBlock {
                  param($wordToComplete, $commandAst, $cursorPosition)
                  $words = @($commandAst.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.Extent.Text })
              $workflow = if ($words.Count) { $words[0] } else { '' }
              if ($words -contains '--') { return }
              $pathOptions = '-o', '--out', '--in', '--save-cast', '--replay-save', '--replay', '--save-frames'
              if ($words.Count -gt 1 -and $words[-2] -in $pathOptions) {
                Get-ChildItem -Path "$wordToComplete*" -ErrorAction SilentlyContinue | ForEach-Object {
                  $candidate = if ($_.PSIsContainer) { "$($_.Name)\\" } else { $_.Name }
                  [System.Management.Automation.CompletionResult]::new($candidate, $candidate, 'ProviderItem', $candidate)
                }
                return
              }
                  $commands = '{{Commands}}'.Split(' '); $shells = '{{Shells}}'.Split(' ')
                  $options = switch ($workflow) {
                    'capture' { '{{CaptureOptions}}'.Split(' ') }
                    'interactive' { '{{InteractiveOptions}}'.Split(' ') }
                    'replay' { '{{ReplayOptions}}'.Split(' ') }
                    'convert' { '{{ConvertOptions}}'.Split(' ') }
                    'live-server' { '{{LiveServerOptions}}'.Split(' ') }
                    'theme' { '--help', '--version' }
                    'completion' { $shells }
                    default { $commands }
                  }
                  $options | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                  }
                }
            """ + "\n";
}
