using YT.Generate.Cuts.Application.VideoProcessing.Helpers;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// As tags chegam de duas origens que não conversam: texto livre do modelo e
/// texto digitado no cadastro do canal. Sem normalizar, o mesmo assunto entra
/// duas vezes com grafias diferentes.
/// </summary>
public class TagListTests
{
    [Fact]
    public void Normalize_TiraCerquilhaPontuacaoEEspacoRepetido()
    {
        Assert.Equal("geopolitica", TagList.Normalize("#geopolitica"));
        Assert.Equal("guerra na ucrania", TagList.Normalize("  guerra   na  ucrânia! ".Replace("ucrânia", "ucrania")));
        // A caixa é preservada de propósito: a tag vai para a descrição da
        // publicação, e "Professor HOC" em minúsculas ficaria errado. Só a
        // comparação de duplicidade ignora caixa e acento.
        Assert.Equal("Banco Central", TagList.Normalize("Banco Central."));
        Assert.Equal("", TagList.Normalize("   "));
        Assert.Equal("", TagList.Normalize(null));
    }

    [Fact]
    public void Normalize_PreservaAcentoELimitaTamanho()
    {
        // Acento e caixa fazem parte da tag: só a COMPARAÇÃO os ignora.
        Assert.Equal("Geopolítica", TagList.Normalize("Geopolítica"));

        var longa = new string('a', 80);
        Assert.Equal(40, TagList.Normalize(longa).Length);
    }

    [Fact]
    public void Merge_RemoveDuplicataIgnorandoAcentoECaixa()
    {
        var tags = TagList.Merge(new[] { "Geopolítica", "geopolitica", "GEOPOLÍTICA", "economia" });

        // Guarda a primeira grafia, que é a escrita corretamente.
        Assert.Equal(new[] { "Geopolítica", "economia" }, tags);
    }

    [Fact]
    public void Merge_PreservaAOrdemDasOrigens()
    {
        // As do canal primeiro: são as que o dono do canal quer em tudo.
        var tags = TagList.Merge(
            new[] { "canal", "sempre" },
            new[] { "modelo", "canal" });

        Assert.Equal(new[] { "canal", "sempre", "modelo" }, tags);
    }

    [Fact]
    public void Merge_AplicaOTetoDeTags()
    {
        var muitas = Enumerable.Range(1, 40).Select(i => $"tag{i}");

        Assert.Equal(TagList.MaxTags, TagList.Merge(muitas).Count);
    }

    [Fact]
    public void Merge_IgnoraOrigensNulas()
    {
        Assert.Equal(new[] { "a" }, TagList.Merge(null, new[] { "a" }, null));
        Assert.Empty(TagList.Merge(null, null));
    }

    [Fact]
    public void Split_AceitaVirgulaPontoEVirgulaEQuebraDeLinha()
    {
        var tags = TagList.Merge(TagList.Split("economia, política; brasil\nmercado"));

        Assert.Equal(new[] { "economia", "política", "brasil", "mercado" }, tags);
    }

    [Fact]
    public void Split_ToleraVazio()
    {
        Assert.Empty(TagList.Split(null));
        Assert.Empty(TagList.Split(""));
        Assert.Empty(TagList.Split("   "));
    }

    [Fact]
    public void Join_DevolveNullQuandoNaoHaTag()
    {
        Assert.Null(TagList.Join(Array.Empty<string>()));
        Assert.Equal("a, b", TagList.Join(new[] { "a", "b" }));
    }

    [Fact]
    public void FluxoCompleto_TagsDoCanalMaisDoModelo()
    {
        var doCanal = TagList.Split("Professor HOC, geopolítica");
        var doModelo = new[] { "#GEOPOLITICA", "ucrânia", "otan" };

        Assert.Equal(
            "Professor HOC, geopolítica, ucrânia, otan",
            TagList.Join(TagList.Merge(doCanal, doModelo)));
    }
}
