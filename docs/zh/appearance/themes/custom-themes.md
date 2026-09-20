---
title: 使用自定义主题
description: 外部主题的安装、更新和删除方法。
since: v0.10
---

除了内置主题，也可以使用本地创建的主题，或在 Git 仓库中公开的主题。

## 安装主题

```bash title="Terminal"
# GitHub 仓库 URL（没有 Git 的环境会自动获取归档）
console2svg theme install owner/my-theme
console2svg theme install owner/my-theme2@branch/path/to/theme

# Git 仓库（需要 git）
console2svg theme install https://my-git.example/themes/my-theme.git
```

已安装的主题可以像内置主题一样应用。

```bash title="Terminal" "-t my-theme"
console2svg capture --theme my-theme -- fastfetch
```

## 管理主题

```bash title="Terminal"
# 列表（也显示内置主题）
console2svg theme list

# 更新指定主题
console2svg theme update my-theme

# 更新所有已安装主题
console2svg theme update

# 卸载
console2svg theme remove my-theme
```

## 相关页面

* [使用内置主题](./built-in-themes.md)
* [内置主题概览](./built-in-theme-list.mdx)
* [创建自定义主题](./create-custom-theme.mdx)
