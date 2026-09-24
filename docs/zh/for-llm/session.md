---
title: LLM 终端自动化交互
description: 通过后台会话守护进程，实现面向 LLM 的交互式终端操作与视觉反馈循环。
since: v0.11
---

正如 Playwright 在 Web 自动化测试中所起的作用一样，LLM 要操作 TUI（文本用户界面），同样需要一个反馈闭环：观察终端当前的视觉状态、发送按键，并评估由此引发的状态变动。
然而，常规的 CLI 执行方式在命令初次输出完成后便立即退出，无法交互式地操作诸如 vim、fzf 或交互式安装向导这类具备持续状态的应用程序。

`console2svg session` 通过在后台常驻守护进程来保持伪终端（PTY）会话，为 LLM 提供了一套基于 JSON 输入输出的编程式终端控制接口。
LLM 可以将屏幕文本缓冲区和渲染出的 SVG 快照作为“眼睛”，自主开展试错闭环，完成 TUI 布局微调与行为验证。

## 智能体前置配置

要让 LLM 智能体理解终端操作流程，请将预置的智能体指令书 [SKILL.md](./use-skill.md) 载入系统提示词中。
智能体将遵循该指南调用下述基于 JSON 的子命令。

所有 `console2svg session` 子命令均以 JSON 格式向标准输出返回响应。
这样可以确保 LLM 能够直接将执行状态和界面内容作为结构化数据进行精确判定，而无需依赖脆弱的正则表达式。

## 交互操作基本流程

LLM 操作会话的标准工作流分为 5 个阶段：启动、调屏、观测、输入、终止。

### 1. 启动会话

指定目标命令启动后台会话：

```bash title="Terminal"
console2svg session start -- bash
```

启动成功后，命令将返回包含唯一 `sessionId` 的 JSON 对象。
后续的所有交互操作均以此 ID 作为目标标识：

```json title="输出示例"
{
  "sessionId": "s_a1b2c3d4e5f6",
  "state": "running",
  "command": "bash",
  "width": 80,
  "height": 24,
  "createdAt": "2025-01-15T10:00:00Z"
}
```

### 2. 调整终端尺寸

多数 TUI 程序会根据终端的列数和行数动态改变排版。
可使用 `session resize` 根据需求调整界面尺寸：

```bash title="Terminal"
console2svg session resize s_a1b2c3d4e5f6 --width 120 --height 30
```

### 3. 观测终端状态

为确认应用程序对上一步输入的响应情况，可读取屏幕文本内容或捕获 SVG 图像：

```bash title="Terminal"
# 等待输出流更新最多 1 秒并读取文本缓冲区
console2svg session read s_a1b2c3d4e5f6 --wait 1s

# 将当前屏幕渲染结果保存为 SVG 图像
console2svg session capture s_a1b2c3d4e5f6 -o /tmp/current-screen.svg
```

`session read` 返回屏幕纯文本及光标坐标，而 `session capture` 则导出包含颜色、字体样式的真实几何排版。
多模态 LLM 可直接读取生成的 SVG 图片，分析是否存在布局变形或色彩对比度异常。
`session list` 仅显示正在启动或运行中的会话。进程已退出的会话不会显示在列表中，但在保留期内仍可通过 ID 读取或捕获。使用 `session stop` 显式停止的会话会被删除，之后无法再通过 ID 访问。

### 4. 发送按键输入

确认当前状态后，向终端发送下一步的按键序列：

```bash title="Terminal"
# 发送普通字符串
console2svg session send s_a1b2c3d4e5f6 --text "git status"

# 发送 Enter 或方向键等特殊功能键
console2svg session send s_a1b2c3d4e5f6 --keys Enter

# 按指定顺序连续发送文本和按键
console2svg session send s_a1b2c3d4e5f6 --text "i" --keys Enter --text "hello" --keys Esc
```

`--text` 和 `--keys` 可以重复指定，输入会按指定顺序发送。
发送完毕后，可再次调用 `session read` 检查界面是否如预期发生状态迁移。

### 5. 终止会话

全部交互流程结束后，应显式关闭后台常驻进程：

```bash title="Terminal"
# 终止指定会话
console2svg session stop s_a1b2c3d4e5f6

# 批量终止所有运行中的会话
console2svg session stop --all --yes
```

终止会话后，守护进程将关闭 PTY、清理关联子进程，并释放 Unix 域套接字文件。
