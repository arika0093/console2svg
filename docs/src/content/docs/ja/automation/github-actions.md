---
title: GitHub Actions
description: Use console2svg in a GitHub Actions workflow.
---

The repository provides an action that installs console2svg and is a convenient choice when the image should track this project. Pin the action to a release tag for a stable build, or use `main` when you intentionally want the latest development behavior.

```yaml
- uses: actions/checkout@v4

- name: Setup console2svg
  uses: arika0093/console2svg@main

- name: Capture command output
  run: |
    console2svg capture -w 120 -h 30 -c -d macos-pc -o output.svg -- dotnet --info

- name: Upload capture
  uses: actions/upload-artifact@v4
  with:
    name: console-capture
    path: output.svg
```

The repository uses this action to regenerate its own examples. See the [CI/CD guide](/automation/ci-cd-support/) for reproducibility and secret-handling recommendations.

## Where to find the capture

1. Open the workflow run in the GitHub Actions tab.
2. Scroll to the **Artifacts** section at the bottom of the run summary.
3. Download `console-capture` and open `output.svg` in a browser.

The artifact contains the rendered SVG only. Verbose logs and replay files are not uploaded by the snippet above; add them as separate private artifacts only when debugging.
