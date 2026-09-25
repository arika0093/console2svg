---
title: 配置文件
description: 以 YAML 格式持久化终端尺寸、主题及渲染设置，供多次执行共享使用的配置文件功能。
since: v0.11
---

`console2svg` 支持将终端尺寸、颜色主题、渲染选项等设置持久化为 **配置文件**（ConfigDocument）。
将常用参数归拢到配置文件中，可以保持命令行调用的精炼。

配置文件仅保存被动配置值。
不包含外部命令的执行或操作步骤的描述。

## 文件格式与 Schema

配置文件采用 YAML 格式（或 JSON 格式）编写。
提供 JSON Schema 以支持编辑器自动补全与验证。

```yaml title="console2svg.config.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ConfigDocument.v1.json
$version: 1

options:
  terminal:
    width: 120
    height: 30
  appearance:
    theme:
      - dracula
    window: macos
    font:
      family: "JetBrains Mono"
      size: 14
```

## 配置发现与优先级

配置文件分为三个层级读取，优先级较低层级的值会被较高层级覆盖。
命令行参数拥有最高优先级，会覆盖所有配置文件中的设置。

1. **全局配置**：位于用户主目录下跨环境共享的全局设置
   * Linux: `~/.config/console2svg/config.yaml`
   * macOS: `~/Library/Application Support/console2svg/config.yaml`
   * Windows: `%APPDATA%\console2svg\config.yaml`
2. **本地配置**：位于工作目录下的项目特定设置
   * 当前工作目录下的 `console2svg.config.yaml`
3. **显式指定**：在命令行通过 `-C` 或 `--config` 指定的任意路径
   * `console2svg capture -C custom-config.yaml -- btop`

各层级中省略的选项将直接继承自较低层级。
覆盖数组类型的属性（如主题列表或背景色列表）时，将替换整个数组而非追加元素。

## 可配置项

在配置文件中的 `options` 键下按分类编写配置。
最常用的配置项包括：

### terminal

指定伪终端（PTY）的单元格尺寸。

```yaml
options:
  terminal:
    width: 120
    height: 30
```

### appearance

配置输出图片的外观主题、窗口装饰与字体。

```yaml
options:
  appearance:
    theme:
      - github-dark
    window: macos-pc
    background:
      - "my-background.png"
```

## 完整配置示例

以下是基于 `console2svg.example.yaml` 与 `todo/171-scenario-config-file.md` 的带注释完整配置文档示例：

```yaml title="console2svg.config.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ConfigDocument.v1.json
$version: 1

# 共享选项：适用于兼容的 console2svg 工作流
options:
  # 终端设置：启动 PTY 时应用的单元格尺寸（区别于 appearance.size 的像素尺寸）
  terminal:
    width: 160 # 相当于命令行 -w
    height: 32 # 相当于命令行 -h

  # 环境变量策略
  environment:
    color: overwrite # overwrite | preserve（相当于 --no-colorenv）
    ci: strip        # strip | preserve（相当于 --no-delete-envs）

  # 交互式终端设置
  interactive:
    mouse: true      # 启用鼠标输入（相当于 --mouse）

  # live-server 设置
  live-server:
    host: ":8080"    # 空主机名绑定至回环地址（localhost:8080）

  # 捕获行为设置
  capture:
    mode: video      # image | video（相当于 -v）
    crop:
      top: 1ch
      bottom: "Hello!:-2"
      left: 1ch
      right: 10px
    video:
      fps: 12
      loop: true
      time:
        start: 0
        end: 10
      timing: deterministic # deterministic | realtime
      coalesce: auto        # auto 或毫秒数值
      fadeout: 0.5          # 淡出秒数
    # 静态图像设置（mode: image 时有效，frame 与 time 排他）
    # image:
    #   frame: 3
    #   time: 2.5

  # 渲染设置：调整已记录的终端输出显示
  render:
    masking:
      auto: true     # 自动遮盖敏感信息
      strings:       # 遮盖字符串列表（覆盖而非追加低优先级数组）
        - password
        - secret
    adjust: spacing  # spacing | spacingAndGlyphs

  # SVG 转换引擎
  converter:
    svg-converter: auto # auto | ffmpeg | rsvg | rsvg-convert | resvg

  # 外观设置（数组将整体替换低层级配置而非追加）
  appearance:
    font:
      family: "Fira Code, Fira Mono, monospace"
      size: 14
    # 图像像素尺寸（通常只指定 width 或 height 其中之一）
    size:
      width: 800
      # height: 400
    theme:
      - nord
    window: macos-pc
    padding: 10
    margin: 8
    pc-padding: 12
    background:
      # 背景颜色或背景图片
      - "#000"
      - "#048"
    opacity: 0.85
    forecolor: "#fff"
    backcolor: "#000"
    header:
      with-command: true # 顶部显示命令名
      prompt: "$ "       # 提示符文本
```
