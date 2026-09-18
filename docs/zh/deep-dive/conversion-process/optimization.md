---
title: 优化转换流水线
description: 减少解析工作、画面复制、SVG DOM、内存分配、栅格化和文件系统 I/O 的实现方式。
---

终端录制在多个层级都包含重复信息。
一次画面更新可能被拆成多次 PTY read，相邻状态通常只改动少量单元格，动画中许多行会重复出现，固定 FPS 的视频采样也可能连续请求同一画面。
console2svg 会在后续阶段付出成本之前逐层消除这些重复。

## 在渲染前减少事件数量

PTY 每次 read 的结果默认不会直接变成一个录制事件。
相邻的小块输出会通过 **输出合并** 进入同一个 `RecordingSession` 事件。

默认窗口为目标帧间隔的四分之一，并限制在 2 到 20 毫秒之间。
一个批次也不会持续超过一个帧间隔，避免连续输出无限延迟事件提交。

TUI 经常通过多次小 write 完成一次重绘。
在录制阶段合并这些 write，可以减少后续 ANSI 解析和候选画面状态的数量。

终端模拟完成后还会进行第二次削减。
`TerminalEmulator.ReplayFrames` 比较画面内容签名，不为没有改变可见单元格的事件保存新帧。
设置正数 FPS 时还会应用最小帧间隔，同一窗口内有多次变化时保留最后一个 pending 状态。
首个状态和最终状态仍然会保存。

静态 SVG 使用不同路径，因为通常只需要一个画面。
默认静态渲染会把同一个 emulator 推进到目标状态，而不是为每个事件创建 `ScreenBuffer` 快照。
这样不会把动画快照成本带入静态图片。

## 通过行共享降低快照成本

如果每个动画状态都 deep copy 整个 cell matrix，复制量会随帧数、行数和列数的乘积增长。
`ScreenBuffer` 使用 **copy-on-write** 行存储避免这种复制。

创建可见快照时，只复制外层的行引用数组。
内部的 `ScreenCell[]` 行会与 live buffer 共享。
后续需要修改共享行时，才单独复制该行。

FPS 窗口中的 pending frame 也利用相同结构。
更新 pending 状态时复制行引用和签名信息，不重新 deep copy 整个画面。

行共享同时用于比较。
两个快照如果引用同一个行数组，`HasSameVisualRow` 可以直接确认该行相同。
只有引用不同的行才需要逐 cell 比较。

## 增量维护视觉签名

画面比较使用 **视觉签名**。
每个 cell 的签名由文字、显示样式和宽字符标记组成。
字符串和样式通过 `XxHash3` 计算，ASCII 单字符的文字签名会预先缓存。

每一行都保存由“列位置加 cell 签名”组成的64位行签名。
开启签名跟踪后，修改一个 cell 不需要重新扫描整行。
旧 cell 的定位签名通过 XOR 移除，再通过 XOR 加入新 cell 的定位签名。

整个画面的签名由各行签名组合得到。
`GetContentSignature` 不包含 cursor，用于判断 terminal content 是否需要新的动画保留帧。
`GetVisualSignature` 还包含 cursor 可见性和位置，用于区分最终渲染状态。

组合行签名所需的临时 byte span 在4096字节以内使用 `stackalloc`。
更大的画面才从 `ArrayPool<byte>` 租用数组。

## 复用常见字符和样式

终端输出会大量重复 ASCII 单字符和相同文字样式。
console2svg 预先创建128个 ASCII 单字符 `string`，避免每次 cell 更新都调用 `char.ToString()` 创建新对象。

`CellStyle` 也在 `ScreenBuffer` 内部复用。
连续文字保持同一 SGR 状态时会走 last-style fast path。
较远位置再次使用同一样式时，由有上限的 dictionary 复用。

如果输入不断生成新的 RGB 组合，style cache 超过256项后会重建，避免无上限增长。

频繁使用的 dictionary 路径会在合适的位置调用 `CollectionsMarshal.GetValueRefOrAddDefault`。
这样可以把“先查找，再插入”的两次 hash table 探测合并为一次 slot 获取。

## 压缩 SVG DOM 和字符串

SVG 不会按一个 cell 一个元素输出。

连续相同的非默认背景色会合并为一个矩形。
连续相同前景样式的 cell 会合并到一个 `<text>`。
中间空格只有在连接后续同样式文字时才保留，行尾空 cell 不输出。
宽字符和作为几何图形绘制的字符会结束当前 text run，避免列宽计算漂移。

`SvgStyleRegistry` 为相同文字样式分配短 CSS class。
`SvgElementRegistry` 会给可复用定义中的首个相同矩形或 path 分配 ID，后续实例改用 `<use>`。

框线字符在序列化前先转换为水平和垂直线段。
位置、颜色、线宽兼容的相邻线段会合并，同色矩形再收集为紧凑的 path data。

