---
title: interactive
description: 対話的なシェルやプログラムを記録するコマンド。
---

```bash title="Terminal"
console2svg interactive [options]
```

対話的なシェルを起動し、録画キーで画面の記録を開始・終了します。

## オプション

`interactive`では、`capture`の出力、端末表示、録画、マスキング、診断に関するオプションを利用できます。`--in`、`--frame`、`--time`、`--replay`系、埋め込み系は利用できません。

### `-o, --out <path>`

出力ファイルを指定します。

### `--stdout`

SVGを標準出力へ書き出します。

### `-m, --mode <image|video>`

出力モードを指定します。

### `-v, --video`

アニメーションSVGを出力します。

### `-w, --width <int|adjust>`

### `-h, --height <int|adjust>`

端末の幅を文字数、高さを行数で指定します。`adjust`では入力に合わせて調整します。

### `--timeout <sec>`

録画を指定秒数で終了します。

### `--save-cast <path>`

録画した出力をasciicast v2ファイルとして保存します。

### `-c, --with-command`

出力の先頭に実行したコマンドを表示します。

### `--header <text>`

### `--prompt <text>`

コマンド行の見出しまたはプロンプトを上書きします。

### `-d, --window [style]`

ウィンドウ枠のスタイルを指定します。値を省略すると`macos`を使用します。

### `--pc-padding <number>`

### `--opacity <number>`

デスクトップ風ウィンドウの余白、不透明度（`0`から`1`）を指定します。

### `-t, --theme <id>`

外観テーマのIDを指定します。複数回指定できます。

### `--forecolor <color>`

### `--backcolor <color>`

文字色または端末の背景色を上書きします。

### `--margin <number>`

### `--padding <number>`

ウィンドウ枠またはシェル内部の余白を指定します。

### `--background <value>`

背景の色または画像を指定します。2回指定するとグラデーションになります。

### `--font <family>`

### `--fontsize <px>`

フォントファミリーまたはサイズを指定します。

### `--mask <pattern>`

### `--mask-auto [bool]`

指定した文字列のマスク、自動シークレットマスキングの有効・無効を設定します。自動マスキングの既定値は有効です。

### `--save-frames <dir>`

アニメーションの各フレームを指定ディレクトリに保存します。

### `--size <WxH>`

出力サイズを`WIDTH`、`WIDTHx*`、`*xHEIGHT`、`WIDTHxHEIGHT`で指定します。

### `--crop-top <value>`

### `--crop-right <value>`

### `--crop-bottom <value>`

### `--crop-left <value>`

上下端を`px`、`ch`、テキスト位置、左右端を`px`または`ch`で切り取ります。

### `--no-loop`

アニメーションSVGをループさせません。

### `--fps <number>`

### `--timing <deterministic|realtime>`

最大サンプリングレートと動画のタイミング制御方式を指定します。

### `--sleep <sec>`

### `--fadeout <sec>`

録画終了後の待機時間または動画のフェードアウト時間を指定します。

### `--coalesce-ms <ms|auto>`

近接する出力イベントをまとめる間隔を指定します。

### `--no-resize`

初期TTYサイズを維持します。

### `--mouse`

マウス追跡をPTYへ転送します。

### `--no-colorenv`

### `--no-delete-envs`

PTYの色設定用環境変数を上書きしない、またはCI用環境変数を削除しないようにします。

### `--adjust <mode>`

SVGテキストの長さ調整方法を`spacing`または`spacingAndGlyphs`から選びます。

### `--svg-converter <converter>`

SVGのラスタライズ方法を`auto`、`ffmpeg`、`rsvg-convert`、`resvg`から選びます。

### `--verbose [path]`

詳細ログを有効にします。値を指定するとログをファイルにも保存します。
