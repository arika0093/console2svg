---
title: theme
description: 管理已安装主题的命令。
---

```bash
console2svg theme list
console2svg theme install <source>
console2svg theme remove <id>
console2svg theme update [id]
```

`list` 显示主题列表，`install` 从目录、归档或 URL 添加主题，`remove` 删除主题，`update` 更新一个或全部主题。

## 子命令和参数

### `list`

列出已安装主题。

### `--format <table|markdown|json>`

选择 `list` 的输出格式。

### `install <source>`

从目录、归档或 URL 添加主题。

### `remove <id>`

删除指定主题 ID 的主题。

### `update [id]`

更新指定主题；省略主题 ID 时更新所有主题。
