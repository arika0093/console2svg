#!/usr/bin/env python3
"""Generate the Betterleaks quick filter from a downloaded upstream config."""
import argparse
import hashlib
import json
import re
import tomllib
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).parent
OUT = ROOT / "QuickLeaks.generated.cs"
REPORT = ROOT / "QuickLeaks.generation-report.json"
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
    if not marker.search(current):
        raise RuntimeError("README metadata markers were not found")
    updated = marker.sub(metadata, current, count=1)
    if updated != current:
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


@dataclass(frozen=True)
class PatternAnalysis:
    anchor: str | None
    fixed_offset: int | None
    fallback_reason: str


def analyze_pattern(pattern: str) -> PatternAnalysis:
    """Conservatively extract a literal fixed at the beginning of a match.

    This is intentionally a small compiler front-end rather than a permissive
    regex rewriter: anything it cannot prove remains on the regex fallback.
    Zero-width anchors and group openers do not advance the fixed offset.
    """
    # Alternation and optional groups require branch-level mandatory-literal
    # analysis. Keep them on the keyword safety net until that lowerer exists.
    if re.search(r"(?<!\\)\|", pattern):
        return PatternAnalysis(None, None, "alternation-not-lowered")

    index = 0
    literal: list[str] = []
    unsupported = None
    while index < len(pattern):
        if pattern.startswith("(?i)", index) or pattern.startswith("(?-i)", index):
            index += 4 if pattern.startswith("(?i)", index) else 5
            continue
        if pattern.startswith("(?:", index):
            index += 3
            continue
        if pattern[index] == "(":
            if pattern.startswith("(?", index):
                unsupported = "group-construct-before-anchor"
                break
            index += 1
            continue
        if pattern[index] in "^$":
            index += 1
            continue
        if pattern[index] == "\\":
            if index + 1 >= len(pattern):
                unsupported = "trailing-escape"
                break
            escaped = pattern[index + 1]
            if escaped in "bBAZzG":
                index += 2
                continue
            if escaped in r"\.^$|?*+()[]{}-":
                literal.append(escaped)
                index += 2
                continue
            unsupported = "character-class-before-anchor"
            break
        character = pattern[index]
        if character in "[.{*+?|)":
            if character in "?*" and literal:
                literal.pop()
            unsupported = "variable-prefix-before-anchor"
            break
        if character == "{" or character == "]":
            unsupported = "quantifier-before-anchor"
            break
        literal.append(character)
        index += 1

    anchor = "".join(literal)
    if len(anchor) >= 4 and anchor.isascii():
        return PatternAnalysis(anchor, 0, "verifier-not-lowered")
    return PatternAnalysis(None, None, unsupported or "no-fixed-literal-prefix")


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

if len(rules) > 65535:
    raise RuntimeError("The generated rules no longer fit in ushort indices")

analyses = [analyze_pattern(pattern) for _, pattern, _, _ in rules]
anchor_rules: dict[str, set[int]] = {}
always_candidates: set[int] = set()
report_rules = []
for rule_index, ((rule_id, pattern, _, keywords), analysis) in enumerate(zip(rules, analyses)):
    specialized = rule_id in {
        "console2svg-credential-uri",
        "generic-credential-uri",
        "curl-auth-header",
        "curl-auth-user",
    }
    source = "compiler"
    anchors = [analysis.anchor] if analysis.anchor else []
    # Until a dedicated verifier replaces the regex fallback, retain the
    # upstream keyword safety net as well. This makes the new front-end a
    # strict superset of the legacy candidate selection during migration.
    safety_anchors = list(dict.fromkeys(keyword for keyword in keywords if keyword))
    anchors.extend(anchor for anchor in safety_anchors if anchor not in anchors)
    if not analysis.anchor:
        source = "betterleaks-keyword"
    anchors = [anchor for anchor in anchors if anchor and anchor.isascii()]
    if not anchors:
        source = "none"
        always_candidates.add(rule_index)
    for anchor in anchors:
        anchor_rules.setdefault(anchor.lower(), set()).add(rule_index)
    report_rules.append({
        "index": rule_index,
        "id": rule_id,
        "engine": "specialized-verifier" if specialized else "regex-fallback",
        "anchorSource": source,
        "anchors": anchors,
        "fixedOffset": analysis.fixed_offset,
        "fallbackReason": None if specialized else analysis.fallback_reason,
        "canCrossNewline": "\\n" in pattern or "\\r" in pattern or "\\s" in pattern,
    })

