using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

        generatedRules.Length.ShouldBe(Filter.RegexFallbackRuleCount * 2);
        generatedRules
            .All(regex =>
                regex.MatchTimeout
                == TimeSpan.FromMilliseconds(
                    regex.Options.HasFlag(RegexOptions.NonBacktracking) ? 100 : 10
                )
            )
            .ShouldBeTrue();
    }

    [Test]
    public void TimedOutRuleFailsClosedWithAConservativeRedaction()
    {
        var pathological = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        var input = new string('a', 100_000) + "!";

        var findings = Filter.ScanRegexFallbackForTesting(pathological, input);

        findings.Length.ShouldBe(1);
        findings[0].Start.ShouldBe(0);
        findings[0].End.ShouldBe(input.Length);
    }

    [Test]
    public void TimedOutRuleBypassesFindingPostProcessing()
    {
        using var report = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "QuickLeaks.generation-report.json")
            )
        );
        var ruleIndex = (ushort)
            report
                .RootElement.GetProperty("rules")
                .EnumerateArray()
                .Single(rule => rule.GetProperty("id").GetString() == "generic-username")
                .GetProperty("index")
                .GetInt32();
        var pathological = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        var input = new string('a', 100_000) + "!";

        var findings = Filter.ScanRegexFallbackForTesting(pathological, input, ruleIndex);

        findings.ShouldBe([new QuickLeaksFinding("generic-username", 0, input.Length)]);
    }

    [Test]
    public void ScanFindsSecretsAndHomeDirectoryPaths()
    {
        var findings = Scan("/home/alice/project");

        findings.Any(finding => finding.RuleId == "console2svg-home-directory").ShouldBeTrue();
    }

    [Test]
    public void SpanScanWritesToCallerOwnedBuffer()
    {
        var findings = new System.Buffers.ArrayBufferWriter<QuickLeaksFinding>(4);

        var count = Filter.Scan("/home/alice/project".AsSpan(), findings);

        count.ShouldBe(findings.WrittenCount);
        findings
            .WrittenSpan.ToArray()
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
    public void CustomLiteralScannerUsesTheSpanWriterPath()
    {
        var scanner = new QuickLeaksScanner(
            new QuickLeaksScannerOptions
            {
                SensitiveLiterals =
                [
                    new QuickLeaksCustomLiteral("custom-user", "alice"),
                    new QuickLeaksCustomLiteral(
                        "custom-host",
                        "EXAMPLE.TEST",
                        StringComparison.OrdinalIgnoreCase
                    ),
                ],
            }
        );
        var findings = new System.Buffers.ArrayBufferWriter<QuickLeaksFinding>();

        scanner.Scan("alice@example.test".AsSpan(), findings);

        findings.WrittenSpan.ToArray().ShouldContain(new QuickLeaksFinding("custom-user", 0, 5));
        findings.WrittenSpan.ToArray().ShouldContain(new QuickLeaksFinding("custom-host", 6, 18));
    }

    [Test]
    public void EarlyModeFindsPartiallyEnteredGithubToken()
    {
        var partialToken = "ghp_" + "abc";

        Scan(partialToken).ShouldBeEmpty();
        Scan(partialToken, QuickLeaksScanMode.Early)
            .Any(finding => finding.RuleId == "github-pat")
            .ShouldBeTrue();
    }

    [Test]
    public void EarlyModeFindsPartiallyEnteredAirtableTokenWithoutKeyword()
    {
        const string partialToken = "patabc.a";

        Scan(partialToken).ShouldBeEmpty();
        Scan(partialToken, QuickLeaksScanMode.Early)
            .Any(finding => finding.RuleId == "airtable-personnal-access-token")
            .ShouldBeTrue();
    }

    [Test]
    public void PrefixTokenVerifierPreservesCaseAndMatchRange()
    {
        var token = "ghp_" + new string('a', 36);
        var input = "--" + token + "--";

        var finding = Scan(input).Single(item => item.RuleId == "github-pat");

        finding.Start.ShouldBe(2);
        finding.End.ShouldBe(2 + token.Length);
        Scan(input.Replace("ghp_", "GHP_", StringComparison.Ordinal))
            .Any(item => item.RuleId == "github-pat")
            .ShouldBeFalse();
    }

    [Test]
    public void PrefixTokenVerifierHonorsIgnoreCaseAndWordBoundaries()
    {
        var clojars = "clojars_" + new string('A', 60);
        Scan(clojars).Any(item => item.RuleId == "clojars-api-token").ShouldBeTrue();

        var aikido = "AIK_CI_" + new string('a', 20);
        Scan("x" + aikido).Any(item => item.RuleId == "aikido-ci-token").ShouldBeFalse();
        Scan(" " + aikido + " ").Any(item => item.RuleId == "aikido-ci-token").ShouldBeTrue();
    }

    [Test]
    [NotInParallel]
    public void PrefixTokenVerifiersMatchRegexOracle()
    {
        using var report = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "QuickLeaks.generation-report.json")
            )
        );
        var optimizedRules = report
            .RootElement.GetProperty("rules")
            .EnumerateArray()
            .Where(rule => rule.GetProperty("engine").GetString() == "prefix-token-verifier")
            .ToArray();

        optimizedRules.Length.ShouldBe(39);
        foreach (var rule in optimizedRules)
        {
            var descriptor = rule.GetProperty("prefixToken");
            var prefix = descriptor.GetProperty("prefix").GetString()!;
            var sampleCharacter = SelectPrefixTokenSampleCharacter(descriptor);
            var ruleId = rule.GetProperty("id").GetString()!;
            var ruleIndex = (ushort)rule.GetProperty("index").GetInt32();

            foreach (var mode in new[] { QuickLeaksScanMode.Normal, QuickLeaksScanMode.Early })
            {
                var minimumProperty =
                    mode == QuickLeaksScanMode.Normal ? "normalMinimum" : "earlyMinimum";
                var minimum = descriptor.GetProperty(minimumProperty).GetInt32();
                var patternProperty =
                    mode == QuickLeaksScanMode.Normal ? "pattern" : "earlyPattern";
                var regex = new Regex(
                    rule.GetProperty(patternProperty).GetString()!,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                );
                var sampleLength = minimum;
                var input = " " + prefix + new string(sampleCharacter, sampleLength) + " ";
                var match = regex.Match(input);
                while (!match.Success && sampleLength < minimum + 2)
                {
                    sampleLength++;
                    input = " " + prefix + new string(sampleCharacter, sampleLength) + " ";
                    match = regex.Match(input);
                }
                match.Success.ShouldBeTrue(
                    $"The generated fixture must match {ruleId} in {mode} mode."
                );
                var expected = Filter.NarrowFindingForTesting(
                    input,
                    ruleIndex,
                    match.Index,
                    match.Index + match.Length
                );

                var actual = Scan(input, mode).Single(finding => finding.RuleId == ruleId);

                actual.ShouldBe(
                    expected,
                    $"The prefix verifier diverged for {ruleId} in {mode} mode."
                );
            }
        }
    }

    private static char SelectPrefixTokenSampleCharacter(JsonElement descriptor)
    {
        var low = ulong.Parse(
            descriptor.GetProperty("classLowMask").GetString()!,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture
        );
        var high = ulong.Parse(
            descriptor.GetProperty("classHighMask").GetString()!,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture
        );
        foreach (var character in "aA0_bB1-+/=")
        {
            var mask = character < 64 ? low : high;
            if (((mask >> (character & 63)) & 1) != 0)
            {
                return character;
            }
        }
        throw new InvalidOperationException(
            "The optimized character class has no testable ASCII member."
        );
    }

    [Test]
    [NotInParallel]
    public void AssignmentVerifiersMatchRegexOracle()
    {
        using var report = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "QuickLeaks.generation-report.json")
            )
        );
        var optimizedRules = report
            .RootElement.GetProperty("rules")
            .EnumerateArray()
            .Where(rule => rule.GetProperty("engine").GetString() == "assignment-verifier")
            .ToArray();

        optimizedRules.Length.ShouldBe(74);
        foreach (var rule in optimizedRules)
        {
            var descriptor = rule.GetProperty("assignment");
            var provider = descriptor.GetProperty("provider").GetString()!;
            var sampleCharacter = SelectPrefixTokenSampleCharacter(descriptor);
            var ruleId = rule.GetProperty("id").GetString()!;
            var ruleIndex = (ushort)rule.GetProperty("index").GetInt32();

            foreach (var mode in new[] { QuickLeaksScanMode.Normal, QuickLeaksScanMode.Early })
            {
                var minimumProperty =
                    mode == QuickLeaksScanMode.Normal ? "normalMinimum" : "earlyMinimum";
                var minimum = descriptor.GetProperty(minimumProperty).GetInt32();
                var input =
                    " "
                    + provider
                    + "_key = '"
                    + new string(sampleCharacter, Math.Max(1, minimum))
                    + "' ";
                var patternProperty =
                    mode == QuickLeaksScanMode.Normal ? "pattern" : "earlyPattern";
                var regex = new Regex(
                    rule.GetProperty(patternProperty).GetString()!,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                );
                var match = regex.Match(input);
                match.Success.ShouldBeTrue(
                    $"The generated fixture must match {ruleId} in {mode} mode."
                );
                var expected = Filter.NarrowFindingForTesting(
                    input,
                    ruleIndex,
                    match.Index,
                    match.Index + match.Length
                );

                var actualMatches = Scan(input, mode)
                    .Where(finding => finding.RuleId == ruleId)
                    .ToArray();
                actualMatches.ShouldNotBeEmpty(
                    $"The assignment verifier missed {ruleId} in {mode} mode."
                );
                var actual = actualMatches.Single();

                actual.ShouldBe(
                    expected,
                    $"The assignment verifier diverged for {ruleId} in {mode} mode."
                );
            }
        }
    }

    [Test]
    public void EarlyModeFindsPartiallyEnteredCredentialUri()
    {
        var partialUri = "https" + "://user:" + "pass";

        Scan(partialUri)
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeFalse();
        Scan(partialUri, QuickLeaksScanMode.Early)
            .Any(finding => finding.RuleId == "console2svg-credential-uri")
            .ShouldBeTrue();
        Scan(partialUri + "@example.test")
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

        Scan(text, QuickLeaksScanMode.Early).ShouldBeEmpty();
        const string header = "curl -H \"Authorization: Bearer abcdefgh\" https://example.test";
        var headerFinding = Scan(header, QuickLeaksScanMode.Early)
            .Single(finding => finding.RuleId == "curl-auth-header");
        header[headerFinding.Start..headerFinding.End].ShouldBe("abcdefgh");

        const string user = "curl -u user:password https://example.test";
        var userValues = Scan(user, QuickLeaksScanMode.Early)
            .Where(finding => finding.RuleId == "curl-auth-user")
            .Select(finding => user[finding.Start..finding.End])
            .ToArray();
        userValues.ShouldBe(["user", "password"]);
    }

    [Test]
    public void CurlVerifierHandlesRepeatedAttachedAndIndependentlyQuotedOptions()
    {
        const string repeatedHeaders =
            "curl -H \"Content-Type: application/json\" -H \"Authorization: Bearer abcdefgh\" https://example.test";
        GetCurlValues(repeatedHeaders, "curl-auth-header").ShouldBe(["abcdefgh"]);

        const string attachedHeader =
            "curl -H\"Authorization: Bearer abcdefgh\" https://example.test";
        GetCurlValues(attachedHeader, "curl-auth-header").ShouldBe(["abcdefgh"]);

        const string attachedUser = "curl -uuser:password https://example.test";
        GetCurlValues(attachedUser, "curl-auth-user").ShouldBe(["user", "password"]);

        const string independentlyQuotedUser = "curl -u \"user\":\"password\" https://example.test";
        GetCurlValues(independentlyQuotedUser, "curl-auth-user").ShouldBe(["user", "password"]);
    }

    private static string[] GetCurlValues(string command, string ruleId) =>
        Scan(command, QuickLeaksScanMode.Early)
            .Where(finding => finding.RuleId == ruleId)
            .Select(finding => command[finding.Start..finding.End])
            .ToArray();

    [Test]
    public void GenericPasswordMasksOnlyTheValue()
    {
        var text = "\"password\": \"123456\"";
        text = "https://user:secret@example.test/path";
        text = "\"password\": \"123456\"";
        var findings = Scan(text);

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        var finding = findings.Single(finding => finding.RuleId == "generic-password");
        text.Substring(finding.Start, finding.End - finding.Start).ShouldBe("123456");
        Scan("PASSWORD=").ShouldBeEmpty();
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
            var findings = Scan(path);
            var finding = findings.Single(finding =>
                finding.RuleId == "console2svg-home-directory"
            );
            path.Substring(finding.Start, finding.End - finding.Start).ShouldBe("alice");
        }
    }

    [Test]
    public void CredentialUriMasksUsernameAndPasswordIndependently()
    {
        var text = "https://user:secret@example.test/path";
        var findings = Scan(text);

        text = "https://user:secret@example.test/path";
        findings = Scan(text);
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
        var findings = Scan("PASSWORD=123456\nDATABASE_URL=postgres://user:secret@host/testdb");

        findings.Any(finding => finding.RuleId == "generic-password").ShouldBeTrue();
        findings.Any(finding => finding.RuleId == "generic-credential-uri").ShouldBeTrue();
    }

    private static QuickLeaksFinding[] Scan(
        string text,
        QuickLeaksScanMode mode = QuickLeaksScanMode.Normal
    )
    {
        var findings = new System.Buffers.ArrayBufferWriter<QuickLeaksFinding>();
        Filter.Scan(text.AsSpan(), findings, mode);
        return findings.WrittenSpan.ToArray();
    }
}
