# ConsoleToSvg benchmarks

BenchmarkDotNet-based performance harness for `ConsoleToSvg.Core`. It measures SVG
generation speed and output size, and — because both sides are compiled managed
in-process — it can also produce machine-code disassembly, managed-memory, and (when
Linux `perf` is available) CPU hardware-counter diagnostics. No Python is involved.

## What is measured

| Benchmark class | Cases | Notes |
| --- | --- | --- |
| `SvgGenerationBenchmarks` | `RenderSvg`, `ParseOnly` × `Small`/`Medium`/`Large` | Deterministic synthetic sessions: full pipeline vs. terminal replay only. |
| `RealWorldBenchmarks` | `RenderStatic`, `RenderAnimated` × `nyancat`/`cmatrix`/`btop` | Pre-recorded asciicast fixtures (`benchmark/fixtures/*.cast`). |
| `AnimatedPipelineBenchmarks` | replay, snapshots, signatures, frame render, complete render | Separates the animated pipeline stages for the real-world fixtures. |

`SvgGenerationBenchmarks` also includes dedicated `ParseWideCharacters` and
`ParseScrollStress` cases. The regular synthetic workload accounts for display width,
so wide-character parsing and intentional scrolling no longer distort its baseline.

In addition to wall-clock time and allocations, two custom columns report the generated
SVG document size in bytes (`SVG size (static)` / `SVG size (animated)`).

## Diagnostics enabled

- **Memory** (`MemoryDiagnoser`) — allocated bytes and GC collections per operation.
- **Disassembly** (`DisassemblyDiagnoser`) — Intel-syntax `.asm` with source interleaved,
  recursing to `maxDepth: 2` so the hot render→emulator→buffer chain
  (`SvgRenderer.Render`, `TerminalEmulator.Process`, `ResolveDefaultTargetFrame`, …) is
  disassembled, not just the benchmark entry point. A combined
  `*-disassembly-report.html` and per-benchmark `*-asm.md` are exported.
- **Hardware counters** — `InstructionRetired`, `TotalCycles`, `BranchInstructions`,
  `CacheMisses`, enabled automatically when Linux `perf` is on `PATH` (and
  `perf_event_paranoid` allows it); silently skipped otherwise.
- **CPU sampling profiler** (`PerfCollectProfiler`) — also gated on Linux `perf`. When
  present it runs each benchmark under `perfcollect` and emits a `*.trace.zip` per
  benchmark plus a flame graph, so you can see *which functions are hot* (the disassembly
  above shows *what code* is generated, the profiler shows *where time actually goes*).
  Open the result with `perfcollect view <trace>.trace.zip` or `perf report`.

Results are exported as GitHub Markdown, HTML, JSON, and CSV into
`BenchmarkDotNet.Artifacts/` (override with `--artifacts <dir>`).

## Fixtures

`benchmark/fixtures/*.cast` are asciicast v2 files recorded by `console2svg` itself
(see the `save-cast` option). They are parsed directly by
`AsciicastFixture` so the benchmark depends on `ConsoleToSvg.Core` only.

## Running

### Current source (HEAD)

```bash
dotnet run -c Release --project benchmark/ConsoleToSvg.Benchmarks
```

### Released source (git tag) — baseline

```bash
benchmark/run-baseline.sh v0.8.0-rc3
```

This checks out the tag under
`../.console2svg-benchmark-worktrees/<repository>/` and compiles the benchmark against
that Core source tree. Keeping baseline worktrees outside the repository avoids
duplicate-project discovery failures in BenchmarkDotNet. Override the worktree root
with `CONSOLE2SVG_BENCHMARK_WORKTREE_ROOT` when needed. Both sides therefore get
identical disassembly/memory/counter diagnostics. Cases that depend on APIs absent from
the selected release, such as streaming writes, are omitted from that baseline build.

### Comparing HEAD vs a release

Run each side into its own artifacts directory, then diff the JSON/CSV:

```bash
benchmark/run-baseline.sh v0.8.0-rc3 -- --artifacts benchmark/artifacts/v0.8.0-rc3
dotnet run -c Release --project benchmark/ConsoleToSvg.Benchmarks \
    -- --artifacts benchmark/artifacts/HEAD
```

Compare the generated files:

- `*-report.csv` / `*-report-github.md` — wall-clock time, allocations, hardware
  counters, and the `SVG size (static)` / `SVG size (animated)` columns.
- `*-report-full.json` — per-measurement data plus metrics (time, memory, counters).
- `*-asm.md` / `*-disassembly-report.html` — per-method machine code, diffable to
  spot JIT-codegen changes between the two source trees.
- `*-trace.zip` (only when `perf` is present) — `perfcollect` CPU samples, usable as a
  flame graph to compare hot functions between the two source trees.

### Common BenchmarkDotNet arguments

```bash
# list benchmarks without running
dotnet run -c Release --project benchmark/ConsoleToSvg.Benchmarks -- --list flat

# run a single case (e.g. only animated btop)
dotnet run -c Release --project benchmark/ConsoleToSvg.Benchmarks \
    -- --filter "*RealWorldBenchmarks.RenderAnimated*" --job short
```

## Why tag-vs-HEAD instead of a published package

The `v0.8.0-rc3` NuGet tool package is a NativeAOT launcher and does not ship a
referenceable managed `ConsoleToSvg.Core` assembly, so an in-process "released" build
cannot be referenced. Comparing the `v0.8.0-rc3` **tag** against **HEAD** (both compiled
managed) gives the same diagnostics while still contrasting the released behavior with
current work.
