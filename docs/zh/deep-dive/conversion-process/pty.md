---
title: 启动 PTY、控制进程并获取输出
description: 创建具有终端行为的子进程，转发输入，合并输出，并在进程结束后完成录制。
---

仅重定向 standard output 不能复现真实终端行为。
program 可能根据是否连接到 TTY 改变颜色、buffering、进度显示和 full-screen UI。
console2svg 会在可用时使用 **PTY**，让子进程看到接近终端的输入输出环境。

## 把 OS PTY 实现放在库边界

当前录制层使用 `Porta.Pty` 创建和管理各 platform 的 PTY。
console2svg 在 backend 外围负责 process 配置、stream、终端尺寸、环境变量、输入转发、时间记录和关闭流程，而不在仓库中维护每个 OS 的 PTY API 实现。

Windows 侧使用 pseudoconsole 类似的字节流语义，文字和 VT control sequence 通过 stream 传递。
Unix 类系统使用常规 controller 和 terminal 两端的 PTY model。

子 process 会得到请求的列数和行数，同时环境变量 `COLUMNS` 和 `LINES` 也设置为相同值。
Windows command 通过 `cmd.exe` 执行，Unix 类系统通过 `/bin/sh` 执行。

Windows 最终需要一个 command-line 表示，因此参数会预先 quote。
`cmd.exe /c` payload 与普通 C runtime 参数的 quote 规则不同，会使用单独处理。

默认情况下，`CI` 和 `TF_BUILD` 等部分 CI 环境标记会从子 shell 环境中移除。
一些 library 即使运行在 TTY 上，也会因为这些变量禁用颜色或交互显示。
需要完整继承父环境时可以关闭该处理。

## 保留编码状态读取输出

PTY output reader 在整个 read loop 中复用一个 byte buffer、一个 char buffer 和一个 stateful decoder。
多字节 UTF-8 字符即使被 OS read 边界切开，也可以在下一次 read 继续解码。

需要把输出镜像到 byte stream 时，会直接转发原始 byte。
不会先 decode 再 encode，因此 forwarding 路径不会改变 VT sequence。
Windows 的 text forwarding 会在需要时临时使用 UTF-8 console output encoding。

用于录制的文字会和 elapsed time 一起加入 `RecordingSession`。
这里不会按行拆分。
carriage return、cursor move、erase 和 alternate screen 操作都需要由后续 terminal emulator 解释。

## 合并短时间内的小块输出

一次视觉更新可能经过多次很小的 PTY read 到达。
如果每次 read 都成为事件，ANSI parser 和 animation reducer 将处理许多没有视觉意义的边界。

console2svg 使用 **输出合并** 聚合相邻 chunk。
默认窗口是视频帧间隔的四分之一，并限制在 2 到 20 毫秒。
一个 batch 也不会超过一个帧间隔持续增长。

显式选项可以改变窗口或关闭合并。
合并后的事件时间使用该 batch 最后一个 chunk 的时间。

## 以 raw byte 转发交互输入

交互捕获会把 host input 切换到接近 **raw 输入** 的状态。
这样箭头键和 Ctrl 组合产生的 VT sequence 可以送给子 process，而不是先被 host 本地处理。

Unix 类系统在 standard input 被重定向时，会在可用情况下使用 `/dev/tty` 继续获得交互输入。

同时保存 replay 时，原始输入 byte 仍然先写入 PTY。
用于 replay model 的解释使用 UTF-8 decoder。
VT sequence 本身是 ASCII，避免 legacy console code page 把 ESC 解释成其他 encoding sequence 的一部分。

read 末尾如果只收到 CSI 等 escape sequence 的前半段，会把 remainder 带到下一次 read。
不会仅因为 stream read 结束，就把未完成 sequence 转成不相关的 key event。

## 避免 echo control byte 进入录制

host input 写入 PTY 后，slave echo 可能让相同 byte 再次出现在 output 中。
Unix 类系统的 `ECHOCTL` 还可能把 ESC 等 control character 显示成 caret notation。

live forwarding 时，console2svg 会尝试通过 PTY controller stream 关闭 slave 的 echo 相关 flag。
该操作依赖 backend 和 platform，因此按 best-effort 处理。
replay input 不需要相同的 host-keyboard echo 控制。

捕获结束时还会关闭 full-screen application 可能留下的 mouse tracking mode。
这一步用于恢复用户的 terminal session，不会修改已经记录的输出内容。

## 在 process 结束后 drain 剩余输出

子 process 退出时，已经写入 PTY 的 byte 不一定都被 parent 读取完成。
console2svg 会在 process exit 后给 output reader 最多500毫秒的 drain 时间。

PTY teardown 在各 platform 的表现并不完全相同。
与关闭 PTY 相符的已知 I/O error 会按 EOF 处理，使之前捕获的事件仍能正常结束录制。

cleanup 本身也有时间上限。
connection dispose 和 output reader shutdown 各自最多等待一秒。
backend 卡在关闭流程时，不会让 CLI 无限等待。

## PTY 无法使用时回退

PTY backend 启动后如果在限制时间内没有产生输出，会作为 startup hang 重试。
最多尝试三次，并在尝试之间短暂等待。

native backend 无法加载，或者重试后仍不能启动时，会回退到使用 redirected stream 的普通 process。

fallback 不能复现全部 TTY-dependent 行为。
它的作用是让 PTY 不可用的环境仍能执行非交互 command，而不是在启动阶段永久停止。
