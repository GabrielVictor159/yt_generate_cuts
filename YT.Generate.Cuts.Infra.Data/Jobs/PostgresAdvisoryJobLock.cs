using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace YT.Generate.Cuts.Infra.Data.Jobs;

/// <summary>
/// Lock global usando <c>advisory lock</c> do PostgreSQL.
/// </summary>
/// <remarks>
/// Esta implementação substitui duas anteriores, cada uma com um defeito próprio:
/// <list type="number">
/// <item>
/// <c>static SemaphoreSlim</c>: valia dentro de um processo só. Dois containers,
/// ou a instância antiga ainda viva num restart, ou o worker rodando no Visual
/// Studio contra o mesmo banco — e duas rodadas corriam em paralelo.
/// </item>
/// <item>
/// O lock distribuído do Hangfire.PostgreSql: é uma linha numa tabela com
/// carimbo de tempo, e <b>ninguém a apaga quando o processo morre</b>. Ela só sai
/// quando alguém tenta adquirir e vê que passou de <c>DistributedLockTimeout</c>
/// — 60 minutos, na configuração deste projeto. Ou seja: matar o container no
/// meio de um download deixava o recurso travado por até uma hora, e todo ciclo
/// registrava "já está sendo monitorado por outra execução" sem nada estar
/// rodando. É exatamente o sintoma de "paro o sistema e o job nunca mais roda",
/// e o modo de depuração do Docker Compose no Visual Studio o provoca a cada
/// F5/Stop, porque o processo é encerrado sem chance de liberar nada.
/// </item>
/// </list>
/// <para>
/// O advisory lock do PostgreSQL resolve os dois: ele é preso à <b>sessão</b>
/// (a conexão). Se o processo morre, a conexão cai e o banco libera o lock na
/// hora — não há órfão nem prazo a esperar. E, enquanto a conexão vive, o lock
/// não expira, então também não existe o risco de outra execução tomá-lo no meio
/// do trabalho.
/// </para>
/// <para>
/// A conexão fica aberta durante todo o trabalho, e é aberta com
/// <c>Pooling=false</c>. Isso não é detalhe: medi contra um PostgreSQL real que
/// devolver a conexão ao pool do Npgsql <b>não</b> libera o advisory lock — a
/// conexão física volta ao pool carregando o lock. Sem pool, fechar a conexão
/// encerra a sessão e o banco libera, o que torna a liberação garantida mesmo se
/// o <c>pg_advisory_unlock</c> explícito falhar. O custo é uma conexão nova por
/// aquisição, irrelevante para jobs que rodam a cada minuto.
/// </para>
/// <para>
/// O <c>Keepalive</c> é ligado para que uma queda por inatividade (NAT, firewall)
/// não solte o lock em silêncio no meio de um download longo.
/// </para>
/// </remarks>
public sealed class PostgresAdvisoryJobLock : IJobLock
{
    /// <summary>Prefixo do recurso, para não colidir com advisory locks de terceiros.</summary>
    private const string Namespace = "ytgc:";

    private const int KeepaliveSeconds = 30;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _connectionString;
    private readonly ILogger<PostgresAdvisoryJobLock> _logger;

    public PostgresAdvisoryJobLock(IConfiguration configuration, ILogger<PostgresAdvisoryJobLock> logger)
    {
        var raw = configuration.GetConnectionString("DefaultConnection")
                  ?? throw new InvalidOperationException(
                      "ConnectionStrings:DefaultConnection é obrigatória para o lock de jobs.");

        _connectionString = BuildLockConnectionString(raw);
        _logger = logger;
    }

    public async Task<bool> TryRunAsync(string resource, TimeSpan wait, Func<Task> action)
    {
        var key = KeyFor(resource);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!await TryAcquireAsync(connection, key, wait))
            return false;

        try
        {
            await action();
        }
        finally
        {
            await ReleaseAsync(connection, key, resource);
        }

        return true;
    }

    // ------------------------------------------------------------------

    private static async Task<bool> TryAcquireAsync(NpgsqlConnection connection, long key, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;

        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync() is true)
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(PollInterval);
        }
    }

    private async Task ReleaseAsync(NpgsqlConnection connection, long key, string resource)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync();
        }
        catch (Exception ex)
        {
            // Recuperável: como a conexão não é pooled, o dispose logo abaixo
            // encerra a sessão e o banco libera o lock de qualquer forma. O aviso
            // fica porque indica conexão instável.
            _logger.LogWarning("Não foi possível liberar explicitamente o lock '{Resource}': {Message}. " +
                               "O fechamento da conexão (sem pool) libera de qualquer forma.", resource, ex.Message);
        }
    }

    /// <summary>
    /// Chave de 64 bits derivada do nome do recurso. SHA-256 em vez de
    /// <c>string.GetHashCode</c> porque precisa ser igual em processos
    /// diferentes — o GetHashCode do .NET é aleatorizado por processo, e usá-lo
    /// aqui daria a cada container uma chave própria, ou seja, lock nenhum.
    /// </summary>
    public static long KeyFor(string resource)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Namespace + resource));
        return BitConverter.ToInt64(hash, 0);
    }

    /// <summary>
    /// Monta a cadeia de conexão do lock. Pública para ser verificável: o
    /// <c>Pooling=false</c> é um requisito de correção, não uma preferência.
    /// </summary>
    public static string BuildLockConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                // Ver o comentário da classe: com pool, o lock sobrevive ao
                // Close() e viaja na conexão reciclada.
                Pooling = false,
            };

            if (builder.KeepAlive <= 0)
                builder.KeepAlive = KeepaliveSeconds;

            builder.ApplicationName = string.IsNullOrWhiteSpace(builder.ApplicationName)
                ? "yt-generate-cuts:job-lock"
                : builder.ApplicationName;

            return builder.ConnectionString;
        }
        catch
        {
            // Cadeia fora do formato esperado: usa como veio, apenas desligando
            // o pool, que é o ajuste de que a correção depende.
            return connectionString.TrimEnd(';') + ";Pooling=false";
        }
    }
}
