---
title: llm
description: AI エージェント向けに最適化された組み込み Agent Skill 文書を出力するコマンド。
---

```bash title="Terminal"
console2svg llm skills
```

`llm` は、GitHub Copilot や Claude Code、Cursor などの AI コーディングエージェントに対して、console2svg の正確な操作手順やベストプラクティスを指示するための **Agent Skill**（エージェント向け指示書）を出力するサブコマンドです。

## コマンドの動作

`console2svg llm skills` を実行すると、バイナリに内蔵された Skill ドキュメント（Markdown 形式）が標準出力へ出力されます。
進捗メッセージや ANSI カラー装飾などは一切含まれないため、リダイレクト機能を使ってエージェント用の設定ファイルへ直接書き出すことができます。

```bash title="Terminal"
console2svg llm skills > .github/skills/console2svg/SKILL.md
```

## Agent Skill に含まれる指示内容

出力される Skill 文書には、人間向けの解説とは異なり、LLM（大規模言語モデル）がタスクを自律遂行する際に必要な以下の実践的ルールが体系化されています。

* **JSON モードの活用**: `--json` オプションを指定して `screen.text` や生成アーティファクトのパスをプログラム的に取得する手順
* **TUI 操作の委譲**: 対話型コマンドに対して `session` サブコマンド（`start`、`read`、`send`、`stop`）を用いて非同期に入出力を行う制御フロー
* **シークレット保護の推奨**: 自動マスキング機能（QuickLeaks）の利用原則と誤検出時の対処法
* **動画プレビューの確認**: 動画生成時に自動サンプリングされる SVG フレームプレビューの参照方法

この Skill をエージェントのコンテキストへ組み込むことで、LLM が誤ったオプションを指定したり、TUI ツールで入力待ちのままハングしたりする事故を防ぐことができます。
