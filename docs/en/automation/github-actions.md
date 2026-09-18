---
title: Use with GitHub Actions
description: Example workflows for automatically generating and committing CLI screenshots in CI pipelines.
---

By combining console2svg with GitHub Actions, you can automatically update README images when repositories are updated or released.

## GitHub Actions

A convenient GitHub Action is also available for CI usage. To use the latest version of `console2svg`, just add the following step to your workflow:

```yaml title=".github/workflows/console2svg.yml"
- uses: arika0093/console2svg@main
```

Specify the console2svg version as follows.

```diff lang="yaml" title=".github/workflows/console2svg.yml"
 - uses: arika0093/console2svg@main
+  with:
+    version: 0.8.3
     # Build from source instead
     # version: develop
```

> [!NOTE]
> The images for this documentation site are automatically generated with the Actions above.


## Workflow examples
### One-off generation

The following is an example workflow that installs console2svg on an Ubuntu runner, generates an SVG, and commits it to the Git repository.

```yaml title=".github/workflows/console2svg.yml" {4-5,10-14}
jobs:
  gen:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup console2svg
        uses: arika0093/console2svg@main

      - name: Generate SVG
        run: console2svg capture -w 120 -c -d macos-pc -o output.svg -- dotnet --version

      - name: Commit Changes
        uses: stefanzweifel/git-auto-commit-action@v7
        with:
          commit_message: "[skip-ci] chore: regenerate SVG"
```

### Automatic sync

You can also automatically update documentation by using the [batch markdown](./document-image-sync.md) feature.
The following is an example workflow that detects markers in Markdown, generates images, and commits them.

```yaml title=".github/workflows/console2svg.yml" {4-5,10-14}
jobs:
  sync:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup console2svg
        uses: arika0093/console2svg@main

      - name: Generate SVGs from Markdown
        run: console2svg batch markdown -i ./docs -o ./assets

      - name: Commit Changes
        uses: stefanzweifel/git-auto-commit-action@v7
        with:
          commit_message: "[skip-ci] chore: regenerate SVGs"
```

> [!WARNING]
> Malicious code embedded in Markdown may be executed, so pay close attention to when this runs.
> In particular, if you run it automatically for external PRs or when merging branches, ensure that only trusted code is executed.

## Automatic environment variable overrides

Some libraries (for example, [chalk](https://www.npmjs.com/package/chalk)) detect CI environments and automatically disable color output.
However, when generating SVG, you almost certainly want colors to always be enabled. Therefore, console2svg automatically sets and deletes the following environment variables by default:

* `TERM=xterm-256color`: Set to enable color support.
* `COLORTERM=truecolor`: Enables TrueColor output.
* `FORCE_COLOR=3`: Same as above. `3` indicates TrueColor.
* `CI` (deleted): Removed intentionally because libraries disable colors when they detect a CI environment.
* `TF_BUILD` (deleted): Removed for the same reason (used by Azure Pipelines).

Use the `--no-colorenv` and `--no-delete-envs` options to disable this behavior.
