#!/usr/bin/env python3
"""Generate the Betterleaks quick filter from a downloaded upstream config."""
import argparse
import hashlib
import json
import re
import tomllib
from pathlib import Path

ROOT = Path(__file__).parent
OUT = ROOT / "QuickLeaks.generated.cs"
README = ROOT / "README.md"
SHA = "2a387a5bad4290a84b9a1eb679bffe70611218cc"
URL = f"https://github.com/betterleaks/betterleaks/blob/{SHA}/config/betterleaks.toml"


def update_readme_metadata(config: Path, rule_count: int) -> None:
    marker = re.compile(
        r"<!-- QUICKLEAKS-METADATA:START -->.*?<!-- QUICKLEAKS-METADATA:END -->",
        re.DOTALL,
    )
    current = README.read_text()
    metadata = (
        "<!-- QUICKLEAKS-METADATA:START -->\n"
        f"- Betterleaks commit: `{SHA}`\n"
        f"- Downloaded config SHA-256: `{hashlib.sha256(config.read_bytes()).hexdigest()}`\n"
        f"- Generated rules: `{rule_count}`\n"
        "<!-- QUICKLEAKS-METADATA:END -->"
    )
    updated = marker.sub(metadata, current, count=1)
    if updated == current:
        raise RuntimeError("README metadata markers were not found")
    README.write_text(updated)


def convert(pattern: str) -> str:
    pattern = pattern.replace("(?P<", "(?<")
    classes = {
        "alnum": "A-Za-z0-9", "alpha": "A-Za-z", "digit": "0-9",
        "lower": "a-z", "upper": "A-Z", "word": "A-Za-z0-9_",
        "space": r"\s",
    }
    return re.sub(
        r"\[\[:([a-z]+):\]\]",
        lambda match: classes.get(match.group(1), match.group(0)),
        pattern,
    )


def is_capturing_group(pattern: str, index: int) -> bool:
    if index + 1 >= len(pattern) or pattern[index + 1] != "?":
        return True
    if pattern.startswith("(?<", index):
        # (?<=...) and (?<!...) are lookbehinds; other (?<...>...) groups are named captures.
        return index + 3 < len(pattern) and pattern[index + 3] not in "=!"
    return pattern.startswith("(?'", index)


def has_capturing_group(pattern: str) -> bool:
    in_character_class = False
    index = 0
    while index < len(pattern):
        character = pattern[index]
        if character == "\\":
            index += 2
            continue
        if character == "[" and not in_character_class:
            in_character_class = True
        elif character == "]" and in_character_class:
            in_character_class = False
        elif not in_character_class and character == "(" and is_capturing_group(pattern, index):
            return True
        index += 1
    return False


def early_pattern(pattern: str) -> str:
    """Relax fixed token lengths only inside the rule's secret-value capture."""
    output = []
    group_stack = []
    whole_match_is_value = not has_capturing_group(pattern)
    in_character_class = False
    index = 0
    quantifier = re.compile(r"\{(\d+)(?:,(\d+))?\}")

    while index < len(pattern):
        character = pattern[index]
        if character == "\\":
            output.append(pattern[index:index + 2])
            index += 2
            continue
        if character == "[" and not in_character_class:
            in_character_class = True
            output.append(character)
            index += 1
            continue
        if character == "]" and in_character_class:
            in_character_class = False
            output.append(character)
            index += 1
            continue
        if not in_character_class and character == "(":
            group_stack.append(is_capturing_group(pattern, index))
            output.append(character)
            index += 1
            continue
        if not in_character_class and character == ")":
            if group_stack:
                group_stack.pop()
            output.append(character)
            index += 1
            continue
        if not in_character_class and character == "{" and (
            whole_match_is_value or any(group_stack)
        ):
            match = quantifier.match(pattern, index)
            if match:
                minimum, maximum = match.groups()
                output.append(f"{{0,{maximum or minimum}}}")
                index = match.end()
                continue

        output.append(character)
        index += 1

    return "".join(output)


def validate_early_pattern() -> None:
    cases = {
        r"prefix(?:[\r\n]{1,2}.*?){1,5}(token[A-Z]{32})":
            r"prefix(?:[\r\n]{1,2}.*?){1,5}(token[A-Z]{0,32})",
        r"(?i)(?<token>[A-Z]{8})-(?:[0-9]{4})":
            r"(?i)(?<token>[A-Z]{0,8})-(?:[0-9]{4})",
        r"(?<=prefix)([a-z]{3,12})": r"(?<=prefix)([a-z]{0,12})",
        r"(?:literal\{4\})(value[0-9]{6})": r"(?:literal\{4\})(value[0-9]{0,6})",
        r"ghp_[0-9A-Za-z]{36}": r"ghp_[0-9A-Za-z]{0,36}",
    }
    for pattern, expected in cases.items():
        actual = early_pattern(pattern)
        if actual != expected:
            raise RuntimeError(f"Early-pattern rewrite failed: {pattern!r} -> {actual!r}")


