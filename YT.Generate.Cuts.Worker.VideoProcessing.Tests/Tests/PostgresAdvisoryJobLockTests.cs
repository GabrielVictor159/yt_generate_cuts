using Npgsql;
using YT.Generate.Cuts.Infra.Data.Jobs;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Tests;

/// <summary>
/// Duas propriedades do lock que não dependem de banco e que, se alguém
/// "arrumar" mais tarde, desligam a exclusão mútua sem nenhum sintoma imediato.
/// </summary>
public class PostgresAdvisoryJobLockTests
{
    [Fact]
    public void ChaveDoRecurso_EstavelEDeterministica()
    {
        // Precisa ser igual em processos diferentes. Com string.GetHashCode
        // (aleatorizado por processo) cada container teria a sua chave — e o
        // lock não excluiria ninguém, silenciosamente.
        Assert.Equal(7963053865878066767L, PostgresAdvisoryJobLock.KeyFor("processing:cuts"));

        Assert.NotEqual(
            PostgresAdvisoryJobLock.KeyFor("processing:cuts"),
            PostgresAdvisoryJobLock.KeyFor("processing:interesting-times"));
    }

    [Fact]
    public void CadeiaDeConexao_DesligaOPoolELigaOKeepalive()
    {
        var built = PostgresAdvisoryJobLock.BuildLockConnectionString(
            "Host=db;Database=monitoring;Username=postgres;Password=x");

        var parsed = new NpgsqlConnectionStringBuilder(built);

        // Medido contra um PostgreSQL real: devolver a conexão ao pool NÃO
        // libera o advisory lock — a conexão volta ao pool carregando o lock.
        // Sem pool, fechar encerra a sessão e o banco libera.
        Assert.False(parsed.Pooling);
        Assert.True(parsed.KeepAlive > 0);
    }

    [Fact]
    public void CadeiaDeConexao_PreservaOQueJaEstavaDefinido()
    {
        var built = PostgresAdvisoryJobLock.BuildLockConnectionString(
            "Host=db;Database=monitoring;Username=postgres;Password=x;Keepalive=10;Application Name=meu-app");

        var parsed = new NpgsqlConnectionStringBuilder(built);

        Assert.Equal(10, parsed.KeepAlive);
        Assert.Equal("meu-app", parsed.ApplicationName);
        Assert.Equal("monitoring", parsed.Database);
    }
}
