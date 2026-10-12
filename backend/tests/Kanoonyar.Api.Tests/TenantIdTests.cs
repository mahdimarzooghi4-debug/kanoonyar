using System;
using Kanoonyar.BuildingBlocks;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class TenantIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_missing_identifier(string value)
    {
        Assert.Throws<ArgumentException>(() => new TenantId(value));
    }

    [Fact]
    public void Preserves_supplied_identifier_without_inventing_identity_policy()
    {
        Assert.Equal("center-001", new TenantId("center-001").Value);
    }
}
