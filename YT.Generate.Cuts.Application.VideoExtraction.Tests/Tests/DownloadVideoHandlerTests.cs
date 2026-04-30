using Microsoft.Extensions.DependencyInjection;
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.Download;

namespace YT.Generate.Cuts.Application.VideoExtraction.Tests.Tests;
public class DownloadVideoHandlerTests : TestsBase
{
    private readonly IAppDispatcher _dispatcher;
    private string videoExampleUrl = "";

    public DownloadVideoHandlerTests()
    {
        _dispatcher = ServiceProvider.GetRequiredService<IAppDispatcher>();
    }

    [Fact]
    public async Task DownloadVideo_ShouldFlowDownload()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        string folderName = "DownloadExamples";
        string fullPath = Path.Combine(baseDirectory, folderName);

        await _dispatcher.Send(new DownloadVideoCommand(videoExampleUrl,fullPath));
    }
}
