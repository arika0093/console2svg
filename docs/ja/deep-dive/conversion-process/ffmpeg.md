---
title: ffmpeg による動画エンコード
description: 端末状態を固定時刻で採取し、PNG へ並列変換して、一つの ffmpeg process へ順番に流す仕組み。
---

動画出力も、静止画と同じ SVG renderer を使います。
各 sample 時刻の端末状態を静止 SVG として表し、PNG へラスタライズした後、時系列の画像として ffmpeg へ渡します。

通常の memory 経路では、連結した SVG document を ffmpeg に分割させません。
`image2pipe` には境界を判定できる PNG frame を送ります。

## 一つの emulator を時刻順に進める

固定 FPS の sampling では、video frame ごとに録画の event 0から replay し直しません。

frame generator は一つの `TerminalEmulator` と、前方向にだけ進む event index を持ちます。
sample 時刻が進んだ分だけ、新しく到達した event を処理します。
時刻から event を選ぶ index も、sample ごとに先頭から検索せず前方向へ進めます。

この構造により、後半の frame を作るたびに前半の event を再処理することを避けます。

## 同じ画面の SVG を再生成しない

emulator を目的時刻まで進めた後、画面の visual signature を取得します。

静止 SVG はその signature を key として最大128件キャッシュします。
複数の sample が同じ画面を指す場合は、同じ XML をもう一度組み立てず、cache 内の同じ SVG `string` object を返します。

FPS を指定しない frame 保存では、連続する visual signature が同じ状態も出力対象から外します。

## PNG 変換だけを並列化し、順序は維持する

SVG から PNG への変換は、renderer によって CPU bound または外部 process bound になります。
video converter は複数の PNG render を並列に開始し、並列数を CPU 数に合わせて最大8件へ制限します。

ffmpeg に渡す frame 順序は変えられません。
pending render task を bounded な FIFO queue へ入れ、先頭の task が完了した順に一つの stdin writer から書き込みます。

この方式なら独立した rasterize 処理を重ねつつ、全 frame の PNG byte array を先に作って memory へ保持することを避けられます。

## SVG object identity で PNG を再利用する

PNG は source SVG より大きくなることがあるため、PNG render cache は16件に制限します。

cache key には SVG 文字列の内容ではなく object reference を使います。
前段の SVG cache が同じ visual signature に対して同一の `string` object を返すためです。

これにより **二段キャッシュ** が成立します。
最初の cache が SVG の再生成を防ぎ、次の cache が同じ SVG の PNG 再ラスタライズを防ぎます。
長い XML 文字列を hash したり全体比較したりする必要もありません。

## 一つの ffmpeg process へ全 frame を送る

動画一本につき ffmpeg process は一つだけ起動します。
すべての PNG frame をその standard input へ書き込みます。

入力には `-f image2pipe -vcodec png -i pipe:0` と指定 FPS を使います。
通常の memory 経路では、連番 SVG file や連番 PNG file を中継しません。

standard output と standard error は process 起動直後から非同期で drain します。
子 process の pipe が満杯になって encode が止まることを防ぎ、失敗時には ffmpeg の diagnostic を残します。

cancel が発生した場合は ffmpeg の process tree 全体を終了させます。
最後の PNG を書いた後で stdin を閉じ、ffmpeg の終了を待ちます。

## format 表示ではなく実変換で SVG 対応を確認する

ffmpeg は SVG pipe format を表示していても、build に必要な SVG decoder が入っていないことがあります。
画像変換で ffmpeg を候補にするときは、最小 SVG を実際に PNG へ変換する probe を行い、その結果を cache します。

in-memory video には別の制約があります。
`image2pipe` は連結した複数 SVG document を frame 単位へ分割できないため、video 側で converter mode が ffmpeg を指していても、PNG を作れる resvg または `rsvg-convert` を先に解決します。
最終段の ffmpeg は動画 encode に専念します。

MP4 codec の検出結果も cache し、変換のたびに `ffmpeg -encoders` を繰り返し実行しません。

## MP4 の codec と偶数寸法を選ぶ

MP4 では `libx264` が利用可能なら優先し、なければ `mpeg4` を使います。
WebM と GIF などでは、MP4 用 codec を強制せず ffmpeg の container に応じた既定選択へ任せます。

出力 pixel format には `yuv420p` を指定し、次の filter を適用します。

```text
pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0
```

奇数の raster width または height を偶数へ切り上げる処理です。
追加されるのは右端または下端の最大1 pixel なので、terminal content の左上位置は動かしません。
