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
失败时请根据 `error.code` 分支；`error.message` 和标准错误诊断面向用户，文字可能调整。

## 交互操作基本流程

智能体操作会话的流程包含七个阶段：启动、尺寸调整、检查与观测、条件等待与输入发送、成果物保存、导出场景、终止会话。

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

### 3. 屏幕检查与观测（read / inspect）

为掌握当前屏幕状态，可读取屏幕文本内容或进行临时目视检查：

```bash title="Terminal"
# 立即读取当前屏幕纯文本
console2svg session read s_a1b2c3d4e5f6

# 读取包含单元格样式、超链接与宽字符信息的结构化数据
console2svg session read s_a1b2c3d4e5f6 --structured

# 生成用于目视检查的临时 SVG（输出路径自动分配）
console2svg session inspect s_a1b2c3d4e5f6
```

#### inspect 与 read 的定位（仅供探索与诊断）

`session read` 和 `session inspect` 是供智能体掌握屏幕状态的 **观测**（Observation）操作。
它们属于 **仅供检查** 的临时探索与诊断手段，而非持久的操作步骤（Action）。

* `session read` 会立即返回视口文本、光标坐标及备用屏幕状态。
* `session inspect` 将终端渲染输出到用户隔离的临时目录下的 SVG 文件并返回路径，无需调用者指定持久输出文件名。
* `read` 与 `inspect` 仅用于检查，因此在导出场景（Scenario）时会自动被排除。

#### 智能体设计原则：将观测转化为条件

智能体在自主推进操作时，应遵循一条关键原则：
如果根据 `read` 或 `inspect` 观察到的信息来决定下一步输入，必须在执行操作（Action）前，将该判断依据以 `session wait` 等 **条件**（Condition）的形式显式声明。

例如，当通过 `read` 观察到界面出现 `Overwrite? [y/N]` 时，不要直接发送 `send --text "y"`。
应当按如下方式先进行条件等待，再发送输入：

```bash title="Terminal"
# 1. 读取屏幕进行检查（Observation）
console2svg session read s_a1b2c3d4e5f6

# 2. 将依赖关系具象化为条件等待（Condition）
console2svg session wait s_a1b2c3d4e5f6 --text "Overwrite? [y/N]"

# 3. 执行下一步操作（Action）
console2svg session send s_a1b2c3d4e5f6 --text "y" --keys Enter
```

通过显式声明条件依赖，在导出场景时即可生成确定且可复现的高质量自动化用例。

### 4. 条件等待与输入发送（wait / send）

#### 条件等待（wait）

等待文本出现，或等待曾经出现的文本消失：

```bash title="Terminal"
# 等待指定文本出现
console2svg session wait s_a1b2c3d4e5f6 --text "Ready"

# 等待文本消失并保持 2 秒稳定状态
console2svg session wait s_a1b2c3d4e5f6 --text "Working" --until absent --stable-for 2s
```

#### 发送输入（send）

确认状态后，向终端发送按键序列：

```bash title="Terminal"
# 发送普通文本
console2svg session send s_a1b2c3d4e5f6 --text "git status"

# 发送特殊命名键
console2svg session send s_a1b2c3d4e5f6 --keys Enter

# 按顺序原子化发送文本、粘贴内容与按键
console2svg session send s_a1b2c3d4e5f6 --text "i" --keys Enter --paste "hello" --keys Esc
```

`--text`、`--paste`、`--keys` 和 `--raw-hex` 可多次指定，并按参数顺序执行。

### 5. 保存持久成果物（capture）

与仅用于临时目视检查的 `inspect` 不同，若需要生成并保留图片成果，请使用 `session capture`：

```bash title="Terminal"
console2svg session capture s_a1b2c3d4e5f6 -o docs/assets/status.svg -d macos -t dracula
```

`capture` 被视为生成成果物的持久 **操作**（Action），因此会被包含在场景导出中。

### 6. 导出为场景文件（export）

智能体交互探索成功的一系列操作路径，可通过 `session export` 导出为可直接运行的 **场景文档**（ScenarioDocument，YAML 格式）：

```bash title="Terminal"
console2svg session export s_a1b2c3d4e5f6 -o tests/scenarios/setup.yaml
```

导出功能会提取会话中的 `send`（输入）、`wait`（条件）、`resize` 与 `capture` 操作，同时自动剔除探索性的 `read` 与 `inspect` 观测。
生成的场景文件可通过 `console2svg scenario run` 重新运行：

```bash title="Terminal"
console2svg scenario run tests/scenarios/setup.yaml
```

这样即可将智能体探索出的交互路径保存为自动化脚本，后续在 CI 与回归测试中直接复用，无需智能体或人工再次介入。

### 7. 终止会话（stop）

全部交互流程结束后，显式关闭后台会话进程：

```bash title="Terminal"
# 终止指定会话
console2svg session stop s_a1b2c3d4e5f6

# 批量终止所有运行中的会话
console2svg session stop --all --yes
```

终止会话后，守护进程将关闭 PTY、清理关联子进程，并释放 Unix 域套接字文件。
可使用 `session list`（或 `session list --all`）查看活跃及保留的会话。
