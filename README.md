# Wave Mute

Sync your Elgato Wave:3 mute button with Discord, both ways.

1. Download the ZIP from [Releases](https://github.com/JustSuperHuman/Wave-Mute/releases/latest).
2. Extract it and run **WaveMute.exe**.
3. Keep Discord open. Tap the mic's mute sensor or toggle mute in Discord.

The tray icon is red when muted, green when unmuted, and gray when paused or disconnected. Right-click it to pause, reconnect, or exit.

Requires Windows 10/11 x64, a first-generation Wave:3, and Discord desktop in English. Runs locally without a Discord login or separate .NET installation.

Mute wins if the states disagree when connecting. Deafen keeps the mic muted. Closing Discord to its tray or opening Settings can hide its controls; reopen the main window to reconnect. Other Wave control apps may need to be closed. Minimizing support depends on Discord exposing its controls.

To build with the .NET 10 SDK:

```powershell
dotnet test WaveMute.sln -c Release
./scripts/Build-Release.ps1
```

Uses WinUSB and [FlaUI](https://github.com/FlaUI/FlaUI). [Wave:3 protocol reference](https://github.com/LukasParke/wave3-research/blob/main/WAVE3_PROTOCOL_SUMMARY.md).
