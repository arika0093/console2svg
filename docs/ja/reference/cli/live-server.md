---
title: live-server
description: ライブ端末SVGを配信するコマンド。
---

```bash
console2svg live-server [options] [host:port]
```

PTYの現在画面をSVGとしてHTTP配信します。待ち受け先を省略すると既定値を使用します。

## オプション

### `--save-cast <path>`

取得した出力をasciicast v2ファイルとして保存します。

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

### `-c, --with-command`

出力の先頭に実行コマンドを表示します。

### `--header <text>`

### `--prompt <text>`

コマンド行の見出しまたはプロンプトを上書きします。

### `-d, --window [style]`

ウィンドウ枠のスタイルを指定します。値を省略すると`macos`を使用します。

### `--pc-padding <number>`

### `--margin <number>`

### `--padding <number>`

デスクトップ風ウィンドウ、ウィンドウ枠、シェル内部の余白を指定します。

### `--opacity <number>`

背景の不透明度を`0`から`1`で指定します。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

テーマ、文字色、端末の背景色を指定または上書きします。

### `--background <value>`

背景の色または画像を指定します。2回指定するとグラデーションになります。

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

フォント、サイズ、SVGテキストの長さ調整方法を指定します。

### `--mask <pattern>`

### `--mask-auto [bool]`

文字列のマスクと自動シークレットマスキングを設定します。

### `--fps <number>`

画面更新の最大サンプリングレートを指定します。

### `--no-resize`

初期TTYサイズを維持します。

### `--mouse`

マウス追跡をPTYへ転送します。

### `--no-colorenv`

### `--no-delete-envs`

色設定用環境変数の上書き、CI用環境変数の削除を設定します。
