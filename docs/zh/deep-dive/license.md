---
title: 许可证
description: console2svg 及相关库的许可证列表。
---

## console2svg

console2svg 本身以 `Apache 2.0` 许可证提供。

```
Copyright 2026 arika0093

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0
```

## 使用的库列表

目前主要[^1]使用以下库：

| 包名 | 许可证 | 说明 |
| --- | --- | --- |
| [Porta.Pty](https://github.com/tomlm/Porta.Pty) | [MIT](https://github.com/tomlm/Porta.Pty/blob/main/LICENSE) | 跨平台 PTY 控制。 |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | 命令行参数解析。 |
| [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | 支持[Shell 补全](../utilities/shell-completion.md)。 |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/) | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | 高速哈希计算（XxHash3）的实现。 |
| [VYaml](https://github.com/hadashiA/VYaml) | [MIT](https://github.com/hadashiA/VYaml/blob/master/LICENSE) | 高速 YAML 序列化器。 |
| [ZLogger](https://github.com/Cysharp/ZLogger) | [MIT](https://github.com/Cysharp/ZLogger/blob/master/LICENSE) | 高速日志输出实现。 |

此外，整个工具还使用以下外部依赖：

| 包名 | 许可证 | 说明 |
| --- | --- | --- |
| [ffmpeg](https://ffmpeg.org/) | [LGPLv2.1+ / GPLv2+](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md) [^2] | 用于视频生成的外部命令。 |
| [resvg](https://github.com/linebender/resvg) | [Apache-2.0](https://github.com/linebender/resvg/blob/main/LICENSE-APACHE) / [MIT](https://github.com/linebender/resvg/blob/main/LICENSE-MIT) | 将 SVG 转换为 PNG 的高速实现。 |
| [betterleaks](https://github.com/betterleaks/betterleaks) | [MIT](https://github.com/betterleaks/betterleaks/blob/main/LICENSE) | 基于其[定义文件](https://github.com/betterleaks/betterleaks/blob/main/config/betterleaks.toml)生成 [QuickLeaks](./conversion-process/quickleaks.md)。 |

[^1]: 不包括开发时依赖（分析器/测试相关等）。
[^2]: Windows 版本捆绑了 [LGPL 构建](https://github.com/arika0093/console2svg/blob/433a29542a175dc7e806629d01ef42f4cc8509c6/scripts/release/build-native-archives.sh#L33-L34)，其他平台不捆绑。
