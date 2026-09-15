---
title: CI/CD support
description: Generate repeatable terminal captures in continuous integration.
---

console2svg is designed to run in non-interactive automation. Use explicit dimensions, timeouts, output paths, and timing options when the result needs to be reproducible. This prevents a runner's terminal size or a command that waits indefinitely from changing the generated artifact.

```bash
console2svg capture -w 120 -h 30 --timeout 10 --mask "$SECRET" -o artifacts/console.svg -- ./generate-output.sh
```

## Recommendations

<Steps>
  <Step title="Install a known version">Pin console2svg in the workflow or use a release tag for the repository action.</Step>
  <Step title="Make the capture deterministic">Set dimensions, a timeout, output path, and—when required—an FPS or replay file.</Step>
  <Step title="Publish only reviewed artifacts">Mask secrets, keep verbose logs private, and upload SVGs or converted files as build artifacts.</Step>
</Steps>

> [!NOTE]
> console2svg enables terminal color environment variables by default and removes CI markers that commonly disable color output. Use `--no-colorenv` or `--no-delete-envs` when that behavior is unsuitable for your runner.

<!-- TODO: Add a workflow diagram showing capture generation, artifact upload, and review. -->
