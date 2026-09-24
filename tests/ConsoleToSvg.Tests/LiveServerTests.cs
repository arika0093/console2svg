using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests;

public sealed class LiveServerTests
{
    [Test]
    public void FrameIntervalUsesConfiguredMaximumFps()
    {
        Program.ResolveLiveFrameInterval(20d).ShouldBe(TimeSpan.FromMilliseconds(50));
        Program.ResolveLiveFrameInterval(0d).ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void TmuxPollingRefreshesScreenWithoutClearingItFirst()
    {
        var command = Program.BuildTmuxPollingCommand("tmux capture-pane -p", 30d);

        command.ShouldContain("awk '{ printf \"\\033[%d;1H%s\\033[K\", NR, $0 }'");
        command.ShouldContain("printf '\\033[J'");
        command.ShouldNotContain("\\033[2J");
        command.IndexOf("captured=$(", StringComparison.Ordinal)
            .ShouldBeLessThan(command.IndexOf("printf '\\033[H'", StringComparison.Ordinal));
    }

    [Test]
    public void SnapshotIncludesStaticPresentationLayers()
    {
        var theme = Theme.Resolve("dark");
        var screen = new ScreenBuffer(4, 2, theme);
        var options = new SvgRenderOptions
        {
            TerminalTheme = theme,
            Background = ["#123456"],
            Chrome = ChromeLoader.Load("macos"),
        };

        var svg = Program.RenderLiveSnapshot(screen, options);

        svg.ShouldContain("#123456");
        svg.ShouldContain("fill=\"#ff5f57\"");
    }

    [Test]
    public async Task InitialFrameQueuedDuringSendKeepsEveryLayer()
    {
        await using var stream = new PausingWriteStream();
        await using var client = new Program.LiveSseClient(stream, static () => { });

        var initialSend = client.SendInitialAsync(
            "old-background",
            "old-window",
            "old-text",
            CancellationToken.None
        );
        await stream.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        client.TrySendInitial("new-background", "new-window", "new-text");
        stream.ResumeWrites();
        await initialSend.WaitAsync(TimeSpan.FromSeconds(5));

        var output = stream.GetText();
        output.ShouldContain("event: background\ndata: new-background");
        output.ShouldContain("event: window\ndata: new-window");
        output.ShouldContain("event: text\ndata: new-text");
    }

    [Test]
    public async Task DisconnectedEventClientFinishesItsRequestTask()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var browser = new TcpClient();
        var acceptTask = listener.AcceptTcpClientAsync();
        await browser.ConnectAsync(endpoint.Address, endpoint.Port);
        var serverClient = await acceptTask;
        var clients = new ConcurrentDictionary<int, Program.LiveSseClient>();
        using var cancellation = new CancellationTokenSource();
        var serveTask = Program.ServeLiveClientAsync(
            serverClient,
            1,
            clients,
            () => Program.LiveFrame.Empty,
            "<html></html>",
            new SvgRenderOptions(),
            cancellation.Token
        );

        try
        {
            var stream = browser.GetStream();
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes("GET /events HTTP/1.1\r\nHost: localhost\r\n\r\n")
            );
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                leaveOpen: true
            );
            string? header;
            do
            {
                header = await reader.ReadLineAsync();
            } while (!string.IsNullOrEmpty(header));

            browser.Client.LingerState = new LingerOption(true, 0);
            browser.Close();
            for (var attempt = 0; attempt < 20 && !serveTask.IsCompleted; attempt++)
            {
                if (clients.TryGetValue(1, out var liveClient))
                {
                    liveClient.TrySendHeartbeat();
                }
                await Task.Delay(50);
            }

            serveTask.IsCompleted.ShouldBeTrue();
            await serveTask;
            clients.ShouldBeEmpty();
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    private sealed class PausingWriteStream : Stream
    {
        private readonly object _gate = new();
        private readonly List<byte> _bytes = [];
        private readonly TaskCompletionSource _resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _writeCount;

        public TaskCompletionSource FirstWriteStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ResumeWrites() => _resume.TrySetResult();

        public string GetText()
        {
            lock (_gate)
            {
                return Encoding.UTF8.GetString([.. _bytes]);
            }
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                FirstWriteStarted.TrySetResult();
                await _resume.Task.WaitAsync(cancellationToken);
            }

            lock (_gate)
            {
                _bytes.AddRange(buffer.Span);
            }
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
