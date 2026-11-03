using WaveMute.Discord;
using Xunit;

namespace WaveMute.Tests;

public sealed class DiscordStateTests
{
    [Theory]
    [InlineData("pressed=true;checked=true;expanded=false", true)]
    [InlineData("checked=false;expanded=false", false)]
    [InlineData("pressed=false", false)]
    [InlineData(" required=false; checked=true; ", true)]
    public void ParsesDiscordAriaStates(string properties, bool expected) => Assert.Equal(expected, DiscordMute.ParseAriaChecked(properties));

    [Theory]
    [InlineData("")]
    [InlineData("checked=mixed")]
    [InlineData("expanded=false")]
    public void UnknownStateCannotBeTreatedAsUnmuted(string properties) => Assert.Throws<InvalidOperationException>(() => DiscordMute.ParseAriaChecked(properties));
}
