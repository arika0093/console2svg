using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
    private static async Task<int> RunLiveServerAsync(AppOptions options, CancellationToken cancellationToken)
    {
        var stderrWriter = new StreamWriter(
            Console.OpenStandardError(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true
        )
        {
            AutoFlush = true,
        };
        Console.SetOut(stderrWriter);

        if (options.LiveServerPort is < 1 or > 65535) { await Console.Error.WriteLineAsync("live-server port must be between 1 and 65535."); return 1; }
        if (!IPAddress.TryParse(options.ListenAddress ?? "127.0.0.1", out var address)) { await Console.Error.WriteLineAsync("--listen must be an IP address."); return 1; }

        var width = ResolveSize(options.Width, options.WidthAdjust, TryGetConsoleWidth, DefaultWidth);
        var height = ResolveSize(options.Height, options.HeightAdjust, TryGetConsoleHeight, DefaultHeight);
        var command = options.DelimitedCommand ?? Array.Empty<string>();
        var ptyOptions = BuildLivePtyOptions(width, height, command, options.NoDeleteEnvs);

        var theme = Theme.Resolve(options.Theme);
        if (!string.IsNullOrWhiteSpace(options.ForeColor)) theme = theme.WithForeground(options.ForeColor);
        if (!string.IsNullOrWhiteSpace(options.BackColor)) theme = theme.WithBackground(options.BackColor);
        var terminal = new TerminalEmulator(width, height, theme);
        var renderOptions = SvgRenderOptionsFactory.Create(options);
        renderOptions.RenderCursor = true;
        string latestSvg = "";
        var clients = new ConcurrentDictionary<int, LiveSseClient>();
        var listener = new TcpListener(address, options.LiveServerPort);
        var hostDisplay = FormatHost(address);
        try { listener.Start(); }
        catch (SocketException ex) { await Console.Error.WriteLineAsync($"Unable to listen on http://{hostDisplay}:{options.LiveServerPort}/: {ex.Message}"); return 1; }
        await Console.Error.WriteLineAsync($"Live terminal: http://{hostDisplay}:{options.LiveServerPort}/");
        using var listenerRegistration = cancellationToken.Register(listener.Stop);
        var acceptTask = AcceptLiveClientsAsync(listener, clients, () => latestSvg, cancellationToken);
        try
        {
            using var connection = await NativePty.SpawnAsync(ptyOptions, cancellationToken).ConfigureAwait(false);
            latestSvg = SvgRenderer.Render(terminal.Buffer, renderOptions);
            var readTask = ReadLiveOutputAsync(connection.ReaderStream, terminal, renderOptions, svg =>
            {
                latestSvg = svg;
                BroadcastSvg(clients, svg);
            }, errorMessage =>
            {
                latestSvg = RenderErrorSvg(width, height, theme, renderOptions, errorMessage);
                BroadcastSvg(clients, latestSvg);
            }, cancellationToken);
            while (!readTask.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                if (!connection.WaitForExit(50)) continue;
                var drained = await Task.WhenAny(readTask, Task.Delay(500, cancellationToken)).ConfigureAwait(false);
                if (drained != readTask) connection.Dispose();
                break;
            }
            await readTask.ConfigureAwait(false);
            listener.Stop();
            try { await acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { return 0; }
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"live-server error: {ex.Message}");
            latestSvg = RenderErrorSvg(width, height, theme, renderOptions, ex.Message);
            BroadcastSvg(clients, latestSvg);
            try { await acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* shutdown requested */ }
            return 0;
        }
        finally
        {
            listener.Stop();
            foreach (var client in clients.Values) await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static NativePtyOptions BuildLivePtyOptions(int width, int height, string[]? command, bool noDeleteEnvs)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                environment[key] = value;
            }
        }

        environment["COLUMNS"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment["LINES"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!noDeleteEnvs)
        {
            environment.Remove("CI");
            environment.Remove("TF_BUILD");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (command is { Length: > 0 })
            {
                return new NativePtyOptions
                {
                    Name = "console2svg-live",
                    Cols = width,
                    Rows = height,
                    Cwd = Environment.CurrentDirectory,
                    App = command[0],
                    Args = command[1..],
                    Environment = environment,
                    DisableInputEcho = false,
                };
            }

            var shell = Environment.GetEnvironmentVariable("COMSPEC");
            if (string.IsNullOrWhiteSpace(shell))
            {
                shell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "cmd.exe"
                );
            }

            return new NativePtyOptions
            {
                Name = "console2svg-live",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = shell,
                Args = ["/k"],
                Environment = environment,
                DisableInputEcho = false,
            };
        }

        var unixShell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(unixShell))
        {
            unixShell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        }

        if (command is { Length: > 0 })
        {
            return new NativePtyOptions
            {
                Name = "console2svg-live",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = command[0],
                Args = command[1..],
                Environment = environment,
                DisableInputEcho = false,
            };
        }

        return new NativePtyOptions
        {
            Name = "console2svg-live",
            Cols = width,
            Rows = height,
            Cwd = Environment.CurrentDirectory,
            App = unixShell,
            Args = ["-i"],
            Environment = environment,
            DisableInputEcho = false,
        };
    }

    private static string RenderErrorSvg(int width, int height, Theme theme, SvgRenderOptions renderOptions, string message)
    {
        var errorTerminal = new TerminalEmulator(width, height, theme);
        errorTerminal.Process("\u001b[2J\u001b[H");
        errorTerminal.Process("\u001b[31;1mlive-server error:\u001b[0m\r\n");
        foreach (var ch in message) errorTerminal.Process(ch.ToString());
        errorTerminal.Process("\r\n\u001b[33mCheck console2svg output for details.\u001b[0m");
        return SvgRenderer.Render(errorTerminal.Buffer, renderOptions);
    }

    private static async Task AcceptLiveClientsAsync(TcpListener listener, ConcurrentDictionary<int, LiveSseClient> clients, Func<string> latest, CancellationToken cancellationToken)
    {
        var nextId = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = ServeLiveClientAsync(client, Interlocked.Increment(ref nextId), clients, latest, cancellationToken);
        }
    }

    private static async Task ServeLiveClientAsync(TcpClient client, int id, ConcurrentDictionary<int, LiveSseClient> clients, Func<string> latest, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
        {
            var request = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            string? header;
            do { header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false); } while (!string.IsNullOrEmpty(header));
            var path = request?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } parts ? parts[1] : "/";
            if (path == "/events")
            {
                await WriteHttpAsync(stream, "200 OK", "text/event-stream; charset=utf-8", null, "Cache-Control: no-cache\r\nConnection: keep-alive\r\n").ConfigureAwait(false);
                var sseClient = new LiveSseClient(stream, () => clients.TryRemove(id, out _));
                clients[id] = sseClient;
                await sseClient.SendInitialAsync(latest(), cancellationToken).ConfigureAwait(false);
                try { await Task.Delay(Timeout.Infinite, sseClient.ClientToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { clients.TryRemove(id, out _); }
                clients.TryRemove(id, out _);
                return;
            }
            if (path == "/snapshot.svg") { await WriteHttpAsync(stream, "200 OK", "image/svg+xml; charset=utf-8", latest()).ConfigureAwait(false); return; }
            if (path == "/health") { await WriteHttpAsync(stream, "200 OK", "application/json", "{\"status\":\"ok\"}").ConfigureAwait(false); return; }
            if (path == "/") { await WriteHttpAsync(stream, "200 OK", "text/html; charset=utf-8", LiveHtml).ConfigureAwait(false); return; }
            await WriteHttpAsync(stream, "404 Not Found", "text/plain", "Not found").ConfigureAwait(false);
        }
    }

    private static void BroadcastSvg(ConcurrentDictionary<int, LiveSseClient> clients, string svg)
    {
        foreach (var client in clients.Values) client.TrySend(svg);
    }

    private static async Task ReadLiveOutputAsync(Stream input, TerminalEmulator terminal, SvgRenderOptions renderOptions, Action<string> publish, Action<string> publishError, CancellationToken cancellationToken)
    {
        const int StartupTimeoutMs = 3000;
        var bytes = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var decoder = Encoding.UTF8.GetDecoder();
        var dataReceived = false;
        var startupTimeoutTask = Task.Delay(StartupTimeoutMs, cancellationToken);
        
        while (true)
        {
            var readTask = input.ReadAsync(bytes, cancellationToken).AsTask();
            var completedTask = await Task.WhenAny(readTask, startupTimeoutTask).ConfigureAwait(false);
            
            if (completedTask == startupTimeoutTask && !dataReceived)
            {
                publishError($"No data received from PTY within {StartupTimeoutMs}ms.\nThe command may be hanging or not producing output.");
                startupTimeoutTask = Task.Delay(Timeout.Infinite, cancellationToken);
            }
            
            var read = await readTask.ConfigureAwait(false);
            if (read == 0) break;
            
            dataReceived = true;
            var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
            if (count == 0) continue;
            terminal.Process(new string(chars, 0, count));
            publish(SvgRenderer.Render(terminal.Buffer, renderOptions));
        }
        var remaining = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        if (remaining > 0)
        {
            terminal.Process(new string(chars, 0, remaining));
            publish(SvgRenderer.Render(terminal.Buffer, renderOptions));
        }
    }

    private sealed class LiveSseClient(NetworkStream stream, Action disconnected) : IAsyncDisposable
    {
        private int _sending;
        private readonly CancellationTokenSource _clientCts = new();
        public CancellationToken ClientToken => _clientCts.Token;
        public async Task SendInitialAsync(string svg, CancellationToken cancellationToken) => await WriteSseAsync(stream, svg, cancellationToken).ConfigureAwait(false);
        public void TrySend(string svg)
        {
            if (Interlocked.Exchange(ref _sending, 1) != 0) return;
            _ = SendAsync(svg);
        }
        private async Task SendAsync(string svg)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await WriteSseAsync(stream, svg, timeout.Token).ConfigureAwait(false); }
            catch { disconnected(); Cancel(); await stream.DisposeAsync().ConfigureAwait(false); }
            finally { Interlocked.Exchange(ref _sending, 0); }
        }
        public void Cancel() { try { _clientCts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ } }
        public async ValueTask DisposeAsync() { _clientCts.Dispose(); await stream.DisposeAsync(); }
    }

    private static async Task WriteSseAsync(NetworkStream stream, string svg, CancellationToken token) => await WriteBytesAsync(stream, "event: svg\ndata: " + svg.Replace("\r", "").Replace("\n", "\ndata: ") + "\n\n", token).ConfigureAwait(false);
    private static async Task WriteHttpAsync(NetworkStream stream, string status, string contentType, string? body, string extra = "") => await WriteBytesAsync(stream, $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\n{extra}Content-Length: {Encoding.UTF8.GetByteCount(body ?? string.Empty)}\r\n\r\n{body}", CancellationToken.None).ConfigureAwait(false);
    private static async Task WriteBytesAsync(NetworkStream stream, string text, CancellationToken token) { var bytes = Encoding.UTF8.GetBytes(text); await stream.WriteAsync(bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }

    private static string FormatHost(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    private const string LiveHtml = """<!doctype html><meta charset="utf-8"><style>html,body,#screen{margin:0;width:100%;height:100%;background:transparent;overflow:hidden}#screen svg{width:100%;height:100%;object-fit:contain}#screen.width svg{width:100%;height:auto}#screen.height svg{width:auto;height:100%}#screen.actual svg{width:auto;height:auto}#menu{display:none;position:fixed;background:#222;color:#fff;padding:4px;font:13px sans-serif;z-index:1}#menu button{display:block;width:100%;border:0;background:transparent;color:inherit;text-align:left;padding:4px}</style><div id=screen></div><div id=menu><button data-mode=width>Fit to width</button><button data-mode=height>Fit to height</button><button data-mode=contain>Contain</button><button data-mode=actual>1:1 display</button></div><script>const s=document.querySelector('#screen'),m=document.querySelector('#menu');new EventSource('/events').addEventListener('svg',e=>s.innerHTML=e.data);document.oncontextmenu=e=>{e.preventDefault();m.style.cssText+=';display:block;left:'+e.clientX+'px;top:'+e.clientY+'px'};m.onclick=e=>{let b=e.target.closest('button');if(b){s.className=b.dataset.mode==='contain'?'':b.dataset.mode;m.style.display='none'}};document.onclick=e=>{if(!m.contains(e.target))m.style.display='none'};</script>""";
}
