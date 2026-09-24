---
title: theme
description: 查看内置主题以及安装、更新和删除自定义主题的命令。
---

```bash title="Terminal"
console2svg theme list [--format table|markdown|json]
console2svg theme install <source>
console2svg theme remove <id>
console2svg theme update [id]
```

`theme` 是一组子命令，用于管理定义终端文字颜色、背景颜色与调色板配色的主题。
虽然 console2svg 随附了大量内置主题，但你可以使用该命令从外部源（目录、ZIP 归档、Git 仓库）添加和管理自定义主题。

## 子命令列表

### `list`

显示已安装的主题列表。
内置主题与用户添加的自定义主题将分别展示。

```bash title="Terminal"
console2svg theme list
```

#### `--format <table|markdown|json>`

指定列表的输出格式。默认值为 `table`（适合终端显示的表格格式）。
若要在脚本中处理列表可选择 `json`，若要转录到文档中可选择 `markdown`。

### `install <source>`

安装新的自定义主题。
参数 `<source>` 支持包含主题文件的本地目录路径、ZIP 归档路径，或公开的主题 URL（如 GitHub 仓库）。

```bash title="Terminal"
console2svg theme install https://github.com/example/my-custom-theme
```

### `remove <id>`

从系统中删除指定 ID 的自定义主题。
需要注意的是，二进制中随附的内置主题无法删除。

```bash title="Terminal"
console2svg theme remove my-custom-theme
```

### `update [id]`

将已安装的自定义主题更新至最新版本。
在参数 `[id]` 中传入特定主题 ID 时仅更新该主题；省略该参数时则批量更新所有已安装的自定义主题。

```bash title="Terminal"
console2svg theme update
```
