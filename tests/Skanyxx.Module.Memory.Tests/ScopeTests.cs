using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Tests;

public sealed class ScopeTests
{
    [Theory]
    [InlineData("company", true)]
    [InlineData("personal:ana", true)]
    [InlineData("team:billing", true)]
    [InlineData("department:eng.ops", true)]
    [InlineData("personal:Ana", false)]
    [InlineData("personal:", false)]
    [InlineData("personal:ana\n", false)]
    [InlineData("personal:a:b", false)]
    [InlineData("company:x", false)]
    [InlineData("group:x", false)]
    [InlineData("personal", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsOnlyKnownLevelsWithValidIds(string? value, bool valid) =>
        Assert.Equal(valid, Scope.TryParse(value, out _));

    // CR m3: one id rule across modules.
    [Theory]
    [InlineData("ana")]
    [InlineData("Ana")]
    [InlineData("kagent/k8s-agent")]
    [InlineData("a@b.c")]
    [InlineData("ana\n")]
    [InlineData("")]
    public void IdRule_IsCoresIdentifier(string id) => Assert.Equal(Identifier.IsValid(id), Scope.IsValidId(id));
}
