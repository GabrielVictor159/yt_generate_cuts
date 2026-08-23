using Microsoft.Extensions.Logging.Abstractions;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.RemoveVideos;
using YT.Generate.Cuts.Domain.Enums;
using YT.Generate.Cuts.Worker.VideoProcessing.Tests.Fakes;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// A etapa de limpeza chamava <c>Directory.Delete</c> num caminho de arquivo, o
/// que lançava <c>DirectoryNotFoundException</c> em toda execução e abortava a
/// rodada. Estes testes fixam o comportamento correto.
/// </summary>
public class RemoveVideosCommandHandlerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "remove-tests-" + Guid.NewGuid().ToString("N"));

    public RemoveVideosCommandHandlerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temporário */ }
    }

    [Fact]
    public async Task ApagaOsArquivosEAvancaOStatus()
    {
        var handle = Guid.NewGuid().ToString("N");
        var video = File.Create(Path.Combine(_dir, handle + ".mp4")).Name;
        var subtitle = File.Create(Path.Combine(_dir, handle + ".pt-PT.srt")).Name;

        var uow = new FakeUnitOfWork();
        var entity = NewVideo(video, subtitle);

        await Handler(uow).Handle(new RemoveVideosCommand(entity), CancellationToken.None);

        Assert.False(File.Exists(video));
        Assert.False(File.Exists(subtitle));

        // O status Removed já existia no enum e ninguém no fluxo o definia: era
        // por isso que os mesmos vídeos voltavam à consulta a cada minuto.
        Assert.Equal(VideoStatusEnum.Removed, entity.Status);
        Assert.Null(entity.VideoPath);
        Assert.Null(entity.SubtitlePath);
        Assert.Equal(1, uow.Commits);
    }

    [Fact]
    public async Task ArquivoJaAusente_NaoEErro()
    {
        // O caso exato do log: o caminho no banco não existe mais no disco.
        // Antes, isso lançava e derrubava a limpeza dos vídeos seguintes.
        var uow = new FakeUnitOfWork();
        var entity = NewVideo(Path.Combine(_dir, "nao-existe.mp4"), null);

        await Handler(uow).Handle(new RemoveVideosCommand(entity), CancellationToken.None);

        Assert.Equal(VideoStatusEnum.Removed, entity.Status);
        Assert.Equal(1, uow.Commits);
    }

    [Fact]
    public async Task RemoveAsSobrasDoMergeIncompleto()
    {
        var handle = Guid.NewGuid().ToString("N");
        var final = File.Create(Path.Combine(_dir, handle + ".mp4")).Name;
        var fragmentoVideo = File.Create(Path.Combine(_dir, handle + ".f398.mp4")).Name;
        var fragmentoAudio = File.Create(Path.Combine(_dir, handle + ".f251.webm")).Name;

        // Arquivo de outro download, na mesma pasta: não pode ser tocado.
        var alheio = File.Create(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".mp4")).Name;

        var uow = new FakeUnitOfWork();

        await Handler(uow).Handle(new RemoveVideosCommand(NewVideo(final, null)), CancellationToken.None);

        Assert.False(File.Exists(final));
        Assert.False(File.Exists(fragmentoVideo));
        Assert.False(File.Exists(fragmentoAudio));
        Assert.True(File.Exists(alheio));
    }

    [Fact]
    public async Task NomeBaseQueNaoEHandle_NaoVarreAPasta()
    {
        // Sem o formato de GUID, a varredura de sobras não roda — evita apagar
        // vizinhos por causa de um caminho legado com nome arbitrário.
        var alvo = File.Create(Path.Combine(_dir, "video-antigo.mp4")).Name;
        var vizinho = File.Create(Path.Combine(_dir, "video-antigo.backup.mp4")).Name;

        await Handler(new FakeUnitOfWork())
            .Handle(new RemoveVideosCommand(NewVideo(alvo, null)), CancellationToken.None);

        Assert.False(File.Exists(alvo));
        Assert.True(File.Exists(vizinho));
    }

    [Fact]
    public async Task ArquivoBloqueado_MantemOVideoNaFila()
    {
        var handle = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_dir, handle + ".mp4");
        File.WriteAllText(path, "conteudo");

        var uow = new FakeUnitOfWork();
        var entity = NewVideo(path, null);

        // Mantém o arquivo aberto sem compartilhar exclusão: no Windows o Delete
        // falha. No Linux o unlink funciona, então o teste aceita os dois
        // resultados — o que ele garante é a coerência entre eles.
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Handler(uow).Handle(new RemoveVideosCommand(entity), CancellationToken.None);
        }

        if (entity.Status == VideoStatusEnum.Removed)
        {
            // Conseguiu apagar: precisa ter persistido.
            Assert.Equal(1, uow.Commits);
        }
        else
        {
            // Não conseguiu: o vídeo continua elegível para nova tentativa e
            // nada foi persistido — e, principalmente, não houve exceção.
            Assert.Equal(VideoStatusEnum.Process, entity.Status);
            Assert.Equal(0, uow.Commits);
            Assert.NotNull(entity.VideoPath);
        }
    }

    // ==================================================================

    private static RemoveVideosCommandHandler Handler(FakeUnitOfWork uow) =>
        new(NullLogger<RemoveVideosCommandHandler>.Instance, uow);

    private static Domain.Entities.Video NewVideo(string? videoPath, string? subtitlePath) => new()
    {
        Id = 1,
        ChannelId = 1,
        Title = "Vídeo de teste",
        Url = "https://www.youtube.com/watch?v=abcdefghijk",
        Status = VideoStatusEnum.Process,
        VideoPath = videoPath,
        SubtitlePath = subtitlePath,
    };
}
