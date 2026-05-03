using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.Domain.Entities;
public class Video
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public string? VideoPath { get; set; }
    public string? SubtitlePath { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public required VideoStatusEnum Status { get; set; }
    public string? Language { get; set; }
    public TimeSpan? Duration { get; set; }

    #region Foreign Keys
    public required long ChannelId { get; set; }
    public virtual MonitoringChannel? Channel { get; set; } 
    public virtual List<Cut> Cuts { get; set; } = new();
    #endregion
}
