using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Mozaic.Platform;

namespace Mozaic.Ipc;

/// <summary>Named-pipe server (current user only) for mozaic-msg and scripts.</summary>
internal sealed class IpcServer(WindowManager wm, string pipeName)
{
    private sealed class Subscriber
    {
        public required HashSet<string> Events { get; init; }
        public BlockingCollection<string> Queue { get; } = new(boundedCapacity: 1000);
    }

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Subscriber> _subscribers = new();

    public string PipeName => pipeName;

    public void Start() => _ = Task.Run(AcceptLoop);

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var client = server;
                server = null;
                _ = Task.Run(() => HandleClient(client));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Log.Error("ipc accept", e);
                try { await Task.Delay(1000, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task HandleClient(NamedPipeServerStream pipe)
    {
        Subscriber? sub = null;
        try
        {
            var utf8 = new UTF8Encoding(false);
            using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            string? line;
            while ((line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false)) != null)
            {
                var (type, payload) = IpcProtocol.ParseRequest(line);
                if (type == "subscribe")
                {
                    sub = new Subscriber { Events = ParseEvents(payload) };
                    lock (_subscribers) _subscribers.Add(sub);
                    await writer.WriteLineAsync("{\"success\":true}").ConfigureAwait(false);
                    // From now on this connection only streams events.
                    foreach (var evt in sub.Queue.GetConsumingEnumerable(_cts.Token))
                        await writer.WriteLineAsync(evt).ConfigureAwait(false);
                    break;
                }
                string response;
                try
                {
                    response = Dispatcher.Invoke(() => wm.HandleIpc(type, payload));
                }
                catch (Exception e)
                {
                    response = "{\"success\":false,\"error\":" + JsonSerializerString(e.Message) + "}";
                }
                await writer.WriteLineAsync(response).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (Exception e)
        {
            Log.Error("ipc client", e);
        }
        finally
        {
            if (sub != null)
            {
                lock (_subscribers) _subscribers.Remove(sub);
                sub.Queue.Dispose();
            }
            pipe.Dispose();
        }
    }

    private static HashSet<string> ParseEvents(string payload)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(payload.Length == 0 ? "[]" : payload);
            foreach (var e in doc.RootElement.EnumerateArray()) set.Add(e.GetString() ?? "");
        }
        catch (JsonException)
        {
            foreach (var p in payload.Split(',', ' ', StringSplitOptions.RemoveEmptyEntries)) set.Add(p);
        }
        return set;
    }

    private static string JsonSerializerString(string s)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) w.WriteStringValue(s);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Queues an event for subscribers (never blocks the main thread).</summary>
    public void Broadcast(string evt, string change)
    {
        lock (_subscribers)
        {
            if (_subscribers.Count == 0) return;
            string json = "{\"event\":" + JsonSerializerString(evt) + ",\"change\":" + JsonSerializerString(change) + "}";
            foreach (var s in _subscribers.ToList())
            {
                if (s.Events.Count > 0 && !s.Events.Contains(evt)) continue;
                if (!s.Queue.TryAdd(json)) _subscribers.Remove(s);
            }
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        lock (_subscribers)
        {
            foreach (var s in _subscribers) s.Queue.CompleteAdding();
            _subscribers.Clear();
        }
    }
}
