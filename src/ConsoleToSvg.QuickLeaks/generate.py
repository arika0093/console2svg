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
        lambda match: (
            f"[{classes[match.group(1)]}]"
            if match.group(1) in classes
            else match.group(0)
        ),
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


@dataclass(frozen=True)
class PrefixTokenAnalysis:
    prefix: str
    prefix_ignore_case: bool
    class_low_mask: int
    class_high_mask: int
    normal_minimum: int
    early_minimum: int
    maximum: int | None
    leading_boundary: bool
    trailing_boundary: bool


@dataclass(frozen=True)
class AssignmentAnalysis:
    provider: str
    class_low_mask: int
    class_high_mask: int
    normal_minimum: int
    early_minimum: int
    maximum: int | None


ASSIGNMENT_CONTEXT = r"""(?:[ \t\w.-]{0,20})[\s'"]{0,3}"""
ASSIGNMENT_SEPARATOR = r'(?:=|>|:{1,3}=|\|\||:|=>|\?=|,)'
ASSIGNMENT_PADDING = r"""[\x60'"\s=]{0,5}"""
ASSIGNMENT_TERMINATOR = r"""(?:\\?['"\x60]|[\s;]|\\[nr]|$)"""


def parse_literal_expression(expression: str) -> str | None:
    output: list[str] = []
    index = 0
    metacharacters = set(r".^$|?*+()[]{}")
    while index < len(expression):
        character = expression[index]
        if character == "\\":
            if index + 1 >= len(expression) or expression[index + 1] not in r"\.^$|?*+()[]{}-_+/=:@":
                return None
            output.append(expression[index + 1])
            index += 2
            continue
        if character in metacharacters or not character.isascii():
            return None
        output.append(character)
        index += 1
    literal = "".join(output)
    return literal if len(literal) >= 4 else None


def parse_assignment_shape(pattern: str) -> tuple[str, int, int, int, int | None] | None:
    scoped = pattern.startswith("(?i:(?:")
    global_case = pattern.startswith("(?i)(?:")
    if not scoped and not global_case:
        return None
    provider_start = 7
    provider_end = pattern.find(")", provider_start)
    if provider_end < 0:
        return None
    provider = parse_literal_expression(pattern[provider_start:provider_end])
    if provider is None:
        return None
    index = provider_end + 1
    if not pattern.startswith(ASSIGNMENT_CONTEXT, index):
        return None
    index += len(ASSIGNMENT_CONTEXT)
    if scoped:
        if index >= len(pattern) or pattern[index] != ")":
            return None
        index += 1
    if not pattern.startswith(ASSIGNMENT_SEPARATOR + ASSIGNMENT_PADDING + "(", index):
        return None
    index += len(ASSIGNMENT_SEPARATOR) + len(ASSIGNMENT_PADDING) + 1
    if index >= len(pattern) or pattern[index] != "[":
        return None
    class_end = pattern.find("]", index + 1)
    if class_end < 0:
        return None
    masks = parse_ascii_character_class(pattern[index + 1:class_end], ignore_case=True)
    if masks is None:
        return None
    index = class_end + 1
    quantifier = re.match(r"\{(\d+)(?:,(\d*))?\}", pattern[index:])
    if quantifier is None:
        return None
    minimum = int(quantifier.group(1))
    maximum_text = quantifier.group(2)
    maximum = minimum if maximum_text is None else (int(maximum_text) if maximum_text else None)
    index += quantifier.end()
    if index >= len(pattern) or pattern[index] != ")":
        return None
    index += 1
    if pattern[index:] != ASSIGNMENT_TERMINATOR:
        return None
    low, high = masks
    return provider, low, high, minimum, maximum


def lower_assignment(pattern: str, early: str) -> AssignmentAnalysis | None:
    normal_shape = parse_assignment_shape(pattern)
    early_shape = parse_assignment_shape(early)
    if normal_shape is None or early_shape is None:
        return None
    if normal_shape[:3] != early_shape[:3] or normal_shape[4] != early_shape[4]:
        return None
    provider, low, high, normal_minimum, maximum = normal_shape
    early_minimum = early_shape[3]
    if early_minimum > normal_minimum:
        return None
    return AssignmentAnalysis(
        provider,
        low,
        high,
        normal_minimum,
        early_minimum,
        maximum,
    )


