---
title: SKILL.md
description: Teach an LLM how to use console2svg.
since: v0.11
---

`console2svg` provides a general-purpose [`SKILL.md` for agents](https://agentskills.io/home).

## Usage

### Using `npx skills`

`SKILL.md` is available in the repository's [`/skills` directory](https://github.com/arika0093/console2svg/tree/main/skills).

```bash Title="Terminal"
npx skills add arika0093/console2svg
```

### Generating it yourself

Use the built-in [`llm skills`](../reference/cli/llm.md) command to generate or update `SKILL.md`.

```bash Title="Terminal"
console2svg llm skills > $(YOUR_SKILLS_DIR)/console2svg/SKILL.md
```
