---
title: batch
description: 从 Markdown 的 c2s 标记批量生成图片的命令。
---

## `batch markdown`

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder]
```

执行 Markdown 或 MDX 中的 `c2s::` 标记，并生成或更新其后的图片链接。

实际生成成功后，会自动更新清单文件 `<output>/assets.json`。筛选生成会保留未选中 Markdown 所拥有的清单条目。dry run 和 placeholder 模式不会修改清单。

### `-i, --input <path>`

指定 Markdown 文件或目录（默认值：`docs`）。

### `-o, --output <dir>`

指定生成图片的输出目标（默认值：`assets`）。

### `--filter <glob>`

用 glob 按相对于输入目录的 filepath 进行筛选。
`*` 会跨目录分隔符匹配。可以多次指定。

### `--dry-run`

不修改文件，只显示计划执行的作业。

### `--placeholder`

不执行命令，只创建缺失的空资源和 Markdown 链接。不会更改现有资源。

### `--verbose [path]`

启用详细日志，并在需要时保存到文件。

## `batch restore`

```bash title="Terminal"
console2svg batch restore <source> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

指定已生成的清单文件，从中下载相关资源并放置到本地。
在文档站点发布时执行 `batch markdown` 后，指定该清单即可把生成结果复制到本地环境中。

### `<source>`

获取 `assets.json` 的来源：本地清单文件或其所在目录、提供原始清单的 HTTP(S) URL，或 `owner/repository@main/path/to/assets` 形式的 Git 来源。

### `-o, --output <dir>`

指定恢复图片的放置目录。必须指定。

### `--filter <glob>`

仅恢复匹配的逻辑资源路径及其所需的对象。可以多次指定。

### `--force`

即使本地大小和 SHA-256 已一致，也重新下载这些条目，而不是跳过。

### `--prune`

删除先前本地 `assets.json` 管理、但在恢复结果中已不存在的过期路径。无关文件和被 `--filter` 排除的条目不会被删除。

### `--dry-run`

不修改文件，只显示计划执行的内容。

标记的详细格式请参阅[同步文档和图片](../../automation/document-image-sync.mdx)。
