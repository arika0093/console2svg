---
title: scenario
description: 在伪终端中自动执行场景文档（ScenarioDocument）的命令。
since: v0.11
---

```bash title="Terminal"
console2svg scenario run <document> [options]
```

`scenario` 是一个子命令，用于读取以 YAML 或 JSON 编写的 **场景文档**（ScenarioDocument），并在伪终端（PTY）中自动执行一系列终端交互操作。
与依赖人工操作时序的旧版 `replay` 不同，`scenario` 基于等待屏幕文字出现或状态变化的 **状态同步** 机制运行。
在 CI 环境下的回归测试以及文档图片的定期更新中，能够获得稳定可靠的复现结果。

## 命令语法与参数

### `scenario run`

执行指定的场景文档。

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml
```

* `<document>`：待执行的场景文档路径（必填）

## 选项

* `--width <width>`：临时覆盖伪终端宽度（列数）。
* `--height <height>`：临时覆盖伪终端高度（行数）。
* `--no-colorenv`：禁用颜色相关环境变量的自动设置。
* `--no-delete-envs`：保留 CI 相关的环境变量而不进行清除。
* `-C, --config <path>`：加载额外的配置文件并叠加到场景选项中。

## 场景执行生命周期

运行 `scenario run` 时，文档中的各个阶段按以下顺序处理：

1. **workingDir**：切换到指定的工作目录（`temporary: true` 会创建临时目录）。
2. **prepare**：启动 PTY 前在控制 Shell 中执行准备命令。
3. **launch**：使用指定的执行文件和参数启动应用程序 PTY。
4. **execute**：按顺序执行定义的各个操作步骤（`send`、`wait`、`resize`、`capture`、`command`）。
5. **verify**：操作结束后在控制 Shell 中执行事后验证命令。
6. **teardown**：验证完成后在控制 Shell 中执行清理命令。

## execute 中的步骤类型

### send

向终端发送输入数据。
在 `inputs` 数组中包含多个输入时，将按顺序连续发送。

```yaml
- type: send
  inputs:
    - text: ":w"
    - keys: Enter
```

* `text`：纯文本
* `keys`：特殊键名（`Enter`、`Escape`、`Tab`、`Ctrl+C`、方向键等）
* `paste`：使用括号粘贴模式传输的文本
* `rawHex`：十六进制编码的原始字节流

### wait

等待屏幕显示状态满足条件。

```yaml
- type: wait
  args:
    text: "Done"
    until: present
    stableFor: 1s
    timeout: 10s
```

* `text`：待匹配的文本
* `regex`：正则表达式模式
* `until`：等待文本出现（`present`）或消失（`absent`）
* `stableFor`：条件需保持满足的时长
* `timeout`：最长等待时间

### capture

将当前屏幕捕获为 SVG 图像。

```yaml
- type: capture
  args:
    output: output.svg
  options:
    appearance:
      theme:
        - nord
```

### resize

调整终端的宽度和高度。

```yaml
- type: resize
  args:
    width: 120
    height: 30
```

### command

在终端步骤之间于控制 Shell 中执行命令。

```yaml
- type: command
  command: "ls -la"
```

## 退出状态码与执行结果

所有步骤均成功完成时返回退出代码 0。
任一步骤超时或命令执行失败时返回退出代码 1。
标准输出将以 JSON 格式输出执行汇总：

```json title="输出示例"
{
  "schemaVersion": 1,
  "status": "completed",
  "sessionId": "s_a1b2c3d4e5f6",
  "exitCode": 0,
  "artifacts": [
    "/path/to/output.svg"
  ]
}
```
