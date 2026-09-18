---
title: tmux integration
description: Capture the active tmux pane or scrollback history directly.
---

Use the `tmux` subcommand to extract the screen or history directly from a running tmux session and convert it to SVG.

## Basic usage

Open another window or pane inside a tmux session, or run the command from an outside shell.

```bash title="Terminal"
console2svg tmux capture -o tmux-current.svg
```

If `--target` is not specified, a menu appears for selecting the pane to capture.

Style options can be specified in the same way as in [capture](../basic-usage/capturing-images/overview.mdx) mode.

```bash title="Terminal" "-d macos-pc" "-t github-dark"
console2svg tmux capture -d macos-pc -t github-dark -o tmux-current.svg
```

## Selecting a pane (`--target`)

Specify the target pane identifier with `--target`.

```bash title="Terminal" "--target"
# Select pane 1 in window 0
console2svg tmux capture -o pane1.svg --target ":0.1"
```

## Retrieving history (`--history`)

To capture previous output as well, specify the number of lines to retrieve with `--history`.

```bash title="Terminal" "--history 100"
# Generate a capture including the previous 100 lines
console2svg tmux capture -o long-log.svg --history 100 
```

With no argument, all available history is included.

```bash title="Terminal" "--history"
console2svg tmux capture -o full-log.svg --history 
```

## `tmux live-server`

You can also use the [live-server](./live-server.md) feature for a tmux pane. Its arguments are the same as `live-server`.

```bash title="Terminal"
console2svg tmux live-server
```
