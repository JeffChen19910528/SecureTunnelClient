using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Core.Tests.WireGuard;

public class WireGuardOutcomeTests
{
    [Fact]
    public void Enum_HasExactlySixteenDistinctMembers()
    {
        var values = Enum.GetValues<WireGuardOutcome>();

        Assert.Equal(16, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Theory]
    [InlineData(WireGuardOutcome.ProcessStartFailed)]
    [InlineData(WireGuardOutcome.DisconnectFailed)]
    [InlineData(WireGuardOutcome.StatusUnavailable)]
    [InlineData(WireGuardOutcome.InterfaceActive)]
    [InlineData(WireGuardOutcome.AwaitingHandshake)]
    public void Enum_ContainsNewC3Outcomes(WireGuardOutcome outcome)
    {
        Assert.True(Enum.IsDefined(outcome));
    }
}
