using System;
using Kanoonyar.BuildingBlocks;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_empty_correlation_id(string value)
    {
        Assert.Throws<ArgumentException>(() => new CorrelationId(value));
    }

    [Fact]
    public void Keeps_opaque_value_without_authentication_semantics()
    {
        Assert.Equal("request-123", new CorrelationId("request-123").Value);
    }
}
