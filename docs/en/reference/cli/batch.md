---
title: batch markdown
description: Command that batch-generates images from Markdown c2s markers.
---

```bash
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run]
```

Executes `c2s::` markers in Markdown or MDX and generates or updates the image link immediately after each marker.

## Options

### `-i, --input <path>`

Specify a Markdown file or directory (default: `docs`).

### `-o, --output <dir>`

Specify the output destination for generated images (default: `assets`).

### `--filter <glob>`

Filter by glob against file paths relative to the input directory. `*` matches across directory separators. Can be specified multiple times.

### `--dry-run`

Show only the jobs that would run without changing files.

### `--verbose [path]`

Enable detailed logs and optionally save them to a file.

For detailed marker syntax, see [Sync documentation and images](../../automation/document-image-sync.md).
