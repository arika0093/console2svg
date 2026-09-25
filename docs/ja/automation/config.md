---
title: 設定ファイル
description: 端末サイズや外観、描画設定を YAML 形式で永続化し、複数の実行で共有する設定ファイル機能。
since: v0.11
---

`console2svg` では、端末サイズ、カラーテーマ、描画オプションなどの設定を **設定ファイル**（ConfigDocument）として永続化できます。
頻繁に指定する引数を設定ファイルへ集約することで、コマンドラインの指定を簡潔に保てます。

設定ファイルは受動的な設定値のみを保持します。外部コマンドの実行や操作手順の記述は含みません。

## ファイル形式とスキーマ

設定ファイルは YAML 形式（または JSON 形式）で記述します。
エディタでの入力補完やバリデーションのために、JSON Schema を提供しています。

```yaml title="console2svg.config.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ConfigDocument.v1.json
$version: 1

options:
  terminal:
    width: 120
    height: 30
  appearance:
    theme:
      - dracula
    window: macos
    font:
      family: "JetBrains Mono"
      size: 14
```

## 設定ファイルの配置場所と優先順位

設定ファイルは 3 つの階層から読み込まれ、深い階層の値が浅い階層の値を上書きします。
コマンドライン引数は、すべての設定ファイルよりも高い優先度で適用されます。

1. **グローバル設定**：利用者のホームディレクトリ配下に配置する全環境共通の設定
   * Linux: `~/.config/console2svg/config.yaml`
   * macOS: `~/Library/Application Support/console2svg/config.yaml`
   * Windows: `%APPDATA%\console2svg\config.yaml`
2. **ローカル設定**：作業ディレクトリ配下に配置するプロジェクト固有の設定
   * カレントディレクトリの `console2svg.config.yaml`
3. **明示的な指定**：CLI 実行時に `-C` または `--config` で指定した任意のパス
   * `console2svg capture -C custom-config.yaml -- btop`

各階層の設定ファイルで省略された項目は、下位の設定をそのまま引き継ぎます。
配列型のプロパティ（テーマ一覧や背景色一覧など）を上書きすると、要素の追加ではなく配列全体の置換として処理されます。

## 設定可能な項目

設定ファイル内の `options` キー配下に、各カテゴリの設定を記述します。
よく使われる設定項目は以下のとおりです。

### terminal

擬似端末（PTY）のセル寸法を指定します。

```yaml
options:
  terminal:
    width: 120
    height: 30
```

### appearance

出力される画像の外観テーマ、ウィンドウ枠、フォントなどを指定します。

```yaml
options:
  appearance:
    theme:
      - github-dark
    window: macos-pc
    background:
      - "my-background.png"
```

## 設定ファイルの完全な例

```yaml title="console2svg.config.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ConfigDocument.v1.json
$version: 1

# 共有設定: console2svg の各ワークフローへ適用される設定群
options:
  # 端末設定: PTY 起動時に適用するセル寸法（appearance.size のピクセル寸法とは別）
  terminal:
    width: 160 # コマンドライン引数の -w に相当
    height: 32 # コマンドライン引数の -h に相当

  # キャプチャ動作設定: 静止画または動画の記録方針
  capture:
    mode: video      # image | video（-v に相当）
    # 画面の切り抜き設定（文字単位 ch やピクセル px で指定可能）
    crop:
      top: 1ch
      bottom: "Hello!:-2"
      left: 1ch
      right: 10px
    # 動画記録時の設定（mode: video 時に有効）
    video:
      fps: 12
      loop: true
      time:
        start: 0
        end: 10
      timing: deterministic # deterministic | realtime
      coalesce: auto        # auto またはミリ秒数値
      fadeout: 0.5          # フェードアウト秒数
    # 静止画記録時の設定（mode: image 時に有効。frame と time はどちらか一方のみ）
    # image:
    #   frame: 3
    #   time: 2.5

  # 描画設定: 記録済み端末出力の表示調整
  render:
    masking:
      auto: true     # シークレット情報の自動マスキング
      strings:       # マスキング対象文字列（配列全体で下位設定を置換）
        - password
        - secret
    adjust: spacing  # spacing | spacingAndGlyphs

  # 変換エンジン設定: SVG からラスタ画像や動画への変換
  converter:
    svg-converter: auto # auto | ffmpeg | rsvg | rsvg-convert | resvg

  # 外観設定: テーマやフォント
  appearance:
    font:
      family: "Fira Code, Fira Mono, monospace"
      size: 14
    # 画像のピクセル寸法（通常は width または height の一方を指定）
    size:
      width: 800
      # height: 400
    theme:
      # 配列で指定。下位層の設定を配列ごと置換
      - nord
    window: macos-pc # ウィンドウ装飾スタイル
    padding: 10      # 内側余白
    margin: 8        # 外側余白
    pc-padding: 12   # PC 装飾時の追加余白
    background:
      # 背景色または背景画像
      - "#000"
      - "#048"
    opacity: 0.85       # 背景不透明度（0.0 〜 1.0）
    forecolor: "#fff" # 前景色の上書き
    backcolor: "#000" # 背景色の上書き
    header:
      with-command: true   # launch のコマンド名を上部ヘッダーに表示
      prompt: "$ "         # プロンプト文字

  # 環境変数制御: console2svg が管理する環境変数のポリシー
  environment:
    color: overwrite # overwrite | preserve（--no-colorenv に相当）
    ci: strip        # strip | preserve（--no-delete-envs に相当）

  # 対話端末設定
  interactive:
    mouse: true      # マウス入力の有効化（--mouse に相当）

  # live-server 設定
  live-server:
    host: ":8080"    # 空のホストはループバック（localhost:8080）にバインド
```
