---
title: capture
description: ターミナル出力をSVGとして記録するコマンド。
---

```bash
console2svg capture [options] -- command [args...]
```

指定したコマンドをPTYで実行し、終了時の画面をSVGとして保存します。`-v`を付けるとアニメーションSVGや動画形式にもできます。

## オプション

### `-o, --out <path>`

出力ファイルを指定します。拡張子に応じてSVG、PNG、GIFなどの形式を選択できます。

### `--stdout`

SVGを標準出力へ書き出します。

### `-m, --mode <image|video>`

出力モードを`image`または`video`から選びます。

### `-v, --video`

アニメーションSVGを出力します。`--mode video`の短縮形です。

### `-w, --width <int|adjust>`

端末の横幅を文字数で指定します。`adjust`では入力に合わせて調整します。

### `-h, --height <int|adjust>`

端末の高さを行数で指定します。`adjust`では入力に合わせて調整します。

### `--timeout <sec>`

録画を指定秒数で終了します。

### `--in <path>`

コマンドを実行せず、入力したasciicast v2ファイルを描画します。

### `--save-cast <path>`

録画した出力をasciicast v2ファイルとして保存します。

### `--embed-cast`

入力または録画元のasciicastデータをSVGに埋め込みます。

### `--embed-logs`

診断ログをSVGに埋め込みます。

### `--embed-replay`

録画したキーボード入力をSVGに埋め込みます。

### `--embed-debug`

すべての診断情報をSVGに埋め込みます。

### `--replay-save <path>`

コマンド実行中のキーボード入力を後で再生できるファイルに保存します。

### `--replay <path>`

保存済みのキーボード入力を再生します。

### `--verbose [path]`

詳細ログを有効にします。値を指定するとログをファイルにも保存します。

### `-c, --with-command`

出力の先頭に実行したコマンドを表示します。

### `--header <text>`

コマンド行の見出しを上書きします。

### `--prompt <text>`

プロンプトの接頭辞を指定します。

### `-d, --window [style]`

ウィンドウ枠のスタイルを指定します。値を省略すると`macos`を使用します。

### `--pc-padding <number>`

デスクトップ風ウィンドウの余白を上書きします。

### `--opacity <number>`

背景の不透明度を`0`から`1`で指定します。

### `-t, --theme <id>`

外観テーマのIDを指定します。複数回指定できます。

### `--forecolor <color>`

端末の文字色を上書きします。

### `--backcolor <color>`

端末の背景色を上書きします。

### `--margin <number>`

ウィンドウ枠の余白を指定します。

### `--padding <number>`

シェル内部の余白を指定します。

### `--background <value>`

デスクトップ背景の色または画像を指定します。2回指定するとグラデーションになります。

### `--font <family>`

CSSフォントファミリーを指定します。

### `--fontsize <px>`

フォントサイズをピクセル単位で指定します。

### `--mask <pattern>`

出力中の指定した文字列をマスクします。複数指定できます。

### `--mask-auto [bool]`

Betterleaksによるシークレットの自動マスキングを有効または無効にします。既定値は有効です。

### `--frame <index>`

動画または複数フレームの入力から、指定したフレーム番号を静止画として出力します。

### `--time <sec>`

指定時刻、または`START-END`形式の時刻範囲を静止画として出力します。

### `--size <WxH>`

出力サイズを指定します。`WIDTH`、`WIDTHx*`、`*xHEIGHT`、`WIDTHxHEIGHT`を指定できます。

### `--save-frames <dir>`

アニメーションの各フレームを指定ディレクトリに保存します。

### `--crop-top <value>`

上端を`px`、`ch`、またはテキスト位置で切り取ります。

### `--crop-right <value>`

右端を`px`または`ch`で切り取ります。

### `--crop-bottom <value>`

下端を`px`、`ch`、またはテキスト位置で切り取ります。

### `--crop-left <value>`

左端を`px`または`ch`で切り取ります。

### `--no-loop`

アニメーションSVGをループさせません。

### `--fps <number>`

フレームの最大サンプリングレートを指定します。

### `--timing <deterministic|realtime>`

動画のタイミング制御方式を指定します。

### `--sleep <sec>`

録画終了後に待機する秒数を指定します。

### `--fadeout <sec>`

動画のフェードアウト時間を指定します。

### `--coalesce-ms <ms|auto>`

近接する出力イベントをまとめる間隔をミリ秒で指定します。`auto`で自動設定します。

### `--no-resize`

端末サイズを変更せず、初期TTYサイズを維持します。

### `--mouse`

対話モードでマウス追跡をPTYへ転送します。

### `--no-colorenv`

PTYの色設定用環境変数を上書きしません。

### `--no-delete-envs`

CI用環境変数を削除しません。

### `--adjust <mode>`

SVGテキストの長さ調整方法を`spacing`または`spacingAndGlyphs`から選びます。

### `--svg-converter <converter>`

SVGをラスタライズする方法を`auto`、`ffmpeg`、`rsvg-convert`、`resvg`から選びます。

```bash
console2svg capture -w 100 -h 24 -c -- fastfetch
```
