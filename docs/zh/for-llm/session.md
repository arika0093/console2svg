---
title: 让 LLM 操作终端
description: 使用 console2svg 让 LLM 捕获并检查命令执行结果。
since: v0.11
---

LLM 可以使用结构化的终端操作功能。

## 动机

提供类似终端版 [`playwright-cli`](https://github.com/microsoft/playwright-cli) 的工具，为 AI 提供一双“眼睛”。

这样，面对改善界面外观等视觉需求时，LLM 可以自主操作终端，并通过反馈循环检查执行结果。

## 使用方法

将 [SKILL.md](./use-skill.md) 加载到 LLM 中。

## 概览

使用 [console2svg session](../reference/cli/session.md) 让 LLM 操作终端。

### 启动会话

启动后会返回会话 ID。

```bash title="Terminal"
# 启动会话
console2svg session start -- bash
# > {"sessionId":"s_randomhash1234","state":"running", ...}
```

### 调整尺寸

某些 TUI 依赖启动时的终端尺寸。可根据需要调整尺寸。

```bash title="Terminal"
console2svg session resize s_randomhash1234 --width 160 --height 40
```

### 查看屏幕

读取屏幕文本或捕获图像，以检查当前状态。

```bash title="Terminal"
# 获取终端文本
console2svg session read s_randomhash1234 --wait 1s
# 捕获图像
console2svg session capture s_randomhash1234 -o /tmp/current-screen.svg
```

### 发送输入

发送输入以操作 TUI。

```bash title="Terminal"
# 发送文本
console2svg session send s_randomhash1234 --text "search query"
# 发送特殊按键
console2svg session send s_randomhash1234 --keys Enter
```

### 停止会话

```bash title="Terminal"
# 停止一个会话
console2svg session stop s_randomhash1234
# 停止所有会话
console2svg session stop --all --yes
```
