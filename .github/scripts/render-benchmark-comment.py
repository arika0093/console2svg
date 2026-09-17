#!/usr/bin/env python3
"""Render a PR benchmark comparison comment from two BenchmarkDotNet artifact dirs.

Scans <base> and <head> recursively for ``*-report.csv`` files (as produced by
``dotnet run --project benchmark/ConsoleToSvg.Benchmarks -- --artifacts <dir>``,
which places results under ``<dir>/results/``), matches rows between the two
sides, and writes a GitHub-flavored Markdown summary.

Row matching key: ``Method`` + every parameter column found between the
``WarmupCount`` and ``Mean`` columns, so it works for any benchmark class
regardless of its parameter names (e.g. ``Fixture``, ``Size``).
Compared metrics: ``Mean`` (wall-clock) and ``Allocated`` (memory).
"""

from __future__ import annotations

import argparse
import csv
import os
import re
import sys
from pathlib import Path

COMMENT_LIMIT = 60_000

TIME_UNITS = {
    "ns": 1.0,
    "us": 1e3,
    "µs": 1e3,
    "μs": 1e3,
    "ms": 1e6,
    "s": 1e9,
}

MEM_UNITS = {
    "B": 1.0,
    "KB": 1024.0,
    "MB": 1024.0**2,
    "GB": 1024.0**3,
    "KIB": 1024.0,
    "MIB": 1024.0**2,
    "GIB": 1024.0**3,
}


def parse_time_to_ns(text: str) -> float | None:
    match = re.match(r"\s*([0-9,.]+)\s*([a-zµμ]+)\s*$", text.strip(), re.IGNORECASE)
    if not match:
        return None
    try:
        value = float(match.group(1).replace(",", ""))
    except ValueError:
        return None
    unit = match.group(2)
    # Normalize micro sign variants before lookup.
    unit = unit.replace("µ", "u").replace("μ", "u").upper()
    table = {k.replace("µ", "U").replace("μ", "U").upper(): v for k, v in TIME_UNITS.items()}
    factor = table.get(unit)
    return value * factor if factor is not None else None


def parse_mem_to_bytes(text: str) -> float | None:
    text = text.strip()
    if text in ("", "-", "NA", "N/A"):
        return None
    match = re.match(r"\s*([0-9,.]+)\s*([a-zA-Z]+)?\s*$", text)
    if not match:
        return None
    try:
        value = float(match.group(1).replace(",", ""))
    except ValueError:
        return None
    unit = (match.group(2) or "B").upper()
    factor = MEM_UNITS.get(unit)
    return value * factor if factor is not None else None


def format_delta(head: float | None, base: float | None) -> str:
    if head is None or base is None or base == 0:
        return "n/a"
    ratio = head / base
    pct = (ratio - 1.0) * 100.0
    return f"x{ratio:.3f} ({pct:+.1f}%)"


def key_columns(fieldnames: list[str]) -> list[str]:
    """Method + parameter columns (those between WarmupCount and Mean)."""
    if "WarmupCount" in fieldnames and "Mean" in fieldnames:
        start = fieldnames.index("WarmupCount") + 1
        end = fieldnames.index("Mean")
        params = [c for c in fieldnames[start:end] if c != "Job"]
        return ["Method"] + params
    return ["Method"] + [c for c in fieldnames if c not in ("Mean", "Allocated")]


def row_label(row: dict[str, str], keys: list[str]) -> str:
    method = row.get("Method", "?")
    params = [f"{k}={row.get(k, '?')}" for k in keys if k != "Method"]
    return f"{method} ({', '.join(params)})" if params else method


