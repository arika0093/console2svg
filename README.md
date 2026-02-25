# ConsoleToSvg

`console2svg` is a .NET global tool that converts terminal output into SVG.

## Install

```sh
dotnet tool install -g ConsoleToSvg
```

## Usage

### Pipe mode

```sh
my-command --color=always | console2svg --out output.svg
```

### PTY command mode

```sh
console2svg --command "git log --oneline" --out output.svg
```

### Animated SVG

```sh
console2svg --command "cargo build" --mode video --out output.svg
```

### Static SVG with crop

```sh
console2svg --command "dotnet --info" --mode image --out output.svg --crop-top 1ch --crop-right 5px
```

## Key options

- `--mode image|video`
- `--command <cmd>`
- `--in <cast-path>`
- `--out <svg-path>`
- `--width <columns>` / `--height <rows>`
- `--frame <index>`
- `--crop-top|--crop-right|--crop-bottom|--crop-left <px|ch>`
- `--save-cast <path>`

## Development

```bash
dotnet pack -o publish
dotnet tool install -g  --add-source ./publish ConsoleToSvg --prerelease
export DOTNET_EnableWriteXorExecute=0
```