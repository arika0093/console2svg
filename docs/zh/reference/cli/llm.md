---
title: llm
description: 输出专为 AI 智能体优化的内置 Agent Skill 文档的命令。
---

```bash title="Terminal"
console2svg llm skills
```

`llm` 是一个子命令，用于输出指导 GitHub Copilot、Claude Code、Cursor 等 AI 编程智能体准确操作 console2svg 的**Agent Skill**（面向智能体的指令文档）与最佳实践。

## 命令行为

运行 `console2svg llm skills` 会将二进制内置的 Skill 文档（Markdown 格式）输出到标准输出。
输出内容完全不包含进度信息或 ANSI 颜色转义字符，因此可以直接使用重定向功能写入智能体配置文件。

```bash title="Terminal"
console2svg llm skills > .github/skills/console2svg/SKILL.md
```

## Agent Skill 包含的指导内容

与面向人类读者的说明文档不同，输出的 Skill 文档系统化整理了 LLM（大语言模型）自主执行任务所需的以下实用规则：

* **使用 JSON 模式**：通过指定 `--json` 选项以程序化方式获取 `screen.text` 和生成产物路径的流程
* **委托 TUI 操作**：针对交互式命令使用 `session` 子命令（`start`、`read`、`send`、`stop`）执行异步输入输出的控制流程
* **机密保护建议**：自动掩码功能（QuickLeaks）的使用原则及误报处理方法
* **检查视频预览**：生成视频时自动采样的静态 SVG 预览帧的引用方法

将此 Skill 纳入智能体的上下文，可以避免 LLM 传递错误选项或在 TUI 工具中等待输入导致挂起。
