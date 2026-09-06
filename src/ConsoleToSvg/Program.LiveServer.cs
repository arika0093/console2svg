using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private static async Task<int> RunLiveServerAsync(AppOptions options, CancellationToken cancellationToken)
    {
        var width = options.Width ?? 0;
        var height = options.Height ?? 0;
        if (width <= 0 || height <= 0 || options.DelimitedCommand is null || options.DelimitedCommand.Length == 0)
        {
            await Console.Error.WriteLineAsync("live-server requires fixed --width, --height, and a command after --.");
            return 1;
        }
        if (options.LiveServerPort is < 1 or > 65535) { await Console.Error.WriteLineAsync("live-server port must be between 1 and 65535."); return 1; }
        if (!IPAddress.TryParse(options.ListenAddress ?? "127.0.0.1", out var address)) { await Console.Error.WriteLineAsync("--listen must be an IP address."); return 1; }

        var theme = Theme.Resolve(options.Theme);
        if (!string.IsNullOrWhiteSpace(options.ForeColor)) theme = theme.WithForeground(options.ForeColor);
        if (!string.IsNullOrWhiteSpace(options.BackColor)) theme = theme.WithBackground(options.BackColor);
        var terminal = new TerminalEmulator(width, height, theme);
        var renderOptions = SvgRenderOptionsFactory.Create(options);
        renderOptions.RenderCursor = true;
        var latestSvg = SvgRenderer.Render(terminal.Buffer, renderOptions);
        var clients = new ConcurrentDictionary<int, NetworkStream>();
        var listener = new TcpListener(address, options.LiveServerPort);
        try { listener.Start(); }
        catch (SocketException ex) { await Console.Error.WriteLineAsync($"Unable to listen on http://{address}:{options.LiveServerPort}/: {ex.Message}"); return 1; }
        await Console.Error.WriteLineAsync($"Live terminal: http://{address}:{options.LiveServerPort}/");
        using var listenerRegistration = cancellationToken.Register(listener.Stop);
        try
        {
            var ptyOptions = new NativePtyOptions { Name = "console2svg-live", Cols = width, Rows = height, Cwd = Environment.CurrentDirectory, App = options.DelimitedCommand[0], Args = options.DelimitedCommand[1..], Environment = CreateLiveEnvironment(width, height) };
            using var connection = await NativePty.SpawnAsync(ptyOptions, cancellationToken).ConfigureAwait(false);
            var acceptTask = AcceptLiveClientsAsync(listener, clients, () => latestSvg, cancellationToken);
            var bytes = new byte[8192];
            var decoder = new UTF8Encoding(false, false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await connection.ReaderStream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                terminal.Process(decoder.GetString(bytes, 0, read));
                latestSvg = SvgRenderer.Render(terminal.Buffer, renderOptions);
                await BroadcastSvgAsync(clients, latestSvg, cancellationToken).ConfigureAwait(false);
            }
            listener.Stop();
            try { await acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { return 0; }
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        finally
        {
            listener.Stop();
            foreach (var client in clients.Values) await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> CreateLiveEnvironment(int width, int height)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) if (entry.Key is string key && entry.Value is string value) env[key] = value;
        env["COLUMNS"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["LINES"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return env;
    }

    private static async Task AcceptLiveClientsAsync(TcpListener listener, ConcurrentDictionary<int, NetworkStream> clients, Func<string> latest, CancellationToken cancellationToken)
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

    private static async Task ServeLiveClientAsync(TcpClient client, int id, ConcurrentDictionary<int, NetworkStream> clients, Func<string> latest, CancellationToken cancellationToken)
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
                clients[id] = stream;
                await WriteSseAsync(stream, latest(), cancellationToken).ConfigureAwait(false);
                try { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); }
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

    private static async Task BroadcastSvgAsync(ConcurrentDictionary<int, NetworkStream> clients, string svg, CancellationToken cancellationToken)
    {
        foreach (var client in clients)
        {
            try { await WriteSseAsync(client.Value, svg, cancellationToken).ConfigureAwait(false); }
            catch
            {
                clients.TryRemove(client.Key, out _);
                await client.Value.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
    private static async Task WriteSseAsync(NetworkStream stream, string svg, CancellationToken token) => await WriteBytesAsync(stream, "event: svg\ndata: " + svg.Replace("\r", "").Replace("\n", "\ndata: ") + "\n\n", token).ConfigureAwait(false);
    private static async Task WriteHttpAsync(NetworkStream stream, string status, string contentType, string? body, string extra = "") => await WriteBytesAsync(stream, $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\n{extra}Content-Length: {Encoding.UTF8.GetByteCount(body ?? string.Empty)}\r\n\r\n{body}", CancellationToken.None).ConfigureAwait(false);
    private static async Task WriteBytesAsync(NetworkStream stream, string text, CancellationToken token) { var bytes = Encoding.UTF8.GetBytes(text); await stream.WriteAsync(bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }

    private const string LiveHtml = """<!doctype html><meta charset=\"utf-8\"><style>html,body,#screen{margin:0;width:100%;height:100%;background:transparent;overflow:hidden}#screen svg{width:100%;height:100%;object-fit:contain}#menu{display:none;position:fixed;background:#222;color:#fff;padding:8px;font:13px sans-serif}</style><div id=screen></div><div id=menu>Fit to width<br>Fit to height<br>Contain<br>1:1 display</div><script>const s=document.querySelector('#screen'),m=document.querySelector('#menu');new EventSource('/events').addEventListener('svg',e=>s.innerHTML=e.data);document.oncontextmenu=e=>{e.preventDefault();m.style.cssText+=';display:block;left:'+e.clientX+'px;top:'+e.clientY+'px'};document.onclick=()=>m.style.display='none';</script>""";
}
