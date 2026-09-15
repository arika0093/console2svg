---
title: Live server
description: Preview a terminal capture in a browser while it is running.
---

`live-server` renders the running terminal in a local browser page instead of writing a capture file. It is useful for streaming, screen sharing, or checking how a command looks before choosing final capture options.

```bash
console2svg live-server
```

The server prints a local URL such as `http://127.0.0.1:38473/`. Open it in a browser to view the current terminal state. Stop the server with `Ctrl+C` when you are done.

## Run a command in the live terminal

```bash
console2svg live-server -d macos-pc --background your-bg.png
```

Use `--port` to select a different port and `--listen` when the server needs to be reachable from another host.

> [!CAUTION]
> The default address is localhost. Do not expose a live terminal beyond a trusted network without considering who can see its output; it may contain commands, paths, or secrets.

![A live terminal server preview](/assets/cmd-liveserver.png)
