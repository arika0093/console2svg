---
title: update
description: 检查 console2svg 的最新版本并自动更新二进制文件的命令。
---

```bash title="Terminal"
console2svg update [options]
```

`update` 是一个子命令，用于检查 GitHub 上的最新发布版本，并将正在运行的 console2svg 自动更新至最新版本（自更新）。
无需手动下载归档并重新解压部署，即可获取最新功能和缺陷修复。

## 命令行为

执行该命令时，将请求 GitHub Releases API 以检查最新版本。
如果存在新版本，会自动下载与当前操作系统及 CPU 架构匹配的发布归档，并替换可执行文件。

需要说明的是，如果通过 Homebrew 或 Scoop 等包管理器安装，为了保持包管理器管理的完整性，默认会阻止自更新并提示使用包管理器进行更新。

## 选项

### `--check`

仅检查是否有可用的新版本，不执行实际的下载或更新。适用于对比当前版本与最新版本。

```bash title="Terminal"
console2svg update --check
```

### `-y, --yes`

跳过更新执行前的确认提示（`Do you want to update? [y/N]`），自动推进更新流程。适用于 CI 脚本或无人值守执行。

### `-f, --force`

即使在由包管理器（如 Homebrew、Scoop 等）管理的环境中，也忽略警告并强制直接覆盖更新二进制文件。
