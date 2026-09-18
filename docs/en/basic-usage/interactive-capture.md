---
title: Interactive capture
description: Manually capture with function keys while running an interactive shell or TUI application.
---

When capturing editor operations such as Vim or interactive work in a REPL, interactive capture lets you take screenshots or recordings at any timing.

## Starting a session

Run the `interactive` command.

```bash title="Terminal"
# Start interactive mode with the default shell
console2svg interactive
# Launch a specific command directly
# console2svg interactive -- vim main.rs
```

Once the session starts, you can type and operate it just like a normal terminal.

![console2svg interactive session](/docs/assets/cmd-interactive.svg)

## Shortcut keys

During the session, you can capture with the following keys.

| Key | Action | 
| :---: | :--- | 
| `F9` | Video capture (start / stop) |
| `F10` | Still-image capture |
| `F12` | Pause during video recording |

## Output destination

By default, output uses the format `output_YYYYMMDD_HHMMSSsss.svg`.
You can specify any file name with the `-o` option (a timestamp is appended automatically).

```bash title="Terminal" "-o my_output.svg"
console2svg interactive -o my_output.svg
# -> my_output_20260101_123456789.svg
```

> [!TIP]
> During interactive execution, timestamps are automatically appended even to user-specified file names to prioritize avoiding duplicate file names when outputting multiple times.

Automatic conversion is also performed when you specify an extension.

```bash title="Terminal" "-o my_result.mp4"
console2svg interactive -o my_result.mp4
# -> my_result_20260101_123456789.mp4
```

> [!NOTE]
> If you specify an extension for a video format, still-image capture with the `F10` key is disabled.


## Combining style options

As with [capture](../basic-usage/capturing-images/overview.mdx) mode, you can specify options such as themes and window styles.

```bash title="Terminal" "-d macos-pc" "-t github-dark"
console2svg interactive -d macos-pc -t github-dark
```
