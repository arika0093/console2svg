---
title: batch
description: Generate, publish, and restore Markdown capture assets.
---

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder]
console2svg batch restore <source> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

Executes `c2s::` markers in Markdown or MDX and generates or updates the image link immediately after each marker.

Generated content is stored once under `.generated` using a stable recipe hash. Human-readable paths referenced by Markdown are materialized as relative symbolic links, or copies where symbolic links are unavailable.

After every successful real generation, `batch markdown` atomically writes `<output>/assets.json`. The manifest records integrity metadata for physical objects separately from the logical paths used by documentation. Filtered generation updates entries owned by selected Markdown files while preserving entries owned by unselected files. Dry runs and placeholder runs do not modify the manifest.

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

### `--verbose [path]`

Enable detailed logs and optionally save them to a file.

## `batch restore` options

`batch restore` reads an asset set from a local `assets.json`, a local directory containing it, an HTTP(S) manifest URL, or a Git repository source such as `owner/repository@main/path/to/assets`. Relative object paths are resolved against the selected source. Content is verified by size and SHA-256 before atomically replacing local objects, after which logical paths are recreated. The verified manifest is written to the output directory.

### `<source>`

Specify a local manifest/directory, HTTP(S) manifest URL, or Git repository source. Required.

### `-o, --output <dir>`

Specify the output directory. Required.

### `--filter <glob>`

Restore only matching logical asset paths and the objects they require. Can be specified multiple times.

### `--force`

Download selected entries even when their local size and SHA-256 already match.

### `--prune`

Remove stale paths that were managed by the previous local `assets.json`. Unrelated files and entries excluded by `--filter` remain untouched.

### `--dry-run`

Report downloads and removals without changing files.

For detailed marker syntax, see [Sync documentation and images](../../automation/document-image-sync.md).
