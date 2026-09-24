---
title: live-server
description: Command to stream current terminal screens as SVG images in real time over HTTP.
---

```bash title="Terminal"
console2svg live-server [options] [host:port]
```

`live-server` is a subcommand that launches a shell or command in a pseudo-terminal, converts its screen output to SVG in real time, and streams it live over HTTP for web browsers.
Simply opening the streaming endpoint (`http://localhost:38473/`) in a browser lets you preview terminal activity with negligible latency.
It is ideal for importing as a browser source in streaming software like OBS Studio, enabling crisp, high-definition terminal sharing during YouTube live streams and conference presentations.

## Connection and Address Binding

When you omit the `[host:port]` argument, the server listens on `127.0.0.1:38473` by default.
To allow connections from other machines on the local network, specify an explicit host and port such as `0.0.0.0:38473` or `localhost:3000`.

```bash title="Terminal"
# Stream locally on the default port (38473)
console2svg live-server

# Stream locally on port 3000
console2svg live-server 127.0.0.1:3000

# Stream allowing external connections
console2svg live-server 0.0.0.0:38473
```

## Options

### Server and Streaming Control

* `--fps <number>`: Specifies maximum sampling rate for screen updates (useful for tuning CPU load and network bandwidth).
* `--no-resize`: Keeps initial startup TTY size fixed without adapting to browser-side resizing.
* `--mouse [bool]`: Forwards mouse events from browser or interactive interface to PTY (default: `true`).
* `--save-cast <path>`: Simultaneously saves all streamed output as an asciicast v2 recording file.

### Appearance and Themes

* `-d, --window [style]`: Specifies window decoration style (`macos`, `macos-pc`, `windows`, etc.).
* `-t, --theme <id>`: Specifies appearance theme ID.
* `--forecolor <color>`, `--backcolor <color>`: Overrides foreground or background color.
* `--background <value>`: Specifies window background color or image.
* `--opacity <number>`: Specifies terminal background opacity (`0.0`–`1.0`).
* `--font <family>`, `--fontsize <px>`: Specifies font family and font size.
* `-c, --with-command`: Displays the executed command line at the top of the screen.
* `--header <text>`, `--prompt <text>`: Overrides command line header text or prompt symbol.
* `--margin <number>`, `--padding <number>`, `--pc-padding <number>`: Fine-tunes margins outside the window, padding inside the shell, and desktop frame spacing.

### Masking and Environment Controls

* `--mask <pattern>`: Masks the specified string pattern.
* `--mask-auto [bool]`: Enables or disables automatic secret detection and masking via QuickLeaks (default: `true`).
* `--no-colorenv`: Disables overriding color-related environment variables.
* `--no-delete-envs`: Preserves CI-related environment variables without automatically stripping them.
* `--adjust <mode>`: Specifies SVG text length adjustment method (`spacing` or `spacingAndGlyphs`).
* `--verbose [path]`: Specifies destination for verbose log output.
