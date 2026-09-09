---
title: Cropping output
description: Crop terminal output by pixels, character units, or text patterns.
---

Use the `--crop-*` options to remove unwanted space or lines from the output.

## Crop by pixels or characters

```bash
console2svg capture --crop-top 1ch --crop-left 5px --crop-right 30px -- dotnet --info
```

The four directions are `--crop-top`, `--crop-right`, `--crop-bottom`, and `--crop-left`. Values can use pixel or character units.

## Crop around text

Pass text instead of a size to crop relative to a matching line:

```bash
console2svg capture --crop-top Host --crop-bottom '.NET runtimes installed:-2' -- dotnet --info
```

The optional numeric suffix adjusts the matching line. This is useful when command output changes length between environments.

![A capture cropped around matching text](/assets/cmd-crop-word.svg)