def can_use_non_backtracking(pattern: str) -> bool:
    """Select only the documented regular subset with a bounded compile size."""
    unsupported = ("(?=", "(?!", "(?<=", "(?<!", "(?>", "(?(", r"\k<", r"\k'")
    quantifiers = re.findall(r"\{(\d+)(?:,(\d*))?\}", pattern)
    return (
        len(pattern) <= 192
        and not any(token in pattern for token in unsupported)
        and re.search(r"(?<!\\)\\[1-9]", pattern) is None
        and re.search(r"(?<!\\)\|", pattern) is None
        and all(int(maximum or minimum) <= 128 for minimum, maximum in quantifiers)
    )


def parse_ascii_character_class(content: str, ignore_case: bool) -> tuple[int, int] | None:
    """Compile a positive ASCII character class into two 64-bit masks."""
    characters: set[int] = set()
    atoms: list[tuple[int, bool]] = []
    index = 0
    while index < len(content):
        if content[index] == "\\":
            if index + 1 >= len(content) or content[index + 1] not in r"\.^$|?*+()[]{}-_+/=:@":
                return None
            value = ord(content[index + 1])
            was_escaped = True
            index += 2
        else:
            value = ord(content[index])
            was_escaped = False
            index += 1
        if value >= 128:
            return None
        atoms.append((value, was_escaped))

    index = 0
    while index < len(atoms):
        if (
            index + 2 < len(atoms)
            and atoms[index + 1][0] == ord("-")
            and not atoms[index + 1][1]
        ):
            start, end = atoms[index][0], atoms[index + 2][0]
            if start > end:
                return None
            characters.update(range(start, end + 1))
            index += 3
        else:
            characters.add(atoms[index][0])
            index += 1

    if ignore_case:
        for value in tuple(characters):
            if ord("a") <= value <= ord("z"):
                characters.add(value - (ord("a") - ord("A")))
            elif ord("A") <= value <= ord("Z"):
                characters.add(value + (ord("a") - ord("A")))

    low = sum(1 << value for value in characters if value < 64)
    high = sum(1 << (value - 64) for value in characters if value >= 64)
    return low, high


def parse_prefix_token_shape(pattern: str) -> tuple[
    str, bool, int, int, int, int | None, bool, bool
] | None:
    """Parse the conservative prefix + ASCII class repetition subset."""
    index = 0
    ignore_case = False
    if pattern.startswith("(?i)"):
        ignore_case = True
        index += 4

    leading_boundary = pattern.startswith(r"\b", index)
    if leading_boundary:
        index += 2

    closing_group = False
    if index < len(pattern) and pattern[index] == "(":
        if pattern.startswith("(?:", index):
            index += 3
        elif pattern.startswith("(?<", index):
            name_end = pattern.find(">", index + 3)
            if name_end < 0:
                return None
            index = name_end + 1
        elif not pattern.startswith("(?", index):
            index += 1
        else:
            return None
        closing_group = True

    prefix_start = index
    prefix: list[str] = []
    metacharacters = set(r".^$|?*+()[]{}")
    while index < len(pattern):
        if pattern.startswith("(?i)", index) or pattern[index] == "[":
            break
        character = pattern[index]
        if character == "\\":
            if index + 1 >= len(pattern) or pattern[index + 1] not in r"\.^$|?*+()[]{}-_+/=:@":
                return None
            prefix.append(pattern[index + 1])
            index += 2
            continue
        if character in metacharacters or not character.isascii():
            return None
        prefix.append(character)
        index += 1

    literal = "".join(prefix)
    if index == prefix_start or len(literal) < 4:
        return None

    class_ignore_case = ignore_case
    if pattern.startswith("(?i)", index):
        class_ignore_case = True
        index += 4
    if index >= len(pattern) or pattern[index] != "[":
        return None
    class_end = index + 1
    escaped = False
    while class_end < len(pattern):
        character = pattern[class_end]
        if character == "]" and not escaped:
            break
        escaped = character == "\\" and not escaped
        if character != "\\":
            escaped = False
        class_end += 1
    if class_end >= len(pattern):
        return None
    masks = parse_ascii_character_class(pattern[index + 1:class_end], class_ignore_case)
    if masks is None:
        return None
    index = class_end + 1

    quantifier = re.match(r"\{(\d+)(?:,(\d*))?\}", pattern[index:])
    if quantifier is None:
        return None
    minimum = int(quantifier.group(1))
    maximum_text = quantifier.group(2)
    maximum = minimum if maximum_text is None else (int(maximum_text) if maximum_text else None)
    if maximum is not None and maximum < minimum:
        return None
    index += quantifier.end()

    if closing_group:
        if index >= len(pattern) or pattern[index] != ")":
            return None
        index += 1

    trailing_boundary = pattern.startswith(r"\b", index)
    if trailing_boundary:
        index += 2
    if index != len(pattern):
        return None

    low, high = masks
    return (
        literal,
        ignore_case,
        low,
        high,
        minimum,
        maximum,
        leading_boundary,
        trailing_boundary,
    )


