namespace LolManager.Models;

public class ChallengeInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public bool Legacy { get; set; }
}

