---
title: llm
description: Command to output built-in Agent Skill documentation optimized for AI agents.
---

```bash title="Terminal"
console2svg llm skills
```

`llm` is a subcommand that prints the **Agent Skill** (instructions for AI agents) guiding AI coding agents—such as GitHub Copilot, Claude Code, and Cursor—on precise operating procedures and best practices for console2svg.

## Command Behavior

Running `console2svg llm skills` outputs the Skill document (in Markdown format) embedded in the binary to standard output.
Because it contains no progress messages or ANSI color codes, you can redirect the output directly into agent configuration files.

```bash title="Terminal"
console2svg llm skills > .github/skills/console2svg/SKILL.md
```

## Content Included in the Agent Skill

Unlike documentation written for human readers, the generated Skill document systematizes the following practical rules necessary for LLMs (Large Language Models) to perform tasks autonomously:

* **Leveraging JSON mode**: Procedures for programmatically retrieving `screen.text` and generated artifact paths using the `--json` option
* **Delegating TUI operations**: Control flows using `session` subcommands (`start`, `read`, `send`, `stop`) for asynchronous input/output with interactive commands
* **Secret protection recommendations**: Usage guidelines for automatic masking (QuickLeaks) and resolving false positives
* **Inspecting video previews**: How to inspect sampled SVG frame previews generated during video recording

Integrating this Skill into an agent's context prevents LLMs from passing invalid options or hanging indefinitely on interactive TUI inputs.
