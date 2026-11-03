using System.Diagnostics;
using System.Collections.Concurrent;
using WaveMute.Discord;
using WaveMute.Hardware;

namespace WaveMute.Sync;

public sealed record BridgeStatus(string State, string Detail, bool? WaveMuted, bool? DiscordMuted, bool? DiscordDeafened, DateTimeOffset UpdatedAt);

public sealed class SyncService : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;
    private volatile bool _paused;
    private int _reset;
    private readonly ConcurrentQueue<(string Command, TaskCompletionSource<string> Completion)> _commands = new();
    private BridgeStatus _status = new("Starting", "Connecting to Wave:3 and Discord…", null, null, null, DateTimeOffset.Now);
    public BridgeStatus Status => Volatile.Read(ref _status);
    public bool Paused => _paused;

    public SyncService()
    {
        _worker = new Thread(Run) { Name = "Wave Mute sync", IsBackground = true };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public void SetPaused(bool paused) { _paused = paused; Interlocked.Exchange(ref _reset, 1); }
    public void Reconnect() => Interlocked.Exchange(ref _reset, 1);
    public Task<string> ExecuteAsync(string command)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue((command, completion));
        return completion.Task;
    }

    private void Run()
    {
        using var wave = new WaveDevice();
        using var discord = new DiscordMute();
        var engine = new SyncEngine();
        var clock = Stopwatch.StartNew();
        while (!_stop.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _reset, 0) != 0) { engine.Reset(); wave.Dispose(); discord.Reset(); }
            while (_commands.TryDequeue(out var request))
            {
                try
                {
                    switch (request.Command)
                    {
                        case "probe":
                            request.Completion.SetResult($"Wave:3 muted: {wave.ReadMute()}\nConfig: {Convert.ToHexString(wave.ReadConfiguration())}\n{discord.Diagnose()}");
                            break;
                        case "wave-status": request.Completion.SetResult($"Wave:3 muted: {wave.ReadMute()}"); break;
                        case "wave-mute true": wave.SetMute(true); request.Completion.SetResult("Wave:3 muted: True"); break;
                        case "wave-mute false": wave.SetMute(false); request.Completion.SetResult("Wave:3 muted: False"); break;
                        default: request.Completion.SetResult("Unknown diagnostic command"); break;
                    }
                }
                catch (Exception error) { request.Completion.SetResult($"ERROR: {error.Message}"); }
            }
            if (_paused)
            {
                Publish("Paused", "Mute changes are independent while paused.", null, null, null);
                if (_stop.Token.WaitHandle.WaitOne(150)) break;
                continue;
            }
            bool? hardware = null;
            DiscordSnapshot? voice = null;
            try
            {
                hardware = wave.ReadMute();
                voice = discord.Read();
                var decision = engine.Step(hardware.Value, voice.Muted, voice.Deafened, clock.Elapsed);
                if (decision.WriteWave || decision.WriteDiscord)
                {
                    AppLog.Write($"{decision.Reason}: {(decision.TargetMuted ? "muted" : "unmuted")}; write Wave={decision.WriteWave}, Discord={decision.WriteDiscord}");
                    // For mute, silence hardware first. For unmute, let Discord
                    // acknowledge first when both destinations need a write.
                    if (decision.TargetMuted && decision.WriteWave) wave.SetMute(true);
                    if (decision.WriteDiscord) discord.SetMute(decision.TargetMuted);
                    if (!decision.TargetMuted && decision.WriteWave) wave.SetMute(false);
                }
                var inSync = hardware == decision.TargetMuted && voice.Muted == decision.TargetMuted;
                Publish(decision.RetryLimitReached ? "Needs attention" : inSync ? "Connected" : "Syncing",
                    decision.RetryLimitReached ? "A mute change was not acknowledged. Choose Reconnect to retry." :
                    voice.Deafened ? "Discord is deafened; microphone stays muted." :
                    inSync ? decision.TargetMuted ? "Wave and Discord are muted." : "Wave and Discord are unmuted." : decision.Reason,
                    hardware, voice.Muted, voice.Deafened);
            }
            catch (Exception error)
            {
                // A recovered connection is a new baseline. Mute wins instead
                // of replaying a stale unmute from before the disconnect.
                engine.Reset();
                Publish("Waiting", error.Message, hardware, voice?.Muted, voice?.Deafened);
                if (_stop.Token.WaitHandle.WaitOne(1700)) break;
            }
            if (_stop.Token.WaitHandle.WaitOne(125)) break;
        }
    }

    private void Publish(string state, string detail, bool? wave, bool? discord, bool? deafened)
    {
        var previous = Status;
        if (previous.State == state && previous.Detail == detail && previous.WaveMuted == wave && previous.DiscordMuted == discord && previous.DiscordDeafened == deafened) return;
        Volatile.Write(ref _status, new(state, detail, wave, discord, deafened, DateTimeOffset.Now));
        AppLog.Write($"{state}: {detail}");
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_worker.Join(TimeSpan.FromSeconds(4))) _stop.Dispose();
    }
}
