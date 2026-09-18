---
title: batch markdown
description: 从 Markdown 的 c2s 标记批量生成图片的命令。
---

```bash
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run]
```

执行 Markdown 或 MDX 中的 `c2s::` 标记，并生成或更新其后的图片链接。

## 选项

### `-i, --input <path>`

指定 Markdown 文件或目录（默认值：`docs`）。

### `-o, --output <dir>`

指定生成图片的输出目标（默认值：`assets`）。

### `--filter <glob>`

用 glob 按相对于输入目录的 filepath 进行筛选。`*` 会跨目录分隔符匹配。可以多次指定。

### `--dry-run`

不修改文件，只显示计划执行的作业。

### `--verbose [path]`

启用详细日志，并在需要时保存到文件。

标记的详细格式请参阅[同步文档和图片](../../automation/document-image-sync.md)。
