using System.Collections.Generic;
using System.Linq;
using LolManager.Models;

namespace LolManager.Services;

public class RuneDataService
{
    private static readonly Dictionary<int, string> StatModIcons = new()
    {
        { 5001, "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsHealthScalingIcon.png" },
        { 5002, "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsArmorIcon.png" },
        { 5003, "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsMagicResIcon.png" },
        { 5005, "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsAttackSpeedIcon.png" },
        { 5007, "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsCDRScalingIcon.png" },
        { 5008, "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsAdaptiveForceIcon.png" }
    };

    public List<RunePath> GetAllPaths()
    {
        return new List<RunePath>
        {
            new RunePath
            {
                Id = 8000,
                Key = "Precision",
                Name = "Точность",
                Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/7201_Precision.png",
                Slots = new List<RuneSlot>
                {
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8005, Key = "PressTheAttack", Name = "Град клинков", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/PressTheAttack/PressTheAttack.png" },
                            new Rune { Id = 8008, Key = "LethalTempo", Name = "Смертельный темп", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/LethalTempo/LethalTempoTemp.png" },
                            new Rune { Id = 8021, Key = "FleetFootwork", Name = "Быстрые ноги", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/FleetFootwork/FleetFootwork.png" },
                            new Rune { Id = 8010, Key = "Conqueror", Name = "Завоеватель", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/Conqueror/Conqueror.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 9923, Key = "AbsorbLife", Name = "Поглощение жизни", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/AbsorbLife/AbsorbLife.png" },
                            new Rune { Id = 9111, Key = "Triumph", Name = "Триумф", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/Triumph.png" },
                            new Rune { Id = 8009, Key = "PresenceOfMind", Name = "Присутствие духа", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/PresenceOfMind/PresenceOfMind.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 9104, Key = "LegendAlacrity", Name = "Легенда: рвение", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/LegendAlacrity/LegendAlacrity.png" },
                            new Rune { Id = 9105, Key = "LegendHaste", Name = "Легенда: ускорение", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/LegendTenacity/LegendTenacity.png" },
                            new Rune { Id = 9103, Key = "LegendBloodline", Name = "Легенда: родословная", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/LegendBloodline/LegendBloodline.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8014, Key = "CoupDeGrace", Name = "Удар милосердия", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/CoupDeGrace/CoupDeGrace.png" },
                            new Rune { Id = 8017, Key = "CutDown", Name = "Сокрушение", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/CutDown/CutDown.png" },
                            new Rune { Id = 8299, Key = "LastStand", Name = "Последний рубеж", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/LastStand/LastStand.png" }
                        }
                    }
                }
            },
            new RunePath
            {
                Id = 8100,
                Key = "Domination",
                Name = "Доминирование",
                Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/7200_Domination.png",
                Slots = new List<RuneSlot>
                {
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8112, Key = "Electrocute", Name = "Казнь электричеством", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/Electrocute/Electrocute.png" },
                            new Rune { Id = 8124, Key = "DarkHarvest", Name = "Темная жатва", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/DarkHarvest/DarkHarvest.png" },
                            new Rune { Id = 8128, Key = "HailOfBlades", Name = "Град клинков", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/HailOfBlades/HailOfBlades.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8126, Key = "CheapShot", Name = "Дешевый выстрел", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/CheapShot/CheapShot.png" },
                            new Rune { Id = 8139, Key = "TasteOfBlood", Name = "Вкус крови", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/TasteOfBlood/GreenTerror_TasteOfBlood.png" },
                            new Rune { Id = 8143, Key = "SuddenImpact", Name = "Внезапный удар", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/SuddenImpact/SuddenImpact.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8136, Key = "ZombieWard", Name = "Зомби-тотем", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/ZombieWard/ZombieWard.png" },
                            new Rune { Id = 8120, Key = "GhostPoro", Name = "Призрачный поро", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/GhostPoro/GhostPoro.png" },
                            new Rune { Id = 8138, Key = "EyeballCollection", Name = "Коллекция глаз", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/EyeballCollection/EyeballCollection.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8135, Key = "TreasureHunter", Name = "Охотник за сокровищами", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/TreasureHunter/TreasureHunter.png" },
                            new Rune { Id = 8134, Key = "IngeniousHunter", Name = "Изобретательный охотник", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/IngeniousHunter/IngeniousHunter.png" },
                            new Rune { Id = 8105, Key = "RelentlessHunter", Name = "Безжалостный охотник", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/RelentlessHunter/RelentlessHunter.png" },
                            new Rune { Id = 8106, Key = "UltimateHunter", Name = "Абсолютный охотник", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/UltimateHunter/UltimateHunter.png" }
                        }
                    }
                }
            },
            new RunePath
            {
                Id = 8200,
                Key = "Sorcery",
                Name = "Колдовство",
                Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/7202_Sorcery.png",
                Slots = new List<RuneSlot>
                {
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8214, Key = "SummonAery", Name = "Призыв Аэри", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/SummonAery/SummonAery.png" },
                            new Rune { Id = 8229, Key = "ArcaneComet", Name = "Магическая комета", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/ArcaneComet/ArcaneComet.png" },
                            new Rune { Id = 8230, Key = "PhaseRush", Name = "Фазовый рывок", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/PhaseRush/PhaseRush.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8224, Key = "NimbusCloak", Name = "Сияющий плащ", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/NimbusCloak/6361.png" },
                            new Rune { Id = 8226, Key = "ManaflowBand", Name = "Поток маны", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/ManaflowBand/ManaflowBand.png" },
                            new Rune { Id = 8275, Key = "NullifyingOrb", Name = "Сфера антимагии", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/NullifyingOrb/Pokeshield.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8210, Key = "Transcendence", Name = "Превосходство", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/Transcendence/Transcendence.png" },
                            new Rune { Id = 8234, Key = "Celerity", Name = "Быстрота", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/Celerity/CelerityTemp.png" },
                            new Rune { Id = 8233, Key = "AbsoluteFocus", Name = "Безупречность", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/AbsoluteFocus/AbsoluteFocus.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8237, Key = "Scorch", Name = "Ожог", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/Scorch/Scorch.png" },
                            new Rune { Id = 8232, Key = "Waterwalking", Name = "Хождение по воде", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/Waterwalking/Waterwalking.png" },
                            new Rune { Id = 8236, Key = "GatheringStorm", Name = "Надвигающаяся буря", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/GatheringStorm/GatheringStorm.png" }
                        }
                    }
                }
            },
            new RunePath
            {
                Id = 8400,
                Key = "Resolve",
                Name = "Храбрость",
                Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/7204_Resolve.png",
                Slots = new List<RuneSlot>
                {
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8437, Key = "GraspOfTheUndying", Name = "Хватка нежити", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/GraspOfTheUndying/GraspOfTheUndying.png" },
                            new Rune { Id = 8439, Key = "Aftershock", Name = "Дрожь земли", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/VeteranAftershock/VeteranAftershock.png" },
                            new Rune { Id = 8465, Key = "Guardian", Name = "Страж", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/Guardian/Guardian.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8446, Key = "Demolish", Name = "Снос", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/Demolish/Demolish.png" },
                            new Rune { Id = 8463, Key = "FontOfLife", Name = "Источник жизни", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/FontOfLife/FontOfLife.png" },
                            new Rune { Id = 8401, Key = "ShieldBash", Name = "Удар щитом", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/MirrorShell/MirrorShell.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8429, Key = "Conditioning", Name = "Воздаяние", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/Conditioning/Conditioning.png" },
                            new Rune { Id = 8444, Key = "SecondWind", Name = "Второе дыхание", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/SecondWind/SecondWind.png" },
                            new Rune { Id = 8473, Key = "BonePlating", Name = "Костяная пластина", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/BonePlating/BonePlating.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8451, Key = "Overgrowth", Name = "Разрастание", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/Overgrowth/Overgrowth.png" },
                            new Rune { Id = 8453, Key = "Revitalize", Name = "Оживление", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Resolve/Revitalize/Revitalize.png" },
                            new Rune { Id = 8242, Key = "Unflinching", Name = "Непоколебимость", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Sorcery/Unflinching/Unflinching.png" }
                        }
                    }
                }
            },
            new RunePath
            {
                Id = 8300,
                Key = "Inspiration",
                Name = "Вдохновение",
                Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/7203_Whimsy.png",
                Slots = new List<RuneSlot>
                {
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8351, Key = "GlacialAugment", Name = "Ледяной нарост", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/GlacialAugment/GlacialAugment.png" },
                            new Rune { Id = 8360, Key = "UnsealedSpellbook", Name = "Книга заклинаний", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/UnsealedSpellbook/UnsealedSpellbook.png" },
                            new Rune { Id = 8369, Key = "FirstStrike", Name = "Первый удар", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/FirstStrike/FirstStrike.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8306, Key = "HextechFlashtraption", Name = "Хекстековый скачок", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/HextechFlashtraption/HextechFlashtraption.png" },
                            new Rune { Id = 8304, Key = "MagicalFootwear", Name = "Магическая обувь", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/MagicalFootwear/MagicalFootwear.png" },
                            new Rune { Id = 8321, Key = "CashBack", Name = "Возврат денег", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/CashBack/CashBack.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8313, Key = "TripleTonic", Name = "Тройное зелье", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/TripleTonic/TripleTonic.png" },
                            new Rune { Id = 8352, Key = "TimeWarpTonic", Name = "Искажение времени", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/TimeWarpTonic/TimeWarpTonic.png" },
                            new Rune { Id = 8345, Key = "BiscuitDelivery", Name = "Доставка печенья", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/BiscuitDelivery/BiscuitDelivery.png" }
                        }
                    },
                    new RuneSlot
                    {
                        Runes = new List<Rune>
                        {
                            new Rune { Id = 8347, Key = "CosmicInsight", Name = "Космическое знание", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/CosmicInsight/CosmicInsight.png" },
                            new Rune { Id = 8410, Key = "ApproachVelocity", Name = "Скорость сближения", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/ApproachVelocity/ApproachVelocity.png" },
                            new Rune { Id = 8316, Key = "JackOfAllTrades", Name = "Мастер на все руки", Icon = "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Inspiration/JackOfAllTrades/JackOfAllTrades.png" }
                        }
                    }
                }
            }
        };
    }

    public List<Rune> GetStatMods()
    {
        return new List<Rune>
        {
            new Rune { Id = 5008, Key = "AdaptiveForce", Name = "Адаптивная сила", Icon = StatModIcons[5008] },
            new Rune { Id = 5005, Key = "AttackSpeed", Name = "Скорость атаки", Icon = StatModIcons[5005] },
            new Rune { Id = 5007, Key = "CDRScaling", Name = "Ускорение умений", Icon = StatModIcons[5007] },
            new Rune { Id = 5002, Key = "Armor", Name = "Броня", Icon = StatModIcons[5002] },
            new Rune { Id = 5003, Key = "MagicRes", Name = "Магическая защита", Icon = StatModIcons[5003] },
            new Rune { Id = 5001, Key = "HealthScaling", Name = "Здоровье", Icon = StatModIcons[5001] }
        };
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
        
        return GetStatMods().FirstOrDefault(r => r.Id == id);
    }
}

