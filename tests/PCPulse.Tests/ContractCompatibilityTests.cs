using PCPulse.Contracts;
using Xunit;

namespace PCPulse.Tests;

public sealed class ContractCompatibilityTests
{
    [Fact]
    public void Current_IsOnePointZero()
    {
        Assert.Equal("1.0", PulseContractVersion.Current);
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.0.0", false)]
    [InlineData("1.1", false)]
    [InlineData("2.0", false)]
    [InlineData("0.9", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void IsCompatible_ValidatesMajorMinorRule(string? version, bool expected)
    {
        var ok = PulseContractVersion.IsCompatible(version, out var reason);

        Assert.Equal(expected, ok);
        if (!expected)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
        else
        {
            Assert.Null(reason);
        }
    }

    [Fact]
    public void GetCurrentPlatform_ReturnsWindowsOrLinuxOnSupportedHost()
    {
        var platform = PulseContractVersion.GetCurrentPlatform();
        Assert.True(platform == PulsePlatform.Windows || platform == PulsePlatform.Linux);
    }
}
