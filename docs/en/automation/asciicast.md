---
title: Record/play asciicast files
description: Saving and playing asciinema asciicast v2 format and embedding metadata in SVG files.
---

console2svg can interoperate with the **asciicast v2** format commonly used by the terminal recording tool asciinema.

## Recording asciicast

Specify the `--save-cast` option to save execution output in asciicast format (`.cast`).

```bash
console2svg capture --save-cast session.cast -- cargo build
```

The saved `.cast` file can also be played with normal tools such as `asciinema play`.

### Embedding data in SVG (`--embed-cast`)

Specify the `--embed-cast` option to embed asciicast data in the metadata area inside the generated SVG file.

```bash
console2svg capture --embed-cast -o output.svg -- fastfetch
```

## Playing and converting asciicast

Use the `cast` subcommand to render SVGs or videos from existing `.cast` files.

```bash
# Output as a static SVG
console2svg cast session.cast -o session.svg

# Output as a video (GIF)
console2svg cast session.cast -v -o session.gif -t monokai
```

Or specify `capture --in`.

```bash
console2svg capture --in session.cast -d macos-pc -o output.svg
```
