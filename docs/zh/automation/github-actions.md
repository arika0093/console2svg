---
title: 在 GitHub Actions 中使用
description: 用于在 CI 流水线中自动生成并提交 CLI 截图的工作流示例。
---

通过与 GitHub Actions 组合使用，可以在仓库更新或发布时自动更新 README 图片。

## GitHub Actions

也可以使用便于 CI 使用的 GitHub Action。若要使用最新版本的 `console2svg`，只需在工作流中添加以下步骤：

```yaml title=".github/workflows/console2svg.yml"
- uses: arika0093/console2svg@main
```

如需指定 console2svg 的版本，请按如下方式指定。

```diff lang="yaml" title=".github/workflows/console2svg.yml"
 - uses: arika0093/console2svg@main
+  with:
+    version: 0.8.3
     # 从源代码构建
     # version: develop
```

> [!NOTE]
> 本文档站点的图片会使用上述 Actions 自动生成。


## 工作流示例
### 单次生成

以下是在 Ubuntu runner 上安装 console2svg、生成 SVG，并提交到 Git 仓库的工作流示例。

```yaml title=".github/workflows/console2svg.yml" {4-5,10-14}
jobs:
  gen:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup console2svg
        uses: arika0093/console2svg@main

      - name: Generate SVG
        run: console2svg capture -w 120 -c -d macos-pc -o output.svg -- dotnet --version

      - name: Commit Changes
        uses: stefanzweifel/git-auto-commit-action@v7
        with:
          commit_message: "[skip-ci] chore: regenerate SVG"
```

### 自动同步

使用 [batch markdown](./document-image-sync.md) 功能，也可以自动更新文档。
以下是检测 Markdown 中的标记、生成图片并提交的工作流示例。

```yaml title=".github/workflows/console2svg.yml" {4-5,10-14}
jobs:
  sync:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup console2svg
        uses: arika0093/console2svg@main

      - name: Generate SVGs from Markdown
        run: console2svg batch markdown -i ./docs -o ./assets

      - name: Commit Changes
        uses: stefanzweifel/git-auto-commit-action@v7
        with:
          commit_message: "[skip-ci] chore: regenerate SVGs"
```

> [!WARNING]
> 由于可能执行嵌入在 Markdown 中的恶意代码，请格外注意执行时机。
> 特别是在外部 PR 或分支合并时自动执行的情况下，请确保只执行可信代码。

## 自动覆盖环境变量

有些库（例如 [chalk](https://www.npmjs.com/package/chalk)）会检测 CI 环境，并自动禁用彩色输出。
但是，生成 SVG 时通常应该始终启用颜色。因此，console2svg 默认会自动设置和删除以下环境变量：

* `TERM=xterm-256color`：设置此变量以启用颜色支持。
* `COLORTERM=truecolor`：启用 TrueColor 输出。
* `FORCE_COLOR=3`：同上。`3` 表示 TrueColor。
* `CI`（删除）：因为库检测到 CI 环境时会禁用颜色，所以会有意删除。
* `TF_BUILD`（删除）：出于同样原因删除（Azure Pipelines 使用）。

若要禁用此行为，请使用 `--no-colorenv` 和 `--no-delete-envs` 选项。
