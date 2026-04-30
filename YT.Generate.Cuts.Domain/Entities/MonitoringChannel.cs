namespace YT.Generate.Cuts.Domain.Entities;
public class MonitoringChannel
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public DateTime CreationDate { get; set; }

    #region Foreign Keys
    public virtual List<Video> Videos { get; set; } = new();
    public virtual List<Monitoring> Monitorings { get; set; } = new();
    #endregion
}
