---
title: Status and verbose logs
description: Inspect the runtime environment and diagnose conversion problems.
---

Use `status` to inspect the installed version and the runtime components console2svg found. It is the fastest first check when a converter works locally but fails on a CI runner.

```bash
console2svg status
console2svg status --json
```

Use `--verbose` when a capture or conversion does not behave as expected. Supply a path to save the diagnostic output alongside the artifact:

```bash
console2svg capture --verbose logs/capture.log -- dotnet --info
```

Verbose diagnostic logs can also be embedded in an SVG when debugging a reproducibility problem.

> [!CAUTION]
> Avoid publishing logs that may contain command arguments, paths, environment data, or terminal input. Delete or secure temporary diagnostics after resolving the issue.

<!-- TODO: Add an annotated `console2svg status --json` example using a non-sensitive local environment. -->
