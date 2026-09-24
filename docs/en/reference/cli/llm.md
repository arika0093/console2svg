---
title: llm
description: Print the bundled Agent Skill for console2svg.
---

```bash title="Terminal"
console2svg llm skills
```

Writes the bundled `console2svg` Agent Skill to standard output. Redirect the
output to a supported skill directory to use it with an agent:

```bash
console2svg llm skills > path/to/skills/console2svg/SKILL.md
```

The command prints only the Skill document, without progress messages or
terminal styling. The Skill is also available in the repository under
`skills/console2svg/SKILL.md` for skill managers that discover repository
skills directly.
