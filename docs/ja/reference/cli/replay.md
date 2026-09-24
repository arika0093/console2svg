---
title: replay
description: 保存されたキーボード入力を自動再生し、同一の操作結果をキャプチャするコマンド。
---

```bash title="Terminal"
console2svg replay <replay.json> [options] -- command [args...]
```

`replay` は、`capture --replay-save <path>` などで事前に記録されたキーボード入力ファイル（`<replay.json>`）を読み込み、指定されたコマンドに対して同一のタイミングでキー入力を自動送信しながら画面を記録するサブコマンドです。
人間が手動で行った対話的コマンド操作（エディタの操作、CLI メニューの選択など）を完全に再現し、最新の出力画面やアニメーション SVG を自動生成する用途に適しています。

## コマンドの動作

`replay` を実行すると、指定したコマンドが PTY（擬似端末）経由で立ち上がり、リプレイファイル内に記録された各キー操作（Enter、矢印キー、Ctrl キー、文字列入力など）が予定時刻に合わせて送信されます。
すべてのキー入力が完了し、指定された待機時間またはコマンド終了に達した段階で、最終画面または動画の生成処理が行われます。

## オプション

`replay` では、キー入力ファイルに加えて `capture` と共通の出力設定、外観テーマ、動画オプションを指定できます。

### 出力とフォーマット

* `-o, --out <path>`: 出力ファイルパスを指定します（拡張子に応じてフォーマットを自動判定）。
* `--format <format>`: 出力形式を明示的に指定します（`svg`、`png`、`gif`、`mp4` など）。
* `-v, --video`: アニメーション SVG または動画形式で出力します。
* `--stdout`: 生成結果を標準出力へ書き出します。

### 再生とタイミング制御

* `--timeout <sec>`: 再生実行の全体タイムアウト時間を秒数で指定します。
* `--sleep <sec>`: 全キー入力の完了後、録画を終了するまでの追加待機秒数を指定します。
* `--fadeout <sec>`: 動画末尾のフェードアウト効果の時間を指定します。
* `--fps <number>`: 動画サンプリングの最大フレームレートを指定します。
* `--timing <deterministic|realtime>`: 動画の再生タイミング制御方式を選択します。

### 端末サイズと画面トリミング

* `-w, --width <int|adjust>`, `-h, --height <int|adjust>`: 端末の横幅（文字数）と高さ（行数）を指定します。
* `--size <WxH>`: 出力画像のピクセル寸法を指定します。
* `--crop-top`, `--crop-bottom`, `--crop-left`, `--crop-right`: 画面の上下左右をピクセルまたは文字単位で切り取ります。

### 外観とテーマ

* `-d, --window [style]`: ウィンドウ装飾スタイル（`macos`、`macos-pc`、`windows` など）を指定します。
* `-t, --theme <id>`: 外観テーマの ID を指定します。
* `--forecolor <color>`, `--backcolor <color>`: 前景色または背景色を上書きします。
* `--background <value>`: ウィンドウ背面の背景色または画像を指定します。
* `--opacity <number>`: 端末背景の不透明度（`0.0`〜`1.0`）を指定します。
* `--font <family>`, `--fontsize <px>`: フォントファミリーとフォントサイズを指定します。
* `-c, --with-command`: 画面上部に実行コマンドラインを表示します。

### 録画と診断情報の埋め込み

* `--save-cast <path>`: 再生時の端末出力を asciicast v2 ファイルとして保存します。
* `--embed-cast`: 元の asciicast データを生成される SVG 内部へメタデータとして埋め込みます。
* `--embed-replay`: 使用したキーボードリプレイデータを SVG 内部へ埋め込みます。
* `--embed-logs`, `--embed-debug`: 診断ログやデバッグ情報を SVG 内部へ埋め込みます。
* `--mask <pattern>`, `--mask-auto [bool]`: 文字列マスクおよび自動シークレット保護を設定します。
* `--svg-converter <converter>`: ラスタライズエンジン（`auto`、`resvg`、`ffmpeg`、`rsvg-convert`）を指定します。
* `--verbose [path]`: 詳細ログの出力先を指定します。
