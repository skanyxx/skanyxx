using FluentValidation;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class ExportAuditValidator : AbstractValidator<ExportAuditCommand>
{
    public ExportAuditValidator()
    {
        RuleFor(c => c.Filter).SetValidator(new AuditFilterValidator());
        RuleFor(c => c.Format).Must(ExportAuditCommand.Formats.Contains).WithMessage("'format' is csv or json.");
    }
}
