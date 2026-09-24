---
title: batch
description: 从 Markdown 中的标记自动生成图片并同步文档与资产的命令。
---

`batch` 是一组子命令，用于扫描文档站点（如 Astro Starlight、Docusaurus、VitePress 等）或 README 的 Markdown/MDX 文件，根据正文中的捕获指令标记批量生成并同步图片。
它可以防止文档中的屏幕截图过时，并在 CI/CD 流水线中始终自动更新最新的执行结果图片。

## `batch markdown`

检测 Markdown 或 MDX 文件中编写的 `c2s::` 标记，执行指定命令，并生成或更新紧随其后的图片链接。

```bash title="Terminal"
console2svg batch markdown [options]
```

### 标记语法

以 Markdown 或 MDX 注释的形式编写运行选项和命令行。

```markdown
<!-- c2s:: -w 100 -h 10 -c -d macos -- fastfetch -->
<img src="/assets/fastfetch.svg" alt="fastfetch output" />
```

在 MDX 中也可以使用 JSX 注释语法：

```mdx
{/* c2s:: -w 100 -c -- git status */}
```

运行 `batch markdown` 后，紧随标记后的图片元素（`<img>` 标签或 `![]()` 语法）的链接 URL 会自动更新为生成的图片文件。
此外，输出目录中会自动生成资产清单文件（`assets.json`），记录命令及输出文件的哈希值。

### 选项

* `-i, --input <path>`：指定目标 Markdown 文件或文档目录的根路径（默认值：`docs`）。
* `-o, --output <dir>`：指定保存生成图片文件的输出目录（默认值：`assets`）。
* `--link-base <path>`：指定插入 Markdown 文件的图片链接的公开 URL 前缀（例如：指定 `--link-base /assets` 时，Markdown 中的链接将变为 `/assets/filename.svg`）。
* `--filter <glob>`：按相对于输入目录的路径使用 glob 模式筛选要处理的文件（可多次指定）。
* `--dry-run`：不生成或重写文件，仅显示计划执行的任务列表。
* `--placeholder`：不实际执行命令，仅创建缺失的空资产文件并插入链接（不修改现有资产）。
* `--verbose [path]`：启用详细日志，并可在需要时保存到指定文件。

## `batch restore`

参考在远程环境或其他分支中已生成的清单文件（`assets.json`），批量下载并恢复对应的图片资产至本地环境。

```bash title="Terminal"
console2svg batch restore <source> --output <dir> [options]
```

适用于将 CI 环境中通过 `batch markdown` 生成并发布的图片快速同步到本地开发环境或部署构建环境。

### 参数与选项

* `<source>`（必选）：清单文件（`assets.json`）的获取来源。除本地文件路径外，还可以指定 HTTP/HTTPS URL 或 Git 仓库 URL。
* `-o, --output <dir>`（必选）：指定恢复和放置图片文件的目录。
* `--filter <glob>`：按清单中的路径使用 glob 模式筛选要恢复的文件。
* `--force`：即使本地与远程的内容哈希（SHA）一致，也不跳过，强制重新获取。
* `--prune`：批量删除未在清单中记录、仅存在于本地输出目录中的多余图片文件。
* `--dry-run`：不实际下载或删除，仅显示计划执行的变更内容。
