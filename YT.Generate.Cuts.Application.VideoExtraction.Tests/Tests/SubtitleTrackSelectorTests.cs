using YT.Generate.Cuts.Application.VideoExtraction.Youtube;

namespace YT.Generate.Cuts.Application.VideoExtraction.Tests.Tests;

/// <summary>
/// A escolha da legenda saiu do argumento <c>--sub-langs</c> do yt-dlp para cá
/// exatamente para poder ser verificada. Antes, "pt.*,en.*" era uma expressão
/// regular que casava (e baixava) todas as variantes de uma vez — a rajada de
/// requisições que devolvia HTTP 429.
/// </summary>
public class SubtitleTrackSelectorTests
{
    private static readonly string[] Preferred = { "pt", "en" };

    [Fact]
    public void Choose_ShouldReturnExactlyOneTrack_NotEveryMatch()
    {
        var manual = new[] { "pt-PT", "en" };
        var automatic = new[] { "pt", "en-orig", "live_chat" };

        var track = SubtitleTrackSelector.Choose(manual, automatic, Preferred);

        Assert.NotNull(track);

        // Legenda humana de variante regional ganha da automática exata: é o
        // texto pontuado, que é o que delimita as frases dos cortes.
        Assert.Equal("pt-PT", track!.Tag);
        Assert.False(track.IsAutomatic);
        Assert.Equal("pt", track.Language);
    }

    [Fact]
    public void Choose_ShouldPreferExactManualOverRegionalVariant()
    {
        var track = SubtitleTrackSelector.Choose(
            manualTags: new[] { "pt-BR", "pt", "pt-PT" },
            automaticTags: Array.Empty<string>(),
            Preferred);

        Assert.Equal("pt", track!.Tag);
    }

    [Fact]
    public void Choose_ShouldFallBackToAutomatic_WhenThereIsNoHumanSubtitle()
    {
        var track = SubtitleTrackSelector.Choose(
            manualTags: Array.Empty<string>(),
            automaticTags: new[] { "pt-orig", "en" },
            Preferred);

        Assert.Equal("pt-orig", track!.Tag);
        Assert.True(track.IsAutomatic);
        Assert.Equal("pt", track.Language);
    }

    [Fact]
    public void Choose_ShouldRespectLanguagePriority()
    {
        // Só existe humana em inglês e automática em português. A ordem pedida é
        // pt primeiro, então o português automático vence: a prioridade é do
        // idioma, não da modalidade.
        var track = SubtitleTrackSelector.Choose(
            manualTags: new[] { "en" },
            automaticTags: new[] { "pt" },
            Preferred);

        Assert.Equal("pt", track!.Tag);
        Assert.True(track.IsAutomatic);
    }

    [Fact]
    public void Choose_ShouldNeverPickLiveChat()
    {
        var track = SubtitleTrackSelector.Choose(
            manualTags: new[] { "live_chat" },
            automaticTags: new[] { "live_chat" },
            Preferred);

        Assert.Null(track);
    }

    [Fact]
    public void Choose_ShouldReturnNull_WhenNoRequestedLanguageExists()
    {
        // Sem queda para idioma alheio: uma legenda em coreano faria a etapa de
        // InterestingTimes analisar um texto que não corresponde ao esperado.
        var track = SubtitleTrackSelector.Choose(
            manualTags: new[] { "ko", "ja" },
            automaticTags: new[] { "ko-KR" },
            Preferred);

        Assert.Null(track);
    }

    [Fact]
    public void Choose_ShouldTreatRegionalRequestAsItsLanguage()
    {
        // Configuração pedindo "pt-BR" não deve deixar de achar "pt-PT".
        var track = SubtitleTrackSelector.Choose(
            manualTags: new[] { "pt-PT" },
            automaticTags: Array.Empty<string>(),
            preferredLanguages: new[] { "pt-BR" });

        Assert.Equal("pt-PT", track!.Tag);
    }

    [Fact]
    public void Choose_ShouldUseDefaultLanguages_WhenNoneConfigured()
    {
        var track = SubtitleTrackSelector.Choose(
            manualTags: new[] { "en" },
            automaticTags: Array.Empty<string>(),
            preferredLanguages: Array.Empty<string>());

        Assert.Equal("en", track!.Tag);
    }

    [Fact]
    public void Choose_ShouldToleratePresentButEmptyTrackLists()
    {
        Assert.Null(SubtitleTrackSelector.Choose(null, null, Preferred));
    }
}
