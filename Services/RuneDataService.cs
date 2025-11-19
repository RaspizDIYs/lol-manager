using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using LolManager.Models;

namespace LolManager.Services;

public class RuneDataService
{
    private static readonly HttpClient Http = new();
    private static List<RunePath>? _cachedPaths;
    private static string _cachedVersion = string.Empty;
    private static Dictionary<int, (string Name, string Icon)> _allPerksById = new();
    
    // Статичный список актуальных осколков (Stat Shards / Mods)
    // Row 1: Offense
    private static readonly Rune StatModAdaptiveForce = new() { Id = 5008, Key = "AdaptiveForce", Name = "Адаптивная сила (+9)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodsadaptiveforceicon.png" };
    private static readonly Rune StatModAttackSpeed = new() { Id = 5005, Key = "AttackSpeed", Name = "Скорость атаки (+10%)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodsattackspeedicon.png" };
    private static readonly Rune StatModAbilityHaste = new() { Id = 5007, Key = "AbilityHaste", Name = "Ускорение умений (+8)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodscdrscalingicon.png" };
    
    // Row 2: Flex
    private static readonly Rune StatModMoveSpeed = new() { Id = 5010, Key = "MoveSpeed", Name = "Скорость передвижения (+2%)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodsmovementspeedicon.png" };
    private static readonly Rune StatModHealthScaling = new() { Id = 5001, Key = "HealthScaling", Name = "Здоровье (10-180)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodshealthplusicon.png" };
    
    // Row 3: Defense
    private static readonly Rune StatModTenacity = new() { Id = 5013, Key = "Tenacity", Name = "Стойкость (+10%)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodstenacityicon.png" };
    private static readonly Rune StatModHealthFlat = new() { Id = 5011, Key = "HealthFlat", Name = "Здоровье (+65)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodshealthscalingicon.png" };

    // Устаревшие, но могут быть в старых страницах
    private static readonly Rune StatModArmor = new() { Id = 5002, Key = "Armor", Name = "Броня (+6)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodsarmoricon.png" };
    private static readonly Rune StatModMagicRes = new() { Id = 5003, Key = "MagicRes", Name = "Сопр. магии (+8)", Icon = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodsmagicresicon.png" };
    
    private static readonly List<Rune> AllStatMods = new()
    {
        StatModAdaptiveForce, StatModAttackSpeed, StatModAbilityHaste,
        StatModMoveSpeed, StatModHealthScaling,
        StatModTenacity, StatModHealthFlat,
        // Добавляем старые для GetRuneById
        StatModArmor, StatModMagicRes
    };

    public List<Rune> GetStatModsRow1()
    {
        return new List<Rune> { StatModAdaptiveForce, StatModAttackSpeed, StatModAbilityHaste };
    }
    
    public List<Rune> GetStatModsRow2()
    {
        return new List<Rune> { StatModAdaptiveForce, StatModMoveSpeed, StatModHealthScaling };
    }
    
    public List<Rune> GetStatModsRow3()
    {
        return new List<Rune> { StatModHealthFlat, StatModTenacity, StatModHealthScaling };
    }

    public List<RunePath> GetAllPaths()
    {
        if (_cachedPaths != null) return new List<RunePath>(_cachedPaths);

        try
        {
            // Получаем последнюю версию
            var versionsJson = Http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json").GetAwaiter().GetResult();
            using (var doc = JsonDocument.Parse(versionsJson))
            {
                _cachedVersion = doc.RootElement[0].GetString() ?? _cachedVersion;
            }

            // Загружаем руны текущей версии
            var url = $"https://ddragon.leagueoflegends.com/cdn/{_cachedVersion}/data/ru_RU/runesReforged.json";
            var json = Http.GetStringAsync(url).GetAwaiter().GetResult();
            var elements = JsonDocument.Parse(json).RootElement;

            var result = new List<RunePath>();

            foreach (var pathEl in elements.EnumerateArray())
            {
                var path = new RunePath
                {
                    Id = pathEl.GetProperty("id").GetInt32(),
                    Key = pathEl.GetProperty("key").GetString() ?? string.Empty,
                    Name = pathEl.GetProperty("name").GetString() ?? string.Empty,
                    Icon = BuildIconUrl(pathEl.GetProperty("icon").GetString())
                };

                var slots = new List<RuneSlot>();
                foreach (var slotEl in pathEl.GetProperty("slots").EnumerateArray())
                {
                    var slot = new RuneSlot { Runes = new List<Rune>() };
                    foreach (var runeEl in slotEl.GetProperty("runes").EnumerateArray())
                    {
                        slot.Runes.Add(new Rune
                        {
                            Id = runeEl.GetProperty("id").GetInt32(),
                            Key = runeEl.GetProperty("key").GetString() ?? string.Empty,
                            Name = runeEl.GetProperty("name").GetString() ?? string.Empty,
                            Icon = BuildIconUrl(runeEl.GetProperty("icon").GetString())
                        });
                    }
                    slots.Add(slot);
                }
                path.Slots = slots;
                result.Add(path);
            }

            _cachedPaths = result;
            return new List<RunePath>(_cachedPaths);
        }
        catch
        {
            _cachedPaths = new List<RunePath>();
            return new List<RunePath>();
        }
    }

    private static string BuildIconUrl(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return string.Empty;
        // В runesReforged.json поле icon вида "perk-images/Styles/.../Icon.png"
        if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return relative;
        return $"https://ddragon.leagueoflegends.com/cdn/img/{relative}";
    }
    
    public RunePath? GetPathById(int id)
    {
        return GetAllPaths().FirstOrDefault(p => p.Id == id);
    }

    public Rune? GetRuneById(int id)
    {
        foreach (var path in GetAllPaths())
        {
            foreach (var slot in path.Slots)
            {
                var rune = slot.Runes.FirstOrDefault(r => r.Id == id);
                if (rune != null) return rune;
            }
        }
        
        // Попробуем среди шардов
        var shard = AllStatMods.FirstOrDefault(r => r.Id == id);
        if (shard != null) return shard;

        // Последний шанс: глобальный справочник перков по ID
        EnsureAllPerksLoaded();
        if (_allPerksById.TryGetValue(id, out var perk))
        {
            return new Rune { Id = id, Key = perk.Name, Name = perk.Name, Icon = perk.Icon };
        }

        return null;
    }

    private void EnsureAllPerksLoaded()
    {
        if (_allPerksById.Count > 0) return;
        try
        {
            var json = Http.GetStringAsync("https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perks.json").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            foreach (var perk in doc.RootElement.EnumerateArray())
            {
                var id = perk.GetProperty("id").GetInt32();
                var name = perk.TryGetProperty("name", out var nm) ? (nm.GetString() ?? id.ToString()) : id.ToString();
                var iconRel = perk.TryGetProperty("iconPath", out var ip) ? ip.GetString() : null;
                var icon = iconRel != null ? $"https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/{iconRel.TrimStart('/')}" : string.Empty;
                _allPerksById[id] = (name, icon);
            }
        }
        catch { }
    }
}

