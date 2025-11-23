using System.Collections.Generic;

namespace LolManager.Models;

public class ChampionInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new(); // Fighter, Tank, Mage, Assassin, Support, Marksman
    public List<string> Aliases { get; set; } = new(); // Сокращения и альтернативные имена
    public List<SkinInfo> Skins { get; set; } = new(); // Список скинов чемпиона
}

