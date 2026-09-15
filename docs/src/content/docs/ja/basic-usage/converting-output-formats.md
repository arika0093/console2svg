---
title: 出力フォーマットを変換する
description: キャプチャ結果をPNG、GIF、MP4、WebMなどへ直接出力します。
---

console2svgのネイティブ出力はSVGですが、`-o`で指定した拡張子に応じて画像・動画へ自動変換できます。変換専用の`convert`サブコマンドはありません。

```bash
console2svg capture -o output.png -- dotnet --version
console2svg capture -o output.gif --timeout 5 -- cmatrix -ab
console2svg capture -o output.mp4 --timeout 5 -- cmatrix -ab
```

PNGやJPEGなどの静止画では、標準で最終フレームが使われます。GIF、WebM、MP4などの動画拡張子を指定した場合は、拡張子から動画出力が自動判定されます。

`-v`は、出力がSVGでもアニメーションにしたい場合や、`--mode video`を短く指定したい場合に使います。逆に動画系の拡張子でも静止画として扱いたい場合は`--mode image`と`--frame`を組み合わせられます。

## 出力ピクセル寸法

`--size`は変換後の画像・動画サイズを指定するオプションです。端末の文字幅・行数を変える`-w`/`-h`とは別物です。

```bash
console2svg capture -w 100 -h 24 -o output.png \
  --size 1280x720 -- dotnet --info
```

## 変換バックエンド

利用可能な変換バックエンドは環境によって異なります。静止画変換ではbundled resvg、`rsvg-convert`、FFmpegなどが利用され、動画出力ではFFmpegが必要です。

現在の環境で何が使えるかは`status`で確認できます。

```bash
console2svg status
console2svg status --format json
```

変換に失敗する場合は`--verbose`も有効にして、[Statusとverboseログ](/ja/reference/status-and-verbose-logs/)も確認してください。

![GIFへ変換したアニメーションキャプチャ](/assets/cmd-matrix-video.gif)
