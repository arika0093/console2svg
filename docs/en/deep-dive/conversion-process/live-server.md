---
title: live-server Delivery Architecture
description: Streaming terminal output via embedded HTTP/SSE server, three-layer separation, update throttling, and client-side rAF coalescing.
---

Relaying a live terminal screen to a web browser in real time requires balancing sub-frame latency with high rendering throughput capable of handling dense text bursts.
The `live-server` subcommand in console2svg avoids heavy external runtimes such as Node.js or standalone reverse proxies. Instead, it implements a lightweight, specialized HTTP/1.1 and **SSE** (Server-Sent Events: a web standard for unidirectional text-based streaming from server to client over HTTP) server directly on top of .NET's native socket abstractions (`TcpListener`).
By stripping away redundant protocol overhead, console2svg delivers responsive browser previews from a single, self-contained binary.

## Embedded HTTP Server and Endpoint Design

By default, `live-server` binds and listens on `127.0.0.1:38473`.
It exposes five minimal, purpose-built HTTP endpoints to connected browser clients:

* `/`: Serves the client viewer interface—an embedded single-file asset combining HTML, CSS, and client-side JavaScript.
* `/events`: An active SSE stream with MIME type `text/event-stream` that continuously pushes live terminal frame updates.
* `/snapshot.svg`: Dynamically renders and serves the current terminal buffer as a standalone, self-contained SVG image.
* `/health`: A lightweight health check endpoint returning `{"status":"ok"}` for container orchestration and uptime monitoring.
* `/settings`: A configuration view for toggling viewport fit modes and diagnostic overlays.

console2svg chooses SSE over WebSockets because unidirectional screen broadcasting requires no client-to-server data frame negotiation or bidirectional socket framing overhead.
Furthermore, SSE integrates seamlessly with standard HTTP proxies, operates without firewall complications, and leverages the browser's native `EventSource` API for transparent reconnection and structured event routing.

## Three-Layer Separation for Bandwidth and DOM Efficiency

Re-transmitting an entire SVG document—including window chrome, shadows, and title bars—on every minor cursor motion wastes bandwidth and incurs heavy browser DOM (Document Object Model) reflow costs.
To eliminate this overhead, console2svg decomposes terminal visual output into **three independent SVG layers**:

1. **Background Layer (`background`)**: Contains the outer window margin and the overall terminal canvas background rectangle. It remains static as long as the terminal dimensions do not change.
2. **Window Chrome Layer (`window`)**: Contains window borders, title bars, drop shadows, and window control buttons. It updates only on terminal resize or window title mutations.
3. **Text Content Layer (`text`)**: Contains the character glyphs, foreground/background text colors, cursor highlights, and cell attributes. It updates dynamically with command execution.

```http title="SSE Stream Wire Format"
event: background
data: <svg xmlns="http://www.w3.org/2000/svg" width="800" height="500">...</svg>

event: window
data: <svg xmlns="http://www.w3.org/2000/svg" width="800" height="500">...</svg>

event: text
data: <svg xmlns="http://www.w3.org/2000/svg" width="800" height="500">...</svg>
```

On the client side, the HTML viewer stacks three container elements (`#background`, `#window-paint`, and `#text`) on top of each other.
When an event arrives, the viewer updates only the targeted container via `innerHTML`.
During active terminal sessions, only the topmost `text` layer changes, allowing the browser engine to bypass reparsing static window chrome elements entirely.

## Server-Side Render Loop and Settle Delay

Terminal emulator buffers (`ScreenBuffer`) should not be rasterized into SVG the instant a single byte arrives.
CLI programs routinely emit complex screen changes across multiple small ANSI escape chunks. Pushing immediate renders would broadcast incomplete screen clears or intermediate cursor jumps.

The server's background render loop (`RenderLiveFramesAsync`) polls buffer versions (`screenVersion`) using an internal timer tuned to approximately 60 fps, enforcing the following control mechanisms:

* **Settle Delay**: After detecting a buffer mutation, the renderer waits for a quiet window of 25 milliseconds (`LiveFrameSettleDelay`) before generating a frame. Coalescing bursty escape sequences into a single atomic frame eliminates visual flickering.
* **FPS Throttling**: Even during heavy, continuous log output, the loop throttles frame dispatch to the maximum configured rate (configured via `--video-fps`, defaulting to 30 fps or ~33 ms per frame), preventing runaway CPU utilization.
* **Static Layer Caching**: When terminal row and column dimensions match the previous frame, generation of the `background` SVG is skipped, reusing the cached string from the initial handshake.

## Backpressure Handling and Latency Prevention

When a client runs over a slow network or minimizes its browser tab, outgoing frames can accumulate in socket buffers, causing preview latency to drift behind reality.
In a real-time monitor, displaying stale terminal state delayed by tens of seconds is unacceptable.

To prevent buffer bloat, the connection manager (`LiveSseClient`) applies **backpressure mitigation** through pending frame overwrite merging.
If a new SVG frame is produced while a previous socket write is still in progress, the client driver discards the intermediate pending frame and overwrites the transmission buffer with the newest snapshot.

Under bandwidth constraints, this design guarantees that the client always skips to the latest visual state rather than falling behind.
Socket writes are bound by a 5-second timeout, ensuring that unresponsive clients are evicted to prevent memory accumulation.
Additionally, the server emits a lightweight `heartbeat` event (`ping`) every 2 seconds to keep connections alive through stateful NAT firewalls.

## Client-Side rAF Coalescing and Diagnostic Instrumentation

Even with server-side throttling, rapid output bursts can still dispatch dozens of `text` events per second.
If the browser executed synchronous `innerHTML` assignments for every event, DOM layout recalculations would choke the main UI thread.

To preserve client responsiveness, the web viewer implements **rAF coalescing** (Burst Coalescing).
Incoming `text` payloads are stored in an in-memory reference (`pendingText`), deferring DOM insertion to the browser's next `requestAnimationFrame` callback.
If multiple SSE payloads arrive within a single vsync period, intermediate frames are silently dropped, applying only the latest payload when the browser is ready to paint.

The viewer also includes a built-in real-time performance HUD:

* **Event Rate (`events/s`)**: Plots successfully applied frames per second using a blue line.
* **Jank Meter (`jank ms/s`)**: Measures rendering stalls exceeding 1.5× the measured display vsync interval using a red line. In a completely fluid render loop, this line stays flat at zero.
* **LoAF (Long Animation Frames) Monitoring**: Utilizes the browser's Long Animation Frames API to record main thread stalls exceeding 50 ms, logging duration and script attribution.

Finally, the viewer provides four automatic viewport scaling modes (`contain` to fit within window bounds while preserving aspect ratio, `width`, `height`, and `actual` 1:1 pixel rendering), ensuring crisp typography across all screen geometries.
