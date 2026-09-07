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
        if (!Console.IsOutputRedirected)
        {
            await Console.Out.WriteAsync("\u001b[2J\u001b[H");
            await Console.Out.FlushAsync(cancellationToken);
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
                await WriteHttpAsync(
                        stream,
                        "200 OK",
                        "text/html; charset=utf-8",
                        EmbeddedLiveSettingsHtml
                    )
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

    private static readonly string EmbeddedLiveHtml = LoadEmbeddedText("console2svg.live.html");
    private static readonly string EmbeddedLiveSettingsHtml = LoadEmbeddedText(
        "console2svg.live-settings.html"
    );

    private static string GetLiveHtml(double opacity) =>
        EmbeddedLiveHtml.Replace(
            "{opacity}",
            opacity.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );

    private static string LoadEmbeddedText(string resourceName)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
