---
title: 生成动画 SVG
description: 将保留的终端状态整理为共享行定义，并通过离散 SMIL 区间切换显示。
---

动画 SVG 从终端状态序列生成，而不是从截图序列生成。
`AnimatedSvgRenderer` 把录制按顺序送入与静态输出相同的 terminal emulator，只保留需要的状态，再转换为可复用行定义。

## 决定哪些终端状态需要保留

PTY 或 asciicast 的事件边界不会自动成为动画帧边界。
一次 TUI 重绘可能包含多次 write，有些事件也只改变 parser state 而不改变可见 cell。

production replay 路径会在每个事件后比较 `ScreenBuffer.GetContentSignature()`。
该签名不包含 cursor，因此 cursor-only 变化不会强制增加正文的 **保留帧**。

`--fps` 为正数时会应用最小帧间隔。
同一时间窗口中出现多次可见变化时，最后一个变化状态会作为 pending frame 保留。

快照使用按行 copy-on-write。
创建可见快照时，未变化行与 live buffer 共享；后续修改某行时才复制该行。
更新 FPS 窗口中的 pending frame 时，也通过复制行引用和签名信息更新状态，而不是 deep copy 整个 cell grid。

首个和最终状态都会保留。
时间正规化如果把多个 frame 压到同一 timestamp，会略微展开它们的时间，保证 SMIL key time 保持有序且不重复。

## 建立行目录而不是完整帧目录

确定保留状态后，console2svg 不会序列化每一个完整 screen。

`PrepareAnimatedRows` 读取每个可见行的视觉签名并建立 **行目录**。
签名用于选择候选定义，但在复用之前还会比较实际 cell。
签名只是加速结构，不是唯一的正确性依据。

发现新的唯一行时，会同时收集该行需要的文字 style。
重复行不需要再为了 style collection 做一次 cell scan。

每个 frame 保存一组行定义 index。
SVG 在 `<defs>` 中只绘制唯一行，在需要显示该状态的位置通过 `<use>` 引用。

## 用行差分表示小范围修改

输入文字和 status 更新常常只改变一行中的少量列。
这类情况可以使用 **行差分**，引用前一个行定义，只覆盖变化列范围。

差分范围必须不超过16列，并且不超过可见行宽度的四分之一。
差分链最大为四层。
这些限制避免为了节省字节而产生很深的 `<use>` 引用树。

边界碰到宽字符 continuation 或宽字符起始 cell 时会扩展，避免拆开一个字符。

存在手动 mask pattern 时会关闭行差分。
秘密 pattern 可能同时包含不变 prefix 和变化 suffix，拆成两个 fragment 会让 matcher 看不到完整字符串。

## 复用行定义的临时渲染空间

行定义仍然需要转换为 text、rectangle、box-drawing path、block element 和 mask overlay。
renderer 使用 `FrameRenderWorkspace` 复用临时 segment List 和 `StringBuilder`。

可见行通过 span 暴露给列循环。
不涉及 scrollback 时，不需要在每个 cell 上重复调用通用 accessor。

## 使用 SMIL 切换连续行状态

对每一条物理行，连续引用同一行定义的 frame 会合并为一个 run。
每个 run 只有一个 `<use>`，并用 **SMIL** 的 `display` animation 指定可见时间。

`calcMode="discrete"` 不进行中间值插值。
这与终端状态切换一致，边界前显示一个行状态，边界后显示另一个状态。

例如：

```xml title="output.svg"
<animate
  attributeName="display"
  values="none;inline;none"
  keyTimes="0;0.25;0.5"
  calcMode="discrete"
  dur="4s"
/>
```

循环输出增加 `repeatCount="indefinite"`。
非循环输出冻结最终 animation state。
fade-out 作用于包含这些行的 group，并在最终 hold 之后发生，不需要修改每个行 animation。

文字 blink 与 screen-state animation 分开，仍通过 CSS animation 表示。

## 单独保存 cursor 状态

cursor 可见性和位置会形成独立的连续 run。
cursor 移动时，文字行可以继续引用原来的行定义。

正文 frame reduction 可以使用不含 cursor 的 content signature，就是因为 cursor timing 在 animation layer 独立保留。
cursor 变化和正文行去重不必绑在同一个 frame 内容中。

## 处理时间范围和最终画面

指定开始时间时，如果此前存在状态，会把开始点之前最后一个状态作为选区初始状态。
随后把所选 timeline 重新映射到0秒起点。

`--sleep` 延长最终状态的显示时间。
没有显式值时仍会提供最小 hold，避免最终状态刚出现就结束。
`--fade-out` 在 hold 之后开始。

full-screen application 退出时经常离开 alternate screen 并恢复空的 main screen。
如果录制尾部只包含这种空画面恢复，renderer 可以去掉该尾段，让前一个有效 terminal state 留在最终画面。
