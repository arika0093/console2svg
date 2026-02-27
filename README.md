# C2S
[![NuGet Version](https://img.shields.io/nuget/v/ConsoleToSvg?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/ConsoleToSvg/) 

`c2s` is a tool that converts terminal output into SVG.

## Overview

The simplest way to use it is to just put the command you want to run after `c2s`. For example, the following command converts the description text of `c2s` into SVG (oh, how meta).

```bash
c2s c2s
```

![](./assets/cmd.svg)

---

You can also generate SVG with a window frame. and some options to customize the appearance.

```bash
c2s -w 120 -c -d macos-pc -- c2s
```

![](./assets/cmd-window.svg)

---

In video mode, you can capture the animation of the command execution and save it as an SVG.

```bash
c2s -v -c -d macos -- copilot --banner
```

![](./assets/cmd-loop.svg)

## Install

You can install it as a global tool using the dotnet command.

```sh
dotnet tool install -g ConsoleToSvg
```

It is also distributed as a static binary.

```sh
# linux
curl -sSL https://github.com/arika0093/c2s/releases/latest/download/c2s-linux-x64 -o c2s
chmod +x c2s

# macos
curl -sSL https://github.com/arika0093/c2s/releases/latest/download/c2s-osx-x64 -o c2s
chmod +x c2s

# windows (cmd)
curl -sSL https://github.com/arika0093/c2s/releases/latest/download/c2s-win-x64.exe -o c2s.exe
```

## Usage

### Pipe mode

Width and height default to the current terminal dimensions.

```sh
my-command | c2s
```

### PTY command mode

```sh
c2s "git log --oneline"
```

Or pass the command after `--`:

```sh
c2s -- dotnet run app.cs
```

### Animated SVG

use `-m video` or `-v` to capture the animation of the command execution and save it as an SVG.

```sh
c2s -v -- nyancat
```

No loop playback:

```sh
c2s -v --no-loop -- nyancat
```

### Static SVG with crop

```sh
# ch: character width, px: pixel
c2s "dotnet --info" --crop-top 1ch --crop-right 5px
```

You can also crop at the position where a specific character appears.
When specifying a character, you can specify it like `:(number)`, which crops at a relative position from the detected line.

For example, the following example crops from the line where the character `Host` is located to 2 lines above the line where the character `.NET runtimes installed:` is located.

```sh
c2s "dotnet --info" --crop-top "Host" --crop-bottom ".NET runtimes installed:-2"
```

### Window chrome and padding

```sh
c2s -d macos-pc --padding 4 -- dotnet --version
```

available themes:
* `none`: no window frame (default)
* `macos`: macOS style window frame (default if `-d` is specified without a value)
* `macos-pc`: macOS style window frame with background and shadow
* `windows`: Windows style window frame
* `windows-pc`: Windows style window frame with background and shadow

## Major options

* `-o`: Output SVG file path (default: `output.svg`)
* `-c`: Prepend the command line to the output as if typed in a terminal.
* `-w`: width of the output SVG (default: terminal width[pipe], 80ch[pty])
* `-h`: height of the output SVG (default: terminal height[pipe], auto[pty])
* `-v`: output to video mode SVG (animated, looped by default)
* `-d`: window chrome style (none, macos, ...)
* `--verbose`: enable verbose logging
* `--crop-*`: crop the output by specified pixels, characters, or text patterns
