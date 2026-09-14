using System.Linq;
using ConsoleToSvg.QuickLeak;
using Filter = ConsoleToSvg.QuickLeak.QuickLeak;

namespace ConsoleToSvg.Tests.QuickLeak;

public sealed class QuickLeakTests
{
    [Test]
    public void ScanFindsSecretsAndHomeDirectoryPaths()
    {
        var findings = Filter.Scan("/home/alice/project");

        findings.Any(finding => finding.RuleId == "console2svg-home-directory").ShouldBeTrue();
    }

    [Test]
    public void EnumerateReturnsFindingsWithoutSortingOrMaterializingAnArray()
    {
        var findings = Filter.Enumerate("/home/alice/project").ToArray();

        findings.ShouldNotBeEmpty();
        findings.Any(finding => finding.RuleId == "console2svg-home-directory").ShouldBeTrue();
    }

    [Test]
    public void EarlyModeFindsPartiallyEnteredGithubToken()
    {
        var partialToken = "ghp_" + "abc";

        Filter.Scan(partialToken).ShouldBeEmpty();
        Filter
            .Scan(partialToken, QuickLeakScanMode.Early)
            .Any(finding => finding.RuleId == "github-pat")
            .ShouldBeTrue();
    }

    [Test]
    public void EarlyModeFindsPartiallyEnteredCredentialUri()
    {
        var partialUri = "https" + "://user:" + "pass";

        Filter
            .Scan(partialUri)
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeFalse();
        Filter
            .Scan(partialUri, QuickLeakScanMode.Early)
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeTrue();
        Filter
            .Scan(partialUri + "@example.test")
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeTrue();
    }

    [Test]
    public void GenericPasswordRequiresAValueAndStartsAtThePasswordKey()
    {
        var findings = Filter.Scan("\"password\": \"123456\"");

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        findings
            .Single(finding => finding.RuleId == "generic-password")
            .Start
            .ShouldBe(1);
        Filter.Scan("PASSWORD=").ShouldBeEmpty();
    }

    [Test]
    public void EnvValuesRemainIndependentlyDetectableAcrossLines()
    {
        var findings = Filter.Scan("PASSWORD=123456\nDATABASE_URL=postgres://user:secret@host/testdb");

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        findings.Any(finding => finding.RuleId == "generic-credential-uri").ShouldBeTrue();
    }
}
