namespace LolManager.Models;

public record LcuRunePage
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public int primaryStyleId { get; set; }
    public int subStyleId { get; set; }
    public int[] selectedPerkIds { get; set; } = Array.Empty<int>();
    public bool current { get; set; }
    public bool isEditable { get; set; }
    public bool isActive { get; set; }
}

public record LcuPerk
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string iconPath { get; set; } = string.Empty;
    // Важно: остальные поля из LCU намеренно не описываем, чтобы избежать ошибок десериализации при изменении типов
}
