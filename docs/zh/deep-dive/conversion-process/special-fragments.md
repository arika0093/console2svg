---
title: 渲染框线和块元素
description: 如何把依赖 font glyph 时容易出现缝隙的 terminal geometry 转换为 cell 对齐的 SVG shape，并合并成紧凑路径。
---

部分 Unicode 字符比普通文字更直接地表示 terminal geometry。
框线字符组成连续 border，block element 表示 cell 的固定填充比例。

只依赖 font glyph 渲染这些字符可能产生可见缝隙。
即使使用 monospace font，glyph bearing、stroke width、hinting 和 antialiasing 仍会随 font、OS 和 SVG renderer 改变。
console2svg 会把支持的几何字符转换为由 terminal cell 尺寸计算出的 SVG shape。

## 把框线字符转换为连接线段

框线字符会被解释为从 cell 中心连接到左、右、上、下边缘的方向。
light 和 heavy variant 使用不同线宽。
short-line variant 只生成对应的局部 segment。

renderer 不会为每个字符立即写一个 SVG path。
它先收集水平和垂直 half-segment，再按 axis position、color 和 stroke width 排序。

能够接触或重叠的兼容线段会合并。
merge tolerance 相对于 cell 尺寸计算，因此在极小 font size 下也不会仅因为绝对坐标接近就连接实际分离的线段。

合并后的 line rectangle 会按 color 分组并输出成紧凑 path data。
这样既减少 SVG element 数，也能减少缩放后相邻 per-cell stroke endpoint 出现细缝的情况。

`╭`、`╮`、`╯`、`╰` 等圆角通过 quadratic Bézier curve 单独绘制。
它们使用相同 cell coordinate system，但不会强行转换成普通正交连接模型。

## 把 block element 转换为矩形

表示 cell 填充比例的 block element 会根据 cell width 和 height 计算 rectangle。

上下分数、左右分数和 quadrant 组合因此对齐到 cell boundary，而不是依赖选定 font 的 glyph box。

颜色相同且可以组成连续区域的相邻 block rectangle 会在序列化前合并。
减少重复 geometry 的同时，不改变占用的 terminal cell。

shade character 保持为 text。
它们依赖 font 的点阵或纹理表现，而不是连续 solid area。
替换成实心矩形会改变字符本身的视觉语义。

## 把 geometry 和 text run 分开

遇到需要作为 shape 绘制的字符时，当前 foreground text run 会结束。

这样可以避免 `textLength` 把实际上由独立 shape 占据的 cell 也算入文字伸缩范围。
shape 后面的普通文字从其 terminal column 开始新的 text run。

静态 SVG、动画 row definition 和视频栅格化使用的 SVG frame 共用同一套 geometry conversion。
输出模式不会改变 terminal border 的构造方式。
