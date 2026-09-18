---
title: replay
description: 保存済みのキーボード入力を再生するコマンド。
---

```bash title="Terminal"
console2svg replay <replay.json> [options] -- command [args...]
```

`--replay-save`で保存した入力ファイルを再生し、同じ操作を繰り返し実行します。

## オプション

`replay`では、`capture`で利用できる出力、端末表示、録画、マスキング、診断のオプションに加えて、入力ファイルを指定します。

### `-o, --out <path>`

### `--stdout`

出力ファイルを指定するか、SVGを標準出力へ書き出します。

### `-m, --mode <image|video>`

### `-v, --video`

出力モードを指定するか、アニメーションSVGを出力します。

### `-w, --width <int|adjust>`

### `-h, --height <int|adjust>`

端末の幅と高さを指定します。`adjust`では入力に合わせて調整します。

### `--timeout <sec>`

### `--sleep <sec>`

録画の制限時間と録画終了後の待機時間を指定します。

### `--save-cast <path>`

再生結果をasciicast v2ファイルとして保存します。

### `--embed-cast`

### `--embed-logs`

### `--embed-replay`

### `--embed-debug`

asciicast、診断ログ、キーボード入力、またはすべての診断情報をSVGへ埋め込みます。

### `--replay <path>`

### `--replay-save <path>`

追加のキーボード入力を再生するファイル、または入力を保存するファイルを指定します。

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

### `-c, --with-command`

### `--header <text>`

### `--prompt <text>`

コマンド表示の有無、見出し、プロンプトを指定します。

### `-d, --window [style]`

### `--pc-padding <number>`

### `--opacity <number>`

ウィンドウ枠、デスクトップ風ウィンドウの余白、不透明度（`0`から`1`）を指定します。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

テーマ、文字色、端末の背景色を指定または上書きします。

### `--margin <number>`

### `--padding <number>`

### `--background <value>`

ウィンドウ枠やシェル内部の余白、背景の色または画像を指定します。

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

フォント、サイズ、SVGテキストの長さ調整方法を指定します。

### `--mask <pattern>`

### `--mask-auto [bool]`

文字列のマスクと自動シークレットマスキングを設定します。

### `--frame <index>`

### `--time <sec>`

静止画として出力するフレーム番号または時刻（`START-END`も可）を指定します。

### `--size <WxH>`

出力サイズを`WIDTH`、`WIDTHx*`、`*xHEIGHT`、`WIDTHxHEIGHT`で指定します。

### `--save-frames <dir>`

アニメーションの各フレームを指定ディレクトリに保存します。

### `--crop-top <value>`

### `--crop-right <value>`

### `--crop-bottom <value>`

### `--crop-left <value>`

画面の上下左右を単位またはテキスト位置で切り取ります。

### `--no-loop`

### `--fps <number>`

### `--timing <deterministic|realtime>`

ループの無効化、最大サンプリングレート、動画のタイミング制御方式を指定します。

### `--fadeout <sec>`

### `--coalesce-ms <ms|auto>`

フェードアウト時間と出力イベントをまとめる間隔を指定します。

### `--mouse`

### `--no-colorenv`

### `--no-delete-envs`

マウス追跡の転送、色設定用環境変数の上書き、CI用環境変数の削除を設定します。

### `--svg-converter <converter>`

SVGのラスタライズ方法を指定します。
