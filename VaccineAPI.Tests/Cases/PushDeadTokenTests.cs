using VaccineAPI.Services;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>A device token is deleted only when FCM proves that token is dead; every other FCM error keeps it.</summary>
public class PushDeadTokenTests
{
    [Fact]
    public void Unregistered_Is_Dead()
    {
        const string body = "{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"details\":[{\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\",\"errorCode\":\"UNREGISTERED\"}]}}";
        Assert.True(PushService.IsDeadToken(body));
    }

    [Fact]
    public void InvalidArgument_On_Token_Field_Is_Dead()
    {
        const string body = "{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.BadRequest\",\"fieldViolations\":[{\"field\":\"message.token\",\"description\":\"not a valid FCM registration token\"}]}]}}";
        Assert.True(PushService.IsDeadToken(body));
    }

    [Fact]
    public void InvalidArgument_On_Payload_Is_Not_Dead()
    {
        const string body = "{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.BadRequest\",\"fieldViolations\":[{\"field\":\"message.data\",\"description\":\"bad\"}]}]}}";
        Assert.False(PushService.IsDeadToken(body));
    }

    [Fact]
    public void Plain_404_Without_Unregistered_Is_Not_Dead() // e.g. wrong project id
        => Assert.False(PushService.IsDeadToken("{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"message\":\"Requested entity was not found.\"}}"));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"error\":{\"code\":403,\"status\":\"PERMISSION_DENIED\",\"details\":[{\"errorCode\":\"SENDER_ID_MISMATCH\"}]}}")]
    [InlineData("{\"error\":{\"code\":503,\"status\":\"UNAVAILABLE\"}}")]
    public void Other_Failures_Are_Not_Dead(string body) => Assert.False(PushService.IsDeadToken(body));
}
