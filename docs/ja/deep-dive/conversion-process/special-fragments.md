---
title: 罫線とブロック要素の SVG 化
description: font glyph だけでは崩れやすい terminal geometry を cell 寸法から SVG shape へ変換し、結合する仕組み。
---

Unicode の一部の文字は、通常の文字より直接的に terminal geometry を表します。
罫線文字は接続した border を表し、block element は cell の一定割合を塗りつぶします。

これらを font glyph だけで描くと、隣接 cell の境界に隙間が見える場合があります。
glyph bearing、stroke width、hinting、antialiasing は、monospace font でも font、OS、SVG renderer によって異なるためです。
console2svg は対応する文字を terminal cell 寸法から求めた SVG shape へ変換します。

## 罫線文字を接続線分へ変換する

罫線文字は、cell 中心から左、右、上、下のどこへ接続するかという情報へ変換します。
light と heavy の variant は異なる線幅を使います。
short line variant は該当する部分線分だけを作ります。

renderer は一文字ごとに即座に SVG path を書かず、まず水平と垂直の半線分を集めます。
同じ axis 位置、色、線幅を持つ線分を sort し、接触または重複している範囲を結合します。

結合許容値は cell 寸法に対する相対値です。
極端に小さい font size でも、絶対座標が近いだけの別線分を誤って一つにしないためです。

結合後の線分は矩形として扱い、同色のものを compact な path data へまとめます。
SVG element 数を減らすと同時に、cell ごとの stroke endpoint が縮小時にわずかに離れて見える問題も抑えます。

`╭`、`╮`、`╯`、`╰` のような角丸は、quadratic Bézier curve として別に扱います。
同じ cell 座標系を使いますが、直交線分の接続 model へ無理に変換しません。

## block element を矩形へ変換する

cell の塗りつぶし割合を表す block element は、cell width と height から矩形を計算します。

上側と下側の分数、左側と右側の分数、quadrant の組合せを、glyph box ではなく cell 境界へ合わせます。
隣接する半 block や quadrant を並べても、font baseline や glyph margin による隙間が入りにくくなります。

同じ色で連続した block rectangle は、可能な場合に serialization 前へ結合します。
占有する terminal cell を変えずに繰り返し geometry を減らします。

shade character は text のまま残します。
これらは連続した solid area ではなく、font が持つ dot pattern の見え方を利用する文字だからです。

## geometry を text run から分離する

shape として描く文字に到達した時点で、通常の foreground text run を終了します。

`textLength` が、実際には独立 shape として描く cell の幅まで含めて調整しないようにするためです。
shape の後に続く文字列は、その terminal column から新しい text run を開始します。

静止 SVG、animation の行定義、動画用に rasterize する SVG frame は、同じ geometry 変換を使います。
出力形式によって terminal border の作り方を変えません。
