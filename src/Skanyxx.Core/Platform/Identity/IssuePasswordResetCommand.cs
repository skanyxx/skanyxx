using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>The owner issues a password-reset link for a person (D157): emailed when SMTP is on, otherwise shown once.</summary>
public sealed record IssuePasswordResetCommand(string ActorId, string UserId) : IRequest<Outcome<PasswordResetIssued>>;
