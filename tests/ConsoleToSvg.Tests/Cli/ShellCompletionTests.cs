using System.Reflection;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed class ShellCompletionTests
{
    [Test]
    public void ScriptsScopeOptionsToTheSelectedWorkflow()
    {
        foreach (var shell in new[] { "bash", "zsh", "fish", "powershell" })
        {
            var script = GetScript(shell);

            script.ShouldNotBeNull();
            script!.ShouldContain("interactive");
            script.ShouldContain("completion");
            script.ShouldContain("bash zsh fish powershell");
            script.ShouldContain("live-server");
        }
    }

    [Test]
    public void BashScriptDoesNotOfferCaptureOnlyOptionsForInteractive()
    {
        var script = GetScript("bash");

        script!.ShouldContain("interactive='--help");
        script!.ShouldNotContain(
            "interactive='--help --version -o --out -w --width -h --height -m --mode -v --video -c --with-command --mask --verbose --frame --time --crop-top --crop-right --crop-bottom --crop-left --theme --forecolor -d --window --padding --no-loop --no-colorenv --no-delete-envs --fps --timing --sleep --fadeout --coalesce-ms --opacity --adjust --background --timeout --font --fontsize --embed-logs --header --prompt --pcmode --pc-padding --backcolor --save-frames --size --svg-converter --in"
        );
    }

    [Test]
    public void ScriptsUseFileCompletionForFileValuedOptions()
    {
        GetScript("bash")!.ShouldContain("--save-cast|--replay-save");
        GetScript("zsh")!.ShouldContain("_files; return");
        GetScript("fish")!.ShouldContain("-l save-cast -r");
        GetScript("powershell")!.ShouldContain("Get-ChildItem -Path");
    }

    [Test]
    public void UnsupportedShellReturnsNull() => GetScript("cmd").ShouldBeNull();

    private static string? GetScript(string shell) =>
        (string?)
            typeof(OptionParser)
                .Assembly.GetType("ConsoleToSvg.Cli.ShellCompletion")!
                .GetMethod("GetScript", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [shell]);
}
