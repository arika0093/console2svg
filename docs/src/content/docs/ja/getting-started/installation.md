---
title: Installation
description: Install console2svg on Windows, macOS, and Linux.
---

console2svg is a single native executable distributed through package managers, release archives, and npm. No extra runtime or library is required for ordinary SVG capture. Choose the installation method that fits the machine where you will run the command.

## Windows

Install from Windows Package Manager:

```powershell
winget install arika0093.console2svg
```

You can also use the Windows release archive or the npm package.

## macOS and Linux

Use the installer script:

```bash
curl -sSL https://raw.githubusercontent.com/arika0093/console2svg/main/install.sh | bash
```

Release archives and Linux packages are available from the [latest GitHub release](https://github.com/arika0093/console2svg/releases/latest).

For Debian or RPM-based Linux systems, the release page also provides `.deb` and `.rpm` packages.

## npm

```bash
npm install --global console2svg
```

## Check the installation

```bash
console2svg --version
console2svg --help
```

> [!TIP]
> Use `console2svg status` when checking an installation used by CI. It reports the application version and the conversion components console2svg can find.

The [supported platforms and dependencies](https://github.com/arika0093/console2svg#supported-platforms) vary by workflow. tmux capture requires tmux; converting to some raster or video formats requires FFmpeg or resvg. The Windows release archive includes FFmpeg.

<!-- TODO: Add a platform-specific installation flow diagram once package availability is finalized. -->
