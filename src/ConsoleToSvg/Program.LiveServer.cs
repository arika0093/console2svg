using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static async Task<int> RunLiveServerAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        if (options.LiveServerPort is < 1 or > 65535)
        {
            await Console.Error.WriteLineAsync("live-server port must be between 1 and 65535.");
            return 1;
        }
        if (!IPAddress.TryParse(options.ListenAddress ?? "127.0.0.1", out var address))
        {
            await Console.Error.WriteLineAsync("--listen must be an IP address.");
            return 1;
        }

        var width = ResolveSize(
            options.Width,
            options.WidthAdjust,
            TryGetConsoleWidth,
            DefaultWidth
        );
        var height = ResolveSize(
            options.Height,
            options.HeightAdjust,
            TryGetConsoleHeight,
            DefaultHeight
        );
        var backgroundRenderOptions = SvgRenderOptionsFactory.Create(options);
        backgroundRenderOptions.IncludeTerminalFrame = false;
        backgroundRenderOptions.IncludeChrome = false;
        backgroundRenderOptions.IncludeClientBackground = false;
        backgroundRenderOptions.Opacity = 1d;
        var chromeRenderOptions = SvgRenderOptionsFactory.Create(options);
        chromeRenderOptions.IncludeTerminalFrame = false;
        chromeRenderOptions.IncludeBackground = false;
        chromeRenderOptions.IncludeClientBackground = false;
        chromeRenderOptions.Opacity = 1d;
        var frameRenderOptions = SvgRenderOptionsFactory.Create(options);
        frameRenderOptions.RenderCursor = true;
        frameRenderOptions.IncludeStaticLayers = false;
        frameRenderOptions.Opacity = 1d;
        string latestBackgroundSvg = "";
        string latestChromeSvg = "";
        string latestFrameSvg = "";
        var screenGate = new object();
        ScreenBuffer? latestScreen = null;
        var screenVersion = 0L;
        var clients = new ConcurrentDictionary<int, LiveSseClient>();
        var listener = new TcpListener(address, options.LiveServerPort);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            await Console.Error.WriteLineAsync(
                $"Unable to listen on http://{address}:{options.LiveServerPort}/: {ex.Message}"
            );
            return 1;
        }
        await Console.Error.WriteLineAsync(
            $"Live terminal: http://{address}:{options.LiveServerPort}/"
        );
        await Console.Error.FlushAsync(cancellationToken);
        using var listenerRegistration = cancellationToken.Register(listener.Stop);
        try
        {
            using var liveLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            var acceptTask = AcceptLiveClientsAsync(
                listener,
                clients,
                () => (latestBackgroundSvg, latestChromeSvg, latestFrameSvg),
                GetLiveHtml(options.Opacity),
                options.Opacity,
                liveLifetime.Token
            );
            var renderTask = RenderLiveFramesAsync(
                () =>
                {
                    lock (screenGate)
                    {
                        return (latestScreen, screenVersion);
                    }
                },
                (backgroundSvg, chromeSvg, frameSvg) =>
                {
                    if (backgroundSvg is not null && chromeSvg is not null)
                    {
                        latestBackgroundSvg = backgroundSvg;
                        latestChromeSvg = chromeSvg;
                        latestFrameSvg = frameSvg;
                        BroadcastInitialSvg(
                            clients,
                            backgroundSvg,
                            chromeSvg,
                            frameSvg,
                            options.Opacity
                        );
                        return;
                    }
                    latestFrameSvg = frameSvg;
                    BroadcastFrameSvg(clients, frameSvg);
                },
                backgroundRenderOptions,
                chromeRenderOptions,
                frameRenderOptions,
                liveLifetime.Token
            );
            var theme = Theme.Resolve(frameRenderOptions.Theme);
            if (!string.IsNullOrWhiteSpace(frameRenderOptions.BackColor))
                theme = theme.WithBackground(frameRenderOptions.BackColor);
            if (!string.IsNullOrWhiteSpace(frameRenderOptions.ForeColor))
                theme = theme.WithForeground(frameRenderOptions.ForeColor);
            await InteractiveRecorder
                .RunAsync(
                    width,
                    height,
                    theme,
                    Encoding.ASCII.GetBytes("\u001b[21~"),
                    Encoding.ASCII.GetBytes("\u001b[20~"),
                    Encoding.ASCII.GetBytes("\u001b[24~"),
                    options.NoDeleteEnvs,
                    options.DelimitedCommand,
                    exitOnCtrlD: true,
                    recordingEnabled: false,
                    screenshotEnabled: false,
                    static (_, _) => Task.FromResult<string?>(null),
                    cancellationToken,
                    onScreenUpdated: screen =>
                    {
                        lock (screenGate)
                        {
                            latestScreen = screen;
                            screenVersion++;
                        }
                    },
                    captureControlsEnabled: false
                )
                .ConfigureAwait(false);
            await liveLifetime.CancelAsync().ConfigureAwait(false);
            listener.Stop();
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            try
            {
                await renderTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"live-server error: {ex.Message}");
            return 1;
        }
        finally
        {
            listener.Stop();
            foreach (var client in clients.Values)
                await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task AcceptLiveClientsAsync(
        TcpListener listener,
        ConcurrentDictionary<int, LiveSseClient> clients,
        Func<(string BackgroundSvg, string ChromeSvg, string FrameSvg)> latest,
        string liveHtml,
        double opacity,
        CancellationToken cancellationToken
    )
    {
        var nextId = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener
                    .AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
                when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted)
            {
                break;
            }
            _ = ServeLiveClientAsync(
                client,
                Interlocked.Increment(ref nextId),
                clients,
                latest,
                liveHtml,
                opacity,
                cancellationToken
            );
        }
    }

    private static async Task ServeLiveClientAsync(
        TcpClient client,
        int id,
        ConcurrentDictionary<int, LiveSseClient> clients,
        Func<(string BackgroundSvg, string ChromeSvg, string FrameSvg)> latest,
        string liveHtml,
        double opacity,
        CancellationToken cancellationToken
    )
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
        {
            var request = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            string? header;
            do
            {
                header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            } while (!string.IsNullOrEmpty(header));
            var path = request?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                is { Length: > 1 } parts
                ? parts[1]
                : "/";
            if (path == "/events")
            {
                await WriteHttpAsync(
                        stream,
                        "200 OK",
                        "text/event-stream; charset=utf-8",
                        null,
                        "Cache-Control: no-cache\r\nConnection: keep-alive\r\n",
                        includeContentLength: false
                    )
                    .ConfigureAwait(false);
                var sseClient = new LiveSseClient(stream, () => clients.TryRemove(id, out _));
                clients[id] = sseClient;
                var (backgroundSvg, chromeSvg, frameSvg) = latest();
                await sseClient
                    .SendInitialAsync(
                        backgroundSvg,
                        chromeSvg,
                        frameSvg,
                        opacity,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    clients.TryRemove(id, out _);
                }
                clients.TryRemove(id, out _);
                return;
            }
            if (path == "/snapshot.svg")
            {
                await WriteHttpAsync(
                        stream,
                        "200 OK",
                        "image/svg+xml; charset=utf-8",
                        latest().FrameSvg
                    )
                    .ConfigureAwait(false);
                return;
            }
            if (path == "/health")
            {
                await WriteHttpAsync(stream, "200 OK", "application/json", "{\"status\":\"ok\"}")
                    .ConfigureAwait(false);
                return;
            }
            if (path == "/")
            {
                await WriteHttpAsync(stream, "200 OK", "text/html; charset=utf-8", liveHtml)
                    .ConfigureAwait(false);
                return;
            }
            if (path == "/settings")
            {
                await WriteHttpAsync(stream, "200 OK", "text/html; charset=utf-8", LiveSettingsHtml)
                    .ConfigureAwait(false);
                return;
            }
            await WriteHttpAsync(stream, "404 Not Found", "text/plain", "Not found")
                .ConfigureAwait(false);
        }
    }

    private static void BroadcastInitialSvg(
        ConcurrentDictionary<int, LiveSseClient> clients,
        string backgroundSvg,
        string chromeSvg,
        string frameSvg,
        double opacity
    )
    {
        foreach (var client in clients.Values)
            client.TrySendInitial(backgroundSvg, chromeSvg, frameSvg, opacity);
    }

    private static void BroadcastFrameSvg(
        ConcurrentDictionary<int, LiveSseClient> clients,
        string svg
    )
    {
        foreach (var client in clients.Values)
            client.TrySendFrame(svg);
    }

    private static async Task RenderLiveFramesAsync(
        Func<(ScreenBuffer? Screen, long Version)> getLatestScreen,
        Action<string?, string?, string> publish,
        SvgRenderOptions backgroundRenderOptions,
        SvgRenderOptions chromeRenderOptions,
        SvgRenderOptions frameRenderOptions,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / 60d));
        var renderedVersion = -1L;
        var staticRendered = false;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var (screen, version) = getLatestScreen();
            if (screen is null || version == renderedVersion)
            {
                continue;
            }

            var backgroundSvg = staticRendered
                ? null
                : SvgRenderer.Render(screen, backgroundRenderOptions);
            var chromeSvg = staticRendered ? null : SvgRenderer.Render(screen, chromeRenderOptions);
            publish(backgroundSvg, chromeSvg, SvgRenderer.Render(screen, frameRenderOptions));
            staticRendered = true;
            renderedVersion = version;
        }
    }

    private sealed class LiveSseClient(NetworkStream stream, Action disconnected) : IAsyncDisposable
    {
        private int _sending;

        public async Task SendInitialAsync(
            string backgroundSvg,
            string chromeSvg,
            string frameSvg,
            double opacity,
            CancellationToken cancellationToken
        )
        {
            await WriteSseAsync(
                    stream,
                    "window-opacity",
                    opacity.ToString(CultureInfo.InvariantCulture),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(backgroundSvg))
            {
                await WriteSseAsync(stream, "background", backgroundSvg, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(chromeSvg))
            {
                await WriteSseAsync(stream, "chrome", chromeSvg, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(frameSvg))
            {
                await WriteSseAsync(stream, "frame", frameSvg, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public void TrySendInitial(
            string backgroundSvg,
            string chromeSvg,
            string frameSvg,
            double opacity
        )
        {
            if (Interlocked.Exchange(ref _sending, 1) != 0)
                return;
            _ = SendInitialAndFrameAsync(backgroundSvg, chromeSvg, frameSvg, opacity);
        }

        public void TrySendFrame(string svg) => TrySend("frame", svg);

        private void TrySend(string eventName, string svg)
        {
            if (Interlocked.Exchange(ref _sending, 1) != 0)
                return;
            _ = SendAsync(eventName, svg);
        }

        private async Task SendAsync(string eventName, string svg)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await WriteSseAsync(stream, eventName, svg, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                disconnected();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        private async Task SendInitialAndFrameAsync(
            string backgroundSvg,
            string chromeSvg,
            string frameSvg,
            double opacity
        )
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await WriteSseAsync(
                        stream,
                        "window-opacity",
                        opacity.ToString(CultureInfo.InvariantCulture),
                        timeout.Token
                    )
                    .ConfigureAwait(false);
                await WriteSseAsync(stream, "background", backgroundSvg, timeout.Token)
                    .ConfigureAwait(false);
                await WriteSseAsync(stream, "chrome", chromeSvg, timeout.Token)
                    .ConfigureAwait(false);
                await WriteSseAsync(stream, "frame", frameSvg, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                disconnected();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    private static async Task WriteSseAsync(
        NetworkStream stream,
        string eventName,
        string svg,
        CancellationToken token
    ) =>
        await WriteBytesAsync(
                stream,
                "event: "
                    + eventName
                    + "\ndata: "
                    + svg.Replace("\r", "").Replace("\n", "\ndata: ")
                    + "\n\n",
                token
            )
            .ConfigureAwait(false);

    private static async Task WriteHttpAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string? body,
        string extra = "",
        bool includeContentLength = true
    ) =>
        await WriteBytesAsync(
                stream,
                $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\n{extra}{(includeContentLength ? $"Content-Length: {Encoding.UTF8.GetByteCount(body ?? string.Empty)}\r\n" : string.Empty)}\r\n{body}",
                CancellationToken.None
            )
            .ConfigureAwait(false);

    private static async Task WriteBytesAsync(
        NetworkStream stream,
        string text,
        CancellationToken token
    )
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static string GetLiveHtml(double opacity) =>
        LiveHtml.Replace(
            "{opacity}",
            opacity.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );

    private const string LiveHtml =
        """<!doctype html><meta charset="utf-8"><style>html,body,#screen{margin:0;width:100%;height:100%;background:transparent;overflow:hidden}#screen{position:relative}#layers,#background,#window,#chrome,#terminal{position:absolute;inset:0}#window{opacity:{opacity}}#screen svg{display:block}#background svg{width:100%;height:auto}#screen.contain{display:grid;align-content:stretch;justify-content:stretch;align-items:start;justify-items:start}#screen.width svg{width:100%;height:auto}#screen.height svg{width:auto;height:100%}#screen.actual svg{width:auto;height:auto}#settings{position:absolute;right:12px;top:12px;z-index:1;display:none;border:0;border-radius:4px;background:#333c;color:#fff;cursor:pointer;font:13px sans-serif;padding:7px 10px}#screen.show-settings #settings,#settings:hover{display:block}#status{display:none;position:fixed;inset:0;place-items:center;background:#0009;color:#fff;font:16px system-ui,sans-serif;pointer-events:none}#status.visible{display:grid}</style><div id=screen><div id=layers><div id=background></div><div id=window><div id=chrome></div><div id=terminal></div></div></div><button id=settings type=button aria-label="Display settings">Settings</button></div><div id=status role=status>Connection to the server was lost. Reconnecting...</div><script>const s=document.querySelector('#screen'),background=document.querySelector('#background'),chromeLayer=document.querySelector('#chrome'),terminal=document.querySelector('#terminal'),settings=document.querySelector('#settings'),status=document.querySelector('#status'),modes=new Set(['width','height','contain','actual']),key='console2svg.live.fit';let mode='contain',events,reconnectTimer;function applyMode(){const svg=terminal.querySelector('svg');if(!svg)return;const svgs=s.querySelectorAll('#background svg,#chrome svg,#terminal svg');svgs.forEach(svg=>{svg.style.width='';svg.style.height=''});if(mode!=='contain')return;const box=svg.viewBox.baseVal,ratio=(box.width||svg.width.baseVal.value)/(box.height||svg.height.baseVal.value),width=ratio>s.clientWidth/s.clientHeight;svgs.forEach(svg=>{svg.style.width=width?'100%':'auto';svg.style.height=width?'auto':'100%'})}function setMode(value){mode=modes.has(value)?value:'contain';s.className=mode;localStorage.setItem(key,mode);applyMode()}function connect(){clearTimeout(reconnectTimer);events=new EventSource('/events');events.addEventListener('window-opacity',e=>document.querySelector('#window').style.opacity=e.data);events.addEventListener('background',e=>{background.innerHTML=e.data;applyMode()});events.addEventListener('chrome',e=>{chromeLayer.innerHTML=e.data;applyMode()});events.addEventListener('frame',e=>{terminal.innerHTML=e.data;applyMode();status.classList.remove('visible')});events.onopen=()=>status.classList.remove('visible');events.onerror=()=>{status.classList.add('visible');events.close();reconnectTimer=setTimeout(connect,1000)}}terminal.onpointermove=e=>s.classList.toggle('show-settings',e.target instanceof SVGElement);terminal.onpointerleave=()=>s.classList.remove('show-settings');setMode(localStorage.getItem(key)||'contain');connect();settings.onclick=()=>window.open('/settings','console2svg-live-settings','popup,width=260,height=230,resizable=no');window.addEventListener('message',e=>{if(e.origin===location.origin&&e.data?.type==='console2svg-live-fit')setMode(e.data.mode)});window.onresize=applyMode;</script>""";

    private const string LiveSettingsHtml =
        """<!doctype html><meta charset="utf-8"><title>Terminal display settings</title><style>body{font:14px system-ui,sans-serif;margin:20px;color:#222}fieldset{border:0;margin:0;padding:0}label{display:block;margin:12px 0}button{float:right;padding:5px 12px}</style><fieldset><legend>Terminal display</legend><label><input type=radio name=fit value=contain> Contain</label><label><input type=radio name=fit value=width> Fit to width</label><label><input type=radio name=fit value=height> Fit to height</label><label><input type=radio name=fit value=actual> 1:1 display</label></fieldset><button type=button onclick="window.close()">Close</button><script>const key='console2svg.live.fit',modes=new Set(['width','height','contain','actual']);function setMode(mode){if(!modes.has(mode))mode='contain';localStorage.setItem(key,mode);opener?.postMessage({type:'console2svg-live-fit',mode},location.origin);document.querySelector(`input[value="${mode}"]`).checked=true}document.querySelectorAll('input[name=fit]').forEach(input=>input.onchange=()=>setMode(input.value));setMode(localStorage.getItem(key)||'contain');</script>""";
}
