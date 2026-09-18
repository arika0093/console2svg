#!/usr/bin/env python3
"""Generate the Betterleaks quick filter from a downloaded upstream config."""
import argparse
import hashlib
import json
import re
import tomllib
from collections import deque
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

class KeywordNode:
    def __init__(self) -> None:
        self.transitions = {}
        self.rules = set()


keyword_nodes = [KeywordNode()]
for rule_index, (_, _, _, keywords) in enumerate(rules):
    for keyword in dict.fromkeys(keyword for keyword in keywords if keyword):
        if not keyword.isascii():
            raise RuntimeError(f"QuickLeaks keywords must be ASCII: {keyword!r}")
        node_index = 0
        for character in keyword:
            next_index = keyword_nodes[node_index].transitions.get(character)
            if next_index is None:
                next_index = len(keyword_nodes)
                keyword_nodes[node_index].transitions[character] = next_index
                keyword_nodes.append(KeywordNode())
            node_index = next_index
        keyword_nodes[node_index].rules.add(rule_index)

keyword_fail = [0] * len(keyword_nodes)
fail_queue = deque()
for child in keyword_nodes[0].transitions.values():
    fail_queue.append(child)
while fail_queue:
    state = fail_queue.popleft()
    for character, target in keyword_nodes[state].transitions.items():
        fallback = keyword_fail[state]
        while fallback != 0 and character not in keyword_nodes[fallback].transitions:
            fallback = keyword_fail[fallback]
        keyword_fail[target] = keyword_nodes[fallback].transitions.get(character, 0)
        if keyword_nodes[keyword_fail[target]].rules:
            keyword_nodes[target].rules |= keyword_nodes[keyword_fail[target]].rules
        fail_queue.append(target)

if len(keyword_nodes) > 65535 or len(rules) > 65535:
    raise RuntimeError("The generated keyword trie no longer fits in ushort indices")

keyword_transitions = []
keyword_outputs = []
keyword_states = []
for node in keyword_nodes:
    transition_start = len(keyword_transitions)
    keyword_transitions.extend(sorted(node.transitions.items()))
    output_start = len(keyword_outputs)
    keyword_outputs.extend(sorted(node.rules))
    keyword_states.append(
        (transition_start, len(node.transitions), output_start, len(node.rules))
    )

keyword_state_data = json.dumps(
    "".join(
        chr(value)
        for state in keyword_states
        for value in state
    )
)
keyword_transition_data = json.dumps(
    "".join(character + chr(state) for character, state in keyword_transitions)
)
keyword_output_data = json.dumps("".join(chr(index) for index in keyword_outputs))
keyword_fail_data = json.dumps("".join(chr(value) for value in keyword_fail))
root_transition_cases = "\n".join(
    f"            (char){ord(character)} => (ushort){state},"
    for character, state in sorted(keyword_nodes[0].transitions.items())
)

candidate_word_count = (len(rules) + 63) // 64
candidate_fields = "\n".join(
    f"        private ulong _word{index};" for index in range(candidate_word_count)
)
candidate_add_cases = "\n".join(
    f"                case {index}: _word{index} |= mask; break;"
    for index in range(candidate_word_count)
)
candidate_contains_cases = "\n".join(
    f"                {index} => _word{index}," for index in range(candidate_word_count)
)

