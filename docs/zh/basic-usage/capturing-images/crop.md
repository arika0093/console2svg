---
title: 裁剪输出
description: 按像素、字符数或文本模式裁剪输出画面上下左右的方法。
---

当你只想在文档中展示构建日志或命令输出的一部分时，可以使用 Crop（裁剪）功能，只截取需要的区域并生成 SVG。

## 裁剪选项

使用以下选项指定各边的裁剪量。

| 选项 | 说明 |
| :--- | :--- |
| `--crop-top` | 裁剪上侧 |
| `--crop-bottom` | 裁剪下侧 |
| `--crop-left` | 裁剪左侧 |
| `--crop-right` | 裁剪右侧 |

## 指定方法

### 像素 (px) 或字符数 (ch)

直接以像素（`px`）或字符数、行数（`ch`）指定数值。

```bash title="Terminal" "--crop-top 20px" "--crop-bottom 3ch" "--crop-left 10px" "--crop-right 10ch"
# 裁剪前
console2svg capture -w 80 -h 12 -- console2svg

# 从顶部裁剪 20px，从底部裁剪 3 行，从左侧裁剪 10px，从右侧裁剪 10 个字符
console2svg capture -w 80 -h 12 \ 
  --crop-top 20px --crop-bottom 3ch \
  --crop-left 10px --crop-right 10ch -- console2svg
```

<!-- c2s::  -w 80 -h 12 --crop-top 20px --crop-bottom 3ch --crop-left 10px --crop-right 10ch -- console2svg -->
![console2svg](../../../../docs-site/public/assets/generated/efd72668b53794a7ddc2851ef05cfd7dc3282247b93581072ec272fe800949b9.svg)

### 通过文本模式裁剪

也可以根据特定字符串出现的位置进行动态裁剪。

```bash title="Terminal" "--crop-bottom"
# 裁剪字符串 "Options" 所在行以下的全部内容
console2svg capture -w 80 -h 12 --crop-bottom "Options" -- console2svg
```

<!-- c2s::  -w 80 -h 12 --crop-bottom "Options" -- console2svg -->
![console2svg](../../../../docs-site/public/assets/generated/91e04cd6256ff1436aff826a9578d2dbe19fe6f4700e49d4cef1a19b55ecdd18.svg)

通过指定偏移行数，也可以从匹配行的前后位置开始裁剪。

```bash title="Terminal" "--crop-bottom"
# 保留到 "Options" 前 2 行（-2）为止
console2svg capture -w 80 -h 12 --crop-bottom "Options::-2" -- console2svg
```

<!-- c2s::  -w 80 -h 12 --crop-bottom "Options::-2" -- console2svg -->
![console2svg](../../../../docs-site/public/assets/generated/64e81dbde347cc79927af4f41ab46394eede6502f4b24034c454379f0cb266e0.svg)
