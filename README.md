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
my-command --color=always | console2svg --out output.svg
```

### PTY command mode

```sh
console2svg --command "git log --oneline" --out output.svg
# or without -c (positional argument)
console2svg "git log --oneline" --out output.svg
```

### Animated SVG

```sh
console2svg --command "cargo build" --mode video --out output.svg
```

### Static SVG with crop

```sh
console2svg --command "dotnet --info" --mode image --out output.svg --crop-top 1ch --crop-right 5px
```

### Custom font

```sh
console2svg --command "git log" --font "Consolas, monospace" --out output.svg
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

## Development

```bash
dotnet pack -o publish
dotnet tool install -g  --add-source ./publish ConsoleToSvg --prerelease
export DOTNET_EnableWriteXorExecute=0
```