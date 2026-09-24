using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;
using Porta.Pty;

namespace ConsoleToSvg.Recording;

public sealed class ManagedSessionManifest
{
    public string Id { get; set; } = string.Empty;
    public string PipeName { get; set; } = string.Empty;
    public int WorkerProcessId { get; set; }
    public DateTimeOffset? WorkerStartedAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

public sealed class ManagedSessionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string State { get; set; } = "starting";
    public int ProcessId { get; set; }
    public DateTimeOffset? ProcessStartedAt { get; set; }
    public int? ExitCode { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int CursorRow { get; set; }
    public int CursorColumn { get; set; }
    public bool CursorVisible { get; set; } = true;
    public bool IsAlternateScreen { get; set; }
    public int ScrollbackRows { get; set; }
    public long Version { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool TextTruncated { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreenBufferSnapshot? Screen { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ManagedSessionRequest
{
    public string Operation { get; set; } = string.Empty;
    public string[]? Command { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool NoDeleteEnvs { get; set; }
    public bool IncludeStructuredScreen { get; set; }
    public ManagedSessionInput[]? Inputs { get; set; }
    public string? Text { get; set; }
    public string? Key { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int WaitMs { get; set; }
    public long SinceVersion { get; set; } = -1;
    public string? OutputPath { get; set; }
    public SvgRenderOptions? RenderOptions { get; set; }
}

public sealed class ManagedSessionInput
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class ManagedSessionResponse
{
    public bool Success { get; set; } = true;
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public ManagedSessionSnapshot Session { get; set; } = new();
}

public sealed class ManagedSessionException(string code, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public string Code { get; } = code;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ManagedSessionManifest))]
[JsonSerializable(typeof(ManagedSessionSnapshot))]
[JsonSerializable(typeof(ManagedSessionRequest))]
[JsonSerializable(typeof(ManagedSessionResponse))]
internal sealed partial class ManagedSessionJsonContext : JsonSerializerContext { }

public static class ManagedTerminalSessionHost
{
    private static readonly TimeSpan ExitedSessionRetention = TimeSpan.FromHours(24);
    private const int MaxScreenTextLength = 200_000;

    public static async Task RunAsync(
        string pipeName,
        string sessionDirectory,
        CancellationToken cancellationToken
    )
    {
        var snapshotPath = Path.Combine(sessionDirectory, "snapshot.json");
        SessionRuntime? runtime = null;
        var shutdown = false;
        while (!shutdown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            var waitTask = server.WaitForConnectionAsync(waitCancellation.Token);
            if (runtime is not null)
            {
                var tasks = new List<Task> { waitTask, runtime.ProcessExitedTask };
                if (runtime.Snapshot.ExpiresAt is DateTimeOffset expiresAt)
                {
                    var remaining = expiresAt - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        await waitCancellation.CancelAsync().ConfigureAwait(false);
                        break;
                    }
                    tasks.Add(Task.Delay(remaining, cancellationToken));
                }

                if (await Task.WhenAny(tasks).ConfigureAwait(false) != waitTask)
                {
                    await waitCancellation.CancelAsync().ConfigureAwait(false);
                    break;
                }
            }
            await waitTask.ConfigureAwait(false);
            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true)
            {
                AutoFlush = true,
            };
            string? requestLine;
            try
            {
                requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            if (requestLine is null)
            {
                continue;
            }

            var request = JsonSerializer.Deserialize(
                requestLine,
                ManagedSessionJsonContext.Default.ManagedSessionRequest
            );
            if (request is null)
            {
                throw new InvalidDataException("The managed session request was empty.");
            }

            ManagedSessionResponse response;
            if (runtime is null && request.Operation == "ready")
            {
                response = new ManagedSessionResponse();
            }
            else if (runtime is null && request.Operation == "start")
            {
                runtime = await SessionRuntime
                    .StartAsync(sessionDirectory, snapshotPath, request, cancellationToken)
                    .ConfigureAwait(false);
                response = runtime.CreateResponse(includeText: true);
            }
            else if (runtime is null)
            {
                response = Failure("session_not_started", "The managed session has not started.");
            }
            else
            {
                (response, shutdown) = await runtime
                    .HandleAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }

            var responseJson = JsonSerializer.Serialize(
                response,
                ManagedSessionJsonContext.Default.ManagedSessionResponse
            );
            try
            {
                await writer
                    .WriteLineAsync(responseJson.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ManagedSessionResponse Failure(string code, string error) =>
        new() { Success = false, ErrorCode = code, Error = error };

    private sealed class SessionRuntime : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly string _snapshotPath;
        private readonly TerminalEmulator _emulator;
        private readonly Task _readerTask;
        private readonly Task _exitTask;
        private readonly IPtyConnection _connection;
        private readonly ManagedSessionSnapshot _snapshot;
        private readonly SemaphoreSlim _snapshotWriteSignal = new(0, 1);
        private readonly SemaphoreSlim _snapshotPersistenceGate = new(1, 1);
        private readonly CancellationTokenSource _snapshotWriterCancellation = new();
        private readonly Task _snapshotWriterTask;
        private TaskCompletionSource _changeSignal = NewScreenChanged();
        private ScreenBuffer? _pendingSnapshotBuffer;
        private bool _snapshotWriteQueued;
        private long _persistedVersion = -1;
        private bool _stopRequested;

        private SessionRuntime(
            string snapshotPath,
            IPtyConnection connection,
            TerminalEmulator emulator,
            ManagedSessionSnapshot snapshot
        )
        {
            _snapshotPath = snapshotPath;
            _connection = connection;
            _emulator = emulator;
            _snapshot = snapshot;
            _snapshot.Screen = _emulator.Buffer.CreateSnapshot();
            UpdateScreenMetadata(_snapshot, _emulator.Buffer);
            PersistSnapshotLocked();
            _snapshotWriterTask = SnapshotWriterAsync();
            _readerTask = ReadOutputAsync();
            _exitTask = MonitorExitAsync();
        }

        public ManagedSessionSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    return CopySnapshot(_snapshot);
                }
            }
        }

        public Task ProcessExitedTask => _exitTask;

        public static async Task<SessionRuntime> StartAsync(
            string sessionDirectory,
            string snapshotPath,
            ManagedSessionRequest request,
            CancellationToken cancellationToken
        )
        {
            if (request.Command is not { Length: > 0 })
            {
                throw new InvalidDataException("A command is required to start a session.");
            }

            var width = ValidateDimension(request.Width, nameof(request.Width));
            var height = ValidateDimension(request.Height, nameof(request.Height));
            var workingDirectory = Path.GetFullPath(
                request.WorkingDirectory ?? Environment.CurrentDirectory
            );
            var command = request.Command;
            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (
                System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables()
            )
            {
                if (item.Key is string key && item.Value is string value)
                {
                    environment[key] = value;
                }
            }

            environment["COLUMNS"] = width.ToString(CultureInfo.InvariantCulture);
            environment["LINES"] = height.ToString(CultureInfo.InvariantCulture);
            if (!request.NoDeleteEnvs)
            {
                environment.Remove("CI");
                environment.Remove("TF_BUILD");
            }

            var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var options = new PtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = workingDirectory,
                App = command[0],
                CommandLine = windows
                    ? PtyCommandLine.QuoteArgs(command[0], command[1..])
                    : command[1..],
                VerbatimCommandLine = windows,
                Environment = environment,
            };
            var connection = await PtyProvider
                .SpawnAsync(options, cancellationToken)
                .ConfigureAwait(false);
            var emulator = new TerminalEmulator(width, height, Theme.Resolve("dark"));
            var id = new DirectoryInfo(sessionDirectory).Name;
            var snapshot = new ManagedSessionSnapshot
            {
                Id = id,
                State = "running",
                ProcessId = connection.Pid,
                ProcessStartedAt = GetProcessStartedAt(connection.Pid),
                Width = width,
                Height = height,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return new SessionRuntime(snapshotPath, connection, emulator, snapshot);
        }

        public async Task<(ManagedSessionResponse Response, bool Shutdown)> HandleAsync(
            ManagedSessionRequest request,
            CancellationToken cancellationToken
        )
        {
            try
            {
                switch (request.Operation)
                {
                    case "read":
                        return (
                            await ReadAsync(request, cancellationToken).ConfigureAwait(false),
                            false
                        );
                    case "send":
                        await SendAsync(request, cancellationToken).ConfigureAwait(false);
                        return (CreateResponse(includeText: false), false);
                    case "resize":
                        Resize(request.Width, request.Height);
                        return (CreateResponse(includeText: false), false);
                    case "capture":
                        await CaptureAsync(request, cancellationToken).ConfigureAwait(false);
                        return (CreateResponse(includeText: false), false);
                    case "stop":
                        await StopAsync().ConfigureAwait(false);
                        return (CreateResponse(includeText: true), true);
                    default:
                        return (
                            Failure(
                                "invalid_operation",
                                $"Unsupported session operation '{request.Operation}'."
                            ),
                            false
                        );
                }
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or InvalidDataException
                            or ArgumentException
                            or IOException
                            or UnauthorizedAccessException
                )
            {
                var code = exception switch
                {
                    ManagedSessionException managed => managed.Code,
                    InvalidDataException or ArgumentException => "invalid_request",
                    UnauthorizedAccessException => "permission_denied",
                    IOException => "io_error",
                    InvalidOperationException => "session_exited",
                    _ => "session_error",
                };
                return (Failure(code, exception.Message), false);
            }
        }

        public ManagedSessionResponse CreateResponse(
            bool includeText,
            bool includeStructuredScreen = false
        )
        {
            lock (_gate)
            {
                var snapshot = CopySnapshot(_snapshot);
                if (!includeText)
                {
                    snapshot.Text = string.Empty;
                }
                if (includeStructuredScreen)
                {
                    snapshot.Screen = _emulator.Buffer.CreateSnapshot();
                }
                // Durable terminal state stays in snapshot.json. It is needed only
                // after the host exits, and would make every IPC response large.
                if (!includeStructuredScreen)
                {
                    snapshot.Screen = null;
                }
                return new ManagedSessionResponse { Session = snapshot };
            }
        }

        private async Task<ManagedSessionResponse> ReadAsync(
            ManagedSessionRequest request,
            CancellationToken cancellationToken
        )
        {
            Task changeTask;
            long version;
            lock (_gate)
            {
                version = _snapshot.Version;
                if (
                    request.WaitMs <= 0
                    || _snapshot.State != "running"
                    || (request.SinceVersion >= 0 && request.SinceVersion != version)
                )
                {
                    return CreateResponse(
                        includeText: true,
                        includeStructuredScreen: request.IncludeStructuredScreen
                    );
                }

                changeTask = _changeSignal.Task;
            }

            var delay = Task.Delay(Math.Min(request.WaitMs, 100), cancellationToken);
            await Task.WhenAny(changeTask, delay).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return CreateResponse(
                includeText: true,
                includeStructuredScreen: request.IncludeStructuredScreen
            );
        }

        private async Task SendAsync(
            ManagedSessionRequest request,
            CancellationToken cancellationToken
        )
        {
            byte[] input;
            lock (_gate)
            {
                EnsureRunning();
                input = EncodeInputs(request);
            }

            await _connection
                .WriterStream.WriteAsync(input, cancellationToken)
                .ConfigureAwait(false);
            await _connection.WriterStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private byte[] EncodeInputs(ManagedSessionRequest request)
        {
            if (request.Inputs is not { Length: > 0 } inputs)
            {
                return request.Text is not null
                    ? Encoding.UTF8.GetBytes(request.Text)
                    : EncodeKey(request.Key, _emulator.InputModes);
            }

            using var stream = new MemoryStream();
            foreach (var input in inputs)
            {
                if (input is null || input.Value is null)
                {
                    throw new InvalidDataException("A session input step is incomplete.");
                }

                var bytes = input.Type switch
                {
                    "text" => Encoding.UTF8.GetBytes(input.Value),
                    "paste" => EncodePaste(input.Value, _emulator.InputModes),
                    "key" => EncodeKey(input.Value, _emulator.InputModes),
                    "raw" => EncodeRawHex(input.Value),
                    _ => throw new InvalidDataException(
                        $"Unsupported session input type '{input.Type}'."
                    ),
                };
                stream.Write(bytes);
            }

            return stream.ToArray();
        }

        private void Resize(int width, int height)
        {
            ValidateDimension(width, nameof(width));
            ValidateDimension(height, nameof(height));
            lock (_gate)
            {
                EnsureRunning();
                _connection.Resize(width, height);
                _emulator.Resize(width, height);
                _snapshot.Width = width;
                _snapshot.Height = height;
                ChangedLocked();
            }
        }

        private async Task CaptureAsync(
            ManagedSessionRequest request,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(request.OutputPath))
            {
                throw new InvalidDataException("An output path is required for session capture.");
            }

            ScreenBuffer screen;
            lock (_gate)
            {
                screen = _emulator.Buffer.Clone();
            }
            var path = Path.GetFullPath(request.OutputPath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var svg = SvgRenderer.Render(
                screen,
                request.RenderOptions
                    ?? new SvgRenderOptions { TerminalTheme = Theme.Resolve("dark") }
            );
            await File.WriteAllTextAsync(
                    path,
                    svg,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        private async Task StopAsync()
        {
            lock (_gate)
            {
                _stopRequested = true;
                if (_snapshot.State == "running" && !TryKillConnection())
                    _snapshot.ExitCode ??= _connection.ExitCode;
            }

            await Task.WhenAny(
                    _exitTask,
                    Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None)
                )
                .ConfigureAwait(false);
            lock (_gate)
            {
                _snapshot.State = "stopped";
                _snapshot.ExitCode ??= _connection.ExitCode;
                _snapshot.ExpiresAt = DateTimeOffset.UtcNow + ExitedSessionRetention;
                ChangedLocked();
            }
            await PersistLatestSnapshotAsync().ConfigureAwait(false);
        }

        private async Task ReadOutputAsync()
        {
            var chars = new char[4096];
            using var reader = new StreamReader(
                _connection.ReaderStream,
                new UTF8Encoding(false),
                false,
                4096,
                true
            );
            try
            {
                while (true)
                {
                    var count = await reader.ReadAsync(chars.AsMemory()).ConfigureAwait(false);
                    if (count <= 0)
                    {
                        break;
                    }

                    lock (_gate)
                    {
                        _emulator.Process(new string(chars, 0, count));
                        ChangedLocked();
                    }
                }
            }
            catch (IOException)
            {
                var stateChanged = false;
                lock (_gate)
                {
                    if (_snapshot.State == "running")
                    {
                        _snapshot.State = "unavailable";
                        _snapshot.ExpiresAt = DateTimeOffset.UtcNow + ExitedSessionRetention;
                        ChangedLocked();
                        stateChanged = true;
                    }
                }
                if (stateChanged)
                {
                    await PersistLatestSnapshotAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task MonitorExitAsync()
        {
            while (!_connection.WaitForExit(100))
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            var stateChanged = false;
            lock (_gate)
            {
                if (_snapshot.State == "running")
                {
                    _snapshot.State = _stopRequested ? "stopped" : "exited";
                    _snapshot.ExitCode = _connection.ExitCode;
                    _snapshot.ExpiresAt = DateTimeOffset.UtcNow + ExitedSessionRetention;
                    ChangedLocked();
                    stateChanged = true;
                }
            }
            if (stateChanged)
            {
                await PersistLatestSnapshotAsync().ConfigureAwait(false);
            }
        }

        private void EnsureRunning()
        {
            if (_snapshot.State != "running")
            {
                throw new ManagedSessionException(
                    "session_exited",
                    $"Session '{_snapshot.Id}' is {_snapshot.State}; it cannot accept input or be resized."
                );
            }
        }

        private void ChangedLocked()
        {
            _snapshot.Version++;
            (_snapshot.Text, _snapshot.TextTruncated) = ReadScreenText(_emulator.Buffer);
            UpdateScreenMetadata(_snapshot, _emulator.Buffer);
            _snapshot.UpdatedAt = DateTimeOffset.UtcNow;
            var previous = _changeSignal;
            _changeSignal = NewScreenChanged();
            previous.TrySetResult();
            _pendingSnapshotBuffer = _emulator.Buffer.Clone();
            if (!_snapshotWriteQueued)
            {
                _snapshotWriteQueued = true;
                _snapshotWriteSignal.Release();
            }
        }

        private void PersistSnapshotLocked()
        {
            var temporaryPath = _snapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var json = JsonSerializer.Serialize(
                _snapshot,
                ManagedSessionJsonContext.Default.ManagedSessionSnapshot
            );
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, _snapshotPath, overwrite: true);
            _persistedVersion = _snapshot.Version;
        }

        private async Task SnapshotWriterAsync()
        {
            var cancellationToken = _snapshotWriterCancellation.Token;
            try
            {
                while (true)
                {
                    await _snapshotWriteSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                        .ConfigureAwait(false);

                    ScreenBuffer? screenBuffer;
                    ManagedSessionSnapshot snapshot;
                    lock (_gate)
                    {
                        screenBuffer = _pendingSnapshotBuffer;
                        _pendingSnapshotBuffer = null;
                        _snapshotWriteQueued = false;
                        snapshot = CopySnapshot(_snapshot);
                    }

                    if (screenBuffer is not null)
                    {
                        snapshot.Screen = screenBuffer.CreateSnapshot();
                    }

                    try
                    {
                        await PersistSnapshotAsync(snapshot).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                        when (exception is IOException or UnauthorizedAccessException or JsonException)
                    {
                        await Console.Error
                            .WriteLineAsync(
                                $"Could not persist managed session snapshot: {exception.Message}"
                            )
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        private async Task PersistLatestSnapshotAsync()
        {
            ScreenBuffer screenBuffer;
            ManagedSessionSnapshot snapshot;
            lock (_gate)
            {
                screenBuffer = _emulator.Buffer.Clone();
                snapshot = CopySnapshot(_snapshot);
            }
            snapshot.Screen = screenBuffer.CreateSnapshot();
            await PersistSnapshotAsync(snapshot).ConfigureAwait(false);
        }

        private async Task PersistSnapshotAsync(ManagedSessionSnapshot snapshot)
        {
            await _snapshotPersistenceGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (snapshot.Version <= _persistedVersion)
                {
                    return;
                }

                var temporaryPath = _snapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var json = JsonSerializer.Serialize(
                        snapshot,
                        ManagedSessionJsonContext.Default.ManagedSessionSnapshot
                    );
                    await File.WriteAllTextAsync(
                            temporaryPath,
                            json,
                            new UTF8Encoding(false),
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    File.Move(temporaryPath, _snapshotPath, overwrite: true);
                    _persistedVersion = snapshot.Version;
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
            finally
            {
                _snapshotPersistenceGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Snapshot.State == "running")
            {
                _ = TryKillConnection();
            }

            await Task.WhenAny(
                    _exitTask,
                    Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None)
                )
                .ConfigureAwait(false);
            await Task.WhenAny(
                    _readerTask,
                    Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None)
                )
                .ConfigureAwait(false);
            _snapshotWriterCancellation.Cancel();
            try
            {
                await _snapshotWriterTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            await PersistLatestSnapshotAsync().ConfigureAwait(false);
            _connection.Dispose();
            _snapshotWriterCancellation.Dispose();
            _snapshotWriteSignal.Dispose();
            _snapshotPersistenceGate.Dispose();
        }

        private bool TryKillConnection()
        {
            try
            {
                _connection.Kill();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static int ValidateDimension(int value, string name)
        {
            if (value is < 1 or > 500)
            {
                throw new ArgumentOutOfRangeException(name, "Terminal dimensions must be 1-500.");
            }

            return value;
        }

        private static DateTimeOffset? GetProcessStartedAt(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return new DateTimeOffset(process.StartTime.ToUniversalTime());
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static byte[] EncodeKey(string? key, TerminalInputModes inputModes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ManagedSessionException("unsupported_key", "A key name is required.");
            }

            var normalized = key.Trim();
            if (normalized.Length == 1 && !char.IsControl(normalized[0]))
            {
                return Encoding.UTF8.GetBytes(normalized);
            }

            var namedSequence = GetNamedKeySequence(normalized, inputModes);
            if (namedSequence is not null)
            {
                return Encoding.UTF8.GetBytes(namedSequence);
            }

            return EncodeModifiedKey(normalized);
        }

        private static string? GetNamedKeySequence(string key, TerminalInputModes inputModes)
        {
            var normalized = key.ToLowerInvariant();
            var keypadSequence = GetKeypadKeySequence(normalized, inputModes.ApplicationKeypad);
            if (keypadSequence is not null)
            {
                return keypadSequence;
            }

            return normalized switch
            {
                "enter" or "return" => "\r",
                "tab" => "\t",
                "shift+tab" => "\u001b[Z",
                "escape" or "esc" => "\u001b",
                "backspace" => "\u007f",
                "insert" => "\u001b[2~",
                "delete" => "\u001b[3~",
                "up" => inputModes.ApplicationCursorKeys ? "\u001bOA" : "\u001b[A",
                "down" => inputModes.ApplicationCursorKeys ? "\u001bOB" : "\u001b[B",
                "right" => inputModes.ApplicationCursorKeys ? "\u001bOC" : "\u001b[C",
                "left" => inputModes.ApplicationCursorKeys ? "\u001bOD" : "\u001b[D",
                "home" => inputModes.ApplicationCursorKeys ? "\u001bOH" : "\u001b[H",
                "end" => inputModes.ApplicationCursorKeys ? "\u001bOF" : "\u001b[F",
                "pageup" => "\u001b[5~",
                "pagedown" => "\u001b[6~",
                "f1" => "\u001bOP",
                "f2" => "\u001bOQ",
                "f3" => "\u001bOR",
                "f4" => "\u001bOS",
                "f5" => "\u001b[15~",
                "f6" => "\u001b[17~",
                "f7" => "\u001b[18~",
                "f8" => "\u001b[19~",
                "f9" => "\u001b[20~",
                "f10" => "\u001b[21~",
                "f11" => "\u001b[23~",
                "f12" => "\u001b[24~",
                _ => null,
            };
        }

        private static string? GetKeypadKeySequence(string key, bool applicationKeypad)
        {
            var digitIndex = key.Length == 3 && key.StartsWith("kp", StringComparison.Ordinal)
                ? "0123456789".IndexOf(key[2])
                : -1;
            if (digitIndex >= 0)
            {
                return applicationKeypad
                    ? $"\u001bO{(char)('p' + digitIndex)}"
                    : digitIndex.ToString(CultureInfo.InvariantCulture);
            }

            return key switch
            {
                "kpdecimal" => applicationKeypad ? "\u001bOn" : ".",
                "kpenter" => applicationKeypad ? "\u001bOM" : "\r",
                "kpadd" => applicationKeypad ? "\u001bOk" : "+",
                "kpsubtract" => applicationKeypad ? "\u001bOm" : "-",
                "kpmultiply" => applicationKeypad ? "\u001bOj" : "*",
                "kpdivide" => applicationKeypad ? "\u001bOo" : "/",
                "kpseparator" => applicationKeypad ? "\u001bOl" : ",",
                _ => null,
            };
        }

        private static byte[] EncodePaste(string text, TerminalInputModes inputModes)
        {
            var content = Encoding.UTF8.GetBytes(text);
            if (!inputModes.BracketedPaste)
            {
                return content;
            }

            return [
                .. Encoding.UTF8.GetBytes("\u001b[200~"),
                .. content,
                .. Encoding.UTF8.GetBytes("\u001b[201~"),
            ];
        }

        private static byte[] EncodeModifiedKey(string key)
        {
            var parts = key.Split('+');
            if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
            {
                throw new ManagedSessionException(
                    "unsupported_key",
                    $"Unsupported terminal key '{key}'."
                );
            }

            var modifiers = 0;
            foreach (var part in parts[..^1])
            {
                var flag = part.ToLowerInvariant() switch
                {
                    "shift" => 1,
                    "alt" or "meta" => 2,
                    "ctrl" or "control" => 4,
                    _ => 0,
                };
                if (flag == 0 || (modifiers & flag) != 0)
                {
                    throw new ManagedSessionException(
                        "unsupported_key",
                        $"Unsupported terminal key '{key}'."
                    );
                }
                modifiers |= flag;
            }

            var value = parts[^1];
            if (
                value.Length == 1
                && char.IsAsciiLetter(value[0])
                && (modifiers & 4) != 0
            )
            {
                var control = (char)(char.ToUpperInvariant(value[0]) & 0x1f);
                var bytes = Encoding.UTF8.GetBytes(control.ToString());
                return (modifiers & 2) == 0 ? bytes : [0x1b, .. bytes];
            }

            var modifierNumber = 1 + modifiers;
            var lowerValue = value.ToLowerInvariant();
            var sequence = lowerValue switch
            {
                "up" => $"\u001b[1;{modifierNumber}A",
                "down" => $"\u001b[1;{modifierNumber}B",
                "right" => $"\u001b[1;{modifierNumber}C",
                "left" => $"\u001b[1;{modifierNumber}D",
                "home" => $"\u001b[1;{modifierNumber}H",
                "end" => $"\u001b[1;{modifierNumber}F",
                "tab" => modifiers == 1
                    ? "\u001b[Z"
                    : $"\u001b[1;{modifierNumber}Z",
                "insert" => $"\u001b[2;{modifierNumber}~",
                "delete" => $"\u001b[3;{modifierNumber}~",
                "pageup" => $"\u001b[5;{modifierNumber}~",
                "pagedown" => $"\u001b[6;{modifierNumber}~",
                "f1" => $"\u001b[1;{modifierNumber}P",
                "f2" => $"\u001b[1;{modifierNumber}Q",
                "f3" => $"\u001b[1;{modifierNumber}R",
                "f4" => $"\u001b[1;{modifierNumber}S",
                "f5" => $"\u001b[15;{modifierNumber}~",
                "f6" => $"\u001b[17;{modifierNumber}~",
                "f7" => $"\u001b[18;{modifierNumber}~",
                "f8" => $"\u001b[19;{modifierNumber}~",
                "f9" => $"\u001b[20;{modifierNumber}~",
                "f10" => $"\u001b[21;{modifierNumber}~",
                "f11" => $"\u001b[23;{modifierNumber}~",
                "f12" => $"\u001b[24;{modifierNumber}~",
                _ => null,
            };
            if (sequence is not null)
            {
                return Encoding.UTF8.GetBytes(sequence);
            }

            if (value.Length == 1 && !char.IsControl(value[0]) && modifiers == 2)
            {
                return [0x1b, .. Encoding.UTF8.GetBytes(value)];
            }
            if (value.Length == 1 && !char.IsControl(value[0]) && modifiers == 1)
            {
                return Encoding.UTF8.GetBytes(char.ToUpperInvariant(value[0]).ToString());
            }

            throw new ManagedSessionException(
                "unsupported_key",
                $"Unsupported terminal key '{key}'."
            );
        }

        private static byte[] EncodeRawHex(string input)
        {
            if (input.Length == 0 || input.Length % 2 != 0)
            {
                throw new InvalidDataException(
                    "Raw terminal input must be a non-empty, even-length hexadecimal byte string."
                );
            }

            try
            {
                return Convert.FromHexString(input);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "Raw terminal input must be a non-empty, even-length hexadecimal byte string.",
                    exception
                );
            }
        }

        private static (string Text, bool Truncated) ReadScreenText(ScreenBuffer buffer)
        {
            var rows = new string[buffer.Height];
            var lastNonEmpty = -1;
            for (var row = 0; row < buffer.Height; row++)
            {
                var line = new StringBuilder(buffer.Width);
                for (var col = 0; col < buffer.Width; col++)
                {
                    var value = buffer.GetCell(row, col).Text;
                    if (!string.IsNullOrEmpty(value))
                    {
                        line.Append(value);
                    }
                }
                rows[row] = line.ToString().TrimEnd();
                if (rows[row].Length > 0)
                {
                    lastNonEmpty = row;
                }
            }

            if (lastNonEmpty < 0)
            {
                return (string.Empty, false);
            }

            var text = string.Join('\n', rows.Take(lastNonEmpty + 1));
            if (text.Length <= MaxScreenTextLength)
            {
                return (text, false);
            }

            var length = char.IsHighSurrogate(text[MaxScreenTextLength - 1])
                ? MaxScreenTextLength - 1
                : MaxScreenTextLength;
            return (text[..length], true);
        }

        private static ManagedSessionSnapshot CopySnapshot(ManagedSessionSnapshot source) =>
            new()
            {
                Id = source.Id,
                State = source.State,
                ProcessId = source.ProcessId,
                ProcessStartedAt = source.ProcessStartedAt,
                ExitCode = source.ExitCode,
                Width = source.Width,
                Height = source.Height,
                CursorRow = source.CursorRow,
                CursorColumn = source.CursorColumn,
                CursorVisible = source.CursorVisible,
                IsAlternateScreen = source.IsAlternateScreen,
                ScrollbackRows = source.ScrollbackRows,
                Version = source.Version,
                Text = source.Text,
                TextTruncated = source.TextTruncated,
                Screen = source.Screen,
                UpdatedAt = source.UpdatedAt,
                ExpiresAt = source.ExpiresAt,
            };

        private static TaskCompletionSource NewScreenChanged() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static void UpdateScreenMetadata(
            ManagedSessionSnapshot snapshot,
            ScreenBuffer buffer
        )
        {
            snapshot.CursorRow = buffer.CursorRow;
            snapshot.CursorColumn = buffer.CursorCol;
            snapshot.CursorVisible = buffer.CursorVisible;
            snapshot.IsAlternateScreen = buffer.IsAlternateScreen;
            snapshot.ScrollbackRows = buffer.ScrollbackCount;
        }
    }
}

public static class ManagedTerminalSessionManager
{
    private static readonly TimeSpan ExitedSessionRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan PipeRequestTimeout = TimeSpan.FromSeconds(5);
    private const string RootDirectoryName = "sessions";

    public static string GetSessionRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "console2svg",
            RootDirectoryName
        );

    public static async Task<ManagedSessionResponse> StartAsync(
        string[] command,
        int width,
        int height,
        string workingDirectory,
        bool noDeleteEnvs,
        CancellationToken cancellationToken
    )
    {
        if (command.Length == 0)
        {
            throw new ArgumentException("A command is required.", nameof(command));
        }

        var root = GetSessionRoot();
        EnsurePrivateDirectory(root);
        var id = $"s_{Guid.NewGuid():N}";
        var directory = Path.Combine(root, id);
        EnsurePrivateDirectory(directory);
        var pipeName = $"c2s-{Guid.NewGuid():N}";
        var manifest = new ManagedSessionManifest
        {
            Id = id,
            PipeName = pipeName,
            StartedAt = DateTimeOffset.UtcNow,
        };
        WriteJson(
            Path.Combine(directory, "manifest.json"),
            manifest,
            ManagedSessionJsonContext.Default.ManagedSessionManifest
        );
        WriteJson(
            Path.Combine(directory, "snapshot.json"),
            new ManagedSessionSnapshot
            {
                Id = id,
                State = "starting",
                Width = width,
                Height = height,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            ManagedSessionJsonContext.Default.ManagedSessionSnapshot
        );

        using var host = StartHostProcess(pipeName, directory);
        manifest.WorkerProcessId = host.Id;
        manifest.WorkerStartedAt = new DateTimeOffset(host.StartTime.ToUniversalTime());
        WriteJson(
            Path.Combine(directory, "manifest.json"),
            manifest,
            ManagedSessionJsonContext.Default.ManagedSessionManifest
        );

        try
        {
            await WaitForPipeAsync(pipeName, host, cancellationToken).ConfigureAwait(false);
            var response = await SendToPipeAsync(
                    pipeName,
                    new ManagedSessionRequest
                    {
                        Operation = "start",
                        Command = command,
                        Width = width,
                        Height = height,
                        WorkingDirectory = workingDirectory,
                        NoDeleteEnvs = noDeleteEnvs,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            EnsureSuccess(response);
            return response;
        }
        catch (Exception exception)
        {
            if (!host.HasExited)
            {
                host.Kill(entireProcessTree: true);
            }
            await host.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var hostOutput = await host
                .StandardOutput.ReadToEndAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var hostError = await host
                .StandardError.ReadToEndAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var hostLogPath = Path.Combine(directory, "host.log");
            var hostLog = File.Exists(hostLogPath)
                ? await File.ReadAllTextAsync(hostLogPath, CancellationToken.None)
                    .ConfigureAwait(false)
                : string.Empty;
            if (hostLog.Length > 4096)
            {
                hostLog = hostLog[^4096..];
            }
            Directory.Delete(directory, recursive: true);
            var diagnostics = string.Join(
                Environment.NewLine,
                new[] { hostOutput.Trim(), hostError.Trim(), hostLog.Trim() }.Where(text =>
                    text.Length > 0
                )
            );
            if (diagnostics.Length > 0)
            {
                throw new InvalidOperationException(
                    $"{exception.Message} Session host: {diagnostics}",
                    exception
                );
            }
            throw;
        }
    }

    public static async Task<ManagedSessionResponse> RequestAsync(
        string id,
        ManagedSessionRequest request,
        CancellationToken cancellationToken
    )
    {
        var (manifest, directory) = LoadManifest(id);
        var initialSnapshot = LoadSnapshot(directory);
        if (initialSnapshot.State is "exited" or "stopped" or "unavailable")
        {
            switch (request.Operation)
            {
                case "read":
                    return new ManagedSessionResponse { Session = initialSnapshot };
                case "send":
                case "resize":
                    throw new ManagedSessionException(
                        "session_exited",
                        $"Session '{id}' is {initialSnapshot.State}; it cannot accept input or be resized."
                    );
                case "capture":
                    await RenderSnapshotAsync(initialSnapshot, request, cancellationToken)
                        .ConfigureAwait(false);
                    return new ManagedSessionResponse { Session = initialSnapshot };
                case "stop":
                    if (initialSnapshot.State == "unavailable")
                    {
                        throw new ManagedSessionException(
                            "host_unavailable",
                            $"The host for session '{id}' is unavailable; it could not be stopped."
                        );
                    }
                    if (initialSnapshot.State == "exited")
                    {
                        initialSnapshot.State = "stopped";
                        initialSnapshot.UpdatedAt = DateTimeOffset.UtcNow;
                        initialSnapshot.ExpiresAt ??=
                            DateTimeOffset.UtcNow + ExitedSessionRetention;
                        WriteJson(
                            Path.Combine(directory, "snapshot.json"),
                            initialSnapshot,
                            ManagedSessionJsonContext.Default.ManagedSessionSnapshot
                        );
                    }
                    await DeleteSessionDirectoryAsync(manifest, directory, cancellationToken)
                        .ConfigureAwait(false);
                    return new ManagedSessionResponse { Session = initialSnapshot };
            }
        }

        try
        {
            using var pipeTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            pipeTimeout.CancelAfter(PipeRequestTimeout);
            var response = await SendToPipeWithRetryAsync(manifest, request, pipeTimeout.Token)
                .ConfigureAwait(false);
            EnsureSuccess(response);
            if (request.Operation == "stop")
            {
                await DeleteSessionDirectoryAsync(manifest, directory, cancellationToken)
                    .ConfigureAwait(false);
            }
            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ManagedSessionException(
                "host_unavailable",
                $"The host for session '{id}' did not respond within {PipeRequestTimeout.TotalSeconds:0} seconds.",
                exception
            );
        }
        catch (Exception exception)
            when (exception is (IOException or TimeoutException)
                && (request.Operation is "read" or "stop")
            )
        {
            var snapshot = LoadSnapshot(directory);
            if (request.Operation == "stop" && snapshot.State != "running")
            {
                if (snapshot.State == "unavailable")
                {
                    throw new ManagedSessionException(
                        "host_unavailable",
                        $"The host for session '{id}' is unavailable; it could not be stopped."
                    );
                }
                snapshot.State = "stopped";
                snapshot.UpdatedAt = DateTimeOffset.UtcNow;
                snapshot.ExpiresAt ??= DateTimeOffset.UtcNow + ExitedSessionRetention;
                WriteJson(
                    Path.Combine(directory, "snapshot.json"),
                    snapshot,
                    ManagedSessionJsonContext.Default.ManagedSessionSnapshot
                );
                await DeleteSessionDirectoryAsync(manifest, directory, cancellationToken)
                    .ConfigureAwait(false);
                return new ManagedSessionResponse { Session = snapshot };
            }

            if (snapshot.State != "running")
            {
                return new ManagedSessionResponse { Session = snapshot };
            }

            throw new ManagedSessionException(
                "host_unavailable",
                $"The host for session '{id}' is unavailable. The session state could not be changed: {exception.Message}"
            );
        }
        catch (Exception exception)
            when (request.Operation == "capture" && exception is (IOException or TimeoutException))
        {
            var snapshot = LoadSnapshot(directory);
            if (snapshot.State == "running")
            {
                throw new ManagedSessionException(
                    "host_unavailable",
                    $"The host for session '{id}' is unavailable. The screen cannot be captured: {exception.Message}"
                );
            }
            await RenderSnapshotAsync(snapshot, request, cancellationToken).ConfigureAwait(false);
            return new ManagedSessionResponse { Session = snapshot };
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            throw new ManagedSessionException(
                "host_unavailable",
                $"The host for session '{id}' is unavailable: {exception.Message}",
                exception
            );
        }
    }

    private static async Task RenderSnapshotAsync(
        ManagedSessionSnapshot snapshot,
        ManagedSessionRequest request,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new InvalidOperationException("An output path is required for session capture.");
        }

        var theme = request.RenderOptions?.TerminalTheme ?? Theme.Resolve("dark");
        var screen = snapshot.Screen is { } savedScreen
            ? ScreenBuffer.FromSnapshot(savedScreen, theme)
            : RestoreLegacyTextSnapshot(snapshot, theme);
        var outputPath = Path.GetFullPath(request.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        await File.WriteAllTextAsync(
                outputPath,
                SvgRenderer.Render(
                    screen,
                    request.RenderOptions ?? new SvgRenderOptions()
                ),
                new UTF8Encoding(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static ScreenBuffer RestoreLegacyTextSnapshot(
        ManagedSessionSnapshot snapshot,
        Theme theme
    )
    {
        var emulator = new TerminalEmulator(snapshot.Width, snapshot.Height, theme);
        emulator.Process(snapshot.Text);
        return emulator.Buffer;
    }

    public static IReadOnlyList<ManagedSessionSnapshot> List()
    {
        var root = GetSessionRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        var sessions = new List<ManagedSessionSnapshot>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(directory);
            if (!IsValidId(id))
            {
                continue;
            }

            var snapshotPath = Path.Combine(directory, "snapshot.json");
            if (!File.Exists(snapshotPath))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }
            var manifest =
                JsonSerializer.Deserialize(
                    File.ReadAllText(manifestPath),
                    ManagedSessionJsonContext.Default.ManagedSessionManifest
                )
                ?? throw new InvalidDataException(
                    $"The managed session '{id}' has invalid metadata."
                );
            var snapshot = RefreshUnavailable(directory, manifest, LoadSnapshot(directory));
            if (snapshot.ExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
            {
                Directory.Delete(directory, recursive: true);
                continue;
            }

            sessions.Add(snapshot);
        }

        return sessions.OrderBy(session => session.UpdatedAt).ToArray();
    }

    private static async Task DeleteSessionDirectoryAsync(
        ManagedSessionManifest manifest,
        string directory,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var host = Process.GetProcessById(manifest.WorkerProcessId);
            if (
                !host.HasExited
                && (
                    manifest.WorkerStartedAt is not DateTimeOffset startedAt
                    || Math.Abs(
                        (host.StartTime.ToUniversalTime() - startedAt.UtcDateTime).TotalSeconds
                    ) < 2
                )
            )
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                waitCancellation.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await host.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"The host for session '{manifest.Id}' did not stop in time; its files were retained."
                    );
                }
            }
        }
        catch (ArgumentException)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            return;
        }

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static string[] GetHostArguments(string pipeName, string directory) =>
        ["session", "host", "--pipe", pipeName, "--directory", directory];

    private static Process StartHostProcess(string pipeName, string directory)
    {
        var processPath =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Cannot determine the console2svg executable path."
            );
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var commandLine = Environment.GetCommandLineArgs();
        if (
            Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (commandLine.Length == 0)
            {
                throw new InvalidOperationException(
                    "Cannot determine the console2svg assembly path."
                );
            }
            startInfo.ArgumentList.Add(commandLine[0]);
        }
        foreach (var argument in GetHostArguments(pipeName, directory))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the managed session host.");
        return process;
    }

    private static async Task WaitForPipeAsync(
        string pipeName,
        Process host,
        CancellationToken cancellationToken
    )
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (host.HasExited)
            {
                throw new InvalidOperationException(
                    $"The managed session host exited with code {host.ExitCode}."
                );
            }

            try
            {
                var response = await SendToPipeAsync(
                        pipeName,
                        new ManagedSessionRequest { Operation = "ready" },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                EnsureSuccess(response);
                return;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("Timed out while starting the managed session host.");
    }

    private static async Task<ManagedSessionResponse> SendToPipeAsync(
        string pipeName,
        ManagedSessionRequest request,
        CancellationToken cancellationToken,
        ManagedSessionManifest? manifest = null
    )
    {
        using var client = await ConnectToPipeAsync(pipeName, cancellationToken, manifest)
            .ConfigureAwait(false);
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true)
        {
            AutoFlush = true,
        };
        var requestJson = JsonSerializer.Serialize(
            request,
            ManagedSessionJsonContext.Default.ManagedSessionRequest
        );
        await writer
            .WriteLineAsync(requestJson.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        var responseJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (responseJson is null)
        {
            throw new IOException(
                "The managed session host closed the connection without a response."
            );
        }
        return JsonSerializer.Deserialize(
                responseJson,
                ManagedSessionJsonContext.Default.ManagedSessionResponse
            )
            ?? throw new InvalidDataException(
                "The managed session host returned an empty response."
            );
    }

    private static async Task<ManagedSessionResponse> SendToPipeWithRetryAsync(
        ManagedSessionManifest manifest,
        ManagedSessionRequest request,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            try
            {
                return await SendToPipeAsync(
                        manifest.PipeName,
                        request,
                        cancellationToken,
                        manifest
                    )
                    .ConfigureAwait(false);
            }
            catch (IOException)
                when ((request.Operation is "read" or "capture") && IsHostRunning(manifest))
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<NamedPipeClientStream> ConnectToPipeAsync(
        string pipeName,
        CancellationToken cancellationToken,
        ManagedSessionManifest? manifest
    )
    {
        while (true)
        {
            var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            try
            {
                await client.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception exception)
                when (manifest is not null && exception is (TimeoutException or IOException))
            {
                await client.DisposeAsync().ConfigureAwait(false);
                if (!IsHostRunning(manifest))
                {
                    throw new IOException(
                        $"The host for session '{manifest.Id}' is unavailable.",
                        exception
                    );
                }
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private static bool IsHostRunning(ManagedSessionManifest manifest)
    {
        try
        {
            using var process = Process.GetProcessById(manifest.WorkerProcessId);
            if (process.HasExited)
            {
                return false;
            }

            return manifest.WorkerStartedAt is not DateTimeOffset startedAt
                || Math.Abs(
                    (process.StartTime.ToUniversalTime() - startedAt.UtcDateTime).TotalSeconds
                ) < 2;
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            return false;
        }
    }

    private static (ManagedSessionManifest Manifest, string Directory) LoadManifest(string id)
    {
        if (!IsValidId(id))
        {
            throw new ManagedSessionException(
                "session_not_found",
                $"Unknown managed session ID '{id}'."
            );
        }

        var directory = Path.Combine(GetSessionRoot(), id);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ManagedSessionException(
                "session_not_found",
                $"Unknown or expired managed session ID '{id}'."
            );
        }

        var manifest =
            JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath),
                ManagedSessionJsonContext.Default.ManagedSessionManifest
            )
            ?? throw new InvalidDataException($"The managed session '{id}' has invalid metadata.");
        var snapshot = RefreshUnavailable(directory, manifest, LoadSnapshot(directory));
        if (snapshot.ExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
        {
            Directory.Delete(directory, recursive: true);
            throw new ManagedSessionException(
                "session_expired",
                $"Managed session '{id}' has expired."
            );
        }

        return (manifest, directory);
    }

    private static ManagedSessionSnapshot RefreshUnavailable(
        string directory,
        ManagedSessionManifest manifest,
        ManagedSessionSnapshot snapshot
    )
    {
        if (snapshot.State is not ("running" or "starting" or "unavailable"))
        {
            return snapshot;
        }

        var workerAlive = false;
        try
        {
            using var worker = Process.GetProcessById(manifest.WorkerProcessId);
            workerAlive =
                !worker.HasExited
                && (
                    manifest.WorkerStartedAt is not DateTimeOffset startedAt
                    || Math.Abs(
                        (worker.StartTime.ToUniversalTime() - startedAt.UtcDateTime).TotalSeconds
                    ) < 2
                );
        }
        catch (ArgumentException)
        {
            workerAlive = false;
        }

        if (workerAlive)
        {
            return snapshot;
        }

        var state = IsSessionProcessAlive(snapshot) ? "unavailable" : "exited";
        if (snapshot.State == state)
        {
            return snapshot;
        }

        snapshot.State = state;
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        snapshot.ExpiresAt = DateTimeOffset.UtcNow + ExitedSessionRetention;
        WriteJson(
            Path.Combine(directory, "snapshot.json"),
            snapshot,
            ManagedSessionJsonContext.Default.ManagedSessionSnapshot
        );
        return snapshot;
    }

    private static bool IsSessionProcessAlive(ManagedSessionSnapshot snapshot)
    {
        if (snapshot.ProcessId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(snapshot.ProcessId);
            return !process.HasExited
                && (
                    snapshot.ProcessStartedAt is not DateTimeOffset startedAt
                    || Math.Abs(
                        (process.StartTime.ToUniversalTime() - startedAt.UtcDateTime).TotalSeconds
                    ) < 2
                );
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ManagedSessionSnapshot LoadSnapshot(string directory)
    {
        var snapshotPath = Path.Combine(directory, "snapshot.json");
        if (!File.Exists(snapshotPath))
        {
            throw new InvalidDataException("The managed session snapshot is missing.");
        }

        return JsonSerializer.Deserialize(
                File.ReadAllText(snapshotPath),
                ManagedSessionJsonContext.Default.ManagedSessionSnapshot
            ) ?? throw new InvalidDataException("The managed session snapshot is invalid.");
    }

    private static void EnsureSuccess(ManagedSessionResponse response)
    {
        if (!response.Success)
        {
            throw new ManagedSessionException(
                response.ErrorCode ?? "session_error",
                response.Error ?? "Managed session operation failed."
            );
        }
    }

    private static bool IsValidId(string id) =>
        id.Length == 34
        && id.StartsWith("s_", StringComparison.Ordinal)
        && id.AsSpan(2).IndexOfAnyExcept("0123456789abcdef".AsSpan()) < 0;

    private static void EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static void WriteJson<T>(
        string path,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo
    )
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }
}