def lower_prefix_token(pattern: str, early: str) -> PrefixTokenAnalysis | None:
    normal_shape = parse_prefix_token_shape(pattern)
    early_shape = parse_prefix_token_shape(early)
    if normal_shape is None or early_shape is None:
        return None
    if normal_shape[:4] != early_shape[:4] or normal_shape[5:] != early_shape[5:]:
        return None
    prefix, prefix_ignore_case, low, high, normal_minimum, maximum, leading, trailing = normal_shape
    early_minimum = early_shape[4]
    if early_minimum > normal_minimum:
        return None
    return PrefixTokenAnalysis(
        prefix,
        prefix_ignore_case,
        low,
        high,
        normal_minimum,
        early_minimum,
        maximum,
        leading,
        trailing,
    )


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
        if character == "{":
            quantifier = re.match(r"\{(\d+)(?:,\d*)?\}", pattern[index:])
            if quantifier is not None and int(quantifier.group(1)) == 0 and literal:
                literal.pop()
            unsupported = "quantifier-before-anchor"
            break
        if character == "]":
            unsupported = "quantifier-before-anchor"
            break
        literal.append(character)
        index += 1

    anchor = "".join(literal)
    if len(anchor) >= 3 and anchor.isascii():
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

# The same anchor set is used by both modes, so every compiler anchor must be
# mandatory in the relaxed Early expression as well as in the normal one.
analyses = [analyze_pattern(early) for _, _, early, _ in rules]
prefix_tokens = [lower_prefix_token(pattern, early) for _, pattern, early, _ in rules]
assignments = [
    None if prefix_tokens[index] else lower_assignment(pattern, early)
    for index, (_, pattern, early, _) in enumerate(rules)
]
anchor_candidate_rules: dict[str, set[int]] = {}
anchor_prefix_rules: dict[str, list[int]] = {}
anchor_assignment_rules: dict[str, list[int]] = {}
anchor_home_directory_rules: dict[str, list[int]] = {}
always_candidates: set[int] = set()
report_rules = []
for rule_index, (
    (rule_id, pattern, early, keywords),
    analysis,
    prefix_token,
    assignment,
) in enumerate(
    zip(rules, analyses, prefix_tokens, assignments)
):
    specialized = rule_id in {
        "console2svg-credential-uri",
        "generic-credential-uri",
        "curl-auth-header",
        "curl-auth-user",
    }
    fixed_layout = rule_id == "console2svg-home-directory"
    if fixed_layout:
        source = "compiler"
        anchors = ["home", "users"]
        for anchor in anchors:
            anchor_home_directory_rules.setdefault(anchor, []).append(rule_index)
    elif prefix_token:
        source = "compiler"
        anchors = [prefix_token.prefix]
        anchor_prefix_rules.setdefault(prefix_token.prefix.lower(), []).append(rule_index)
    elif assignment:
        source = "compiler"
        anchors = [assignment.provider]
        anchor_assignment_rules.setdefault(assignment.provider.lower(), []).append(rule_index)
    else:
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
    if not fixed_layout and not prefix_token and not assignment:
        for anchor in anchors:
            anchor_candidate_rules.setdefault(anchor.lower(), set()).add(rule_index)
    report_rules.append({
        "index": rule_index,
        "id": rule_id,
        "engine": (
            "fixed-layout-verifier"
            if fixed_layout
            else "prefix-token-verifier"
            if prefix_token
            else "assignment-verifier"
            if assignment
            else "specialized-verifier"
            if specialized
            else "regex-fallback"
        ),
        "anchorSource": source,
        "anchors": anchors,
        "fixedOffset": 0 if fixed_layout or prefix_token or assignment else analysis.fixed_offset,
        "fallbackReason": (
            None
            if specialized or fixed_layout or prefix_token or assignment
            else analysis.fallback_reason
        ),
        "canCrossNewline": "\\n" in pattern or "\\r" in pattern or "\\s" in pattern,
        "pattern": pattern,
        "earlyPattern": early,
        "prefixToken": (
            {
                "prefix": prefix_token.prefix,
                "prefixIgnoreCase": prefix_token.prefix_ignore_case,
                "classLowMask": f"{prefix_token.class_low_mask:016X}",
                "classHighMask": f"{prefix_token.class_high_mask:016X}",
                "normalMinimum": prefix_token.normal_minimum,
                "earlyMinimum": prefix_token.early_minimum,
                "maximum": prefix_token.maximum,
                "leadingBoundary": prefix_token.leading_boundary,
                "trailingBoundary": prefix_token.trailing_boundary,
            }
            if prefix_token
            else None
        ),
        "assignment": (
            {
                "provider": assignment.provider,
                "classLowMask": f"{assignment.class_low_mask:016X}",
                "classHighMask": f"{assignment.class_high_mask:016X}",
                "normalMinimum": assignment.normal_minimum,
                "earlyMinimum": assignment.early_minimum,
                "maximum": assignment.maximum,
            }
            if assignment
            else None
        ),
    })

