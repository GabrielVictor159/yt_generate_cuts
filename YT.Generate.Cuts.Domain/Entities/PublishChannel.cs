
using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.Domain.Entities;
public class PublishChannel
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Login { get; set; }
    public required string Password { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public TypePublishEnum TypePublish { get; set; }
    #region Foreign Keys
    public virtual List<Monitoring> Monitorings { get; set; } = new();
    #endregion
}
