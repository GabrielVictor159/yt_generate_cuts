using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YT.Generate.Cuts.Domain.Entities;

namespace YT.Generate.Cuts.Infra.Data.Configurations;

/// <summary>
/// Relação entre canal monitorado e perfil de edição.
/// </summary>
/// <remarks>
/// Explícita por causa do <see cref="DeleteBehavior.SetNull"/>. A convenção do EF
/// para uma chave estrangeira anulável é <c>ClientSetNull</c>, que no banco vira
/// <c>RESTRICT</c>: apagar um perfil de edição em uso falharia com erro de
/// integridade. O comportamento desejado é o oposto — o canal simplesmente volta
/// ao padrão global. Sem esta configuração, modelo e migration divergem e o EF
/// recusa a subida com <c>PendingModelChangesWarning</c>.
/// </remarks>
public class MonitoringChannelConfiguration : IEntityTypeConfiguration<MonitoringChannel>
{
    public void Configure(EntityTypeBuilder<MonitoringChannel> builder)
    {
        builder.HasOne(c => c.EditionConfiguration)
            .WithMany(e => e.MonitoringChannels)
            .HasForeignKey(c => c.EditionConfigurationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
