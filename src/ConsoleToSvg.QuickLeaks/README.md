# ConsoleToSvg.QuickLeaks

`ConsoleToSvg.QuickLeaks` is a dependency-free .NET secret detector generated from Betterleaks rules.
It is maintained inside console2svg.

<!-- QUICKLEAKS-METADATA:START -->
- Betterleaks commit: `2a387a5bad4290a84b9a1eb679bffe70611218cc`
- Downloaded config SHA-256: `a8f553eb634ac3c5c1ca3f0dc95e2b8abdea7d14b622604c5af07f30ce7926ef`
- Generated rules: `465`
<!-- QUICKLEAKS-METADATA:END -->

## Use as a library

Scan UTF-16 text into a caller-owned buffer and receive rule identifiers plus offsets:

```csharp
using ConsoleToSvg.QuickLeaks;

var findings = new ArrayBufferWriter<QuickLeaksFinding>();
QuickLeaks.Scan(text.AsSpan(), findings);
foreach (var finding in findings.WrittenSpan)
{
    Console.WriteLine($"{finding.RuleId}: {finding.Start}..{finding.End}");
}
```

The span/writer API is the only scanning API, so callers cannot accidentally
select a materializing compatibility path. The scanner does not allocate on its
clean hot path.

The optional `QuickLeaksScanMode.Early` mode uses relaxed generated quantifiers to detect partially entered token values.

```csharp
QuickLeaks.Scan(text.AsSpan(), findings, QuickLeaksScanMode.Early);
```

This mode detects secrets while they are being entered by relaxing quantifiers such as
`any_password=([0-9A-Za-z]{64})` to `{0,64}`. Because this also increases the false-positive
rate, use it with care.

## Generated rules

The generator conservatively analyzes each regex and emits a generation report.
At runtime, `SearchValues<string>` performs one case-insensitive multi-pattern
anchor search. A generated discriminator maps each anchor occurrence to rule
indices. Prefix-token rules are verified immediately at that anchor position;
a compact bitset runs only the selected fallback rules.

For the pinned rule set, 39 prefix-token rules, 74 assignment rules, and one
fixed-layout home-directory rule are lowered to allocation-free verifiers.
Another 36 fallback rules use the non-backtracking regex engine; unsupported or
large automata retain bounded backtracking with fail-closed timeout handling.

Construction-time custom literals are available through `QuickLeaksScanner`.
They use their own reusable `SearchValues<string>` indexes and the same span/writer API.

Compiler-proven anchors and Betterleaks keywords are deliberately distinct.
During the verifier migration, upstream keywords remain as a recall-preserving
safety net. Regex fallback uses span-based `Regex.EnumerateMatches`, and a timeout
produces a conservative redaction instead of a silent false negative.
Betterleaks' rule IDs, regexes, and keywords are retained.
Betterleaks expression filters, validators, and network calls are intentionally not included.

ConsoleToSvg adds these local rules:

- `console2svg-home-directory` for Unix and Windows home-directory prefixes.
- `console2svg-credential-uri` for `schema://user:pass@` credentials.
  - masks the username and password independently, preserving the scheme, separators,
    and host; Early mode also detects the value before `@` is entered.
- `console2svg-git-identity` for `User Name <address@example.com>` identities such as
  those printed by `git log`. It masks the display name and email local part
  independently, preserving the email domain.

## Refreshing the generated source

`fetch_betterleaks.py` downloads the pinned Betterleaks configuration.
`generate.py` consumes that download and the checked-in `betterleaks.toml.patch` TOML fragment.
It emits the anchor dispatcher, prefix-token verifiers, chunked fallback regex
source files, and
`QuickLeaks.generation-report.json`. Both scripts update the metadata block in
this README with the Betterleaks commit, downloaded configuration SHA-256, and
generated rule count.

```sh
python3 fetch_betterleaks.py /tmp/betterleaks.toml
python3 generate.py /tmp/betterleaks.toml
```

Review the downloaded configuration and generated diff before committing it.

## Attribution and license

The generated Betterleaks-derived rules are from
[Betterleaks](https://github.com/betterleaks/betterleaks), pinned to the commit
shown above. Betterleaks is distributed under the MIT License:

```text
MIT License

Copyright (c) 2026 Zachary Rice

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
