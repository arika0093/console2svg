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

## Annotated `status --json` example

Output below is from `console2svg status --json` on a non-sensitive local container. Paths and versions will differ on your machine; only the shape is stable.

```json
{
  "application": { "version": "0.1.0", "executablePath": "/usr/local/bin/console2svg", "nativeAot": false, "installChannel": "local" },
  "platform": { "operatingSystem": "Ubuntu 24.04", "architecture": "X64", "framework": ".NET 10.0" },
  "renderers": {
    "resvg": { "available": true, "version": "bundled", "path": "(bundled)" },
    "rsvgConvert": { "available": true, "version": "2.58.0", "path": "/usr/bin/rsvg-convert" },
    "ffmpeg": { "available": true, "version": "ffmpeg version 7.0", "path": "/usr/bin/ffmpeg", "source": "PATH" }
  },
  "optionalFeatures": {
    "git": { "available": true, "version": "git version 2.43.0" },
    "tmux": { "available": true, "version": "tmux 3.4" }
  },
  "themes": { "builtIn": 21, "installed": 0, "directory": "~/.local/share/console2svg/themes" },
  "terminal": {
    "ansiColor": { "available": true, "reason": null },
    "environment": { "TERM": "xterm-256color", "COLORTERM": "truecolor", "CI": "true" }
  },
  "outputFormats": [
    { "name": "svg", "available": true, "extensions": ".svg", "converter": "native" },
    { "name": "png", "available": true, "extensions": ".png", "converter": "resvg" },
    { "name": "gif", "available": true, "extensions": ".gif", "converter": "ffmpeg", "codec": "gif" },
    { "name": "mp4", "available": true, "extensions": ".mp4", "converter": "ffmpeg", "codec": "libx264" },
    { "name": "webm", "available": true, "extensions": ".webm", "converter": "ffmpeg", "codec": "vp9" }
  ]
}
```

- `renderers`: which SVG-to-image/video backend was found (`resvg` bundled, `rsvg-convert`, `ffmpeg`).
- `optionalFeatures`: `git`/`tmux` availability for recording helpers.
- `themes`: bundled vs. user-installed theme counts and the user theme directory.
- `terminal`: ANSI color detection and the environment variables that influenced it.
- `outputFormats`: per-extension availability, converter, and codec used for `-o` conversion.
