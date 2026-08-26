#!/usr/bin/env bash
#
# Runs the ConsoleToSvg benchmark suite against a released source tree (a git tag),
# producing results you can diff against a run of the current source (HEAD).
#
# Usage:
#   benchmark/run-baseline.sh [tag] [-- <BenchmarkDotNet args>]
#
# Examples:
#   # Compare the v0.8.0-rc3 release against the current source:
#   benchmark/run-baseline.sh v0.8.0-rc3 -- --artifacts benchmark/artifacts/v0.8.0-rc3
#   dotnet run -c Release --project benchmark/ConsoleToSvg.Benchmarks \
#       -- --artifacts benchmark/artifacts/HEAD
#
# The tag is checked out into a git worktree outside the repository so both the
# released and current Core source trees are compiled managed in-process, which is what
# makes disassembly/memory/CPU-instruction diagnostics available on both sides. Keeping
# it outside the repository also prevents BenchmarkDotNet from finding duplicate
# benchmark project names while generating its build harness.
set -euo pipefail

TAG="${1:-v0.8.0-rc3}"
shift || true
if [[ "${1:-}" == "--" ]]; then
    shift
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BENCH_PROJECT="$REPO_ROOT/benchmark/ConsoleToSvg.Benchmarks/ConsoleToSvg.Benchmarks.csproj"
WORKTREE_ROOT="${CONSOLE2SVG_BENCHMARK_WORKTREE_ROOT:-$(dirname "$REPO_ROOT")/.console2svg-benchmark-worktrees/$(basename "$REPO_ROOT")}"
WORKTREE="$WORKTREE_ROOT/$TAG"
CORE_PROJECT="$WORKTREE/src/ConsoleToSvg.Core/ConsoleToSvg.Core.csproj"

if [[ ! -f "$CORE_PROJECT" ]]; then
    echo "Creating worktree for $TAG at $WORKTREE ..." >&2
    mkdir -p "$WORKTREE_ROOT"
    git -C "$REPO_ROOT" worktree add "$WORKTREE" "$TAG"
else
    echo "Using existing worktree at $WORKTREE" >&2
fi

echo "Benchmarking ConsoleToSvg.Core @ $TAG ($CORE_PROJECT)" >&2
exec dotnet run -c Release --project "$BENCH_PROJECT" \
    -p:ConsoleToSvgCoreProject="$CORE_PROJECT" \
    -p:ConsoleToSvgBaseline=true \
    -- "$@"
