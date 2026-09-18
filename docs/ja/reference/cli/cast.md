---
title: cast
description: asciicast v2ファイルを描画するコマンド。
---

```bash title="Terminal"
console2svg cast <cast> [options]
```

asciicast v2のイベントを端末画面として再生し、SVGまたはアニメーションSVGを生成します。

## オプション

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

### `--fadeout <sec>`

処理時間、終了後の待機時間、動画のフェードアウト時間を指定します。

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

### `-c, --with-command`

### `--header <text>`

### `--prompt <text>`

コマンド表示の有無、見出し、プロンプトを指定します。

### `-d, --window [style]`

### `--pc-padding <number>`

### `--opacity <number>`

ウィンドウ枠、デスクトップ風ウィンドウの余白、不透明度を指定します。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

テーマ、文字色、端末の背景色を指定または上書きします。

### `--margin <number>`

### `--padding <number>`

### `--background <value>`

余白と背景の色または画像を指定します。

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

フォント、サイズ、SVGテキストの長さ調整方法を指定します。

### `--mask <pattern>`

### `--mask-auto [bool]`

文字列のマスクと自動シークレットマスキングを設定します。

### `--frame <index>`

### `--time <sec>`

### `--size <WxH>`

フレーム番号、時刻、出力サイズを指定します。

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

### `--coalesce-ms <ms|auto>`

近接する出力イベントをまとめる間隔を指定します。

### `--mouse`

### `--no-colorenv`

### `--no-delete-envs`

マウス追跡の転送、色設定用環境変数の上書き、CI用環境変数の削除を設定します。

### `--svg-converter <converter>`

SVGのラスタライズ方法を指定します。
