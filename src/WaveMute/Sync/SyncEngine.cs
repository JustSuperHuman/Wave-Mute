namespace WaveMute.Sync;

public sealed record SyncDecision(bool TargetMuted, bool WriteWave, bool WriteDiscord, string Reason, bool RetryLimitReached);

// This class has no device/UI dependencies. Time comes from the caller so races
// and late acknowledgements can be exercised deterministically in tests.
public sealed class SyncEngine
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan EchoLifetime = TimeSpan.FromSeconds(3);
    private const int MaxAttempts = 3;
    private bool? _lastWave;
    private bool? _lastDiscord;
    private bool _target;
    private string _reason = "Initial connection";
    private readonly Destination _wave = new();
    private readonly Destination _discord = new();

    public SyncDecision Step(bool wave, bool discord, bool deafened, TimeSpan now)
    {
        var waveEcho = _wave.Observe(wave, _lastWave, now);
        var discordEcho = _discord.Observe(discord, _lastDiscord, now);
        var waveChanged = _lastWave.HasValue && wave != _lastWave && !waveEcho;
        var discordChanged = _lastDiscord.HasValue && discord != _lastDiscord && !discordEcho;
        var target = _target;
        if (!_lastWave.HasValue)
        {
            target = wave || discord;
            _reason = "Connected: mute wins an initial disagreement";
        }
        else if (waveChanged && discordChanged)
        {
            target = wave || discord;
            _reason = "Both changed: mute wins a simultaneous disagreement";
        }
        else if (waveChanged)
        {
            target = wave;
            _reason = "Wave → Discord";
        }
        else if (discordChanged)
        {
            target = discord;
            _reason = "Discord → Wave";
        }

        if (deafened)
        {
            target = true;
            _reason = "Discord is deafened: keep the microphone muted";
        }
        if (target != _target || waveChanged || discordChanged)
        {
            _wave.ResetAttempts();
            _discord.ResetAttempts();
        }
        _target = target;
        _lastWave = wave;
        _lastDiscord = discord;
        var writeWave = _wave.ShouldWrite(wave, target, now);
        var writeDiscord = _discord.ShouldWrite(discord, target, now);
        return new(target, writeWave, writeDiscord, _reason,
            (wave != target && _wave.Exhausted) || (discord != target && _discord.Exhausted));
    }

    public void Reset()
    {
        _lastWave = _lastDiscord = null;
        _wave.Clear();
        _discord.Clear();
    }

    private sealed class Destination
    {
        private readonly List<(bool Muted, TimeSpan At)> _writes = [];
        private TimeSpan? _lastWrite;
        private int _attempts;
        public bool Exhausted => _attempts >= MaxAttempts;

        public bool Observe(bool value, bool? previous, TimeSpan now)
        {
            _writes.RemoveAll(w => now - w.At > EchoLifetime);
            if (value == previous || !previous.HasValue) return false;
            var echo = _writes.Any(w => w.Muted == value);
            _writes.RemoveAll(w => w.Muted == value);
            return echo;
        }

        public bool ShouldWrite(bool actual, bool target, TimeSpan now)
        {
            if (actual == target) { _attempts = 0; return false; }
            if (Exhausted || (_lastWrite.HasValue && now - _lastWrite < RetryDelay)) return false;
            _attempts++;
            _lastWrite = now;
            _writes.Add((target, now));
            return true;
        }

        public void ResetAttempts() { _attempts = 0; _lastWrite = null; }
        public void Clear() { _writes.Clear(); _lastWrite = null; _attempts = 0; }
    }
}