anchors = sorted(anchor_rules, key=lambda value: (value[0], -len(value), value))
anchor_values = ",\n        ".join(json.dumps(anchor) for anchor in anchors)
candidate_word_count = (len(rules) + 63) // 64
candidate_fields = "\n".join(
    f"        private ulong _word{index};" for index in range(candidate_word_count)
)
candidate_add_cases = "\n".join(
    f"                case {index}: _word{index} |= mask; break;"
    for index in range(candidate_word_count)
)
candidate_take_cases = "\n".join(
    f"                    case {index}: word = _word{index}; _word{index} = 0; break;"
    for index in range(candidate_word_count)
)
always_adds = "\n".join(f"        candidates.Add({index});" for index in sorted(always_candidates))

buckets: dict[str, list[str]] = {}
for anchor in anchors:
    buckets.setdefault(anchor[0].lower(), []).append(anchor)
dispatch_cases = []
for first, bucket in sorted(buckets.items()):
    checks = []
    for anchor in sorted(bucket, key=lambda value: (-len(value), value)):
        adds = " ".join(f"candidates.Add({index});" for index in sorted(anchor_rules[anchor]))
        checks.append(
            f"                if (tail.StartsWith({json.dumps(anchor)}, StringComparison.OrdinalIgnoreCase)) {{ {adds} }}"
        )
    dispatch_cases.append(
        f"            case (char){ord(first)}:\n" + "\n".join(checks) + "\n                break;"
    )
anchor_dispatch = "\n".join(dispatch_cases)

rule_id_cases = "\n".join(
    f"        {index} => {json.dumps(rule_id)}," for index, (rule_id, _, _, _) in enumerate(rules)
)
postprocessor_cases = "\n".join(
    f"        {index} => FindingPostProcessor.{kind},"
    for index, (rule_id, _, _, _) in enumerate(rules)
    if (kind := {
        "console2svg-git-identity": "GitIdentity",
        "console2svg-credential-uri": "CredentialUri",
        "generic-credential-uri": "CredentialUri",
        "console2svg-home-directory": "HomeDirectory",
        "generic-username": "GenericUsername",
    }.get(rule_id))
)
rule_dispatch_cases = "\n".join(
    (
        f"            case {index}: FindCredentialUriMatches(text, mode, (ushort){index}, {str(rule_id == 'generic-credential-uri').lower()}, ref sink); break;"
        if rule_id in ("console2svg-credential-uri", "generic-credential-uri")
        else f"            case {index}: FindCurlMatches(text, (ushort){index}, {str(rule_id == 'curl-auth-header').lower()}, ref sink); break;"
        if rule_id in ("curl-auth-header", "curl-auth-user")
        else f"            case {index}: FindRuleMatches(mode == QuickLeaksScanMode.Early ? EarlyRule{index}() : Rule{index}(), text, (ushort){index}, ref sink); break;"
    )
    for index, (rule_id, _, _, _) in enumerate(rules)
)

