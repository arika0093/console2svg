---
title: 組み込みテーマを使う
description: console2svg に標準搭載されているテーマの適用方法。
---

console2svg には、すぐに使える組み込みテーマが含まれています。テーマIDを
`-t` または `--theme` に渡すだけで、ターミナルの配色やウィンドウの見た目を変更できます。

## テーマを適用する

```bash title="Terminal" "-t nord"
console2svg capture -w 100 -h 24 -c -t nord -- console2svg
```

<!-- c2s:: -w 100 -h 24 -c -t nord -- console2svg -->
![console2svg capture with a built-in theme](../../../assets/cmd-theme.svg)

テーマIDは [組み込みテーマの一覧](./built-in-theme-list.mdx) で確認できます。`theme list` では、
組み込みテーマとインストール済みテーマをまとめて確認できます。

```bash title="Terminal" "--format markdown"
console2svg theme list
console2svg theme list --format markdown
```

`-t` は通常のキャプチャだけでなく、`replay` や `interactive` などテーマを受け取る
コマンドでも使用できます。

```bash title="Terminal" "-t nord" "-t cyberpunk-pc"
console2svg replay ./session.json -t nord -- bash
console2svg interactive -t cyberpunk-pc -o capture.svg
```

> [!NOTE]
> [組み込みウインドウ枠](./built-in-window-themes.mdx)もテーマの一部として定義されています。
> そのため、`-t macos`のような指定も機能します。
