using WaveMute.Discord;
using WaveMute.Hardware;
using WaveMute.Sync;
using System.Runtime.InteropServices;

namespace WaveMute;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--status"])
            {
                var running = CommandPipe.SendAsync("status").GetAwaiter().GetResult();
                if (running is not null) { Console.WriteLine(running); return 0; }
            }
            if (args is ["--probe"] or ["--wave-status"] || (args is ["--wave-mute", var requested] && bool.TryParse(requested, out _)))
            {
                var request = args.Length == 1 ? args[0][2..] : "wave-mute " + args[1].ToLowerInvariant();
                var running = CommandPipe.SendAsync(request).GetAwaiter().GetResult();
                if (running is not null)
                {
                    var message = System.Text.Json.JsonSerializer.Deserialize<string>(running)!;
                    Console.WriteLine(message);
                    return message.StartsWith("ERROR:", StringComparison.Ordinal) ? 1 : 0;
                }
            }
            if (args.Length == 1 && args[0] is "--pause" or "--resume" or "--reconnect" or "--exit")
            {
                var reply = CommandPipe.SendAsync(args[0][2..]).GetAwaiter().GetResult();
                Console.WriteLine(reply ?? "Wave Mute is not running.");
                return reply is null ? 1 : 0;
            }
            using var wave = new WaveDevice();
            if (args is ["--wave-status"])
            {
                Console.WriteLine($"Wave:3 muted: {wave.ReadMute()}");
                Console.WriteLine($"Config: {Convert.ToHexString(wave.ReadConfiguration())}");
                return 0;
            }
            if (args is ["--status"] or ["--probe"])
            {
                Console.WriteLine($"Wave:3 muted: {wave.ReadMute()}");
                using var discord = new DiscordMute();
                Console.WriteLine(discord.Diagnose());
                return 0;
            }
            if (args is ["--wave-mute", var value] && bool.TryParse(value, out var muted))
            {
                wave.SetMute(muted);
                Console.WriteLine($"Wave:3 muted: {wave.ReadMute()}");
                return 0;
            }
            if (args is ["--discord-mute", var discordValue] && bool.TryParse(discordValue, out var discordMuted))
            {
                using var discord = new DiscordMute();
                discord.SetMute(discordMuted);
                Console.WriteLine("Discord mute requested. Use --probe to verify acknowledgement.");
                return 0;
            }
            if (args.Length > 0)
            {
                Console.WriteLine("Usage: WaveMute [--status|--probe|--wave-status|--pause|--resume|--reconnect|--exit|--console]\nDiagnostics: --wave-mute true|false, --discord-mute true|false");
                if (args is not ["--console"]) return args is ["--help"] ? 0 : 2;
            }
            using var singleton = new Mutex(true, "Local\\WaveMute.v1", out var firstInstance);
            if (!firstInstance) { Console.WriteLine("Wave Mute is already running. Use --status to inspect it."); return 0; }
            try
            {
                if (args is ["--console"])
                {
                    using var service = new SyncService();
                    using var stop = new CancellationTokenSource();
                    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
                    var server = CommandPipe.ServeAsync(service, stop.Cancel, stop.Token);
                    Console.WriteLine("Wave Mute running. Ctrl+C to stop.");
                    while (!stop.Token.WaitHandle.WaitOne(1000)) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(service.Status));
                    server.GetAwaiter().GetResult();
                }
                else
                {
                    FreeConsole();
                    ApplicationConfiguration.Initialize();
                    Application.Run(new TrayApp());
                }
                return 0;
            }
            finally { singleton.ReleaseMutex(); }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }

    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeConsole();
}
