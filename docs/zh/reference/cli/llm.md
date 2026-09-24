---
title: llm
description: 输出 console2svg 内置的 Agent Skill。
---

```bash title="Terminal"
console2svg llm skills
```

将内置的 `console2svg` Agent Skill 写入标准输出。可将输出重定向到受支持的
Skill 目录，以便在 Agent 中使用：

```bash
console2svg llm skills > path/to/skills/console2svg/SKILL.md
```

此命令只输出 Skill 文档，不包含进度消息或终端样式。对于可直接发现仓库内
Skill 的管理工具，仓库也在 `skills/console2svg/SKILL.md` 中提供相同内容。
