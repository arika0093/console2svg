---
title: Configuration files
description: Persist terminal dimensions, themes, and rendering options in YAML format for use across multiple commands.
since: v0.11
---

`console2svg` allows persisting terminal dimensions, color themes, and rendering options as a **configuration document** (ConfigDocument).
Centralizing frequently used arguments into configuration files keeps command-line invocations concise.

Configuration documents contain only passive settings.
They never execute arbitrary external commands or operational steps.

## File Format and Schema

Configuration documents are written in YAML (or JSON).
A JSON Schema is provided for editor auto-completion and validation.

```yaml title="console2svg.config.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ConfigDocument.v1.json
$version: 1

options:
  terminal:
    width: 120
    height: 30
  appearance:
    theme:
      - dracula
    window: macos
    font:
      family: "JetBrains Mono"
      size: 14
```

## Discovery and Precedence

Configuration documents are loaded across three tiers, with deeper tiers overriding shallower ones.
Command-line arguments take the highest precedence and override all configuration documents.

1. **Global configuration**: User-wide settings shared across environments
   * Linux: `~/.config/console2svg/config.yaml`
   * macOS: `~/Library/Application Support/console2svg/config.yaml`
   * Windows: `%APPDATA%\console2svg\config.yaml`
2. **Local configuration**: Project-specific settings in the working directory
   * `./console2svg.config.yaml` in the current working directory
3. **Explicit configuration**: Any path specified via `-C` or `--config` on the CLI
   * `console2svg capture -C custom-config.yaml -- btop`

Omitted properties inherit from lower-precedence sources.
Overriding array properties (such as themes or background colors) replaces the entire array rather than appending items.

## Configurable Options

Settings are organized by category under the `options` key.
The most commonly configured options include:

### terminal

Specifies pseudo-terminal (PTY) dimensions in cells.

```yaml
options:
  terminal:
    width: 120
    height: 30
```

### appearance

Configures theme, window decoration, and font for output images.

```yaml
options:
  appearance:
    theme:
      - github-dark
    window: macos-pc
    background:
      - "my-background.png"
```

## Complete Configuration Example

Below is a complete configuration document example with comments, based on `console2svg.example.yaml` and `todo/171-scenario-config-file.md`:

```yaml title="console2svg.config.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ConfigDocument.v1.json
$version: 1

# Shared options: these apply to compatible console2svg workflows.
options:
  # PTY dimensions in cells (distinct from appearance.size in pixels).
  terminal:
    width: 160 # -w
    height: 32 # -h

  # Policies for console2svg-managed environment variables.
  environment:
    color: overwrite   # overwrite | preserve. (--no-colorenv)
    ci: strip          # strip | preserve. (--no-delete-envs)

  # Interactive terminal settings.
  interactive:
    mouse: true        # --mouse

  # Live-server options.
  live-server:
    host: ":8080"      # Empty host means loopback (localhost:8080)

  # Capture behavior.
  capture:
    mode: video        # image | video (-v)
    crop:
      top: 1ch
      bottom: "Hello!:-2"
      left: 1ch
      right: 10px
    video:
      fps: 12
      loop: true
      time:
        start: 0
        end: 10
      timing: deterministic # deterministic | realtime
      coalesce: auto        # auto or non-negative milliseconds
      fadeout: 0.5          # seconds
    # For image mode, specify at most one of frame and time:
    # image:
    #   frame: 3
    #   time: 2.5

  # Rendering of recorded terminal contents.
  render:
    masking:
      auto: true
      strings:
        - password
        - secret
    adjust: spacing    # spacing | spacingAndGlyphs

  # SVG conversion backend.
  converter:
    svg-converter: auto # auto | ffmpeg | rsvg | rsvg-convert | resvg

  # Rendered appearance. Arrays replace lower-priority arrays rather than append.
  appearance:
    font:
      family: "Fira Code, Fira Mono, monospace"
      size: 14
    # Image dimensions in pixels (normally specify either width or height).
    size:
      width: 800
      # height: 400
    theme:
      - nord
    window: macos-pc
    padding: 10
    margin: 8
    pc-padding: 12
    background:
      # Background colors or images.
      - "#000"
      - "#048"
    opacity: 0.85
    forecolor: "#fff"
    backcolor: "#000"
    header:
      with-command: true
      prompt: "$ "
```
