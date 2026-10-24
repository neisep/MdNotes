using System.IO.Pipes;

namespace MdNotes.Services;

/// <summary>
/// Keeps MdNotes to one process per user. The first instance listens on a named pipe; a later
/// launch hands its file over and exits, so double-clicking a second note opens another window
/// rather than another copy of the app.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int ConnectTimeoutMs = 300;

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Held by the listening instance for as long as it listens, so a launch can tell there is no one
    /// to hand off to without waiting out a pipe connect. Global, because the pipe is: on Unix an
    /// unprefixed name is scoped to the login session, and a launch from a terminal would miss an
    /// instance started from the desktop.
    /// </summary>
    private Mutex? _listening;

    private string ListeningName => $@"Global\{_pipeName}";

    /// <summary>Raised off the UI thread with the requested path, or "" for a plain launch.</summary>
    public event Action<string>? FileRequested;

    public SingleInstance(string appName)
    {
        var user = string.Concat(Environment.UserName.Where(char.IsLetterOrDigit));
        _pipeName = $"{appName}-{user}";
    }

    /// <summary>
    /// Offers the request to an instance that is already running. True when one took it, meaning
    /// this process has nothing left to do.
    /// </summary>
    public bool TryHandOff(string? filePath)
    {
        // The common cold start: nothing is running, so there is no pipe, and Connect would retry for
        // its whole timeout before saying so - 300 ms before Avalonia even starts. Anything other than
        // a clean "does not exist" falls through to the connect, which stays the real test: a mutex
        // left behind by a crashed instance just costs the timeout it always did.
        if (!InstanceMayBeListening()) return false;

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(filePath ?? string.Empty);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Starts answering later launches. Call only after <see cref="TryHandOff"/> came back false.</summary>
    public void Listen()
    {
        try
        {
            _listening = new Mutex(initiallyOwned: false, ListeningName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException
                                       or PlatformNotSupportedException)
        {
            // Without it later launches still find the pipe; they only wait for the timeout to learn
            // when it is not there.
        }
        _ = Task.Run(ListenLoopAsync);
    }

    private bool InstanceMayBeListening()
    {
        try
        {
            if (!Mutex.TryOpenExisting(ListeningName, out var running)) return false;
            running.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return true;
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync(_cts.Token) is { } line)
                    FileRequested?.Invoke(line);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A half-open connection: drop it and take the next one.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _listening?.Dispose();
    }
}
