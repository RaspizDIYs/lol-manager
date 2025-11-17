namespace LolManager.Models;

public record RegionInfo(string Code, string Name)
{
    public override string ToString() => Name;
}
