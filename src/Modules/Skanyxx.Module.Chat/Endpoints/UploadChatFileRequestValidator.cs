using FastEndpoints;
using FluentValidation;

namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class UploadChatFileRequestValidator : Validator<UploadChatFileRequest>
{
    public UploadChatFileRequestValidator() => RuleFor(r => r.File).NotNull().WithMessage("Send the file in the form field \"file\".");
}
