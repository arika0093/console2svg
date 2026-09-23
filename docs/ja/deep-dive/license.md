---
title: ライセンス
description: console2svgおよび関連ライブラリのライセンス一覧。
---

## console2svg

console2svg自体は `Apache 2.0`ライセンスで提供されています。

```
Copyright 2026 arika0093

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0
```

## 使用ライブラリ一覧

現在、主に[^1]以下のライブラリを使用しています。

| パッケージ名 | ライセンス | 説明 |
| --- | --- | --- |
| [Porta.Pty](https://github.com/tomlm/Porta.Pty) | [MIT](https://github.com/tomlm/Porta.Pty/blob/main/LICENSE) | クロスプラットフォームのPTY制御。 |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | コマンドライン引数のパース。 |
| [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | [シェル補完](../utilities/shell-completion.md)のサポート。 |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/) | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | 高速なハッシュ計算(XxHash3)の実装。 |
| [VYaml](https://github.com/hadashiA/VYaml) | [MIT](https://github.com/hadashiA/VYaml/blob/master/LICENSE) | 高速なYAMLシリアライザー。 |
| [ZLogger](https://github.com/Cysharp/ZLogger) | [MIT](https://github.com/Cysharp/ZLogger/blob/master/LICENSE) | 高速なログ出力の実装。 |

また、ツール全体としては以下の外部依存を使用しています。

| パッケージ名 | ライセンス | 説明 |
| --- | --- | --- |
| [ffmpeg](https://ffmpeg.org/) | [LGPLv2.1+ / GPLv2+](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md) [^2] | 動画生成のための外部コマンド。 |
| [resvg](https://github.com/linebender/resvg) | [Apache-2.0](https://github.com/linebender/resvg/blob/main/LICENSE-APACHE) / [MIT](https://github.com/linebender/resvg/blob/main/LICENSE-MIT) | SVGをPNGに変換するための高速実装。 |
| [betterleaks](https://github.com/betterleaks/betterleaks) | [MIT](https://github.com/betterleaks/betterleaks/blob/main/LICENSE) | [定義ファイル](https://github.com/betterleaks/betterleaks/blob/main/config/betterleaks.toml)をベースに[QuickLeaks](./conversion-process/quickleaks.md)を生成。 |

[^1]: 開発時の依存関係(アナライザー/テスト関係など)を除く。
[^2]: Windows版では[LGPL版ビルド](https://github.com/arika0093/console2svg/blob/433a29542a175dc7e806629d01ef42f4cc8509c6/scripts/release/build-native-archives.sh#L33-L34)を同梱しており、それ以外では同梱していない。

