---
title: live-server
description: ターミナルの現在画面を SVG 画像としてリアルタイム HTTP 配信するコマンド。
---

```bash title="Terminal"
console2svg live-server [options] [host:port]
```

`live-server` は、擬似端末上でシェルやコマンドを起動し、その画面出力をリアルタイムに SVG へ変換して HTTP 経由でブラウザ向けにライブ配信するサブコマンドです。
ブラウザから配信エンドポイント（`http://localhost:38473/`）を開くだけで、端末作業の様子を遅延なくプレビュー表示できます。
OBS Studio などの配信ツールでブラウザソースとして取り込み、YouTube ライブやカンファレンス配信でターミナル画面を高精細に共有する用途などに適しています。

## 接続先とアドレス指定

引数 `[host:port]` を省略した場合、サーバーは既定で `127.0.0.1:38473` でリッスンを開始します。
ローカルネットワーク上の他のマシンから接続したい場合は、`0.0.0.0:38473` や `localhost:3000` のように明示的なホストとポートを指定します。

```bash title="Terminal"
# 既定のポート（38473）でローカル配信
console2svg live-server

# ポート 3000 でローカル配信
console2svg live-server 127.0.0.1:3000

# 外部からの接続を許可して配信
console2svg live-server 0.0.0.0:38473
```

## オプション

### サーバーと配信制御

* `--fps <number>`: 画面更新の最大サンプリングレートを指定します（CPU 負荷やネットワーク帯域の調整に利用）。
* `--no-resize`: ブラウザ側のリサイズに追従せず、起動時の初期 TTY サイズを固定して維持します。
* `--mouse [bool]`: ブラウザまたは対話画面からのマウス操作イベントを PTY へ転送します（既定値: `true`）。
* `--save-cast <path>`: 配信中の全出力を asciicast v2 録画ファイルとして同時に保存します。

### 外観とテーマ

* `-d, --window [style]`: ウィンドウ装飾スタイル（`macos`、`macos-pc`、`windows` など）を指定します。
* `-t, --theme <id>`: 外観テーマの ID を指定します。
* `--forecolor <color>`, `--backcolor <color>`: 前景色または背景色を上書きします。
* `--background <value>`: ウィンドウ背面の背景色や画像を指定します。
* `--opacity <number>`: 端末背景の不透明度（`0.0`〜`1.0`）を指定します。
* `--font <family>`, `--fontsize <px>`: フォントファミリーとフォントサイズを指定します。
* `-c, --with-command`: 画面上部に実行コマンドラインを表示します。
* `--header <text>`, `--prompt <text>`: コマンド行の見出しテキストやプロンプト記号を上書きします。
* `--margin <number>`, `--padding <number>`, `--pc-padding <number>`: ウィンドウ外側、シェル内側、デスクトップ枠の余白を数値で微調整します。

### マスキングと環境制御

* `--mask <pattern>`: 指定した文字列パターンをマスクします。
* `--mask-auto [bool]`: QuickLeaks によるシークレットの自動検出・マスキングを有効または無効にします（既定値: `true`）。
* `--no-colorenv`: カラー関連の環境変数の上書きを停止します。
* `--no-delete-envs`: CI 関連の環境変数を自動除外せず保持します。
* `--adjust <mode>`: SVG テキストの長さ調整方法（`spacing` または `spacingAndGlyphs`）を指定します。
* `--verbose [path]`: 詳細ログの出力先を指定します。