validate_early_pattern()


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("config", type=Path, help="Downloaded Betterleaks TOML configuration")
parser.add_argument("--output", type=Path, default=OUT, help="Generated C# path")
args = parser.parse_args()

patch_path = ROOT / "betterleaks.toml.patch"
config = tomllib.loads(args.config.read_text())

rules = []
for rule in config["rules"]:
    if rule.get("regex"):
        pattern = convert(rule["regex"])
        rules.append(
            (rule["id"], pattern, convert(rule.get("early_regex", early_pattern(pattern))),
             tuple(keyword.lower() for keyword in rule.get("keywords", [])))
        )

if patch_path.exists():
    patch = tomllib.loads(patch_path.read_text())
    for rule in patch.get("rules", []):
        if rule.get("regex"):
            pattern = convert(rule["regex"])
            rules.append(
                (rule["id"], pattern, convert(rule.get("early_regex", early_pattern(pattern))),
                 tuple(keyword.lower() for keyword in rule.get("keywords", [])))
            )

keyword_index = {}
for index, (_, _, _, keywords) in enumerate(rules):
    for keyword in dict.fromkeys(keyword for keyword in keywords if keyword):
        keyword_index.setdefault(keyword[0], []).append((keyword, index))

keyword_entries = []
for initial, keywords in sorted(keyword_index.items()):
    values = ", ".join(
        f"new KeywordRule({json.dumps(keyword)}, {index})"
        for keyword, index in keywords
    )
    keyword_entries.append(f"            [{json.dumps(initial)}[0]] = [{values}],")
keyword_entries = "\n".join(keyword_entries)

rule_dispatch = "\n".join(
    f"        if (candidates[{index}]) foreach (var finding in FindRuleMatches(mode == QuickLeaksScanMode.Early ? EarlyRule{index}() : Rule{index}(), text, {json.dumps(rule_id)})) yield return finding;"
    for index, (rule_id, _, _, _) in enumerate(rules)
)
regex_declarations = "\n".join(
    f"    [GeneratedRegex({json.dumps(pattern)}, RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]\n"
    f"    private static partial Regex Rule{index}();\n"
    f"    [GeneratedRegex({json.dumps(early)}, RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]\n"
    f"    private static partial Regex EarlyRule{index}();"
    for index, (_, pattern, early, _) in enumerate(rules)
)

with args.output.open("w") as output:
    output.write(rf"""// <auto-generated />
// Generated from Betterleaks config: {URL}
// Betterleaks commit: {SHA}
// Betterleaks is MIT licensed; see upstream LICENSE.
// ConsoleToSvg rules are defined in betterleaks.toml.patch.

using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConsoleToSvg.QuickLeaks;

/// <summary>Generated Betterleaks and ConsoleToSvg secret detection rules.</summary>
public static partial class QuickLeaks
{{
    private const int MatchTimeoutMilliseconds = 10;

    /// <summary>Associates a keyword with the generated rule that should be tested.</summary>
    /// <param name="Value">The case-insensitive keyword.</param>
    /// <param name="RuleIndex">The generated rule index.</param>
    private readonly record struct KeywordRule(string Value, int RuleIndex);

    private static readonly FrozenDictionary<char, KeywordRule[]> KeywordIndex =
        new Dictionary<char, KeywordRule[]>
        {{
{keyword_entries}
        }}.ToFrozenDictionary();

    private static bool[] FindCandidateRules(string text)
    {{
        var candidates = new bool[{len(rules)}];
        var textSpan = text.AsSpan();
        for (var index = 0; index < textSpan.Length; index++)
        {{
            if (!KeywordIndex.TryGetValue(char.ToLowerInvariant(textSpan[index]), out var keywords))
            {{
                continue;
            }}
            foreach (var keyword in keywords)
            {{
                if (!candidates[keyword.RuleIndex] && textSpan[index..].StartsWith(keyword.Value, StringComparison.OrdinalIgnoreCase))
                {{
                    candidates[keyword.RuleIndex] = true;
                }}
            }}
        }}
        return candidates;
    }}

    private static IEnumerable<QuickLeaksFinding> EnumerateGeneratedRules(string text, QuickLeaksScanMode mode)
    {{
        var candidates = FindCandidateRules(text);
{rule_dispatch}
    }}

    private static IReadOnlyList<QuickLeaksFinding> FindRuleMatches(Regex regex, string text, string ruleId)
    {{
        var findings = new List<QuickLeaksFinding>();
        try
        {{
            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {{
                findings.Add(new QuickLeaksFinding(ruleId, match.Index, match.Index + match.Length));
            }}
        }}
        catch (RegexMatchTimeoutException)
        {{
            return [];
        }}

        return findings;
    }}

{regex_declarations}
}}
""")

print(f"generated {len(rules)} rules")
update_readme_metadata(args.config, len(rules))
