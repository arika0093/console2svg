---
title: 组织 SVG 图层
description: 分离静态 chrome、终端背景、前景、cursor 和 mask，并让所有元素共享同一坐标系。
---

终端画面中的元素变化频率不同。
canvas 背景和 window chrome 通常保持不变，cell 背景和文字可以独立变化，cursor 也可以在行正文不变时移动。
SVG 通过 **图层分离** 保留这种差异，动画不必复制可复用的静态内容。

## 在一个 Context 中解析坐标

`SvgDocumentBuilder.Context` 先计算 crop 后的可见行和列，再解析 cell metric、margin、padding、chrome offset、command header 高度、canvas size、output size 和 `viewBox`。

cell width 为 font size 的0.6倍。
cell height 为 font size 的 `18 / 14` 倍，baseline offset 以 font size 为基准。

文字、背景、box drawing、block element、cursor 和 mask overlay 都使用同一个 `Context`。
各图层不会维护互相独立的 terminal-grid 坐标计算。

只指定一个输出尺寸时，另一个尺寸按比例计算。
宽高都指定时，会把自然 canvas 按比例放进目标矩形，多余区域通过 view box 表示。
terminal grid 不会在不同图层中以不同方式拉伸。

## 静态外层只输出一次

SVG root 放置 output dimensions、view box、公共 CSS、可复用定义和 canvas 背景。

window chrome 位于其上。
desktop 类型的 chrome 可以包含独立背景区域、shadow、frame 和 title area。
terminal client area 随后使用解析后的 terminal background 填充，避免 chrome 内部 padding 意外透明。

`--with-command` header 也属于静态区域。
动画 SVG 只输出一次这些外层内容，不会随 terminal state 重复。

## 分开终端背景和前景

静态 SVG 可以把 terminal background 和 foreground 分成两个 group 渲染。

background pass 会把横向连续的相同有效背景色合并成一个 rectangle。
与 base terminal background 相同的 cell 不生成独立矩形。

foreground pass 输出 text run、block geometry、box-drawing geometry、cursor 和 mask overlay。
只渲染 background 的 pass 不执行自动或手动 mask scan，因为该 pass 没有可暴露秘密的 foreground text。

mask overlay 位于被替换文字之上。
秘密值不会仅仅依赖另一个 opaque layer 来遮住。

## 共享 style 和 geometry

`SvgStyleRegistry` 为每一种唯一有效文字样式分配短 CSS class。
foreground color 和 decoration 不需要重复写入每个 `<text>`。

`SvgElementRegistry` 对可复用定义中的 rectangle 和 path 进行相同处理。
首个 geometry 获得 ID，后续等价 geometry 使用 `<use>`。

位置为0并且 SVG 默认值已经等价时，会省略该 position attribute。
单次节省很小，但动画行定义中相同结构会出现很多次。

## 限定动画真正切换的图层

动画输出把唯一行内容放入 `<defs>`。
可见 terminal body 使用 `<use>` 引用行定义，并通过 discrete SMIL display interval 切换。

cursor run 与行正文分开。
cursor-only 变化只更新 cursor layer，不需要重建行定义。

canvas 背景、chrome、command header、content transform 和 crop 保持在行切换机制之外。
静态 SVG 和动画 SVG 因此使用相同 terminal geometry 和颜色解释，只改变重复正文的引用方式。
