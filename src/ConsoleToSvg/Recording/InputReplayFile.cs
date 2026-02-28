using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public static class InputReplayFile
{
    public static async Task<List<(double Time, string Data)>> ReadAllAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        var events = new List<(double Time, string Data)>();
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            if (
                doc.RootElement.ValueKind != JsonValueKind.Array
                || doc.RootElement.GetArrayLength() < 2
            )
            {
                continue;
            }

            var time = doc.RootElement[0].GetDouble();
            var data = doc.RootElement[1].GetString() ?? string.Empty;
            events.Add((time, data));
        }

        return events;
    }

    public static void WriteEvent(TextWriter writer, double timeSeconds, string data)
    {
        using var ms = new MemoryStream();
        using var jw = new Utf8JsonWriter(ms);
        jw.WriteStartArray();
        jw.WriteNumberValue(timeSeconds);
        jw.WriteStringValue(data);
        jw.WriteEndArray();
        jw.Flush();
        var line = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        writer.WriteLine(line);
    }

    public sealed class ReplayStream : Stream
    {
        private readonly (double Time, byte[] Data)[] _events;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _eventIndex;
        private int _eventOffset;

        public ReplayStream(IList<(double Time, string Data)> events)
        {
            _events = new (double, byte[])[events.Count];
            for (var i = 0; i < events.Count; i++)
            {
                _events[i] = (events[i].Time, Encoding.UTF8.GetBytes(events[i].Data));
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Use ReadAsync.");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            while (_eventIndex < _events.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (time, data) = _events[_eventIndex];

                if (_eventOffset == 0)
                {
                    var delay = time - _stopwatch.Elapsed.TotalSeconds;
                    if (delay > 0)
                    {
                        await Task
                            .Delay(TimeSpan.FromSeconds(delay), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                var available = data.Length - _eventOffset;
                if (available <= 0)
                {
                    _eventIndex++;
                    _eventOffset = 0;
                    continue;
                }

                var toCopy = Math.Min(count, available);
                Array.Copy(data, _eventOffset, buffer, offset, toCopy);
                _eventOffset += toCopy;
                if (_eventOffset >= data.Length)
                {
                    _eventIndex++;
                    _eventOffset = 0;
                }

                return toCopy;
            }

            return 0;
        }
    }
}
