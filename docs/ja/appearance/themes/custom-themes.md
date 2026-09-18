---
title: カスタムテーマを使う
description: 外部テーマのインストール・更新・削除方法。
---

組み込みテーマに加えて、ローカルで作ったテーマや、Gitリポジトリで公開されたテーマも利用できます。

## テーマをインストールする

```bash title="Terminal"
# GitHubリポジトリのURL（Gitがない環境では自動的にアーカイブを取得）
console2svg theme install owner/my-theme
console2svg theme install owner/my-theme2@branch/path/to/theme

# Gitリポジトリ (要 git)
console2svg theme install https://my-git.example/themes/my-theme.git
```

インストールしたテーマは、組み込みテーマと同じように適用できます。

```bash title="Terminal" "-t my-theme"
console2svg capture -t my-theme -- fastfetch
```

## テーマを管理する

```bash title="Terminal"
# 一覧（組み込みテーマも表示）
console2svg theme list

# 指定テーマを更新
console2svg theme update my-theme

# すべてのインストール済みテーマを更新
console2svg theme update

# アンインストール
console2svg theme remove my-theme
```

## 関連ページ

* [組み込みテーマを使う](./built-in-themes.md)
* [組み込みテーマの一覧](./built-in-theme-list.mdx)
* [カスタムテーマを作る](./create-custom-theme.mdx)
