
using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.Domain.Entities;
public class Cut
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required TimeOnly InitialTime { get; set; }
    public required TimeOnly FinallyTime { get; set; }
    public required string CutPath { get; set; }
    public DateTime CreationDate { get; set; }
    public required CutStatusEnum Status { get; set; } 
    #region Foreign Keys
    public long? VideoId { get; set; }
    public virtual Video? Video { get; set; }
    public long? PublishChannelId { get; set; }
    public virtual PublishChannel? PublishChannel { get; set; }
    #endregion
}
