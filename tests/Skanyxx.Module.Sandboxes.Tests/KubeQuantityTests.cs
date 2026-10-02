namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class KubeQuantityTests
{
    [Theory]
    [InlineData("500m", 0.5)]
    [InlineData("2", 2)]
    [InlineData("1.5", 1.5)]
    [InlineData("1k", 1000)]
    [InlineData("1Ki", 1024)]
    [InlineData("1Gi", 1073741824)]
    [InlineData("1G", 1000000000)]
    public void Parses(string value, double expected)
    {
        Assert.True(KubeQuantity.TryParse(value, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData("1Gi\n")]
    [InlineData("1234567890123Ti")]
    public void Rejects(string value) => Assert.False(KubeQuantity.TryParse(value, out _));

    [Fact]
    public void Compares_AcrossSuffixes()
    {
        Assert.True(KubeQuantity.Exceeds("1Gi", "1G"));
        Assert.False(KubeQuantity.Exceeds("1000m", "1"));
        Assert.Equal("2", KubeQuantity.Max("1", "2"));
        Assert.Equal("100m", KubeQuantity.Min("250m", "100m"));
        Assert.Equal("250m", KubeQuantity.Min("250m", null));
    }
}
