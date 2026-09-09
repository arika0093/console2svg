---
title: Masking sensitive output
description: Replace passwords, tokens, and other sensitive strings before rendering.
---

Use `--mask` to replace sensitive strings with asterisks before terminal output is rendered. Pass one or more literal values; each matched value is replaced by a run of asterisks of the same length, including in a command header.

```bash
console2svg capture --mask password token-12345 -- my-command
```

Masking applies to terminal output and command headers, including animated captures. Each pattern is replaced with a run of asterisks of the same length.

## CI example

Keep secrets out of generated artifacts:

```bash
console2svg capture --mask "$API_TOKEN" -o output.svg -- ./show-environment.sh
```

## Important limitations

> [!CAUTION]
> Masking changes only rendered output. It does not automatically remove a secret from the original command, replay file, cast file, verbose log, shell history, or CI log. Treat those inputs and artifacts as sensitive too.

<!-- TODO: Add a generated before-and-after masking example that contains only synthetic credentials. -->
