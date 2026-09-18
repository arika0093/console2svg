---
title: 解释终端控制序列
description: 以有状态方式处理被拆分的 VT 输出，并更新 cursor、cell、样式、scroll 和 alternate screen。
---

终端输出是一系列修改画面状态的操作，不是带装饰的普通字符串。
carriage return 可以覆盖当前行，CSI 可以不打印字符而移动 cursor，full-screen application 也可以切换 alternate screen 并只重绘局部区域。
如果先删除 escape sequence，就会丢失重建最终画面需要的操作。

console2svg 在 SVG 生成之前通过 **有状态解析** 把这些操作写入 `ScreenBuffer`。

## 不把 read 边界当成控制序列边界

OS read 可能结束在 ESC、CSI、OSC 或其他控制序列的中间。
`AnsiParser` 会保存未完成的 sequence，并在下一次 `Process` 调用继续解析。

Unix echo 还可能把 ESC 显示成 `^[` 这样的 caret notation。
以这种形式到达的 OSC 有单独 pending state。
处理范围会受到限制，普通可见的 `^[` 文本不会被广泛当成控制序列吞掉。

OSC 和 DCS payload 不是 printable cell，因此会跳过直到 terminator。
G0 和 G1 character set designation，以及 SO 和 SI 选择，也会作为 parser state 保存。
DEC special graphics 可以在渲染前映射成对应的框线字符。

## 不用字符串 split 解析 CSI 参数

CSI 参数直接从 span 读取。
parser 不需要使用 `string.Split` 为每个参数创建字符串或数组。

首先计算参数数量。
16个以内使用 `stackalloc` 的 integer span。
参数更多时才从 `ArrayPool<int>` 租用数组，并在处理结束后归还。

常见 SGR 和 cursor-control sequence 参数很少，因此不会为每条 sequence 产生一个 heap array。

private marker 与参数区分开处理。
不支持的 private sequence 会被忽略，不会误当成其他标准命令。

## 把控制操作应用到 cell 状态

实现的 CSI 包括 cursor 相对移动和绝对定位、display erase、line erase、字符插入和删除、行插入和删除、scroll、scroll region、tab control、insert mode、repeat、save 和 restore。

DEC private mode 处理 alternate screen、origin mode 和 cursor visibility。
alternate screen 使用独立 cell buffer。
TUI 结束后可以恢复 main screen，而不是把 full-screen 内容混进 shell history。

cursor save 和 restore 还保留会影响后续字符布局的 terminal state。
目标不是只恢复收到 sequence 那一刻的画面，而是保持该操作对后续输出的语义。

## 把 SGR 解析为 cell 样式

SGR 更新 `TextStyle`。
其中保存 bold、faint、italic、underline、blink、reverse、hidden、strikethrough、overline、foreground、background 和 underline color。

颜色支持常规16色、xterm 256色和 true RGB。
256色由 active theme 的前16色、6×6×6 color cube 和 grayscale 组成。
使用 semicolon 或 colon 分隔的 extended color 形式会归一到相同 style state。

`ScreenBuffer` 会 intern 相同 style，避免每个相邻 cell 都创建独立 style object。
连续字符保持同一 SGR 状态时会直接复用上一个 `CellStyle`。

## 按终端 cell 对齐 Unicode

一个 UTF-16 code unit 不一定等于一个 terminal cell。
surrogate pair 会在放置前组合。
combining mark 和 variation selector 会附加到前一个 cell，zero-width character 不推进 cursor 列。

宽字符占两列。
首个 cell 保存文字，后一个 cell 标记为 continuation。
覆盖操作和动画行差分都会考虑 continuation，避免后续 SVG text run 偏移一列。

variation selector 16 还可能把前一个符号切换为宽 emoji presentation。
所需宽度调整在 cell model 中完成，而不是留到 SVG geometry 阶段。

## 用 ScreenBuffer 作为后续格式的共同输入

解析完成后，renderer 不再需要理解原始 CSI syntax。
后续阶段读取已经解析好的文字、style、width flag、cursor、active screen 和 scroll state。

同一个 **`ScreenBuffer`** 还包含 animation 和 video sampling 使用的视觉签名，以及 copy-on-write 行共享。
VT 语义只在这一层解释一次，SVG、PNG 和视频路径消费同一份终端状态。