anchors = sorted(
    set(anchor_candidate_rules)
    | set(anchor_prefix_rules)
    | set(anchor_assignment_rules)
    | set(anchor_home_directory_rules),
    key=lambda value: (value[0], -len(value), value),
)
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
        actions = [
            f"candidates.Add({index});"
            for index in sorted(anchor_candidate_rules.get(anchor, ()))
        ]
        actions.extend(
            f"VerifyPrefixToken{index}(text, anchorStart, mode, ref sink);"
            for index in anchor_prefix_rules.get(anchor, ())
        )
        actions.extend(
            f"VerifyHomeDirectoryAt(text, anchorStart, (ushort){index}, ref sink);"
            for index in anchor_home_directory_rules.get(anchor, ())
        )
        actions.extend(
            f"VerifyAssignment{index}(text, anchorStart, mode, ref sink);"
            for index in anchor_assignment_rules.get(anchor, ())
        )
        action_text = " ".join(actions)
        checks.append(
            f"                if (tail.StartsWith({json.dumps(anchor)}, StringComparison.OrdinalIgnoreCase)) {{ {action_text} }}"
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
    if prefix_tokens[index] is None
    and assignments[index] is None
    and rule_id != "console2svg-home-directory"
)

prefix_verifiers = []
for index, prefix_token in enumerate(prefix_tokens):
    if prefix_token is None:
        continue
    maximum = prefix_token.maximum if prefix_token.maximum is not None else -1
    comparison = (
        "StringComparison.OrdinalIgnoreCase"
        if prefix_token.prefix_ignore_case
        else "StringComparison.Ordinal"
    )
    prefix_verifiers.append(
        f"""    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void VerifyPrefixToken{index}(
        ReadOnlySpan<char> text,
        int anchorStart,
        QuickLeaksScanMode mode,
        ref FindingSink sink) => VerifyPrefixToken(
            text,
            anchorStart,
            (ushort){index},
            {json.dumps(prefix_token.prefix)},
            {comparison},
            0x{prefix_token.class_low_mask:016X}UL,
            0x{prefix_token.class_high_mask:016X}UL,
            {prefix_token.normal_minimum},
            {prefix_token.early_minimum},
            {maximum},
            {str(prefix_token.leading_boundary).lower()},
            {str(prefix_token.trailing_boundary).lower()},
            mode,
            ref sink);"""
    )
prefix_verifier_methods = "\n\n".join(prefix_verifiers)

