using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Email;
using Skanyxx.Module.Identity.Passwords;

namespace Skanyxx.Module.Identity.Features.Passwords;

/// <summary>
/// D156: queues the request and answers at once, the same for every email — nothing is looked up before the answer, so
/// neither the body nor the timing tells whether an account exists. Only the configuration is ever refused (no SMTP, or
/// no address to build the link on), which says nothing about accounts.
/// </summary>
internal sealed class RequestPasswordResetHandler(
    LinkMail mail, InviteLinks links, ResetRequestQueue queue, ClientAddress client, ILogger<RequestPasswordResetHandler> logger)
    : IRequestHandler<RequestPasswordResetCommand, Outcome<bool>>
{
    public const string Unavailable = "Password reset by email is not available here. Ask the owner for a reset link.";

    public const string Answer = "If that email belongs to an account that can reset its password, a link is on its way. It works once, for a short time.";

    public Task<Outcome<bool>> Handle(RequestPasswordResetCommand command, CancellationToken ct)
    {
        if (!mail.Enabled || !links.CanBuild)
            return Task.FromResult(Outcome<bool>.NotFound(Unavailable));

        if (!queue.TryEnqueue(new ResetRequest(command.Email, client.Current?.ToString(), links.BaseUrl())))
            logger.LogWarning("Password-reset request from {RemoteIp} dropped: {Capacity} requests are already waiting", client.Current,
                ResetRequestQueue.Capacity);
        return Task.FromResult(Outcome<bool>.Accepted(true));
    }
}
