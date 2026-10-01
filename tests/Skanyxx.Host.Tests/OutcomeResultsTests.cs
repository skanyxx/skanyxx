using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>CR NT2: pages (<c>HttpStatus</c>) and endpoints (<c>ToHttp</c>) answer every outcome with the same status.</summary>
public sealed class OutcomeResultsTests
{
    public static TheoryData<OutcomeStatus> Statuses() => new(Enum.GetValues<OutcomeStatus>());

    [Theory]
    [MemberData(nameof(Statuses))]
    public void ToHttp_AnswersWithHttpStatus(OutcomeStatus status)
    {
        var result = new Outcome<string>(status, "value", "message").ToHttp(v => v);

        Assert.Equal(status.HttpStatus(), Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
