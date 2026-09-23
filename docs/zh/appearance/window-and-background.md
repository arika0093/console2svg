---
title: 指定窗口边框和背景
description: 指定 macOS 风格或 Windows 风格窗口装饰（Chrome）、背景颜色和背景图片的方法。
---

通过设置窗口装饰（如标题栏）和背景，可以调整截图的外观。

## 窗口边框样式

使用 `-d` 或 `--window <style>` 选项指定窗口边框。只指定不带值的 `-d` 时，会应用 `macos`。

```bash title="Terminal" "-d macos-pc"
# 带投影的 macOS 风格样式
console2svg capture -d macos-pc -- fastfetch
```

<!-- c2s:: -d macos-pc -- fastfetch -->

### 列表

内置窗口边框和显示示例汇总在[内置窗口边框](./themes/built-in-window-themes.mdx)中。

## 指定背景

可以使用 `--background` 选项设置背景颜色或背景图片。

### 纯色背景

指定颜色代码（HEX）。

```bash title="Terminal" "--background"
console2svg capture -h 10 -c -d macos-pc --opacity 0.85 \
  --background "#003060" -- dotnet --version
```

<!-- c2s::  -w 64 -h 6 -c -d macos-pc --background "#003060" --opacity 0.85 -- dotnet --version -->


### 渐变

指定多个颜色代码。

```bash title="Terminal" "--background"
console2svg capture -h 10 -c -d macos-pc --opacity 0.85 \
  --background "#004060" "#0080c0" -- dotnet --version
```

<!-- c2s::  -w 64 -h 6 -c -d macos-pc --background "#004060" "#0080c0" --opacity 0.85 -- dotnet --version -->


### 图片背景

指定图片文件路径后，可以像桌面背景一样显示。

```sh title="Terminal" "--background image.png"
console2svg capture -h 10 -c -d macos-pc --opacity 0.85 \
  --background image.png -- dotnet --version
```

<!-- c2s::  -w 64 -h 6 -c -d macos-pc --background ../../../assets/image1.png --opacity 0.85 -- dotnet --version -->


## 留白和不透明度

* **`--margin <px>`**：窗口边框外侧的边距。
* **`--padding <px>`**：终端内侧（文字与边框之间）的内边距。
* **`--pc-padding <px>`**：`macos-pc` 或 `windows-pc` 的外侧留白大小。
* **`--opacity <0.0〜1.0>`**：背景不透明度（例：`--opacity 0.9`）。
