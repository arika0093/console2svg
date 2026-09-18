---
title: 记录和播放输入 replay
description: 如何把交互输入正规化为带时间的 key event，并在播放时转换回 VT byte sequence 送入 PTY。
---

replay 保存的是输入操作，不是 terminal output。
播放时会重新在 PTY 中启动 command，并在记录的时刻发送这些操作，再重新捕获因此产生的 terminal output。

文件保存跨 platform 的 key 含义，而不是 Windows console event 或 Unix input structure。

## 正规化 replay 时间

第一个 input event 可以带有从录制开始计算的绝对 `time`。
后续 event 可以通过 `tick` 保存相对前一个 event 的时间差。

读取时，有 `time` 的 event 优先使用绝对值。
只有 `tick` 的 event 会累加到前一个时间，最终转换为统一的 absolute timeline。

metadata 还保存 format、application 信息和 total duration。
录制结束边界不能只由最后一次 key 输入推导，因为输入结束后子 process 可能继续停留在 prompt。

播放会把 total duration 加1秒作为上限。
超过该时间仍未完成时会报告 timeout。

## 不让 live input 的 read 边界破坏 VT sequence

同时保存 replay 时，forwarding path 仍会先把原始 byte 写入 PTY。

用于 replay model 的解释使用 UTF-8 decoder。
VT key sequence 是 ASCII。
如果让 legacy console code page 解释 ESC，在某些 Windows encoding 中可能会消费或重新解释后续 byte。

stream read 可能在 CSI、SS3、OSC、DCS、APC、PM 或 SOS 中间结束。
parser 会检测末尾未完成的 escape sequence，并把 remainder 带到下一块输入。

terminal protocol control string 不一定是用户主动输入的 key。
这类 terminal traffic 不会被当作普通用户 replay event 保存。

## 保存 key 的语义而不是 host key code

常见 key 被正规化为 `ArrowUp`、`Home`、`Delete`、`F1` 到 `F12` 等名称。
Shift、Alt、Ctrl、Meta 作为独立 modifier 保存。

printable Unicode 保存为文字，而不是 platform key code。
surrogate pair 保持为一个 logical key value。

普通 key model 无法表达的输入可以使用 `raw` event。

## 把 replay event 还原为 VT byte

播放时，key name 和 modifier 会转换成 terminal application 期待的 VT sequence。

Enter、Tab、arrow、navigation key 和 function key 使用相应 escape sequence。
Ctrl 加 alphabet 转换为 control byte。
Alt 可通过 ESC prefix 表示，printable text 编码为 UTF-8。

这些 byte 会写入与 live input 相同的 PTY writer。
keyboard 和 replay 因此共享相同的 output-capture terminal path。

## 按时间发送而不改变顺序

replay stream 会先准备 event 的 byte 表示，并在 consumer read 时等待目标 event 的计划时间。

如果处理已经落后于计划时间，不会再增加额外 delay。
stream 会保持原始顺序并继续追赶。

单个 event 可能大于 consumer 的 read buffer。
stream 会保存当前 event 内的 offset，通过多次 read 返回完整数据。
stream boundary 的分割不会丢失剩余输入。
