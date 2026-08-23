using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YoutubeExplode;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Entrega um único <see cref="YoutubeClient"/> para toda a aplicação,
/// opcionalmente autenticado por cookies.
/// </summary>
/// <remarks>
/// Antes cada handler fazia <c>new YoutubeClient()</c>, e cada instância cria o
/// seu próprio <see cref="System.Net.Http.HttpClient"/> — sem reaproveitar
/// conexão e sem lugar para injetar cookies.
/// <para>
/// Usamos de propósito o construtor que recebe apenas os cookies, e não o que
/// recebe um HttpClient: o handler interno do YoutubeExplode
/// (<c>YoutubeHttpHandler</c>, que trata retentativa e limite de requisições)
/// é <c>internal</c>, então passar um HttpClient próprio o descartaria.
/// </para>
/// </remarks>
public sealed class YoutubeClientProvider
{
    private readonly Lazy<YoutubeClient> _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<YoutubeClientProvider> _logger;

    public YoutubeClientProvider(IConfiguration configuration, ILogger<YoutubeClientProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _client = new Lazy<YoutubeClient>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public YoutubeClient Client => _client.Value;

    private YoutubeClient Build()
    {
        var path = _configuration["YouTube:CookiesFile"];

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogInformation(
                "[YouTube] Cliente anônimo. Se o YouTube recusar o manifest dos vídeos, exporte os cookies de " +
                "uma sessão logada e aponte YouTube__CookiesFile para o arquivo.");
            return new YoutubeClient();
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("[YouTube] YouTube__CookiesFile aponta para '{Path}', que não existe. Seguindo anônimo.", path);
            return new YoutubeClient();
        }

        try
        {
            var cookies = NetscapeCookieFile.Parse(File.ReadAllText(path));

            if (cookies.Count == 0)
            {
                _logger.LogWarning("[YouTube] Nenhum cookie válido em '{Path}'. Seguindo anônimo.", path);
                return new YoutubeClient();
            }

            _logger.LogInformation("[YouTube] Cliente autenticado com {Count} cookie(s) de '{Path}'.", cookies.Count, path);
            return new YoutubeClient(cookies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YouTube] Falha ao ler os cookies de '{Path}'. Seguindo anônimo.", path);
            return new YoutubeClient();
        }
    }
}
