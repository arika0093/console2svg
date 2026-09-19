---
title: batch
description: Generate, publish, and restore Markdown capture assets.
---

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder] [--manifest <path>]
console2svg batch restore --input <path-or-url> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

Executes `c2s::` markers in Markdown or MDX and generates or updates the image link immediately after each marker.

Generated content is stored once under `.generated` using a stable recipe hash. Human-readable paths referenced by Markdown are materialized as relative symbolic links, or copies where symbolic links are unavailable.

## `batch markdown` options

### `-i, --input <path>`

Specify a Markdown file or directory (default: `docs`).

### `-o, --output <dir>`

Specify the output destination for generated images (default: `assets`).

### `--filter <glob>`

Filter by glob against file paths relative to the input directory. `*` matches across directory separators. Can be specified multiple times.

### `--dry-run`

Show only the jobs that would run without changing files.

### `--placeholder`

Create missing empty assets and update Markdown links without executing setup, capture, or teardown commands. Existing assets are never replaced. This option does not write a manifest.

### `--manifest <path>`

After successful generation, write a versioned JSON manifest containing content SHA-256 hashes, sizes, media types, canonical object URLs, and logical aliases.

### `--verbose [path]`

Enable detailed logs and optionally save them to a file.

## `batch restore` options

`batch restore` reads a manifest from a local file or HTTP(S) URL. Relative asset URLs are resolved against the manifest location. Downloads are verified by size and SHA-256 before atomically replacing local canonical objects, after which logical aliases are recreated.

### `-i, --input <path-or-url>`

Specify the local or remote manifest. Required.

### `-o, --output <dir>`

Specify the output directory. Required.

### `--filter <glob>`

Restore only entries whose canonical path or alias matches a glob. Can be specified multiple times.

### `--force`

Download selected entries even when their local size and SHA-256 already match.

### `--prune`

Remove files under the output directory that are not declared by the manifest. Files declared by filtered-out entries are preserved.

### `--dry-run`

Report downloads and removals without changing files.

For detailed marker syntax, see [Sync documentation and images](../../automation/document-image-sync.md).
