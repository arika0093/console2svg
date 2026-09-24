---
title: Logging
description: Display and save verbose logs and embed debug information in SVG files.
---

This page explains logging for investigation and troubleshooting, and embedding information in SVG metadata.

## Logging options

* **`--verbose`**: Write detailed execution logs to `console2svg_yyyyMMddHHmmss.log` in the current directory.
* **`--verbose <path>`** / **`--verbose-log <path>`**: Write detailed logs to the specified file, replacing its existing contents.

Verbose logs do not dump inherited environment variable values.

```bash title="Terminal" "--verbose-log debug.log"
console2svg capture --verbose-log debug.log -o output.svg -- fastfetch
```

## Embedding debug information in SVG

console2svg can embed diagnostic data in the metadata area of generated SVG documents.

* **`--embed-logs`**: Embed runtime logs in the SVG.
* **`--embed-cast`**: Embed the terminal event stream (asciicast v2).
* **`--embed-replay`**: Embed keyboard input events.
* **`--embed-debug`**: Embed all of the above (logs, cast, and replay) at once.

```bash title="Terminal" "--embed-debug"
console2svg capture --embed-debug -o debug.svg -- my-app
```

The generated SVG looks unchanged, but sharing a single SVG file in a bug report lets developers inspect the runtime logs and input/output details.
