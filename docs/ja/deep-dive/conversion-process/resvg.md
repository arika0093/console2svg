---
title: resvg による PNG ラスタライズ
description: 同梱した resvg を process 内で呼び、font state と managed、native memory の所有権を管理する仕組み。
---

PNG を出力するには SVG renderer が必要です。
console2svg は Rust 製 resvg の小さな C ABI wrapper を同梱し、通常の PNG 経路で browser process や ffmpeg の SVG decoder 有無へ依存しないようにします。

## ラスタライズを process 内で完結させる

native wrapper は usvg で SVG を parse し、resvg と tiny-skia で pixmap へ描画し、その pixmap を PNG に encode します。

system font の探索結果は process 全体で共有できる状態です。
Rust 側は **`OnceLock<Arc<Database>>`** を使い、最初の一回だけ font database を作って system font を load します。
後続 render は同じ database の共有参照を使います。

.NET 側から明示的に warm up できるため、converter detection の時点で初期化を済ませることもできます。
動画の任意の一フレームだけが、偶然 system font 探索の初回コストを負うことを避けられます。

## raster size の決め方を固定する

native wrapper は SVG の intrinsic size と、任意指定の raster width、height から出力寸法を決めます。

幅と高さを両方指定した場合は、その値を使います。
片方だけ指定した場合は、SVG の aspect ratio からもう一方を計算します。
どちらも指定しない場合は SVG 自体の寸法を使います。

最終寸法は1から16384 pixel の範囲へ clamp してから tiny-skia の pixmap を確保します。
0 pixel の surface や、誤指定による極端な native allocation をそのまま通しません。

## managed 側の入力 buffer を再利用する

`ResvgNative.RenderToPng` は SVG `string` の UTF-8 byte 数を先に計算します。
必要な byte array を `ArrayPool<byte>` から借り、その span へ SVG を encode して native function へ渡します。
呼び出し後は借りた array を pool へ返します。

native renderer が返す PNG buffer の所有者は native 側です。
.NET wrapper は PNG を managed `byte[]` へ copy し、`finally` で対応する native free function を必ず呼びます。

呼び出し側へ渡すのは managed PNG だけです。
Rust 側の allocation 方法を呼び出し側が推測して解放する構造にはしません。

native status は SVG parse、PNG encode、render、allocation の失敗を分けて返します。
.NET 側はそれぞれを例外へ変換し、空 buffer を成功扱いにはしません。

## 同梱 asset の場所を先に探索する

native library resolver は、通常の loader resolution より先に console2svg の bundled asset directory を調べます。

release archive のように executable の隣へ native library を置く配置と、package のように sibling library directory へ置く配置の両方を扱うためです。
portable install や symbolic link 経由でも、current working directory だけに依存せず library を探せます。

## converter mode に応じて fallback を制御する

auto mode では、利用可能なら同梱 resvg を優先します。
他の画像変換経路では、`rsvg-convert` や実際に SVG decode を確認できた ffmpeg へ fallback できます。

利用者が resvg を明示指定した場合は、native library を load できなくても別 renderer へ黙って切り替えません。
renderer を指定した場合の再現性と、auto mode の可用性を別の方針として扱います。
