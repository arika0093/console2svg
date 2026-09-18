---
title: 使用 resvg 栅格化 PNG
description: 如何调用捆绑的 native resvg，在进程内复用字体状态，并明确管理 managed 与 native memory 的所有权。
---

PNG 输出需要 SVG renderer。
console2svg 捆绑了一个小型 Rust C ABI wrapper，用来调用 resvg。
普通 PNG 路径因此不需要依赖 browser process，也不需要假设系统 ffmpeg 一定包含 SVG decoder。

## 在当前 process 内完成栅格化

native wrapper 使用 usvg 解析 SVG，通过 resvg 和 tiny-skia 绘制 pixmap，再把 pixmap 编码成 PNG。

system font discovery 是可以跨 frame 共享的 process-wide 状态。
Rust 侧使用 **`OnceLock<Arc<Database>>`**。
第一次调用时创建 font database 并加载 system font，后续 render 只复用共享引用。

.NET 侧也可以显式 warm up 该 database。
converter detection 阶段即可完成初始化，避免视频中的某个随机 frame 单独承担首次 font discovery 成本。

## 明确计算 raster 尺寸

native wrapper 读取 SVG intrinsic size，并结合可选的 raster width 和 height 计算输出尺寸。

宽高都指定时直接使用给定值。
只指定一边时，根据 SVG aspect ratio 推导另一边。
两边都未指定时使用 SVG 自身尺寸。

最终尺寸会 clamp 到1至16384 pixel。
这样可以避免0尺寸 surface，也能限制误配置造成的极端 native allocation。

## 复用 managed 输入 buffer

`ResvgNative.RenderToPng` 先计算 SVG `string` 的 UTF-8 byte 数量。
随后从 `ArrayPool<byte>` 租用 array，把 SVG encode 到该 span，再调用 native function。
调用结束后 array 会归还 pool。

native renderer 返回的 PNG buffer 由 native 侧拥有。
.NET wrapper 将其复制到 managed `byte[]`，并在 `finally` 中调用对应 free function。

调用方只接触 managed PNG。
Rust 侧使用何种 allocator 不需要暴露给上层。

native status code 会区分 SVG parse、PNG encode、render 和 allocation failure。
.NET wrapper 会把这些状态转换成不同异常，而不会把空或部分 buffer 当作成功结果。

## 优先搜索捆绑的 native asset

native library resolver 会先检查 console2svg 的 bundled asset directory，再交给普通 loader resolution。

release archive 可能把 library 放在 executable 旁边，package layout 也可能把 native asset 放到 sibling library directory。
显式搜索可以覆盖这些布局。

portable install 和 symbolic link 场景也能因此减少对 current working directory 的依赖。

## 根据 converter mode 决定是否 fallback

auto mode 会优先选择可用的 bundled resvg。
其他图像转换路径还可以 fallback 到 `rsvg-convert`，或者已经通过实际转换验证 SVG decode 能力的 ffmpeg。

用户明确指定 resvg 时，如果 native library 无法加载，不会静默切换到其他 renderer。
显式 renderer 选择强调可复现性，auto mode 则优先保证可用性。
