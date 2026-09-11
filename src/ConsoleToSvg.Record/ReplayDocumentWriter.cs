using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

internal interface IReplayInputRecorder
{
    void AppendEvent(InputEvent inputEvent);
}

/// <summary>Collects captured input and writes a complete canonical v2 session.</summary>
internal sealed class ReplayDocumentWriter : IReplayInputRecorder
{
    private readonly string _path;
    private readonly ReplayDocumentV2 _document;
    private readonly List<InputEvent> _events = [];

    internal ReplayDocumentWriter(string path, ReplayDocumentV2 document)
    {
        _path = path;
        _document = document;
    }

    public void AppendEvent(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        _events.Add(
            new InputEvent
            {
                Time = inputEvent.Time,
                Tick = inputEvent.Tick,
                Key = inputEvent.Key,
                Modifiers = [.. inputEvent.Modifiers],
                Type = inputEvent.Type,
            }
        );
    }

    internal Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _document.Steps = ReplayMigration.MigrateEvents(_events);
        return ReplayDocumentFile.WriteAsync(_path, _document, cancellationToken);
    }
}