def load_report(path: Path) -> tuple[list[str], dict[str, dict[str, str]]]:
    with path.open(newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        fields = reader.fieldnames or []
        keys = key_columns(fields)
        rows: dict[str, dict[str, str]] = {}
        for raw in reader:
            if not raw.get("Method"):
                continue
            rows[row_label(raw, keys)] = raw
        return keys, rows


def class_name(csv_path: Path) -> str:
    name = csv_path.name
    for prefix in ("ConsoleToSvg.Benchmarks.", "ConsoleToSvg.Benchmarks_"):
        if name.startswith(prefix):
            name = name[len(prefix):]
            break
    return name.removesuffix("-report.csv")


def compare_table(
    name: str,
    base_rows: dict[str, dict[str, str]],
    head_rows: dict[str, dict[str, str]],
) -> str:
    lines = [
        f"### {name}",
        "",
        "| Benchmark | base Mean | head Mean | Δ Mean | base Allocated | head Allocated | Δ Allocated |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    # Head is the source of truth for row order (dicts preserve CSV order):
    # list head rows first (matched + newly added benchmarks), then
    # base-only rows (removed benchmarks).
    for label in list(head_rows) + [l for l in base_rows if l not in head_rows]:
        base = base_rows.get(label, {})
        head = head_rows.get(label, {})
        if label not in base_rows:
            lines.append(
                f"| `{label}` | — | `{head.get('Mean', '?')}` | new | — "
                f"| `{head.get('Allocated', '?')}` | new |"
            )
            continue
        if label not in head_rows:
            lines.append(
                f"| `{label}` | `{base.get('Mean', '?')}` | — | removed | "
                f"`{base.get('Allocated', '?')}` | — | removed |"
            )
            continue
        mean_delta = format_delta(
            parse_time_to_ns(head.get("Mean", "")),
            parse_time_to_ns(base.get("Mean", "")),
        )
        alloc_delta = format_delta(
            parse_mem_to_bytes(head.get("Allocated", "")),
            parse_mem_to_bytes(base.get("Allocated", "")),
        )
        lines.append(
            f"| `{label}` | `{base.get('Mean', '?')}` | `{head.get('Mean', '?')}` | {mean_delta} "
            f"| `{base.get('Allocated', '?')}` | `{head.get('Allocated', '?')}` | {alloc_delta} |"
        )
    return "\n".join(lines)


def find_reports(root: Path) -> dict[str, Path]:
    return {p.name: p for p in sorted(root.rglob("*-report.csv"))}


def render(base_dir: Path, head_dir: Path) -> tuple[str, bool]:
    """Returns (markdown_body, ok). ok is False when comparison was impossible."""
    base_reports = find_reports(base_dir) if base_dir.is_dir() else {}
    head_reports = find_reports(head_dir) if head_dir.is_dir() else {}

    base_sha = os.environ.get("BASE_SHA", "")[:8]
    head_sha = os.environ.get("HEAD_SHA", "")[:8]
    base_ref = os.environ.get("BASE_REF", "")
    head_ref = os.environ.get("HEAD_REF", "")
    run_url = os.environ.get("RUN_URL", "")
    base_result = os.environ.get("BASE_RESULT", "unknown")
    head_result = os.environ.get("HEAD_RESULT", "unknown")

    header = [
        "## Benchmark result: base vs head (full)",
        "",
        f"- base: `{base_ref}` @ `{base_sha}` ({base_result})",
        f"- head: `{head_ref}` @ `{head_sha}` ({head_result})",
        f"- workflow run: {run_url}" if run_url else "- workflow run: n/a",
        "",
        "Note: `ubuntu-latest` shared runners are noisy, and base/head run on "
        "different machines in parallel, so small differences are expected. "
        "Treat this as a regression signal, not a precise measurement. "
        "Full logs (`*-report-github.md`, `*-report-full.json`, disassembly) "
        "are in the workflow artifacts.",
        "",
    ]

    if not base_reports or not head_reports:
        missing = []
        if not base_reports:
            missing.append(f"base (`{base_dir}`, job result: {base_result})")
        if not head_reports:
            missing.append(f"head (`{head_dir}`, job result: {head_result})")
        body = "\n".join(
            header
            + [
                "### Result: incomplete",
                "",
                "No comparable `*-report.csv` found for: " + ", ".join(missing) + ".",
                "Check the failed job logs and the uploaded artifacts from the linked run.",
            ]
        )
        return body, False

    sections = []
    common = sorted(set(base_reports) & set(head_reports))
    for filename in common:
        _, base_rows = load_report(base_reports[filename])
        _, head_rows = load_report(head_reports[filename])
        sections.append(compare_table(class_name(base_reports[filename]), base_rows, head_rows))

    extras = []
    for filename in sorted(set(base_reports) - set(head_reports)):
        extras.append(f"- only in base: `{filename}`")
    for filename in sorted(set(head_reports) - set(base_reports)):
        extras.append(f"- only in head: `{filename}`")
    if extras:
        sections.append("### Unmatched report files\n\n" + "\n".join(extras))

    return "\n".join(header + ["\n\n".join(sections)]), True


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    body, _ = render(Path(args.base), Path(args.head))
    if len(body) > COMMENT_LIMIT:
        body = (
            body[:COMMENT_LIMIT]
            + "\n\n... (truncated: comment size limit reached. "
            + "See workflow artifacts for full reports.)"
        )
    Path(args.output).write_text(body + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
