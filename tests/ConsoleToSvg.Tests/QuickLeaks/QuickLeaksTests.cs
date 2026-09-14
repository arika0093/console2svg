using System.Linq;
using ConsoleToSvg.QuickLeaks;
using Filter = ConsoleToSvg.QuickLeaks.QuickLeaks;

namespace ConsoleToSvg.Tests.QuickLeaks;

public sealed class QuickLeaksTests
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
            .Scan(partialToken, QuickLeaksScanMode.Early)
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
            .Scan(partialUri, QuickLeaksScanMode.Early)
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeTrue();
        Filter
            .Scan(partialUri + "@example.test")
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeTrue();
    }

    [Test]
    public void GenericPasswordMasksOnlyTheValue()
    {
        var text = "\"password\": \"123456\"";
        var findings = Filter.Scan(text);

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        var finding = findings.Single(finding => finding.RuleId == "generic-password");
        text.Substring(finding.Start, finding.End - finding.Start).ShouldBe("123456");
        Filter.Scan("PASSWORD=").ShouldBeEmpty();
    }

    [Test]
    public void HomeDirectoryMasksOnlyTheUsername()
    {
        foreach (
            var path in new[]
            {
                "/home/alice/project",
                "C:\\Users\\alice\\project",
                "\\Users\\alice",
                "C:/Users/alice/project",
            }
        )
        {
            var findings = Filter.Scan(path);
            var finding = findings.Single(
                finding => finding.RuleId == "console2svg-home-directory"
            );
            path.Substring(finding.Start, finding.End - finding.Start).ShouldBe("alice");
        }
    }

    [Test]
    public void CredentialUriMasksOnlyThePassword()
    {
        var text = "https://user:secret@example.test/path";
        var findings = Filter.Scan(text);

        foreach (
            var finding in findings.Where(finding =>
                finding.RuleId is "generic-credential-uri" or "console2svg-credential-uri"
            )
        )
        {
            text.Substring(finding.Start, finding.End - finding.Start).ShouldBe("secret");
        }
    }

    [Test]
    public void EnvValuesRemainIndependentlyDetectableAcrossLines()
    {
        var findings = Filter.Scan("PASSWORD=123456\nDATABASE_URL=postgres://user:secret@host/testdb");

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        findings.Any(finding => finding.RuleId == "generic-credential-uri").ShouldBeTrue();
    }
}
