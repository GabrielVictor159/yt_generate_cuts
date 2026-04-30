namespace YT.Generate.Cuts.Domain.Entities;
public class Monitoring
{
    public long Id { get; set; }
    public DateTime CreationDate { get; set; }

    #region Foreign Keys
    public long MonitoringChannelId { get; set; }
    public virtual MonitoringChannel? MonitoringChannel { get; set; }
    public long PublishChannelId { get; set; }
    public virtual PublishChannel? PublishChannel { get; set; }
    #endregion
}
