# ConsoleToSvg.QuickLeak

`ConsoleToSvg.QuickLeak` is a dependency-free .NET secret detector generated from Betterleaks rules.
It is maintained inside console2svg.

<!-- QUICKLEAK-METADATA:START -->
- Betterleaks commit: `2a387a5bad4290a84b9a1eb679bffe70611218cc`
- Downloaded config SHA-256: `a8f553eb634ac3c5c1ca3f0dc95e2b8abdea7d14b622604c5af07f30ce7926ef`
- Generated rules: `464`
<!-- QUICKLEAK-METADATA:END -->

## Use as a library

Scan a string and receive rule identifiers plus UTF-16 offsets:

```csharp
using ConsoleToSvg.QuickLeak;

var findings = QuickLeak.Scan(text);
foreach (var finding in findings)
{
    Console.WriteLine($"{finding.RuleId}: {finding.Start}..{finding.End}");
}
```

`QuickLeak.Scan` materializes results and sorts them by location. Use
`QuickLeak.Enumerate` when the caller can process generated-rule order without
allocating a result array:

```csharp
foreach (var finding in QuickLeak.Enumerate(text))
{
    HandleFinding(finding);
}
```

The optional `QuickLeakScanMode.Early` mode uses relaxed generated quantifiers to detect partially entered token values.

```csharp
var lists = QuickLeak.Enumerate(text, QuickLeakScanMode.Early);
```

This mode detects secrets while they are being entered by relaxing quantifiers such as
`any_password=([0-9A-Za-z]{64})` to `{0,64}`. Because this also increases the false-positive
rate, use it with care.

## Generated rules

The generated detector uses a keyword index before evaluating source-generated regular expressions.
Betterleaks' rule IDs, regexes, and keywords are retained.
Betterleaks expression filters, validators, and network calls are intentionally not included.

ConsoleToSvg adds these local rules:

- `console2svg-home-directory` for Unix and Windows home-directory prefixes.
- `console2svg-credential-uri` for `schema://user:pass@` credentials.
  - with an Early-mode variant that also detects the value before `@` is entered.

## Refreshing the generated source

`fetch_betterleaks.py` downloads the pinned Betterleaks configuration.
`generate.py` consumes that download and the checked-in `betterleaks.toml.patch` TOML fragment.
Both scripts update the metadata block in this README with the Betterleaks commit, downloaded configuration SHA-256, and generated rule count.

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
