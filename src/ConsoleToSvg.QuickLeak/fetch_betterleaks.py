#!/usr/bin/env python3
"""Fetch the pinned Betterleaks rule configuration for local code generation."""
import argparse
import hashlib
import re
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).parent
README = ROOT / "README.md"
SHA = "2a387a5bad4290a84b9a1eb679bffe70611218cc"
URL = f"https://raw.githubusercontent.com/betterleaks/betterleaks/{SHA}/config/betterleaks.toml"


def update_readme_metadata(config_hash: str) -> None:
    marker = re.compile(
        r"<!-- QUICKLEAK-METADATA:START -->.*?<!-- QUICKLEAK-METADATA:END -->",
        re.DOTALL,
    )
    current = README.read_text()
    existing_count = re.search(r"Generated rules: `(\d+)`", current)
    rule_count = existing_count.group(1) if existing_count else "pending"
    metadata = (
        "<!-- QUICKLEAK-METADATA:START -->\n"
        f"- Betterleaks commit: `{SHA}`\n"
        f"- Downloaded config SHA-256: `{config_hash}`\n"
        f"- Generated rules: `{rule_count}`\n"
        "<!-- QUICKLEAK-METADATA:END -->"
    )
    updated = marker.sub(metadata, current, count=1)
    if updated == current:
        raise RuntimeError("README metadata markers were not found")
    README.write_text(updated)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path, help="Path for the downloaded TOML file")
args = parser.parse_args()

with urlopen(URL) as response:
    content = response.read()
    args.output.write_bytes(content)

update_readme_metadata(hashlib.sha256(content).hexdigest())
print(f"downloaded {URL} -> {args.output}")
