using YT.Generate.Cuts.Application.VideoEdition.Helpers;

namespace YT.Generate.Cuts.Worker.VideoEdition.Tests.Tests;

/// <summary>
/// A cadeia de filtros é onde a edição acerta ou erra em silêncio: um filtro
/// errado não falha, só entrega o vídeo distorcido ou sem legenda.
/// </summary>
public class EditionArgumentsTests
{
    private static readonly EditionSettings Vertical = new(1080, 1920, true, "veryfast", 23);

    [Fact]
    public void Filtro_EscalaSemDistorcerECompletaComBarra()
    {
        var filter = EditionArguments.BuildFilter(1080, 1920, null);

        // force_original_aspect_ratio=decrease + pad: o corte 16:9 cabe inteiro
        // no 9:16 e o resto vira barra preta. Sem isso a imagem seria esticada.
        Assert.Contains("scale=1080:1920:force_original_aspect_ratio=decrease", filter);
        Assert.Contains("pad=1080:1920:(ow-iw)/2:(oh-ih)/2:color=black", filter);

        // Sem setsar, um vídeo de pixel não quadrado sai com as dimensões certas
        // e ainda assim esticado no player.
        Assert.Contains("setsar=1", filter);
    }

    [Fact]
    public void Filtro_QueimaLegendaDepoisDoScale()
    {
        var filter = EditionArguments.BuildFilter(1080, 1920, "/tmp/c.srt");

        // A ordem importa: legenda antes do scale seria redimensionada junto com
        // a imagem e chegaria borrada.
        Assert.True(filter.IndexOf("scale=", StringComparison.Ordinal)
                    < filter.IndexOf("subtitles=", StringComparison.Ordinal));
        Assert.EndsWith("subtitles=filename='/tmp/c.srt'", filter);
    }

    [Fact]
    public void Filtro_SemLegenda_NaoInclueOFiltroDeLegenda()
    {
        Assert.DoesNotContain("subtitles", EditionArguments.BuildFilter(1080, 1920, null));
        Assert.DoesNotContain("subtitles", EditionArguments.BuildFilter(1080, 1920, "   "));
    }

    [Theory]
    [InlineData(@"C:\cortes\a.srt", @"C\:\\cortes\\a.srt")]
    [InlineData("/tmp/a,b.srt", "/tmp/a\\,b.srt")]
    [InlineData("/tmp/it's.srt", "/tmp/it\\'s.srt")]
    public void EscapeForFilter_EscapaOQueOFiltergraphInterpreta(string path, string expected)
    {
        // Dois-pontos e barra invertida têm significado dentro do filtergraph.
        // Num caminho do Windows os dois aparecem, e sem escapar o ffmpeg lê
        // "opção desconhecida" em vez de nome de arquivo.
        Assert.Equal(expected, EditionArguments.EscapeForFilter(path));
    }

    [Fact]
    public void Build_MontaOsArgumentosNaOrdemQueOFfmpegEspera()
    {
        var args = EditionArguments.Build("/in.mp4", "/out.mp4", "/c.srt", Vertical).ToList();

        Assert.True(args.IndexOf("-i") < args.IndexOf("-vf"));
        Assert.Equal("/in.mp4", args[args.IndexOf("-i") + 1]);
        Assert.Equal("/out.mp4", args[^1]);

        Assert.Contains("libx264", args);
        Assert.Contains("veryfast", args);
        Assert.Equal("23", args[args.IndexOf("-crf") + 1]);

        // yuv420p e faststart: o que as plataformas de publicação exigem para
        // ler em streaming.
        Assert.Contains("yuv420p", args);
        Assert.Contains("+faststart", args);
        Assert.Contains("aac", args);
    }

    [Fact]
    public void Build_UsaAResolucaoDasConfiguracoes()
    {
        var args = EditionArguments.Build("/in.mp4", "/out.mp4", null,
            new EditionSettings(720, 1280, false, "medium", 18));

        Assert.Contains(args, a => a.Contains("scale=720:1280"));
        Assert.Contains("medium", args);
        Assert.Equal("18", args.ToList()[args.ToList().IndexOf("-crf") + 1]);
    }
}
