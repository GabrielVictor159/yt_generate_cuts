using FluentValidation;

namespace YT.Generate.Cuts.Application.VideoExtraction.Commands.Download;
public class DownloadVideoCommandValidator : AbstractValidator<DownloadVideoCommand>
{
    public DownloadVideoCommandValidator()
    {
        RuleFor(x => x.saveDirectory)
             .NotNull().NotEmpty().WithMessage("The video saving directory is required.");

        RuleFor(x => x.videoUri)
            .NotNull().NotEmpty().WithMessage("The video URL is required.");
    }
}
