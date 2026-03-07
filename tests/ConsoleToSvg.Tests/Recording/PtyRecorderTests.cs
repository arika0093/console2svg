using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConsoleToSvg.Tests.Recording;

public sealed class PtyRecorderTests
{
    [Test]
    public async Task ReadOutputAsync_ForwardsRawBytesWithoutTransform()
    {
        const string text = "\x1b[31mこんにちは\x1b[0m";
        var inputBytes = Encoding.UTF8.GetBytes(text);
        await using var input = new MemoryStream(inputBytes);
        await using var forwarded = new MemoryStream();
        var session = new RecordingSession(width: 40, height: 5);

        await InvokeReadOutputAsync(
            input,
            session,
            forwarded,
            forwardOutputWriter: null,
            Encoding.UTF8
        );

        session.Events.Count.ShouldBe(1);
        session.Events[0].Data.ShouldBe(text);
        forwarded.ToArray().SequenceEqual(inputBytes).ShouldBeTrue();
    }

    [Test]
    public async Task ReadOutputAsync_ForwardsRawBytesEvenWhenOutputEncodingIsUtf16()
    {
        const string text = "\x1b[32m日本語\x1b[0m";
        var outputEncoding = Encoding.Unicode;
        var inputBytes = outputEncoding.GetBytes(text);
        await using var input = new MemoryStream(inputBytes);
        await using var forwarded = new MemoryStream();
        var session = new RecordingSession(width: 40, height: 5);

        await InvokeReadOutputAsync(
            input,
            session,
            forwarded,
            forwardOutputWriter: null,
            outputEncoding
        );

        session.Events.Count.ShouldBe(1);
        session.Events[0].Data.ShouldBe(text);
        forwarded.ToArray().SequenceEqual(inputBytes).ShouldBeTrue();
    }

    [Test]
    public async Task ReadOutputAsync_DecodesUsingConfiguredOutputEncoding()
    {
        const string text = "漢字\r\ndone";
        var outputEncoding = Encoding.Unicode;
        var inputBytes = outputEncoding.GetBytes(text);
        await using var input = new MemoryStream(inputBytes);
        var session = new RecordingSession(width: 40, height: 5);

        await InvokeReadOutputAsync(
            input,
            session,
            forwardOutput: null,
            forwardOutputWriter: null,
            outputEncoding: outputEncoding
        );

        session.Events.Count.ShouldBe(1);
        session.Events[0].Data.ShouldBe(text);
    }

    [Test]
    public async Task ReadOutputAsync_ForwardsDecodedTextToTextWriter()
    {
        const string text = "\x1b[36m⣿ hello 日本語\x1b[0m";
        var inputBytes = Encoding.UTF8.GetBytes(text);
        await using var input = new ChunkedReadStream(inputBytes, maxChunkSize: 3);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var session = new RecordingSession(width: 40, height: 5);

        await InvokeReadOutputAsync(
            input,
            session,
            forwardOutput: null,
            forwardOutputWriter: writer,
            outputEncoding: Encoding.UTF8
        );

        writer.ToString().ShouldBe(text);
        session.Events.Count.ShouldBe(1);
        session.Events[0].Data.ShouldBe(text);
    }

    private static async Task InvokeReadOutputAsync(
        Stream readerStream,
        RecordingSession session,
        Stream? forwardOutput,
        TextWriter? forwardOutputWriter,
        Encoding outputEncoding
    )
    {
        var method =
            typeof(PtyRecorder).GetMethod(
                "ReadOutputAsync",
                BindingFlags.NonPublic | BindingFlags.Static
            ) ?? throw new InvalidOperationException("PtyRecorder.ReadOutputAsync was not found.");

        var stopwatch = Stopwatch.StartNew();
        var task =
            (Task?)
                method.Invoke(
                    null,
                    [
                        readerStream,
                        session,
                        stopwatch,
                        CancellationToken.None,
                        NullLogger.Instance,
                        forwardOutput,
                        forwardOutputWriter,
                        outputEncoding,
                    ]
                )
            ?? throw new InvalidOperationException("PtyRecorder.ReadOutputAsync did not return a Task.");
        await task.ConfigureAwait(false);
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maxChunkSize;

        public ChunkedReadStream(byte[] data, int maxChunkSize)
        {
            _inner = new MemoryStream(data, writable: false);
            _maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, Math.Min(count, _maxChunkSize));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            return _inner.ReadAsync(
                buffer,
                offset,
                Math.Min(count, _maxChunkSize),
                cancellationToken
            );
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var max = Math.Min(buffer.Length, _maxChunkSize);
            return _inner.ReadAsync(buffer[..max], cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
