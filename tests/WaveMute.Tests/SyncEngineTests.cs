using WaveMute.Sync;
using Xunit;

namespace WaveMute.Tests;

public sealed class SyncEngineTests
{
    private readonly SyncEngine _engine = new();
    private SyncDecision Step(bool wave, bool discord, int ms = 0, bool deafen = false) => _engine.Step(wave, discord, deafen, TimeSpan.FromMilliseconds(ms));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingStartupDoesNotWrite(bool muted)
    {
        var decision = Step(muted, muted);
        Assert.Equal(muted, decision.TargetMuted);
        Assert.False(decision.WriteWave || decision.WriteDiscord);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StartupMismatchMutesTheOtherSide(bool wave, bool discord)
    {
        var d = Step(wave, discord);
        Assert.True(d.TargetMuted);
        Assert.Equal(!wave, d.WriteWave);
        Assert.Equal(!discord, d.WriteDiscord);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HardwareChangesPropagateOnce(bool start)
    {
        Step(start, start);
        var write = Step(!start, start, 125);
        Assert.Equal(!start, write.TargetMuted);
        Assert.True(write.WriteDiscord);
        Assert.False(write.WriteWave);
        var acknowledged = Step(!start, !start, 250);
        Assert.False(acknowledged.WriteDiscord || acknowledged.WriteWave);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscordChangesPropagateOnce(bool start)
    {
        Step(start, start);
        var write = Step(start, !start, 125);
        Assert.Equal(!start, write.TargetMuted);
        Assert.True(write.WriteWave);
        Assert.False(write.WriteDiscord);
        var acknowledged = Step(!start, !start, 250);
        Assert.False(acknowledged.WriteDiscord || acknowledged.WriteWave);
    }

    [Fact]
    public void DelayedAcknowledgementCannotReverseANewerDiscordChange()
    {
        Step(false, false);
        Assert.True(Step(false, true, 125).WriteWave);
        Assert.False(Step(false, false, 250).TargetMuted);
        var lateEcho = Step(true, false, 1000);
        Assert.False(lateEcho.TargetMuted);
        Assert.True(lateEcho.WriteWave);
        Assert.False(lateEcho.WriteDiscord);
        Assert.False(Step(false, false, 1125).WriteWave);
    }

    [Fact]
    public void DelayedAcknowledgementCannotReverseANewerHardwareChange()
    {
        Step(false, false);
        Assert.True(Step(true, false, 125).WriteDiscord);
        Assert.False(Step(false, false, 250).TargetMuted);
        var lateEcho = Step(false, true, 1000);
        Assert.False(lateEcho.TargetMuted);
        Assert.True(lateEcho.WriteDiscord);
        Assert.False(lateEcho.WriteWave);
    }

    [Fact]
    public void UnacknowledgedChangesAreRateLimitedAndBounded()
    {
        Step(false, false);
        Assert.True(Step(true, false, 125).WriteDiscord);
        Assert.False(Step(true, false, 250).WriteDiscord);
        Assert.True(Step(true, false, 875).WriteDiscord);
        var finalAttempt = Step(true, false, 1625);
        Assert.True(finalAttempt.WriteDiscord);
        Assert.True(Step(true, false, 3000).RetryLimitReached);
        Assert.False(Step(true, false, 4000).WriteDiscord);
    }

    [Fact]
    public void AcknowledgementClearsRetryWarning()
    {
        Step(false, false);
        Step(true, false, 125); Step(true, false, 875); Step(true, false, 1625);
        var d = Step(true, true, 1800);
        Assert.False(d.RetryLimitReached);
        Assert.False(d.WriteDiscord || d.WriteWave);
    }

    [Fact]
    public void DeafenMutesHardwareAndNeverUndeafens()
    {
        Step(false, false);
        var d = Step(false, true, 125, deafen: true);
        Assert.True(d.TargetMuted);
        Assert.True(d.WriteWave);
        Assert.False(Step(false, true, 250, deafen: true).WriteDiscord);
        Assert.True(Step(false, true, 375, deafen: true).TargetMuted);
    }

    [Fact]
    public void HardwareCannotUnmuteWhileDiscordIsDeafened()
    {
        Step(true, true, deafen: true);
        var d = Step(false, true, 125, deafen: true);
        Assert.True(d.TargetMuted);
        Assert.True(d.WriteWave);
        Assert.False(d.WriteDiscord);
    }

    [Fact]
    public void ReconnectDiscardsOldStateAndUsesMuteFirst()
    {
        Step(false, false);
        _engine.Reset();
        var d = Step(false, true, 1000);
        Assert.True(d.TargetMuted);
        Assert.True(d.WriteWave);
        Assert.False(d.WriteDiscord);
    }

    [Fact]
    public void RepeatedGenuineTogglesAreNotMistakenForEchoes()
    {
        Step(false, false);
        for (var i = 0; i < 20; i++)
        {
            var muted = i % 2 == 0;
            var d = Step(muted, !muted, 300 * (i + 1));
            Assert.Equal(muted, d.TargetMuted);
            Assert.True(d.WriteDiscord);
            var ack = Step(muted, muted, 300 * (i + 1) + 125);
            Assert.False(ack.WriteWave || ack.WriteDiscord);
        }
    }

    [Fact]
    public void ReversingMuteDoesNotWaitForTheRetryDelay()
    {
        Step(false, false);
        Assert.True(Step(true, false, 125).WriteDiscord);
        Step(true, true, 250);
        var reversed = Step(false, true, 375);
        Assert.False(reversed.TargetMuted);
        Assert.True(reversed.WriteDiscord);
    }

    [Fact]
    public void ANewUserChangeRecoversFromExhaustedRetries()
    {
        Step(false, false);
        Step(true, false, 125); Step(true, false, 875); Step(true, false, 1625);
        Assert.True(Step(true, false, 2000).RetryLimitReached);
        var reversed = Step(false, false, 2125);
        Assert.False(reversed.TargetMuted);
        Assert.False(reversed.RetryLimitReached);
        Assert.True(Step(true, false, 2250).WriteDiscord);
    }

    [Fact]
    public void DelayedAndInterleavedChangesEventuallyConverge()
    {
        // Exercise 100 repeatable asynchronous device/UI schedules, followed
        // by a quiet period. Writes have independently randomized latency.
        for (var seed = 0; seed < 100; seed++)
        {
            var random = new Random(seed);
            var engine = new SyncEngine();
            var wave = false;
            var discord = false;
            var pending = new List<(int Due, bool Wave, bool Muted)>();
            SyncDecision? decision = null;
            for (var ms = 0; ms < 20_000; ms += 50)
            {
                foreach (var write in pending.Where(w => w.Due <= ms).OrderBy(w => w.Due).ToArray())
                {
                    if (write.Wave) wave = write.Muted; else discord = write.Muted;
                    pending.Remove(write);
                }
                if (ms < 10_000 && random.Next(10) == 0)
                {
                    if (random.Next(2) == 0) wave = !wave; else discord = !discord;
                }
                decision = engine.Step(wave, discord, false, TimeSpan.FromMilliseconds(ms));
                if (decision.WriteWave) pending.Add((ms + random.Next(1, 500), true, decision.TargetMuted));
                if (decision.WriteDiscord) pending.Add((ms + random.Next(1, 500), false, decision.TargetMuted));
            }
            Assert.Equal(wave, discord);
            Assert.Equal(wave, decision!.TargetMuted);
            Assert.False(decision.WriteWave || decision.WriteDiscord || decision.RetryLimitReached);
        }
    }
}
