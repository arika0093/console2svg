---
title: SKILL.md
description: 教会 LLM 如何使用 console2svg。
since: v0.11
---

`console2svg` 提供通用的[面向 Agent 的 `SKILL.md`](https://agentskills.io/home)。

## 使用方法

### 使用 `npx skills`

仓库的 [`/skills` 目录](https://github.com/arika0093/console2svg/tree/main/skills)中提供了 `SKILL.md`。

```bash Title="Terminal"
npx skills add arika0093/console2svg
```

### 自行生成

使用内置的 [`llm skills`](../reference/cli/llm.md) 命令生成或更新 `SKILL.md`。

```bash Title="Terminal"
console2svg llm skills > $(YOUR_SKILLS_DIR)/console2svg/SKILL.md
```
