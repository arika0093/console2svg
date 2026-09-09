---
title: Features
description: The main features that make console2svg useful for terminal screenshots and recordings.
---

console2svg turns terminal output into presentation-ready images and animations without sacrificing the sharpness of terminal text. It is useful whenever the terminal is part of the story: documenting a command, preserving a reproducible build result, or explaining an interactive workflow.

## Crisp, scalable output

SVG output keeps terminal text sharp at any zoom level. It is useful for documentation, blog posts, issue reports, and social media images.

## Static and animated captures

Capture a single terminal state, record a command as an animated SVG, or convert the result to formats such as PNG, GIF, WebM, and MP4. A timeout and a final-frame delay make long-running commands practical to capture.

## Multiple capture workflows

Use the workflow that matches your command:

- Pipe existing output into `console2svg capture`.
- Run a command through a PTY.
- Capture a normal interactive shell.
- Replay recorded input.
- Capture or stream a tmux pane.

## Presentation-ready appearance

Customize terminal themes, colors, fonts, window chrome, backgrounds, padding, margins, prompts, and headers.

## Automation-friendly

Timeouts, replay files, frame output, deterministic timing, and GitHub Actions support make captures reproducible in CI. Explicit terminal dimensions are especially useful when the same image is generated on several runners.

## Safe sharing

Use [masking](/basic-usage/masking-sensitive-output/) to replace sensitive strings before they are rendered into the output.

![A colorful terminal capture rendered by console2svg](/assets/cmd-btop.svg)

<!-- TODO: Add a compact comparison image showing static, animated, and interactive capture side by side. -->
