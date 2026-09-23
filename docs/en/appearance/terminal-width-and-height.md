---
title: Specify width and height
description: How to fix the terminal display area.
---

By default, the output size of `capture` is determined from the width and height used by the command or terminal.
Use `-w` / `--width` to specify width and `-h` / `--height` to specify height in character cells.

## Fixing width and height

```bash title="Terminal" "-w 50 -h 5"
console2svg capture -w 50 -h 5 -- console2svg
```

<!-- c2s::  -w 50 -h 5 -- console2svg -->
![console2svg](/assets/generated/57425e021433.svg)

This example creates a terminal area that is 50 characters wide and 5 lines tall.
If the command output exceeds the area, wrapping and scrolling occur just as they would in a terminal.

## Specifying only width or height

Width and height can be specified independently. The side you do not specify is determined from the normal terminal size or the command output.

```bash title="Terminal" "--width 50" "-h 5"
# Fix only the width to 50 characters
console2svg capture --width 50 -- console2svg

# Fix only the height to 5 lines
console2svg capture -h 5 -- console2svg
```
