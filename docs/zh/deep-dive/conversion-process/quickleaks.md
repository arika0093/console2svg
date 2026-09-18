---
title: 使用 QuickLeaks 自动遮盖敏感信息
description: 如何缩小 Betterleaks 派生的匹配范围，将其映射回终端单元格，并在常见的无匹配路径上避免额外分配。
---

自动遮盖在终端文字写入 SVG 之前执行检测。
detector 返回 UTF-16 范围，renderer 再判断这些范围覆盖哪些 terminal cell。
把字符串检测和 cell geometry 分开，可以让检测逻辑保持普通字符串处理，同时在绘制阶段保留终端列位置。

## 区分 QuickLeaks 和 Betterleaks 的职责

**QuickLeaks** 是由固定版本的 Betterleaks rule set 和 console2svg 自有规则生成的内置 .NET detector。
当前生成代码包含465条规则。

它保留 Betterleaks 的 rule ID、keyword 和 regular expression，但不会嵌入 Betterleaks 的完整执行模型。
expression filter、validator、provider 或 network check，以及 repository-context 逻辑都会被有意省略。

因此，QuickLeaks finding 表示“文字匹配了类似秘密信息的 pattern”。
它不证明该值是有效 credential，也不证明 provider 会接受该值。

规则被生成为 C# source，因此运行时不需要启动外部 scanner process，也不需要读取额外的 rule 配置文件。

## 在 regex 之前用 Aho-Corasick 缩小候选规则

如果每个画面都运行数百个 regular expression，自动遮盖在动画场景中会产生明显成本。

生成器会根据 rule keyword 构建 **Aho-Corasick automaton**。
输入字符串只扫描一次，命中的 keyword 会把对应规则写入紧凑 bitset。
随后只运行候选规则的 source-generated regex。

automaton 的 state、transition、output 和 failure link 被打包为 UTF-16 常量字符串，而不是生成大量数组初始化代码。
只有一个出口的 state 使用直接比较。
出口数量不超过8时使用短 linear scan，更多时使用 binary search。

regex 通过 `GeneratedRegex` 生成，并设置固定 match timeout。
单个 pathological input 因此不会让某条规则无限运行。

keyword stage 只负责候选过滤。
最终匹配范围仍由各条规则的 regex 决定。

## Early 模式只放宽秘密值部分

输入尚未完成时，可以使用 **Early 模式** 提前遮盖。

生成器不会无差别放宽所有固定长度量词。
它只修改 rule 中 secret-value capture 内部的固定长度。
例如，最终要求32字符的 token，可以在输入到较短 prefix 时先匹配。
capture 外部用于上下文判断的量词保持不变。

Early 模式更容易产生 false positive，因此不会作为最终输出的默认模式。

## 保留有助于理解输出的上下文

regex 返回的整个范围不一定都需要消失。

普通 `key=value` 形式只遮盖 value，保留 key。
credential URI 可以分别遮盖 username 和 password，同时保留 scheme、separator 和 host。
home-directory 规则保留目录前缀，只遮盖用户相关部分。
Git identity 规则分别遮盖 display name 和 email local part，同时保留 email domain。

这些范围调整让读者仍能判断输出结构，同时避免把检测到的敏感值保留在 SVG 中。

## 把字符串范围映射回 terminal cell

renderer 会先把可见区域正规化为一个字符串。
宽字符 continuation cell 会按列映射需要处理，行尾空 cell 被去除。
如果下一物理行只是 terminal wrap 的继续，则中间不插入 newline。
这样，一个因为自动换行跨两行显示的 token 在 detector 输入中仍保持连续。

自动遮盖使用 **两阶段映射**。
第一阶段只构造正规化字符串并运行 QuickLeaks。
如果没有 finding，就不会分配逐字符坐标表。

只有发现 finding 后，第二阶段才重新构造正规化字符串，同时记录每个字符来自哪个 `(row, column)`。
随后把 UTF-16 finding 范围转换成 terminal cell 集合。

多数画面不包含秘密信息时，这种做法可以避免每次都为全文字符创建坐标条目。

## 从 SVG text 中移除原始值

被遮盖 cell 不会只依赖不透明矩形覆盖原文字。

对应文字会替换为 `*`，连续的 mask cell 还会绘制 stripe overlay。
SVG 是可检查的文本格式。
如果只覆盖视觉层，原始值仍可能通过 source inspection、copy 或 search 被取得。

替换不会改变 cell 数量。
后续文字的列位置保持与原终端一致。

## 让遮盖与动画复用保持兼容

自动和手动 mask scan 只在渲染 foreground 时执行。
只输出 background 的 pass 不会建立检测状态。

`FrameRenderWorkspace` 提供正规化文本使用的 `StringBuilder`，重复渲染行定义时可以复用 backing buffer。

存在手动 mask pattern 时会禁用行差分。
literal pattern 可能跨越不变 base 内容和变化 delta 内容。
拆分行后 matcher 将无法获得完整字符串，因此必须保留整行上下文。
