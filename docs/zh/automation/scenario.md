---
title: 使用场景进行操作自动化
description: 基于状态同步与语义化操作定义，可靠复现终端交互操作的场景功能。
since: v0.11
---

`console2svg` 支持将对终端的一系列操作定义为 **场景**（ScenarioDocument）以复现相同的交互操作。
非常适合在 CI 流水线中进行回归测试以及定期更新文档插图。

旧版的 `replay` 命令采用重放录制按键与时间戳的方式。
受机器负载和网络延迟影响，画面渲染延迟容易导致按键在非预期时刻发送而失败。
「场景」功能以等待屏幕特定文字或状态转移的 **状态同步** 为核心设计，能够跨越环境差异实现确定性的复现。

## 场景文件基本结构

场景文件使用 YAML 格式（或 JSON 格式）编写。

```yaml title="demo-scenario.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ScenarioDocument.v1.json
$version: 1

options:
  terminal:
    width: 100
    height: 24

scenario:
  launch:
    executable: vim
    args:
      - demo.txt
  execute:
    - type: wait
      args:
        text: "~"
    - type: send
      inputs:
        - text: "iHello, console2svg!"
        - keys: Enter
        - keys: Escape
    - type: capture
      args:
        output: output.svg
      options:
        appearance:
          theme:
            - nord
```

## 场景生命周期

场景按照以下阶段依次执行生命周期：

1. **workingDir**：设置工作目录（支持自动创建临时目录）。
2. **prepare**：启动 PTY 前在控制 Shell 中运行准备命令（如生成测试文件）。
3. **launch**：在 PTY 中启动指定的程序（`executable`）及参数（`args`）。
4. **execute**：按顺序执行输入发送、条件等待和画面捕获。
5. **verify**：操作结束后在控制 Shell 中执行验证命令（如检查生成文件）。
6. **teardown**：验证完成后在控制 Shell 中执行清理命令。

## execute 中的核心步骤

`execute` 列表包含按顺序对程序 PTY 执行的操作。

### send

向终端发送字符串或特殊键。
在 `inputs` 中列出的多个输入将按顺序连续发送。

```yaml
- type: send
  inputs:
    - text: "npm test"
    - keys: Enter
```

每个输入项必须指定且仅指定以下之一：

* `text`：普通字符串
* `keys`：命名键或组合键（`Enter`、`Escape`、`Tab`、`Ctrl+C`、方向键等）
* `paste`：支持括号粘贴模式的粘贴文本
* `rawHex`：十六进制编码的原始字节流（如 `1B5B41`）

### wait

等待终端屏幕状态满足指定条件。

```yaml
- type: wait
  args:
    text: "Compiled successfully"
    until: present
    stableFor: 1s
    timeout: 30s
```

* `text`：字面量匹配字符串
* `regex`：正则表达式模式
* `until`：等待出现（`present`）或消失（`absent`）
* `stableFor`：条件必须保持满足的时长
* `timeout`：最长等待时间

### capture

将当前终端屏幕保存为 SVG 图片。
支持设置针对该步骤的外观主题与窗口样式。

```yaml
- type: capture
  args:
    output: docs/assets/tui-screen.svg
  options:
    appearance:
      theme:
        - dracula
```

### resize

动态调整终端的宽度和高度。

```yaml
- type: resize
  args:
    width: 120
    height: 36
```

### command

在执行阶段运行控制 Shell 命令。

```yaml
- type: command
  command: "touch /tmp/flag-ready"
```

## 运行场景

使用 `console2svg scenario run` 执行场景文件：

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml
```

也可在命令行临时覆盖终端尺寸：

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml --width 120 --height 30
```

## 从托管会话导出

除手动编写场景外，还可以将通过 `console2svg session` 交互执行的成功记录直接导出为场景文件：

```bash title="Terminal"
console2svg session export s_abc123 -o generated-scenario.yaml
```

导出过程会提取会话中执行的 `send`、`wait` 和 `capture` 等操作。
一旦导出成功路径，即可在 CI 中重复运行该场景，无需智能体或人工再次介入。

## 完整场景示例

以下是基于 `todo/171-scenario-config-file.md` 的带注释完整场景文档示例，涵盖完整的执行生命周期（准备、启动、操作执行、验证、清理）：

```yaml title="full-scenario.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ScenarioDocument.v1.json
$version: 1

# 1. 场景执行时的基础选项（各 capture 步骤的基础设置）
# 可填写 console2svg.config.yaml 文件的内容。
options:
  terminal:
    width: 160
    height: 32
  appearance:
    theme:
      - nord
    window: macos-pc

# 2. 场景定义主体
scenario:
  # 工作目录（省略时默认为运行命令时的当前目录）
  working-dir:
    path: "/tmp/test-scenario"
    # 或自动生成临时目录（默认行为）
    # temporary: true

  # 准备阶段：启动 PTY 前在控制 Shell 中执行的环境准备命令
  prepare:
    - type: command
      command: |
        echo "Preparing test environment..."

  # 启动阶段：在 PTY 中启动的应用程序与参数
  launch:
    executable: vim
    args:
      - hello.txt
    options:
      # 仅在启动阶段生效的终端尺寸覆盖
      terminal:
        width: 160
        height: 32

  # 执行阶段：按顺序对应用程序执行的终端操作
  execute:
    - type: send
      inputs:
        - text: "i"
        - text: "hello world"
        - keys: Enter

    # 等待 "hello world" 出现并保持 2 秒稳定状态
    - type: wait
      args:
        text: "hello world"
        until: present
        stable-for: 2s
        timeout: 5s

    # 退出插入模式并保存到日志文件
    - type: send
      inputs:
        - keys: Esc
        - text: ":w hello.log"
        - keys: Enter

    # 运行 Shell 命令确认文件生成
    - type: command
      command: |
        test -f hello.log

    # 捕获当前屏幕并应用单步特定的主题
    - type: capture
      args:
        output: hello-world.svg
      options:
        appearance:
          theme:
            - dracula

    # 退出 vim
    - type: send
      inputs:
        - text: ":q"
        - keys: Enter

  # 验证阶段：应用程序退出后，在控制 Shell 中验证结果
  verify:
    - type: command
      command: |
        grep "hello world" hello.log

  # 清理阶段：验证完成后在控制 Shell 中清理临时文件
  teardown:
    - type: command
      command: |
        rm hello.log
```
