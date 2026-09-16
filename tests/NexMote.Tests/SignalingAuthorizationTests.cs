using Microsoft.AspNetCore.Authorization;
using NexMote.Api.Hubs;
using Xunit;

namespace NexMote.Tests;

public sealed class SignalingAuthorizationTests
{
    [Fact]
    public void SubscribeToDeviceFeed_requires_authenticated_user()
    {
        var attribute = GetAuthorizationAttribute(nameof(SignalingHub.SubscribeToDeviceFeed));

        Assert.NotNull(attribute);
        Assert.Equal("AnyUser", attribute!.Policy);
    }

    [Fact]
    public void UnsubscribeFromDeviceFeed_requires_authenticated_user()
    {
        var attribute = GetAuthorizationAttribute(nameof(SignalingHub.UnsubscribeFromDeviceFeed));

        Assert.NotNull(attribute);
        Assert.Equal("AnyUser", attribute!.Policy);
    }

    private static AuthorizeAttribute? GetAuthorizationAttribute(string methodName) =>
        typeof(SignalingHub)
            .GetMethod(methodName)
            ?.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();
}