assignment_verifiers = []
for index, assignment in enumerate(assignments):
    if assignment is None:
        continue
    maximum = assignment.maximum if assignment.maximum is not None else -1
    assignment_verifiers.append(
        f"""    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void VerifyAssignment{index}(
        ReadOnlySpan<char> text,
        int anchorStart,
        QuickLeaksScanMode mode,
        ref FindingSink sink) => VerifyAssignment(
            text,
            anchorStart,
            (ushort){index},
            {json.dumps(assignment.provider)},
            0x{assignment.class_low_mask:016X}UL,
            0x{assignment.class_high_mask:016X}UL,
            {assignment.normal_minimum},
            {assignment.early_minimum},
            {maximum},
            mode,
            ref sink);"""
    )
assignment_verifier_methods = "\n\n".join(assignment_verifiers)

specialized_indices = {
    index
    for index, (rule_id, _, _, _) in enumerate(rules)
    if rule_id in {
        "console2svg-credential-uri",
        "generic-credential-uri",
        "curl-auth-header",
        "curl-auth-user",
    }
}
regex_fallback_indices = [
    index
    for index in range(len(rules))
    if prefix_tokens[index] is None
    and assignments[index] is None
    and index not in specialized_indices
    and rules[index][0] != "console2svg-home-directory"
]
non_backtracking_indices = {
    index
    for index in regex_fallback_indices
    if can_use_non_backtracking(rules[index][1])
    and can_use_non_backtracking(rules[index][2])
}

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
    private const int MatchTimeoutMilliseconds = 100;
    private const int NonBacktrackingMatchTimeoutMilliseconds = 100;
    internal const int RegexFallbackRuleCount = {len(regex_fallback_indices)};
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

    private static CandidateRules FindCandidateRules(
        ReadOnlySpan<char> text,
        QuickLeaksScanMode mode,
        ref FindingSink sink)
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
            DispatchAnchors(text, anchorStart, mode, ref candidates, ref sink);
            offset = anchorStart + 1;
        }}
        return candidates;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DispatchAnchors(
        ReadOnlySpan<char> text,
        int anchorStart,
        QuickLeaksScanMode mode,
        ref CandidateRules candidates,
        ref FindingSink sink)
    {{
        var tail = text[anchorStart..];
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
        var candidates = FindCandidateRules(text, mode, ref sink);
        while (candidates.TryTake(out var ruleIndex))
        {{
            DispatchRule(ruleIndex, text, mode, ref sink);
        }}
    }}

{prefix_verifier_methods}

{assignment_verifier_methods}

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
                sink.AddFinal(ruleIndex, 0, text.Length);
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
for chunk_start in range(0, len(regex_fallback_indices), chunk_size):
    declarations = []
    for index in regex_fallback_indices[chunk_start:chunk_start + chunk_size]:
        _, pattern, early, _ = rules[index]
        options = "RegexOptions.CultureInvariant"
        timeout = "MatchTimeoutMilliseconds"
        if index in non_backtracking_indices:
            options += " | RegexOptions.NonBacktracking"
            timeout = "NonBacktrackingMatchTimeoutMilliseconds"
        declarations.append(
            f"    [GeneratedRegex({json.dumps(pattern)}, {options}, {timeout})]\n"
            f"    private static partial Regex Rule{index}();\n"
            f"    [GeneratedRegex({json.dumps(early)}, {options}, {timeout})]\n"
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
    "prefixTokenVerifierRuleCount": sum(
        1 for item in report_rules if item["engine"] == "prefix-token-verifier"
    ),
    "assignmentVerifierRuleCount": sum(
        1 for item in report_rules if item["engine"] == "assignment-verifier"
    ),
    "fixedLayoutVerifierRuleCount": sum(
        1 for item in report_rules if item["engine"] == "fixed-layout-verifier"
    ),
    "regexFallbackRuleCount": len(regex_fallback_indices),
    "nonBacktrackingFallbackRuleCount": len(non_backtracking_indices),
    "backtrackingFallbackRuleCount": len(regex_fallback_indices) - len(non_backtracking_indices),
    "rules": report_rules,
}
REPORT.write_text(json.dumps(report, indent=2) + "\n")

print(
    f"generated {len(rules)} rules, {len(anchors)} anchors, "
    f"{report['compilerAnchorRuleCount']} compiler-anchored rules"
)
update_readme_metadata(args.config, len(rules))