引入该框线合并优化时，相关提交中的 btop benchmark 将目标 SVG 从 68.5 KB 降到 31.3 KB，并把框线路径从417个降到5个。
这些数字只描述当时的 btop workload 和实现版本，不表示任意终端输出都具有固定削减比例。

`SvgWriter` 还会减少数字序列化产生的临时字符串。
整数和浮点数通过 `TryFormat` 写入 stack buffer，再直接写出 span。
`StringBuilder` 使用 `GetChunks()` 写出已有 chunk，不先创建第二份完整字符串。

## 按行去重动画内容

动画 SVG 不会复制每个保留帧的完整画面。
`PrepareAnimatedRows` 构建 **行目录**，相同内容的行只定义一次。

候选查找首先使用行视觉签名。
签名相同后还会比较实际 cell 内容，hash collision 不会直接造成错误复用。

只有发现新的唯一行时才收集该行的文字样式。
行目录构建和 style collection 在同一次扫描中完成，不需要先扫描全部 frame 收集 style，再扫描一次做行去重。

## 把局部行变化编码成差分

一行只有少量列改变时，可以使用 **行差分**。
新定义通过 `<use>` 引用前一个行定义，只在其上绘制变化列。

差分范围必须不超过16列，并且不超过可见宽度的四分之一。
差分引用链深度限制在四层以内。
这些限制防止为了减少文件大小而产生过深的 `<use>` dependency chain。

差分边界碰到宽字符 continuation 时会扩大范围，避免拆开一个字符。

存在手动 mask pattern 时不会使用行差分。
pattern 可能跨越不变的 base 内容和变化 fragment，拆开后 matcher 将无法看到完整字符串。

## 复用行渲染工作区

输出唯一行时需要临时保存水平线段、垂直线段、圆角、block rectangle 和合并后的 rectangle。
`FrameRenderWorkspace` 持有这些 List，每次只 `Clear()` 后复用。

foreground text、mask 正规化文字和 path data 使用的 `StringBuilder` 也放在同一 workspace。
这样不会为每一个唯一行重复创建同类临时对象。

可见行在列循环外通过 `GetVisibleRow` 取得 span。
不涉及 scrollback 的普通路径不需要为每个 cell 调用通用 accessor。

## 按连续行状态生成 SMIL

完成行去重后，也不会为每个 frame 都生成一个 `<use>`。
同一物理行连续引用相同定义的 frame 会合并为一个 run。

一个 `<use>` 表示整个 run，`display` 的 discrete SMIL animation 决定其显示区间。
cursor 也单独按连续状态分组，因此 cursor-only 变化不会复制行正文。

DOM 数量因此更接近“每一行实际发生内容切换的次数”，而不是简单的“帧数乘以行数”。

## 在视频阶段复用 SVG 和 PNG

固定 FPS 视频帧生成只使用一个 `TerminalEmulator`，并按录制顺序持续推进。
每个 sample 不会从事件0重新 replay 到目标事件。
sample time 增加时，event index 也只向前移动。

静态 SVG 以画面视觉签名为 key，最多缓存128项。
相同画面再次出现时会返回 cache 中同一个 SVG `string` 对象，而不是重新构建同内容 XML。

视频 converter 以该 object identity 作为 PNG cache 的 key。
相同画面通过 **两级缓存** 同时避免 SVG 重建和 PNG 重栅格化。
也不需要对很长的 SVG 字符串重新计算内容 hash 或做全文比较。

PNG 可能比源 SVG 占用更多内存，因此 PNG cache 限制为16项。

SVG 到 PNG 可以并发执行，并发度按照 CPU 数量决定，最大为8。
完成的 PNG 仍通过 bounded FIFO 按原始 frame 顺序写入 ffmpeg。
不会先把整段视频的 PNG 全部保存在内存中。

## 在内存中传递视频帧

一段视频只启动一个 ffmpeg process。
PNG frame 直接写入 `image2pipe` 标准输入，普通内存路径不使用编号 PNG 文件作为中转。

捆绑的 resvg 也在当前 process 内调用。
system font database 只初始化一次并被后续 frame 共享。
传入 native 侧的 SVG UTF-8 buffer 从 `ArrayPool<byte>` 租用。

这些处理把逐 frame 的 process 启动、文件创建、重新读取和删除从普通视频路径中移除。

## 分阶段测量优化效果

benchmark project 分别测量 terminal replay、production frame 准备、唯一行目录构建、行定义输出、SMIL 输出、静态 SVG、动画 SVG 和真实 asciicast workload。
FPS 和自动 mask 的不同组合也有独立测量。

`MemoryDiagnoser` 记录 managed allocation 和 GC。
Linux 上能够使用 `perf` 时，还可以收集 retired instructions、cycles、branches、cache misses、CPU sampling，以及 render 到 buffer 更新调用链的更深 disassembly。

减少 SVG byte 数、减少生成 CPU、减少 allocation 是不同的优化目标。
分阶段 benchmark 可以分别观察这些变化，而不是把它们当成同一个性能指标。
