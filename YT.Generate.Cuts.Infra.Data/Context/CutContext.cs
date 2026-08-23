using Microsoft.EntityFrameworkCore;
using System.Reflection;
using YT.Generate.Cuts.Domain.Entities;

namespace YT.Generate.Cuts.Infra.Data.Context;
public class CutContext : DbContext
{
    public CutContext(DbContextOptions<CutContext> options) : base(options)
    {
    }
    public DbSet<Cut> Cuts { get; set; }

    /// <summary>
    /// Declarado explicitamente para a tabela nascer como "EditionConfigurations".
    /// Sem o DbSet, o EF nomeia a tabela pelo tipo (singular) — e a migration
    /// criaria "EditionConfigurations" enquanto o modelo esperaria
    /// "EditionConfiguration". O diferenciador de modelo do EF acusa isso na
    /// subida, mas o barato é não introduzir a divergência.
    /// </summary>
    public DbSet<EditionConfiguration> EditionConfigurations { get; set; }
    public DbSet<Monitoring> Monitorings { get; set; }
    public DbSet<MonitoringChannel> MonitoringChannels { get; set; }
    public DbSet<PublishChannel> publishChannels { get; set; }
    public DbSet<Video> Videos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.Entity.GetType().GetProperty("CreatedDate") != null))
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property("CreatedDate").CurrentValue = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
