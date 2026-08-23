using System.Net;

namespace YT.Generate.Cuts.Application.VideoExtraction.Youtube;

/// <summary>
/// Lê o formato <c>cookies.txt</c> (Netscape), que é o que as extensões de
/// navegador e o <c>yt-dlp --cookies</c> exportam.
/// </summary>
/// <remarks>
/// Layout, separado por TAB:
/// <c>domínio | flag | caminho | secure | expiração | nome | valor</c>.
/// Linhas iniciadas por <c>#</c> são comentário, com exceção do prefixo
/// <c>#HttpOnly_</c>, que antecede um domínio válido.
/// </remarks>
public static class NetscapeCookieFile
{
    private const string HttpOnlyPrefix = "#HttpOnly_";

    public static List<Cookie> Parse(string? content)
    {
        var cookies = new List<Cookie>();
        if (string.IsNullOrWhiteSpace(content)) return cookies;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ');
            if (line.Length == 0) continue;

            if (line.StartsWith(HttpOnlyPrefix, StringComparison.OrdinalIgnoreCase))
                line = line[HttpOnlyPrefix.Length..];
            else if (line.StartsWith('#'))
                continue;

            var parts = line.Split('\t');
            if (parts.Length < 7) continue;

            var domain = parts[0].Trim();
            var path = string.IsNullOrWhiteSpace(parts[2]) ? "/" : parts[2].Trim();
            var secure = parts[3].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            var name = parts[5].Trim();
            var value = parts[6].Trim();

            if (name.Length == 0 || domain.Length == 0) continue;

            try
            {
                // O construtor de Cookie valida nome e domínio e lança em valores
                // fora do padrão; um cookie ruim não deve derrubar o arquivo todo.
                var cookie = new Cookie(name, value, path, domain) { Secure = secure };

                if (long.TryParse(parts[4].Trim(), out var epoch) && epoch > 0)
                    cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

                cookies.Add(cookie);
            }
            catch (Exception)
            {
                // linha inválida: ignorada de propósito
            }
        }

        return cookies;
    }
}
