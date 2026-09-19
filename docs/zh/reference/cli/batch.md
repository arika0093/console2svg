---
title: batch
description: 从 Markdown 的 c2s 标记批量生成图片的命令。
---

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder] [--manifest <path>]
console2svg batch restore --input <path-or-url> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
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

### `--placeholder`

不执行命令，只创建缺失的空资源并更新 Markdown 链接。不会替换现有资源。

### `--manifest <path>`

生成成功后写入包含 SHA-256、大小、媒体类型和逻辑别名的 JSON 清单。

### `--verbose [path]`

启用详细日志，并在需要时保存到文件。

## `batch restore`

从本地文件或 HTTP(S) URL 清单恢复资源。必须指定 `-i, --input` 和 `-o, --output`。支持 `--filter`、`--force`、`--prune` 和 `--dry-run`。下载内容会通过大小和 SHA-256 验证，并重新创建逻辑别名。

标记的详细格式请参阅[同步文档和图片](../../automation/document-image-sync.md)。
