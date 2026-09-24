---
title: llm
description: console2svgの組み込みAgent Skillを出力するコマンド。
---

```bash title="Terminal"
console2svg llm skills
```

組み込みの `console2svg` Agent Skillを標準出力へ書き出します。エージェントで
利用するには、対応するSkillディレクトリへリダイレクトします。

```bash
console2svg llm skills > path/to/skills/console2svg/SKILL.md
```

このコマンドは、進捗メッセージやターミナル装飾を含めず、Skill文書だけを
出力します。リポジトリのSkillを直接検出するSkill管理ツール向けに、
`skills/console2svg/SKILL.md` にも同じ内容を配置しています。
