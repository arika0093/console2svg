---
title: SKILL.md
description: console2svgの使用方法をLLMに教える
since: v0.11
---

`console2svg`は、一般的な[エージェント向けの`SKILL.md`](https://agentskills.io/home)を提供しています。

## 利用方法

### `npx skills`を使用する場合

[レポジトリの`/skills`](https://github.com/arika0093/console2svg/tree/main/skills)上で`SKILL.md`を提供しています。

```bash Title="Terminal"
npx skills add arika0093/console2svg
```

### 自己生成する場合

組み込みの[`llm skills`](../reference/cli/llm.md)コマンドを使用して、`SKILL.md`を生成/更新できます。

```bash Title="Terminal"
console2svg llm skills > $(YOUR_SKILLS_DIR)/console2svg/SKILL.md
```
