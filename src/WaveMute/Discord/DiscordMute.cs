using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace WaveMute.Discord;

public sealed record DiscordSnapshot(bool Muted, bool Deafened, string Method);

public sealed class DiscordMute : IDisposable
{
    private readonly UIA3Automation _automation = new()
    {
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        TransactionTimeout = TimeSpan.FromSeconds(1)
    };
    private AutomationElement? _root;
    private AutomationElement? _mute;
    private AutomationElement? _deafen;
    private nint _window;

    public DiscordSnapshot Read()
    {
        try
        {
            EnsureControls();
            return new(ReadState(_mute!), _deafen is not null && ReadState(_deafen), "Windows accessibility");
        }
        catch
        {
            Reset();
            throw;
        }
    }

    public void SetMute(bool muted)
    {
        var state = Read();
        if (state.Muted == muted) return;
        if (!muted && state.Deafened) throw new InvalidOperationException("Discord is deafened. Undeafen in Discord before unmuting.");
        if (!_mute!.IsEnabled) throw new InvalidOperationException("Discord's mute switch is disabled.");
        if (_mute.Patterns.Toggle.IsSupported) _mute.Patterns.Toggle.Pattern.Toggle();
        else if (_mute.Patterns.Invoke.IsSupported) _mute.Patterns.Invoke.Pattern.Invoke();
        else if (_mute.Patterns.LegacyIAccessible.IsSupported) _mute.Patterns.LegacyIAccessible.Pattern.DoDefaultAction();
        else throw new InvalidOperationException("Discord's mute switch has no supported accessibility action.");
    }

    public string Diagnose()
    {
        var snapshot = Read();
        return $"Muted={snapshot.Muted}; Deafened={snapshot.Deafened}; Minimized={IsIconic(_window)}; {Describe(_mute!)}";
    }

    private void EnsureControls()
    {
        if (_mute is not null && IsWindow(_window)) return;
        var windows = new List<nint>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName is "Discord" or "DiscordPTB" or "DiscordCanary" && process.MainWindowHandle != 0)
                        windows.Add(process.MainWindowHandle);
                }
                catch (InvalidOperationException) { } // Process exited during enumeration.
            }
        }
        if (windows.Count == 0) throw new IOException("Discord desktop is not running or its window is closed to the tray. Open its main window.");
        if (windows.Count > 1) throw new IOException("More than one Discord window is open. Keep one Discord client open.");
        _window = windows[0];
        _root = _automation.FromHandle(_window);
        var panel = _root.FindFirstDescendant(cf => cf.ByName("User status and settings")) ?? _root;
        var candidates = panel.FindAllDescendants(cf => cf.ByName("Mute").Or(cf.ByName("Unmute")));
        var switches = candidates.Where(e => IsSwitch(e) || e.ControlType == ControlType.Button).ToArray();
        _mute = switches.FirstOrDefault(IsSwitch) ?? switches.SingleOrDefault();
        if (_mute is null) throw new IOException("Discord's mute switch is unavailable. Open the main Discord window, sign in, and close Settings or modal dialogs. The current adapter requires Discord in English.");
        _deafen = panel.FindFirstDescendant(cf => cf.ByName("Deafen").Or(cf.ByName("Undeafen")));
        if (_deafen is null) throw new IOException("Discord's deafen state is unavailable. Open its main window and choose Reconnect.");
    }

    private static bool IsSwitch(AutomationElement element) =>
        string.Equals(element.Properties.AriaRole.ValueOrDefault, "switch", StringComparison.OrdinalIgnoreCase)
        || element.Patterns.Toggle.IsSupported;

    public static bool ParseAriaChecked(string properties)
    {
        foreach (var property in properties.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = property.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && (pair[0] == "checked" || pair[0] == "pressed"))
                return pair[1] switch { "true" => true, "false" => false, _ => throw new InvalidOperationException("Discord mute state is indeterminate.") };
        }
        throw new InvalidOperationException("Discord's accessibility control did not expose its mute state.");
    }

    private static bool ReadState(AutomationElement element)
    {
        if (element.Patterns.Toggle.IsSupported)
            return element.Patterns.Toggle.Pattern.ToggleState.Value switch { ToggleState.On => true, ToggleState.Off => false, _ => throw new InvalidOperationException("Discord mute state is indeterminate.") };
        var aria = element.Properties.AriaProperties.ValueOrDefault;
        if (!string.IsNullOrEmpty(aria) && (aria.Contains("checked=", StringComparison.Ordinal) || aria.Contains("pressed=", StringComparison.Ordinal)))
        {
            return ParseAriaChecked(aria);
        }
        if (element.Patterns.LegacyIAccessible.IsSupported)
        {
            var state = element.Patterns.LegacyIAccessible.Pattern.State.Value;
            return ((uint)state & (0x10u | 0x8u)) != 0; // STATE_SYSTEM_CHECKED or PRESSED
        }
        if (element.Name == "Unmute") return true;
        if (element.Name == "Mute" && element.ControlType == ControlType.Button) return false;
        throw new InvalidOperationException("Discord's mute state could not be determined.");
    }

    private static string Describe(AutomationElement element) =>
        $"Name={element.Name}; Type={element.ControlType}; Role={element.Properties.AriaRole.ValueOrDefault}; Aria={element.Properties.AriaProperties.ValueOrDefault}; Patterns={string.Join(',', element.GetSupportedPatterns().Select(p => p.Name))}";

    public void Reset() { _root = null; _mute = null; _deafen = null; _window = 0; }
    public void Dispose() => _automation.Dispose();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
}