engine = rf"""// <auto-generated />
// Generated from Betterleaks config: {URL}
// Betterleaks commit: {SHA}

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ConsoleToSvg.QuickLeaks;

public static partial class QuickLeaks
{{
    private const int MatchTimeoutMilliseconds = 10;
    private static readonly SearchValues<string> s_anchors = SearchValues.Create(
        new string[]
        {{
        {anchor_values}
        }},
        StringComparison.OrdinalIgnoreCase);

    private struct CandidateRules
    {{
{candidate_fields}
        private int _nextWord;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int ruleIndex)
        {{
            var mask = 1UL << (ruleIndex & 63);
            switch (ruleIndex >> 6)
            {{
{candidate_add_cases}
                default: throw new ArgumentOutOfRangeException(nameof(ruleIndex));
            }}
        }}

        public bool TryTake(out int ruleIndex)
        {{
            while (_nextWord < {candidate_word_count})
            {{
                ulong word;
                var wordIndex = _nextWord++;
                switch (wordIndex)
                {{
{candidate_take_cases}
                    default: word = 0; break;
                }}
                if (word == 0)
                {{
                    continue;
                }}
                var bit = BitOperations.TrailingZeroCount(word);
                ruleIndex = (wordIndex << 6) + bit;
                word &= word - 1;
                _nextWord--;
                switch (wordIndex)
                {{
{candidate_add_cases.replace(' |= mask', ' = word').replace('ruleIndex >> 6', 'wordIndex').replace('var mask = 1UL << (ruleIndex & 63);', '')}
                }}
                return true;
            }}
            ruleIndex = -1;
            return false;
        }}
    }}

    private static CandidateRules FindCandidateRules(ReadOnlySpan<char> text)
    {{
        var candidates = new CandidateRules();
{always_adds}
        var offset = 0;
        while (offset < text.Length)
        {{
            var relative = text[offset..].IndexOfAny(s_anchors);
            if (relative < 0)
            {{
                break;
            }}
            var anchorStart = offset + relative;
            DispatchAnchors(text[anchorStart..], ref candidates);
            offset = anchorStart + 1;
        }}
        return candidates;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DispatchAnchors(ReadOnlySpan<char> tail, ref CandidateRules candidates)
    {{
        var first = tail[0];
        if (first is >= 'A' and <= 'Z')
        {{
            first = (char)(first + ('a' - 'A'));
        }}
        switch (first)
        {{
{anchor_dispatch}
        }}
    }}

    private static void ScanGeneratedRules(
        ReadOnlySpan<char> text,
        QuickLeaksScanMode mode,
        ref FindingSink sink)
    {{
        var candidates = FindCandidateRules(text);
        while (candidates.TryTake(out var ruleIndex))
        {{
            DispatchRule(ruleIndex, text, mode, ref sink);
        }}
    }}

    private static void DispatchRule(
        int ruleIndex,
        ReadOnlySpan<char> text,
        QuickLeaksScanMode mode,
        ref FindingSink sink)
    {{
        switch (ruleIndex)
        {{
{rule_dispatch_cases}
        }}
    }}

    private static void FindRuleMatches(
        Regex regex,
        ReadOnlySpan<char> text,
        ushort ruleIndex,
        ref FindingSink sink)
    {{
        try
        {{
            foreach (var match in regex.EnumerateMatches(text))
            {{
                sink.Add(ruleIndex, match.Index, match.Index + match.Length);
            }}
        }}
        catch (RegexMatchTimeoutException)
        {{
            // Redaction is fail-closed: a pathological fallback must never turn
            // into a silent false negative.
            if (!text.IsEmpty)
            {{
                sink.Add(ruleIndex, 0, text.Length);
            }}
        }}
    }}

    internal static string GetRuleId(ushort ruleIndex) => ruleIndex switch
    {{
{rule_id_cases}
        _ => throw new ArgumentOutOfRangeException(nameof(ruleIndex)),
    }};

    private static FindingPostProcessor GetPostProcessor(ushort ruleIndex) => ruleIndex switch
    {{
{postprocessor_cases}
        _ => FindingPostProcessor.Default,
    }};
}}
"""
args.output.write_text(engine)

for stale in ROOT.glob("QuickLeaks.Regex*.generated.cs"):
    stale.unlink()
chunk_size = 64
for chunk_start in range(0, len(rules), chunk_size):
    declarations = []
    for index in range(chunk_start, min(chunk_start + chunk_size, len(rules))):
        _, pattern, early, _ = rules[index]
        declarations.append(
            f"    [GeneratedRegex({json.dumps(pattern)}, RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]\n"
            f"    private static partial Regex Rule{index}();\n"
            f"    [GeneratedRegex({json.dumps(early)}, RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]\n"
            f"    private static partial Regex EarlyRule{index}();"
        )
    (ROOT / f"QuickLeaks.Regex{chunk_start // chunk_size}.generated.cs").write_text(
        "// <auto-generated />\nusing System.Text.RegularExpressions;\n\n"
        "namespace ConsoleToSvg.QuickLeaks;\n\npublic static partial class QuickLeaks\n{\n"
        + "\n".join(declarations) + "\n}\n"
    )

report = {
    "schemaVersion": 1,
    "betterleaksCommit": SHA,
    "ruleCount": len(rules),
    "compilerAnchorRuleCount": sum(1 for item in report_rules if item["anchorSource"] == "compiler"),
    "keywordFallbackRuleCount": sum(1 for item in report_rules if item["anchorSource"] == "betterleaks-keyword"),
    "unfilteredFallbackRuleCount": len(always_candidates),
    "uniqueAnchorCount": len(anchors),
    "specializedVerifierRuleCount": sum(
        1 for item in report_rules if item["engine"] == "specialized-verifier"
    ),
    "rules": report_rules,
}
REPORT.write_text(json.dumps(report, indent=2) + "\n")

print(
    f"generated {len(rules)} rules, {len(anchors)} anchors, "
    f"{report['compilerAnchorRuleCount']} compiler-anchored rules"
)
update_readme_metadata(args.config, len(rules))
