---
title: SVG のレイヤー構造
description: 静的な chrome、端末背景、前景、cursor、mask を分離し、一つの座標系で描画する仕組み。
---

端末画面を構成する要素は、同じ頻度では変化しません。
canvas 背景と window chrome は通常固定され、cell 背景と文字は別々に変わり、cursor は行本文を変えずに移動できます。
SVG はこの違いを **レイヤー分離** として表し、animation で再利用できる内容を複製しません。

## 座標を一つの Context で決める

`SvgDocumentBuilder.Context` は crop 後の可視行と可視列を決め、cell metric、margin、padding、chrome offset、command header の高さ、canvas size、output size、`viewBox` をまとめて計算します。

cell 幅は font size の0.6倍です。
cell 高は font size の `18 / 14` 倍で、baseline offset は font size を基準にします。

文字、背景矩形、罫線、block element、cursor、mask overlay は同じ `Context` の値を参照します。
要素ごとに別の座標計算を持たせず、同じ terminal grid へ合わせます。

出力寸法を片方だけ指定した場合は、もう一方を比例計算します。
幅と高さを両方指定した場合は、自然な canvas を指定矩形へ収め、余った領域を view box 側で表現します。
各 layer が独自に terminal grid を引き伸ばすことはありません。

## 外側の静的レイヤーを一度だけ描く

SVG root には output size、view box、共通 CSS、再利用する定義、canvas 背景を置きます。

その上へ window chrome を配置します。
desktop 形式の chrome は専用の背景領域、shadow、frame、title area を持てます。
続いて terminal client area を解決済みの背景色で塗り、chrome 内の padding が意図しない透過にならないようにします。

`--with-command` の header も静的領域へ置きます。
animation SVG では、これらを terminal state ごとに複製せず一度だけ出力します。

## 端末背景と前景を別 pass にする

静止画では terminal background と foreground を別 group として描けます。

background pass は、同じ有効背景色が横に続く cell を一つの矩形へまとめます。
base terminal background と同じ cell は個別の矩形を出しません。

foreground pass は text run、block geometry、box-drawing geometry、cursor、mask overlay を出します。
background だけを描く pass では、foreground text が存在しないため自動 mask と手動 mask の scan を実行しません。

mask overlay は置換した文字より上へ配置します。
別の opaque layer の下へ秘密文字列を残して隠す方式にはしません。

## style と geometry を定義として共有する

`SvgStyleRegistry` は、一意な実効文字 style ごとに短い CSS class を一つ割り当てます。
foreground color と decoration を各 `<text>` へ繰り返し書きません。

`SvgElementRegistry` は、再利用可能な定義内の同じ矩形と path を共有します。
最初の geometry に ID を付け、同一要素が再登場した場合は `<use>` を出します。

位置が0で SVG の既定値と同じ場合は、不要な position attribute を省略します。
一つの削減量は小さくても、行定義が多数ある animation では同じ attribute の繰り返しを減らせます。

## アニメーションで切り替える範囲を限定する

animation SVG では、一意な行本文を `<defs>` に置きます。
可視 terminal body は `<use>` で行定義を参照し、discrete な SMIL の表示区間で切り替えます。

cursor run は行本文と別に出力します。
cursor だけが変わったときに、行定義を作り直す必要はありません。

canvas 背景、chrome、command header、content transform、crop は行切替の外側に置きます。
静止 SVG と animation SVG で terminal の座標と配色解釈は共通のまま、繰り返す本文の参照方法だけを変えます。
