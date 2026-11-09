using System.Text.Json;
using PrivacyGuard.LiveObs;
using Xunit;

namespace PrivacyGuard.LiveObs.Tests;

public sealed class ObsAudioSyncProtocolTests
{
    [Theory]
    [InlineData(-950)]
    [InlineData(0)]
    [InlineData(350)]
    [InlineData(20000)]
    public void ValidOffsetHasExactObsWebsocketFields(int offsetMs)
    {
        string requestId = Guid.NewGuid().ToString();
        string json = ObsAudioSyncProtocol.SetOffsetRequest(requestId, "Mic/Aux", offsetMs);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(6, doc.RootElement.GetProperty("op").GetInt32());
        var data = doc.RootElement.GetProperty("d");
        Assert.Equal("SetInputAudioSyncOffset", data.GetProperty("requestType").GetString());
        Assert.Equal(requestId, data.GetProperty("requestId").GetString());
        var request = data.GetProperty("requestData");
        Assert.Equal("Mic/Aux", request.GetProperty("inputName").GetString());
        Assert.Equal(offsetMs, request.GetProperty("inputAudioSyncOffset").GetInt32());
    }

    [Fact]
    public void InvalidAudioInputAndOffsetsFailValidation()
    {
        Assert.Throws<ArgumentException>(() => ObsAudioSyncProtocol.Validate("", 200));
        Assert.Throws<ArgumentException>(() => ObsAudioSyncProtocol.Validate("bad\nname", 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObsAudioSyncProtocol.Validate("Mic", -951));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObsAudioSyncProtocol.Validate("Mic", 20001));
        Assert.Throws<ArgumentException>(() =>
            ObsAudioSyncProtocol.SetOffsetRequest("not-guid", "Mic", 50));
    }

    [Fact]
    public void AuthenticationUsesChallengeSaltAndNeverReturnsPlaintext()
    {
        var result = ObsAudioSyncProtocol.Authentication("test-password", "salt", "challenge");
        Assert.NotEmpty(result);
        Assert.DoesNotContain("test-password", result);
        Assert.Equal(result, ObsAudioSyncProtocol.Authentication("test-password", "salt", "challenge"));
        Assert.NotEqual(result, ObsAudioSyncProtocol.Authentication("test-password2", "salt", "challenge"));
    }
}
