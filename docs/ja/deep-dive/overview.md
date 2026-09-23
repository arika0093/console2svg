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

メンテナンス性の観点から、できるだけ少ない[パッケージ](./license.md)のみを使用することを目標にします。
