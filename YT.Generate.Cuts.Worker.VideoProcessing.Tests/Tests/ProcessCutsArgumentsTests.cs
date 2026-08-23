using YT.Generate.Cuts.Application.VideoProcessing.Commands.ProcessCuts;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// O corte era gerado "com sucesso" e com a duração errada, então nenhum erro
/// aparecia no log. Estes testes fixam a forma dos argumentos, que é onde o
/// defeito morava.
/// </summary>
public class ProcessCutsArgumentsTests
{
    private static readonly TimeSpan Start = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    [Fact]
    public void UsaDuracaoComTNaoInstanteComTo()
    {
        var args = Build();

        // -to é opção de saída e conta do início do arquivo GERADO. Depois do
        // -ss a saída começa em zero, então "-to 00:05:30" produzia 5m30s de
        // corte em vez de terminar em 5m30s do vídeo. Medido: 10s pedidos → 50s.
        Assert.DoesNotContain("-to", args);

        var t = args.ToList().IndexOf("-t");
        Assert.True(t >= 0);
        Assert.Equal("30", args[t + 1]);
    }

    [Fact]
    public void SeekVemAntesDoInput()
    {
        var args = Build().ToList();

        // -ss antes do -i posiciona a leitura; depois do -i o ffmpeg decodifica
        // desde o começo do arquivo, o que num vídeo longo é a diferença entre
        // segundos e minutos.
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.Equal("300", args[args.IndexOf("-ss") + 1]);
    }

    [Fact]
    public void PorPadraoReencoda()
    {
        var args = Build();

        // Com -c copy o corte só começa e termina em quadro-chave: na medição,
        // 10 segundos pedidos viraram 30. Para cortes de 15 a 60 segundos esse
        // desvio é o conteúdo inteiro.
        Assert.Contains("libx264", args);
        Assert.DoesNotContain("copy", args);
    }

    [Fact]
    public void ComStreamCopy_NaoReencoda()
    {
        var args = Build(streamCopy: true);

        Assert.Contains("copy", args);
        Assert.DoesNotContain("libx264", args);

        // Ainda com duração, não instante.
        Assert.DoesNotContain("-to", args);
    }

    [Fact]
    public void UsaSegundosComPontoDecimalIndependenteDaCultura()
    {
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            // pt-BR usa vírgula decimal; o ffmpeg não aceita.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");

            var args = ProcessCutsCommandHandler.BuildArguments(
                "/in.mp4", "/out.mp4", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.25),
                streamCopy: false, preset: "veryfast", crf: 23).ToList();

            Assert.Equal("1.5", args[args.IndexOf("-ss") + 1]);
            Assert.Equal("2.25", args[args.IndexOf("-t") + 1]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = anterior;
        }
    }

    [Fact]
    public void SaidaEOUltimoArgumento()
    {
        var args = Build();
        Assert.Equal("/out.mp4", args[^1]);
        Assert.Contains("+faststart", args);
    }

    private static IReadOnlyList<string> Build(bool streamCopy = false) =>
        ProcessCutsCommandHandler.BuildArguments(
            "/in.mp4", "/out.mp4", Start, Duration, streamCopy, "veryfast", 23);
}
