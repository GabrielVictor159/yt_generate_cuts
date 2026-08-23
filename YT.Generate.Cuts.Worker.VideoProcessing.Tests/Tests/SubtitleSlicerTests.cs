using YT.Generate.Cuts.Application.VideoProcessing.Helpers;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// A legenda do corte tem de vir com os tempos relativos ao CORTE, não ao vídeo:
/// é o que permite a um editor inserir as falas sem conhecer o instante em que o
/// corte começou.
/// </summary>
public class SubtitleSlicerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "subs-" + Guid.NewGuid().ToString("N"));

    public SubtitleSlicerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temporário */ }
    }

    private const string Source = """
        1
        00:00:05,000 --> 00:00:08,000
        antes do corte

        2
        00:01:58,000 --> 00:02:02,000
        cruzando a borda de entrada

        3
        00:02:10,000 --> 00:02:14,500
        bem no meio

        4
        00:02:28,000 --> 00:02:35,000
        cruzando a borda de saida

        5
        00:03:00,000 --> 00:03:04,000
        depois do corte
        """;

    // Janela do corte: 02:00 -> 02:30 (30 segundos).
    private static readonly TimeSpan Start = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan End = Start + TimeSpan.FromSeconds(30);

    [Fact]
    public void RebaseiaOsTemposParaZero()
    {
        var cues = SubtitleSlicer.Slice(SubtitleOptimizer.ParseCues(Source), Start, End);

        Assert.Equal(3, cues.Count);

        // A fala que começa antes do corte é aparada na borda: no corte ela
        // aparece do instante zero.
        Assert.Equal(TimeSpan.Zero, cues[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), cues[0].End);
        Assert.Equal("cruzando a borda de entrada", cues[0].Text);

        // 02:10 do vídeo = 10s do corte.
        Assert.Equal(TimeSpan.FromSeconds(10), cues[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(14.5), cues[1].End);

        // A fala que passa do fim é aparada no fim do corte (30s).
        Assert.Equal(TimeSpan.FromSeconds(28), cues[2].Start);
        Assert.Equal(TimeSpan.FromSeconds(30), cues[2].End);
    }

    [Fact]
    public void DescartaOQueEstaForaDaJanela()
    {
        var cues = SubtitleSlicer.Slice(SubtitleOptimizer.ParseCues(Source), Start, End);
        var textos = cues.Select(c => c.Text).ToList();

        Assert.DoesNotContain("antes do corte", textos);
        Assert.DoesNotContain("depois do corte", textos);
    }

    [Fact]
    public void ReindexaAsFalasDeUmAN()
    {
        var cues = SubtitleSlicer.Slice(SubtitleOptimizer.ParseCues(Source), Start, End);

        // Um SRT cujos índices não começam em 1 é recusado por vários players.
        Assert.Equal(new[] { 1, 2, 3 }, cues.Select(c => c.Id));
    }

    [Fact]
    public void RenderizaSrtValido()
    {
        var cues = SubtitleSlicer.Slice(SubtitleOptimizer.ParseCues(Source), Start, End);
        var srt = SubtitleSlicer.Render(cues);

        Assert.StartsWith("1\n00:00:00,000 --> 00:00:02,000\ncruzando a borda de entrada", srt);

        // Vírgula, não ponto: é o separador de milissegundos do formato SRT.
        Assert.Contains("00:00:10,000 --> 00:00:14,500", srt);
        Assert.DoesNotContain("00:00:10.000", srt);
    }

    [Fact]
    public void SliceFile_GravaOArquivoEDevolveOCaminho()
    {
        var source = Path.Combine(_dir, "video.srt");
        File.WriteAllText(source, Source);
        var target = Path.Combine(_dir, "cut.srt");

        var written = SubtitleSlicer.SliceFile(source, Start, End, target);

        Assert.Equal(target, written);
        Assert.Contains("bem no meio", File.ReadAllText(target));
    }

    [Fact]
    public void SliceFile_DevolveNullQuandoNaoHaLegendaDeOrigem()
    {
        Assert.Null(SubtitleSlicer.SliceFile(null, Start, End, Path.Combine(_dir, "a.srt")));
        Assert.Null(SubtitleSlicer.SliceFile("", Start, End, Path.Combine(_dir, "b.srt")));
        Assert.Null(SubtitleSlicer.SliceFile(Path.Combine(_dir, "nao-existe.srt"), Start, End, Path.Combine(_dir, "c.srt")));
    }

    [Fact]
    public void SliceFile_DevolveNullEnaoCriaArquivoQuandoAJanelaNaoTemFalas()
    {
        var source = Path.Combine(_dir, "video.srt");
        File.WriteAllText(source, Source);
        var target = Path.Combine(_dir, "vazio.srt");

        // Janela entre duas falas: 02:15 -> 02:20 não pega nenhuma... na verdade
        // pega, então usamos um trecho realmente silencioso: 00:10 -> 00:20.
        var written = SubtitleSlicer.SliceFile(source, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), target);

        Assert.Null(written);

        // Corte sem legenda não deve deixar um .srt vazio para trás.
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void JanelaInvertida_NaoProduzNada()
    {
        Assert.Empty(SubtitleSlicer.Slice(SubtitleOptimizer.ParseCues(Source), End, Start));
    }

    [Fact]
    public void DescartaFalasApararadasCurtasDemaisParaLer()
    {
        // A fala 3 (02:10 -> 02:14,5) fica com 100ms se a janela terminar em
        // 02:10,1 — pisca na tela sem dar tempo de ler.
        var cues = SubtitleSlicer.Slice(
            SubtitleOptimizer.ParseCues(Source),
            TimeSpan.FromSeconds(125),
            TimeSpan.FromSeconds(130.1));

        Assert.DoesNotContain("bem no meio", cues.Select(c => c.Text));
    }
}
