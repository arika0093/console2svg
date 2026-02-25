# ConsoleToSvg

`console2svg` is a .NET global tool that converts terminal output into SVG.

## Install

```sh
dotnet tool install -g ConsoleToSvg
```

## Usage

### Pipe mode

Width and height default to the current terminal dimensions (via `Console.WindowWidth`/`Console.WindowHeight`).

```sh
my-command | console2svg
```

### PTY command mode

```sh
console2svg "git log --oneline"
```

### Animated SVG

```sh
console2svg "dotnet build" -m video
```

### Static SVG with crop

```sh
# ch: charactor width, px: pixel
console2svg "dotnet --info" --crop-top 1ch --crop-right 5px
```

## Key options

- `-m, --mode image|video`
- `-c, --command <cmd>` (also works as a bare positional argument)
- `--in <cast-path>`
- `-o, --out <svg-path>`
- `-w, --width <columns>` / `-h, --height <rows>` (default: auto-detected from terminal in pipe mode, 80×24 for PTY)
- `--frame <index>`
- `--crop-top|--crop-right|--crop-bottom|--crop-left <px|ch|text:pattern>`
- `--theme dark|light`
- `--font <css-font-family>`
- `--save-cast <path>`
- `--help`
