---
title: 設計思想
description: console2svg の設計思想、
---

console2svg は、ターミナルの出力を正確かつ高品質なベクター画像（SVG）や動画へ変換するために設計されたCLIツールです。

## 設計思想

以下の設計思想に基づき、console2svg は開発されています。

### どこでも使える

私はWindowsユーザーです。(まあ、大体の人はそうでしょう)  
`brew`インストールしか書いてないツールには、正直うんざりしています。  

そのため、Linux/macOS/Windows の主要なプラットフォームで動作することを目指しています。


### 箱から出してすぐ使える

この手の変換ツールは、色々なものを高度に組み合わせて使う必要があり、それが大層面倒だったことが開発動機の一つです。  
console2svg は、インストール後すぐに使えることを目指しています。

* Linux/macOS環境では、`ffmpeg`以外の依存関係は全てバイナリに組み込まれています。
* Windows環境では、リリースアーカイブ内に`ffmpeg`も含め全てが同梱されています。
  * 同梱バイナリには [btbN/FFmpeg-Builds](https://github.com/btbN/FFmpeg-Builds) を使用しています。
  * これは、Windows環境では`ffmpeg`を別途インストール/パス設定するのが面倒であるためです。

### 手間をかけさせない

console2svg は、ユーザーに余計な手間をかけさせないことを重視しています。  
例えば[対話的キャプチャ](../basic-usage/interactive-capture.md)や、[tmux support](../utilities/tmux.md)などの機能です。

> [!TIP]
> アイデアがあれば、ぜひ[Issue](https://github.com/arika0093/console2svg/issues)を立ててください！


### 高品質な出力

console2svg は、ターミナル出力を可能な限り忠実に再現することを目指しています。  

> [!NOTE]
> とはいえ、完全な再現にはまだまだ課題があります。  
> 表示に問題がある場合はぜひ[Issue](https://github.com/arika0093/console2svg/issues)へ報告してください。

### パッケージ依存を抑える

現在、console2svgは以下の依存のみを使用しています。[^1]

| パッケージ名 | ライセンス | 説明 |
| --- | --- | --- |
| [Porta.Pty](https://github.com/tomlm/Porta.Pty) | [MIT](https://github.com/tomlm/Porta.Pty/blob/main/LICENSE) | クロスプラットフォームのPTY制御。 |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | コマンドライン引数のパース。 |
| [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) | [シェル補完](../utilities/shell-completion.md)のサポート。 |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/) | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | 高速なハッシュ計算(XxHash3)の実装。<br />ターミナル出力のハッシュ値を計算し、再利用するために使用。 |
| [VYaml](https://github.com/hadashiA/VYaml) | [MIT](https://github.com/hadashiA/VYaml/blob/master/LICENSE) | 高速なYAMLシリアライザー。 |
| [ZLogger](https://github.com/Cysharp/ZLogger) | [MIT](https://github.com/Cysharp/ZLogger/blob/master/LICENSE) | 高速なログ出力の実装。 |

また、ツール全体としては以下の外部依存を使用しています。

| パッケージ名 | ライセンス | 説明 |
| --- | --- | --- |
| [ffmpeg](https://ffmpeg.org/) | [LGPLv2.1+ / GPLv2+](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md) [^2] | 動画生成のための外部コマンド。 |
| [resvg](https://github.com/linebender/resvg) | [Apache-2.0](https://github.com/linebender/resvg/blob/main/LICENSE-APACHE) / [MIT](https://github.com/linebender/resvg/blob/main/LICENSE-MIT) | SVGをPNGに変換するための高速実装。 |
| [betterleaks](https://github.com/betterleaks/betterleaks) | [MIT](https://github.com/betterleaks/betterleaks/blob/main/LICENSE) | [定義ファイル](https://github.com/betterleaks/betterleaks/blob/main/config/betterleaks.toml)をベースに[QuickLeaks](./conversion-process/quickleaks.md)を生成しています。 |


[^1]: 開発時の依存関係(アナライザー/テスト関係など)を除く。
[^2]: Windows版では[LGPL版ビルド](https://github.com/arika0093/console2svg/blob/433a29542a175dc7e806629d01ef42f4cc8509c6/scripts/release/build-native-archives.sh#L33-L34)を同梱しており、それ以外では同梱していない。
