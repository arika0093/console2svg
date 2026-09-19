---
title: batch
description: Generate images in bulk from c2s markers in Markdown.
---

## `batch markdown`

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder]
```

Executes `c2s::` markers in Markdown or MDX, and generates or updates the image link immediately after each marker.

On successful generation, the manifest file `<output>/assets.json` is updated automatically. Filtered runs preserve entries owned by unselected Markdown files. Dry runs and placeholder runs do not modify the manifest.

### `-i, --input <path>`

Specify a Markdown file or directory (default: `docs`).

### `-o, --output <dir>`

Specify the destination for generated images (default: `assets`).

### `--filter <glob>`

Filter by glob against file paths relative to the input directory.
`*` matches across directory separators. Can be specified multiple times.

### `--dry-run`

Show only the jobs that would run, without changing files.

### `--placeholder`

Create missing empty assets and Markdown links without executing commands. Existing assets are left unchanged.

### `--verbose [path]`

Enable detailed logs, and save them to a file if needed.

## `batch restore`

```bash title="Terminal"
console2svg batch restore <source> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

Specify a generated manifest file to download the related resources and place them locally.
When `batch markdown` runs at documentation publish time, specifying that manifest lets you copy the generated results into your local environment.

### `<source>`

Where to fetch `assets.json` from: a local manifest file or the directory containing it, an HTTP(S) URL serving the raw manifest, or a Git source such as `owner/repository@main/path/to/assets`.

### `-o, --output <dir>`

Specify the directory where restored images are placed. Required.

### `--filter <glob>`

Restore only matching logical asset paths and the objects they require. Can be specified multiple times.

### `--force`

Re-download entries even when the local size and SHA-256 already match, instead of skipping them.

### `--prune`

Delete stale paths that were managed by the previous local `assets.json` but no longer exist in the restored set. Unrelated files and entries excluded by `--filter` are left untouched.

### `--dry-run`

Show what would be done without changing files.

For detailed marker syntax, see [Sync documentation and images](../../automation/document-image-sync.mdx).