rule_dispatch = "\n".join(
    f"        if (candidates.Contains({index})) foreach (var finding in FindRuleMatches(mode == QuickLeaksScanMode.Early ? EarlyRule{index}() : Rule{index}(), text, {json.dumps(rule_id)})) yield return finding;"
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
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ConsoleToSvg.QuickLeaks;

/// <summary>Generated Betterleaks and ConsoleToSvg secret detection rules.</summary>
public static partial class QuickLeaks
{{
    private const int MatchTimeoutMilliseconds = 10;

    // Aho-Corasick automaton over lowercased ASCII keywords. Each state stores
    // transition start/count and output start/count as four UTF-16 code units.
    // Transitions store a character/state pair, outputs store rule indices, and fails
    // store the fallback state. Outputs include fallback matches. Packing the automaton
    // avoids thousands of static array initializer instructions.
    private const string KeywordStates = {keyword_state_data};
    private const string KeywordTransitions = {keyword_transition_data};
    private const string KeywordOutputs = {keyword_output_data};
    private const string KeywordFails = {keyword_fail_data};

    private struct CandidateRules
    {{
{candidate_fields}

        public void Add(int ruleIndex)
        {{
            var mask = 1UL << (ruleIndex & 63);
            switch (ruleIndex >> 6)
            {{
{candidate_add_cases}
                default: throw new ArgumentOutOfRangeException(nameof(ruleIndex));
            }}
        }}

        public readonly bool Contains(int ruleIndex)
        {{
            var word = (ruleIndex >> 6) switch
            {{
{candidate_contains_cases}
                _ => 0UL,
            }};
            return (word & (1UL << (ruleIndex & 63))) != 0;
        }}
    }}

    private static CandidateRules FindCandidateRules(string text)
    {{
        var candidates = new CandidateRules();
        var textSpan = text.AsSpan();
        ushort state = 0;
        for (var index = 0; index < textSpan.Length; index++)
        {{
            var value = NormalizeKeywordCharacter(textSpan[index]);
            if (state == 0)
            {{
                state = GetRootState(value);
                if (state == 0)
                {{
                    continue;
                }}
            }}
            else
            {{
                ushort next;
                while ((next = GetNextState(state, value)) == 0)
                {{
                    state = KeywordFails[state];
                    if (state == 0)
                    {{
                        next = GetRootState(value);
                        break;
                    }}
                }}
                if (next == 0)
                {{
                    state = 0;
                    continue;
                }}
                state = next;
            }}
            var stateOffset = state * 4;
            if (KeywordStates[stateOffset + 3] != 0)
            {{
                var outputStart = (int)KeywordStates[stateOffset + 2];
                var outputEnd = outputStart + KeywordStates[stateOffset + 3];
                for (var output = outputStart; output < outputEnd; output++)
                {{
                    candidates.Add(KeywordOutputs[output]);
                }}
            }}
        }}
        return candidates;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char NormalizeKeywordCharacter(char value)
    {{
        if (value >= 'A' && value <= 'Z')
        {{
            return (char)(value + ('a' - 'A'));
        }}
        return value <= (char)127 ? value : char.ToLowerInvariant(value);
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetRootState(char value) => value switch
    {{
{root_transition_cases}
        _ => 0,
    }};

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetNextState(ushort stateIndex, char value)
    {{
        var stateOffset = stateIndex * 4;
        var count = (int)KeywordStates[stateOffset + 1];
        var start = (int)KeywordStates[stateOffset];
        if (count == 1)
        {{
            var transitionOffset = start * 2;
            return KeywordTransitions[transitionOffset] == value ? (ushort)KeywordTransitions[transitionOffset + 1] : (ushort)0;
        }}
        if (count <= 8)
        {{
            var scanEnd = start + count;
            for (var transition = start; transition < scanEnd; transition++)
            {{
                var transitionOffset = transition * 2;
                if (KeywordTransitions[transitionOffset] == value)
                {{
                    return (ushort)KeywordTransitions[transitionOffset + 1];
                }}
            }}
            return 0;
        }}
        var low = start;
        var high = start + count - 1;
        while (low <= high)
        {{
            var middle = low + ((high - low) >> 1);
            var transitionOffset = middle * 2;
            var transitionValue = KeywordTransitions[transitionOffset];
            if (transitionValue == value)
            {{
                return (ushort)KeywordTransitions[transitionOffset + 1];
            }}
            if (transitionValue < value)
            {{
                low = middle + 1;
            }}
            else
            {{
                high = middle - 1;
            }}
        }}
        return 0;
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
