---
title: 使用 ffmpeg 编码视频
description: 如何按时间采样终端状态，并行栅格化 PNG，再按顺序写入一个 ffmpeg image2pipe process。
---

视频输出使用与静态图片相同的 SVG renderer。
每个 sample 时刻先生成静态 SVG，再栅格化为 PNG，最后把 PNG 按时间顺序发送给 ffmpeg。

普通内存路径不会要求 ffmpeg 从连续 SVG 文档中识别 frame 边界。
`image2pipe` 接收 PNG，因为每一张 PNG 都具有可从 byte stream 解析的独立边界。

## 让一个 emulator 按时间前进

固定 FPS sampling 不会为每个 video frame 从 event 0 重新 replay。

frame generator 只维护一个 `TerminalEmulator` 和一个单调递增的 event index。
sample time 向前推进时，只处理新到达的事件。
用于找到目标 event 的 index 也只向前移动。

终端 replay 工作量因此更接近录制 event stream 本身，而不是让每个后续 frame 重复处理此前所有事件。

## 复用相同画面的 SVG

emulator 到达目标时刻后，会取得 screen visual signature。

静态 SVG 以该 signature 为 key，最多缓存128项。
多个 sample 显示同一画面时，会直接返回 cache 中同一个 SVG `string` object，而不是重新构造等价 XML。

没有指定 FPS 的 frame 保存路径也会跳过连续相同 visual signature 的状态。

## 并行栅格化，按原顺序写入

SVG 到 PNG 的转换可能是 CPU-bound，也可能依赖外部 process，取决于 rasterizer。

video converter 可以同时启动多个 PNG render。
并发度跟随 processor count，并限制为最多8。

ffmpeg input 顺序不能改变。
pending render task 放入 bounded FIFO queue，只有队首 task 的结果会写入标准输入。

这样既能重叠独立 rasterization 工作，也不会提前把整个视频的 PNG byte array 都堆积在内存中。

## 通过 SVG object identity 复用 PNG render

PNG 往往比源 SVG 占用更多 memory，因此 raster cache 比 SVG cache 更小，最多保存16项。

cache key 使用 SVG `string` 的 object reference，而不是内容。
前一阶段的 SVG cache 对同一 visual signature 返回相同 object，因此这里可以直接复用同一个 PNG-render task。

这形成 **两级缓存**。
第一层避免重新生成 SVG，第二层避免重新栅格化 PNG。
也不需要为很长的 XML string 再做内容 hash 或全文比较。

## 用一个 ffmpeg process 接收所有 frame

console2svg 为完整视频只启动一个 ffmpeg process。
所有 PNG frame 都写入它的 standard input。

输入参数使用 `-f image2pipe -vcodec png -i pipe:0` 和目标 frame rate。
普通内存路径不需要创建编号 SVG 或 PNG 文件。

process 启动后会立即异步 drain standard output 和 standard error。
这样可以避免子 process pipe 填满后阻塞，同时保留失败时需要的 ffmpeg diagnostic。

取消操作会尝试结束整个 ffmpeg process tree。
最后一张 PNG 写完后关闭 stdin，再等待 encoder 退出。

## 用实际转换探测能力

ffmpeg 即使显示 SVG pipe format，也不代表当前 build 一定包含可以栅格化 SVG 的 decoder。
在图像转换路径中，console2svg 会执行一次最小 SVG 到 PNG 的实际转换并缓存结果。

in-memory video 有不同约束。
如果选择的 converter mode 指向 ffmpeg，video pipeline 仍会先解析出能够生成 PNG 的 renderer，例如 bundled resvg 或 `rsvg-convert`。
最终 ffmpeg process 已经承担视频编码，`image2pipe` 也不能把连续 SVG 文档可靠拆成独立 frame。

codec discovery 结果也会缓存，避免每次转换都重新运行 `ffmpeg -encoders`。

## 选择 MP4 codec 并补齐偶数尺寸

MP4 会优先使用可用的 `libx264`，否则 fallback 到 `mpeg4`。
WebM、GIF 等其他 container 在没有 MP4 codec 要求时交给 ffmpeg 的 container 默认 encoder。

输出使用 `yuv420p`，并应用：

```text
pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0
```

该 filter 把奇数 width 或 height 向上补到偶数。
最多只在右侧或底部增加1 pixel，因此 terminal content 的左上位置保持不变。
