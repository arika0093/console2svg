using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private static readonly TimeSpan LiveFrameSettleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan LiveFrameMaximumDelay = TimeSpan.FromMilliseconds(100);

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
        var listenHost = options.ListenAddress ?? "127.0.0.1";
        var address = await ResolveListenAddressAsync(listenHost, cancellationToken);
        if (address is null)
        {
            await Console.Error.WriteLineAsync(
                $"live-server host '{listenHost}' is not a valid IP address or host name."
            );
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
        var windowRenderOptions = SvgRenderOptionsFactory.Create(options);
        windowRenderOptions.IncludeBackground = false;
        windowRenderOptions.IncludeTerminalForeground = false;
        windowRenderOptions.IncludeTerminalBaseBackground = false;
        var textRenderOptions = SvgRenderOptionsFactory.Create(options);
        textRenderOptions.RenderCursor = options.RequestedTmuxAction != TmuxAction.LiveServer;
        textRenderOptions.IncludeStaticLayers = false;
        textRenderOptions.IncludeTerminalBackground = true;
        textRenderOptions.IncludeTerminalBaseBackground = false;
        textRenderOptions.Opacity = 1d;
        string latestBackgroundSvg = "";
        string latestWindowSvg = "";
        string latestTextSvg = "";
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
                () => (latestBackgroundSvg, latestWindowSvg, latestTextSvg),
                GetLiveHtml(),
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
                (backgroundSvg, windowSvg, textSvg) =>
                {
                    if (backgroundSvg is not null && windowSvg is not null)
                    {
                        latestBackgroundSvg = backgroundSvg;
                        latestWindowSvg = windowSvg;
                        latestTextSvg = textSvg;
                        BroadcastInitialSvg(clients, backgroundSvg, windowSvg, textSvg);
                        return;
                    }
                    if (textSvg == latestTextSvg)
                        return;
                    latestTextSvg = textSvg;
                    BroadcastTextSvg(clients, textSvg);
                },
                backgroundRenderOptions,
                windowRenderOptions,
                textRenderOptions,
                liveLifetime.Token
            );
            var theme = textRenderOptions.TerminalTheme ?? Theme.Resolve(textRenderOptions.Theme);
            if (textRenderOptions.Chrome?.ThemeBackgroundOverride is string chromeBackground)
                theme = theme.WithBackground(chromeBackground);
            if (!string.IsNullOrWhiteSpace(textRenderOptions.BackColor))
                theme = theme.WithBackground(textRenderOptions.BackColor);
            if (!string.IsNullOrWhiteSpace(textRenderOptions.ForeColor))
                theme = theme.WithForeground(textRenderOptions.ForeColor);
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
                    captureControlsEnabled: false,
                    forwardToConsole: options.LiveServerForwardToConsole,
                    terminalSizeProvider: options.LiveServerResize
                        ? new Func<(int Width, int Height)>(() =>
                            (
                                TryGetConsoleWidth() ?? DefaultWidth,
                                TryGetConsoleHeight() ?? DefaultHeight
                            )
                        )
                        : null
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

    private static async Task<IPAddress?> ResolveListenAddressAsync(
        string host,
        CancellationToken cancellationToken
    )
    {
        if (IPAddress.TryParse(host, out var address))
            return address;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .ConfigureAwait(false);
            foreach (var candidate in addresses)
            {
                if (candidate.AddressFamily == AddressFamily.InterNetwork)
                    return candidate;
            }

            return addresses.Length > 0 ? addresses[0] : null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task AcceptLiveClientsAsync(
        TcpListener listener,
        ConcurrentDictionary<int, LiveSseClient> clients,
        Func<(string BackgroundSvg, string WindowSvg, string TextSvg)> latest,
        string liveHtml,
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
                cancellationToken
            );
        }
    }

    private static async Task ServeLiveClientAsync(
        TcpClient client,
        int id,
        ConcurrentDictionary<int, LiveSseClient> clients,
        Func<(string BackgroundSvg, string WindowSvg, string TextSvg)> latest,
        string liveHtml,
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
                var (backgroundSvg, windowSvg, textSvg) = latest();
                await sseClient
                    .SendInitialAsync(backgroundSvg, windowSvg, textSvg, cancellationToken)
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
                        latest().TextSvg
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
        string windowSvg,
        string textSvg
    )
    {
        foreach (var client in clients.Values)
            client.TrySendInitial(backgroundSvg, windowSvg, textSvg);
    }

    private static void BroadcastTextSvg(
        ConcurrentDictionary<int, LiveSseClient> clients,
        string svg
    )
    {
        foreach (var client in clients.Values)
            client.TrySendText(svg);
    }

    private static async Task RenderLiveFramesAsync(
        Func<(ScreenBuffer? Screen, long Version)> getLatestScreen,
        Action<string?, string?, string> publish,
        SvgRenderOptions backgroundRenderOptions,
        SvgRenderOptions windowRenderOptions,
        SvgRenderOptions textRenderOptions,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / 60d));
        var renderedVersion = -1L;
        var staticRendered = false;
        var renderedWidth = 0;
        var renderedHeight = 0;
        var observedVersion = -1L;
        var pendingSince = Stopwatch.GetTimestamp();
        var publishedAt = pendingSince;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var (screen, version) = getLatestScreen();
            if (screen is null)
            {
                continue;
            }

            var now = Stopwatch.GetTimestamp();
            if (version != observedVersion)
            {
                observedVersion = version;
                pendingSince = now;
            }

            if (screen.Width != renderedWidth || screen.Height != renderedHeight)
            {
                staticRendered = false;
                renderedWidth = screen.Width;
                renderedHeight = screen.Height;
            }

            if (version == renderedVersion && staticRendered)
            {
                continue;
            }

            var settled = Stopwatch.GetElapsedTime(pendingSince) >= LiveFrameSettleDelay;
            var overdue = Stopwatch.GetElapsedTime(publishedAt) >= LiveFrameMaximumDelay;
            if (!settled && !overdue)
            {
                continue;
            }

            var backgroundSvg = staticRendered
                ? null
                : SvgRenderer.Render(screen, backgroundRenderOptions);
            var windowSvg = staticRendered ? null : SvgRenderer.Render(screen, windowRenderOptions);
            publish(backgroundSvg, windowSvg, SvgRenderer.Render(screen, textRenderOptions));
            staticRendered = true;
            renderedVersion = version;
            publishedAt = now;
        }
    }

    private sealed class LiveSseClient(NetworkStream stream, Action disconnected) : IAsyncDisposable
    {
        private readonly object _sendGate = new();
        private int _sending;
        private string? _pendingText;

        public async Task SendInitialAsync(
            string backgroundSvg,
            string windowSvg,
            string textSvg,
            CancellationToken cancellationToken
        )
        {
            lock (_sendGate)
            {
                _sending = 1;
            }

            try
            {
                if (!string.IsNullOrEmpty(backgroundSvg))
                {
                    await WriteSseAsync(stream, "background", backgroundSvg, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (!string.IsNullOrEmpty(windowSvg))
                {
                    await WriteSseAsync(stream, "window", windowSvg, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (!string.IsNullOrEmpty(textSvg))
                {
                    await WriteSseAsync(stream, "text", textSvg, cancellationToken)
                        .ConfigureAwait(false);
                }
                await SendPendingTextAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CompleteSend();
            }
        }

        public void TrySendInitial(string backgroundSvg, string windowSvg, string textSvg)
        {
            lock (_sendGate)
            {
                _pendingText = textSvg;
                if (_sending != 0)
                {
                    return;
                }

                _sending = 1;
            }
            _ = SendInitialAndTextAsync(backgroundSvg, windowSvg);
        }

        public void TrySendText(string textSvg)
        {
            lock (_sendGate)
            {
                _pendingText = textSvg;
                if (_sending != 0)
                {
                    return;
                }

                _sending = 1;
            }
            _ = SendAsync();
        }

        private async Task SendInitialAndTextAsync(string backgroundSvg, string windowSvg)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await WriteSseAsync(stream, "background", backgroundSvg, timeout.Token)
                    .ConfigureAwait(false);
                await WriteSseAsync(stream, "window", windowSvg, timeout.Token)
                    .ConfigureAwait(false);
                await SendPendingTextAsync(timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                disconnected();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                CompleteSend();
            }
        }

        private async Task SendPendingTextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                string? textSvg;
                lock (_sendGate)
                {
                    textSvg = _pendingText;
                    _pendingText = null;
                }

                if (textSvg is null)
                {
                    return;
                }

                await WriteSseAsync(stream, "text", textSvg, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task SendAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await SendPendingTextAsync(timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                disconnected();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                CompleteSend();
            }
        }

        private void CompleteSend()
        {
            lock (_sendGate)
            {
                if (_pendingText is null)
                {
                    _sending = 0;
                    return;
                }
            }

            _ = SendAsync();
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

    private static string GetLiveHtml() => EmbeddedLiveHtml;

    private static string LoadEmbeddedText(string resourceName)
    {
        using var stream =
            typeof(Program).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was not found."
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
