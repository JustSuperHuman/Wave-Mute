using System.IO.Pipes;
using System.Text.Json;
using WaveMute.Sync;

namespace WaveMute;

internal static class CommandPipe
{
    public const string Name = "WaveMute.Local.v1";

    public static async Task ServeAsync(SyncService service, Action exit, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var command = await reader.ReadLineAsync(requestTimeout.Token);
                switch (command)
                {
                    case "probe" or "wave-status" or "wave-mute true" or "wave-mute false":
                        await writer.WriteLineAsync(JsonSerializer.Serialize(await service.ExecuteAsync(command)));
                        continue;
                    case "pause": service.SetPaused(true); break;
                    case "resume": service.SetPaused(false); break;
                    case "reconnect": service.Reconnect(); break;
                    case "exit": break;
                    case "status": break;
                    default: await writer.WriteLineAsync("Unknown command"); continue;
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(service.Status));
                if (command == "exit") { exit(); return; }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { }
            catch (IOException error) { AppLog.Write($"Control pipe: {error.Message}"); }
        }
    }

    public static async Task<string?> SendAsync(string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2000, timeout.Token);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(command);
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (TimeoutException) { return null; }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
    }
}
