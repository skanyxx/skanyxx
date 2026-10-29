using FluentValidation;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>Type by extension and by content (a renamed binary is refused), size by kind; all limits in <c>Chat:Files</c>.</summary>
internal sealed class UploadChatFileValidator : AbstractValidator<UploadChatFileCommand>
{
    public UploadChatFileValidator(ChatFiles files)
    {
        var options = files.Options;
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200)
            .Must(n => files.KindOf(Path.GetExtension(n)) is not null)
            .WithMessage($"Chat takes text files ({string.Join(" ", options.TextExtensions)}) and images ({string.Join(" ", options.ImageExtensions)}).");
        RuleFor(c => c.Size).GreaterThan(0).WithMessage("The file is empty.");
        When(c => files.KindOf(Path.GetExtension(c.Name)) == ChatFileKinds.Text && c.Size > 0, () =>
        {
            RuleFor(c => c.Size).LessThanOrEqualTo(options.MaxTextBytes)
                .WithMessage($"Text files may be up to {options.MaxTextBytes / 1024} KB.");
            RuleFor(c => c.Bytes).Must(b => FileContent.Text(b) is not null)
                .When(c => c.Size <= options.MaxTextBytes)
                .WithMessage("This is not a UTF-8 text file. Save it as UTF-8 text, or attach a supported image.");
        });
        When(c => files.KindOf(Path.GetExtension(c.Name)) == ChatFileKinds.Image && c.Size > 0, () =>
        {
            RuleFor(c => c.Size).LessThanOrEqualTo(options.MaxImageBytes)
                .WithMessage($"Images may be up to {options.MaxImageBytes / 1024} KB.");
            RuleFor(c => c).Must(c => FileContent.ImageType(Path.GetExtension(c.Name).ToLowerInvariant(), c.Bytes) is not null)
                .When(c => c.Size <= options.MaxImageBytes)
                .WithName("Bytes")
                .WithMessage(c => $"The file's content is not a {Path.GetExtension(c.Name).ToLowerInvariant()} image.");
        });
    }
}
