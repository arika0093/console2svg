---
title: License
description: Licenses for console2svg and its related libraries.
---

## console2svg

console2svg itself is provided under the `Apache 2.0` license.

```
Copyright 2026 arika0093

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0
```

## List of libraries used

console2svg currently uses the following libraries, mainly[^1].

| Package | License | Description |
| --- | --- | --- |
| [Porta.Pty](https://github.com/tomlm/Porta.Pty) | [MIT](https://github.com/tomlm/Porta.Pty/blob/main/LICENSE) | Cross-platform PTY control. |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | Command-line argument parsing. |
| [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | Support for [shell completions](../utilities/shell-completion.md). |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/) | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | Fast hash calculation (XxHash3) implementation. |
| [VYaml](https://github.com/hadashiA/VYaml) | [MIT](https://github.com/hadashiA/VYaml/blob/master/LICENSE) | Fast YAML serializer. |
| [ZLogger](https://github.com/Cysharp/ZLogger) | [MIT](https://github.com/Cysharp/ZLogger/blob/master/LICENSE) | Fast logging implementation. |

The tool as a whole also uses the following external dependencies.

| Package | License | Description |
| --- | --- | --- |
| [ffmpeg](https://ffmpeg.org/) | [LGPLv2.1+ / GPLv2+](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md) [^2] | External command for video generation. |
| [resvg](https://github.com/linebender/resvg) | [Apache-2.0](https://github.com/linebender/resvg/blob/main/LICENSE-APACHE) / [MIT](https://github.com/linebender/resvg/blob/main/LICENSE-MIT) | Fast implementation for converting SVG to PNG. |
| [betterleaks](https://github.com/betterleaks/betterleaks) | [MIT](https://github.com/betterleaks/betterleaks/blob/main/LICENSE) | Generates [QuickLeaks](./conversion-process/quickleaks.md) based on its [definition file](https://github.com/betterleaks/betterleaks/blob/main/config/betterleaks.toml). |

[^1]: Excluding development dependencies such as analyzers and tests.
[^2]: The Windows version bundles the [LGPL build](https://github.com/arika0093/console2svg/blob/433a29542a175dc7e806629d01ef42f4cc8509c6/scripts/release/build-native-archives.sh#L33-L34); other platforms do not bundle it.
