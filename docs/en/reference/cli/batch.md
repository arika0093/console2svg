---
title: batch
description: Subcommands for automatically generating images from markers in Markdown and synchronizing documentation assets.
---

`batch` provides subcommands for scanning Markdown and MDX files in documentation sites (such as Astro Starlight, Docusaurus, VitePress) or READMEs, and automatically generating and synchronizing images from capture instruction markers in the text.
This prevents outdated screenshots in documentation and lets you automatically keep execution result images up to date within CI/CD pipelines.

## `batch markdown`

Detects `c2s::` markers written in Markdown or MDX files, executes the specified commands, and generates or updates the image link immediately following each marker.

```bash title="Terminal"
console2svg batch markdown [options]
```

### Marker Syntax

Write the execution options and command line inside a Markdown or MDX comment.

```markdown
<!-- c2s:: -w 100 -h 10 -c -d macos -- fastfetch -->
<img src="/assets/fastfetch.svg" alt="fastfetch output" />
```

For MDX, you can also use JSX comment syntax:

```mdx
{/* c2s:: -w 100 -c -- git status */}
```

When you run `batch markdown`, the target URL of the image element (`<img>` tag or `![]()` syntax) immediately following the marker is automatically updated to the generated image file.
Additionally, an asset manifest file (`assets.json`) is automatically generated in the output directory, recording hash values for commands and output files.

### Options

* `-i, --input <path>`: Specifies the target Markdown file or the root path of the documentation directory (default: `docs`).
* `-o, --output <dir>`: Specifies the destination directory where generated image files are saved (default: `assets`).
* `--link-base <path>`: Specifies the public URL prefix for image links inserted into Markdown files (e.g., specifying `--link-base /assets` formats links in Markdown as `/assets/filename.svg`).
* `--filter <glob>`: Filters target files by glob pattern relative to the input directory (can be specified multiple times).
* `--dry-run`: Displays the list of tasks scheduled to run without generating or modifying any files.
* `--placeholder`: Creates missing empty asset files and inserts links without executing commands (leaves existing assets unchanged).
* `--verbose [path]`: Enables detailed logging and optionally saves it to a file.

## `batch restore`

Downloads and restores corresponding image assets to the local environment in bulk by referencing a manifest file (`assets.json`) generated in a remote environment or another branch.

```bash title="Terminal"
console2svg batch restore <source> --output <dir> [options]
```

This is ideal for quickly synchronizing images generated and published by `batch markdown` in CI into local development or deployment build environments.

### Arguments and Options

* `<source>` (required): Source of the manifest file (`assets.json`). Accepts a local file path, an HTTP/HTTPS URL, or a Git repository URL.
* `-o, --output <dir>` (required): Specifies the directory where image files are restored and placed.
* `--filter <glob>`: Filters files to restore by glob pattern against paths in the manifest.
* `--force`: Forcibly re-fetches entries instead of skipping them even when the local content hash (SHA) matches the remote one.
* `--prune`: Deletes extraneous image files present only in the local output directory that are not listed in the manifest.
* `--dry-run`: Displays the scheduled changes without performing actual downloads or deletions.
