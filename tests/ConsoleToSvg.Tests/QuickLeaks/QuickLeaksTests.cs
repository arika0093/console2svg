using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ConsoleToSvg.QuickLeaks;
using Filter = ConsoleToSvg.QuickLeaks.QuickLeaks;

namespace ConsoleToSvg.Tests.QuickLeaks;

public sealed class QuickLeaksTests
{
    [Test]
    public void GeneratedRulesHaveFiniteMatchTimeouts()
    {
        var generatedRules = typeof(Filter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method =>
                method.ReturnType == typeof(Regex)
                && (method.Name.StartsWith("Rule") || method.Name.StartsWith("EarlyRule"))
            )
            .Select(method => (Regex)method.Invoke(null, null)!)
            .ToArray();

        generatedRules.Length.ShouldBe(930);
        generatedRules
            .All(regex => regex.MatchTimeout == TimeSpan.FromMilliseconds(10))
            .ShouldBeTrue();
    }

    [Test]
    public void TimedOutRuleIsIgnoredWithoutProducingARedaction()
    {
        var findRuleMatches = typeof(Filter).GetMethod(
            "FindRuleMatches",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var pathological = new Regex(
            "(a+)+$",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(1)
        );
        var input = new string('a', 100_000) + "!";

        var findings = (IEnumerable<QuickLeaksFinding>)
            findRuleMatches.Invoke(null, [pathological, input, "test-timeout"])!;

        findings.ShouldBeEmpty();
    }

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
    public void EarlyModeDoesNotBacktrackOnOrdinaryMultilineCurlDocumentation()
    {
        var text = string.Join(
            '\n',
            "curl -sSL https://example.test/releases/latest/download/archive.tar.gz",
            "tar -xzf archive.tar.gz",
            "chmod +x example",
            "",
            "The easiest way is the install script.",
            "",
            "curl -sSL https://example.test/install.sh | bash",
            "",
            "You can also install via a package manager."
        );

        Filter.Scan(text, QuickLeaksScanMode.Early).ShouldBeEmpty();
        Filter
            .Scan(
                "curl -H \"Authorization: Bearer abcdefgh\" https://example.test",
                QuickLeaksScanMode.Early
            )
            .Any(finding => finding.RuleId == "curl-auth-header")
            .ShouldBeTrue();
        Filter
            .Scan("curl -u user:password https://example.test", QuickLeaksScanMode.Early)
            .Any(finding => finding.RuleId == "curl-auth-user")
            .ShouldBeTrue();
    }

    [Test]
    public void GenericPasswordMasksOnlyTheValue()
    {
        var text = "\"password\": \"123456\"";
        text = "https://user:secret@example.test/path";
        text = "\"password\": \"123456\"";
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
    public void CredentialUriMasksUsernameAndPasswordIndependently()
    {
        var text = "https://user:secret@example.test/path";
        var findings = Filter.Scan(text);

        text = "https://user:secret@example.test/path";
        findings = Filter.Scan(text);
        var credentialFindings = findings
            .Where(finding =>
                finding.RuleId is "generic-credential-uri" or "console2svg-credential-uri"
            )
            .Select(finding => text.Substring(finding.Start, finding.End - finding.Start))
            .Distinct()
            .ToArray();

        credentialFindings.ShouldBe(["user", "secret"]);
    }

    [Test]
    public void EnvValuesRemainIndependentlyDetectableAcrossLines()
    {
        var findings = Filter.Scan("PASSWORD=123456\nDATABASE_URL=postgres://user:secret@host/testdb");

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        findings.Any(finding => finding.RuleId == "generic-credential-uri").ShouldBeTrue();
    }
}
