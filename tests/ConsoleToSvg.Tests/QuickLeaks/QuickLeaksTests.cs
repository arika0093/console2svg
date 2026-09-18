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
    public void TimedOutRuleFailsClosedWithAConservativeRedaction()
    {
        var pathological = new Regex(
            "(a+)+$",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(1)
        );
        var input = new string('a', 100_000) + "!";

        var findings = Filter.ScanRegexFallbackForTesting(pathological, input);

        findings.Count.ShouldBe(1);
        findings[0].Start.ShouldBe(0);
        findings[0].End.ShouldBe(input.Length);
    }

    [Test]
    public void ScanFindsSecretsAndHomeDirectoryPaths()
    {
        var findings = Filter.Scan("/home/alice/project");

        findings.Any(finding => finding.RuleId == "console2svg-home-directory").ShouldBeTrue();
    }

    [Test]
    public void EnumeratePreservesTheCompatibilityApi()
    {
        var findings = Filter.Enumerate("/home/alice/project").ToArray();

        findings.ShouldNotBeEmpty();
        findings.Any(finding => finding.RuleId == "console2svg-home-directory").ShouldBeTrue();
    }

    [Test]
    public void SpanScanWritesToCallerOwnedBuffer()
    {
        var findings = new System.Buffers.ArrayBufferWriter<QuickLeaksFinding>(4);

        var count = Filter.Scan("/home/alice/project".AsSpan(), findings);

        count.ShouldBe(findings.WrittenCount);
        findings.WrittenSpan
            .ToArray()
            .Any(finding => finding.RuleId == "console2svg-home-directory")
            .ShouldBeTrue();
    }

    [Test]
    public void SpanScanCleanPathDoesNotAllocate()
    {
        const string text = "Building project and running 128 tests: all tests passed.";
        var findings = new System.Buffers.ArrayBufferWriter<QuickLeaksFinding>(1);
        Filter.Scan(text.AsSpan(), findings);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            findings.Clear();
            if (Filter.Scan(text.AsSpan(), findings) != 0)
            {
                throw new InvalidOperationException("The clean fixture unexpectedly matched.");
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
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
        const string header =
            "curl -H \"Authorization: Bearer abcdefgh\" https://example.test";
        var headerFinding = Filter
            .Scan(header, QuickLeaksScanMode.Early)
            .Single(finding => finding.RuleId == "curl-auth-header");
        header[headerFinding.Start..headerFinding.End].ShouldBe("abcdefgh");

        const string user = "curl -u user:password https://example.test";
        var userValues = Filter
            .Scan(user, QuickLeaksScanMode.Early)
            .Where(finding => finding.RuleId == "curl-auth-user")
            .Select(finding => user[finding.Start..finding.End])
            .ToArray();
        userValues.ShouldBe(["user", "password"]);
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
