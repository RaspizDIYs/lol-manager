namespace LolManager.Models;

public class SkinInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SkinNumber { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public int BackgroundSkinId { get; set; }
}

