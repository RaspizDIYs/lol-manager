using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LolManager.Services;

public class DataDragonService
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private string _latestVersion = "15.23.1";
    private readonly SemaphoreSlim _championLoadLock = new(1, 1);
    private readonly SemaphoreSlim _spellLoadLock = new(1, 1);
    private readonly SemaphoreSlim _versionLoadLock = new(1, 1);
    private Dictionary<string, string>? _cachedChampions;
    private Dictionary<string, string>? _cachedSpells;
    private bool _versionFetched;
    
    public DataDragonService(ILogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ = EnsureLatestVersionAsync();
    }
    
    private async Task EnsureLatestVersionAsync()
    {
        if (_versionFetched) return;
        
        await _versionLoadLock.WaitAsync();
        try
        {
            if (_versionFetched) return;
            
            var response = await _httpClient.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
            using var doc = JsonDocument.Parse(response);
            var newVersion = doc.RootElement[0].GetString();
            
            if (!string.IsNullOrEmpty(newVersion) && newVersion != _latestVersion)
            {
                _latestVersion = newVersion;
                _logger.Info($"DataDragon: Обновлена версия до {_latestVersion}");
            }
            else
            {
                _logger.Info($"DataDragon: Текущая версия {_latestVersion}");
            }
            
            _versionFetched = true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"DataDragon: Не удалось получить последнюю версию, используется {_latestVersion}: {ex.Message}");
            _versionFetched = true;
        }
        finally
        {
            _versionLoadLock.Release();
        }
    }
    
    public async Task<string> GetLatestVersionAsync()
    {
        await EnsureLatestVersionAsync();
        return _latestVersion;
    }

    private readonly Dictionary<string, Models.ChampionInfo> _championInfoCache = [];

    public async Task<Dictionary<string, string>> GetChampionsAsync()
    {
        await _championLoadLock.WaitAsync();
        try
        {
            if (_cachedChampions != null)
            {
                return _cachedChampions;
            }

            Dictionary<string, string> champions = [];
            _championInfoCache.Clear();
            
            var version = await GetLatestVersionAsync();
            var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/ru_RU/champion.json";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            
            foreach (var champ in doc.RootElement.GetProperty("data").EnumerateObject())
            {
                var englishName = champ.Name; // "Aatrox", "MonkeyKing"
                var displayName = champ.Value.GetProperty("name").GetString() ?? champ.Name;
                var id = champ.Value.GetProperty("key").GetString() ?? champ.Name;
                
                if (!int.TryParse(id, out var championId))
                    continue;
                
                var tags = new List<string>();
                if (champ.Value.TryGetProperty("tags", out var tagsArray))
                {
                    foreach (var tag in tagsArray.EnumerateArray())
                    {
                        var tagValue = tag.GetString();
                        if (!string.IsNullOrEmpty(tagValue))
                            tags.Add(tagValue);
                    }
                }
                
                var aliases = GetChampionAliases(englishName, displayName);
                
                var skins = new List<Models.SkinInfo>();
                if (champ.Value.TryGetProperty("skins", out var skinsArray) && skinsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var skin in skinsArray.EnumerateArray())
                    {
                        if (skin.TryGetProperty("id", out var skinIdProp) && 
                            skin.TryGetProperty("num", out var skinNumProp) &&
                            skin.TryGetProperty("name", out var skinNameProp))
                        {
                            var skinId = skinIdProp.GetInt32();
                            var skinNum = skinNumProp.GetInt32();
                            var skinName = skinNameProp.GetString() ?? string.Empty;
                            
                            var backgroundSkinId = championId * 1000 + skinNum;
                            
                            skins.Add(new Models.SkinInfo
                            {
                                Id = skinId,
                                Name = skinName,
                                SkinNumber = skinNum,
                                ChampionName = displayName,
                                ChampionId = championId,
                                BackgroundSkinId = backgroundSkinId
                            });
                        }
                    }
                }
                
                var info = new Models.ChampionInfo
                {
                    DisplayName = displayName,
                    EnglishName = englishName,
                    Id = id,
                    ImageFileName = englishName,
                    Tags = tags,
                    Aliases = aliases,
                    Skins = skins
                };
                
                _championInfoCache[displayName] = info;
                champions[displayName] = id;
            }
            
            _cachedChampions = champions;
            _logger.Info($"DataDragon: Загружено {champions.Count} чемпионов.");
            return _cachedChampions;
        }
        catch (Exception ex)
        {
            _logger.Error($"DataDragon: Ошибка загрузки чемпионов: {ex.Message}");
            return [];
        }
        finally
        {
            _championLoadLock.Release();
        }
    }
    
    public Models.ChampionInfo? GetChampionInfo(string displayName)
    {
        return _championInfoCache.TryGetValue(displayName, out var info) ? info : null;
    }
    
    public string GetChampionImageFileName(string displayName)
    {
        if (_championInfoCache.TryGetValue(displayName, out var info))
            return info.ImageFileName;
        
        return displayName;
    }

    public string GetChampionSplashartUrl(string displayName, int skinId = 0)
    {
        if (_championInfoCache.TryGetValue(displayName, out var info))
        {
            return $"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{info.EnglishName}_{skinId}.jpg";
        }
        
        return $"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{displayName}_{skinId}.jpg";
    }
    
    private static List<string> GetChampionAliases(string englishName, string displayName)
    {
        var aliases = new List<string> { englishName.ToLowerInvariant(), displayName.ToLowerInvariant() };
        
        // Популярные сокращения (английские, русские, сленг)
        var shortcuts = new Dictionary<string, List<string>>
        {
            { "Aatrox", new List<string> { "атрокс", "aatr", "демон" } },
            { "Ahri", new List<string> { "ари", "лиса", "ahri" } },
            { "Akali", new List<string> { "акали", "aka", "ака" } },
            { "Akshan", new List<string> { "акшан", "акш" } },
            { "Alistar", new List<string> { "алистар", "али", "бык" } },
            { "Amumu", new List<string> { "амуму", "аму", "мумия" } },
            { "Anivia", new List<string> { "анивия", "ani", "птица" } },
            { "Annie", new List<string> { "энни", "анни", "девочка" } },
            { "Aphelios", new List<string> { "афелиос", "апх", "aph", "луна" } },
            { "Ashe", new List<string> { "эш", "ash", "лук" } },
            { "AurelionSol", new List<string> { "Asol", "асол", "аурелион", "дракон", "aurel", "sol" } },
            { "Azir", new List<string> { "азир", "ази", "птица", "император" } },
            { "Bard", new List<string> { "бард", "бар" } },
            { "Belveth", new List<string> { "белвет", "бел", "bel", "пустота" } },
            { "Blitzcrank", new List<string> { "блиц", "блитз", "blitz", "робот" } },
            { "Brand", new List<string> { "бранд", "бран", "огонь" } },
            { "Braum", new List<string> { "браум", "бра", "усы", "щит" } },
            { "Briar", new List<string> { "бриар", "бри" } },
            { "Caitlyn", new List<string> { "кейтлин", "кейт", "cait", "cait", "снайпер" } },
            { "Camille", new List<string> { "камилла", "кам", "cam", "ноги" } },
            { "Cassiopeia", new List<string> { "Cass", "касс", "кассиопея", "змея" } },
            { "ChoGath", new List<string> { "Cho", "чо", "чогат", "чогас", "пустота" } },
            { "Corki", new List<string> { "корки", "кор", "самолет" } },
            { "Darius", new List<string> { "дариус", "дар", "дар", "топор" } },
            { "Diana", new List<string> { "диана", "диа", "dia", "луна" } },
            { "DrMundo", new List<string> { "Mundo", "мундо", "др", "mundo", "доктор" } },
            { "Draven", new List<string> { "дрейвен", "драв", "draven", "топоры" } },
            { "Ekko", new List<string> { "эко", "экко", "eko", "время" } },
            { "Elise", new List<string> { "элиза", "эли", "паук", "spider" } },
            { "Evelynn", new List<string> { "Eve", "ева", "эва", "evelynn", "инкуб" } },
            { "Ezreal", new List<string> { "Ez", "эз", "изреал", "ezr", "блонд" } },
            { "Fiddlesticks", new List<string> { "Fiddle", "фидл", "фид", "пугало", "scarecrow" } },
            { "Fiora", new List<string> { "фиора", "фио", "fio", "дуэлянт" } },
            { "Fizz", new List<string> { "физ", "физз", "рыба" } },
            { "Galio", new List<string> { "галио", "гал", "камень" } },
            { "Gangplank", new List<string> { "GP", "гп", "гангпланк", "планк", "gang", "пират" } },
            { "Garen", new List<string> { "гарен", "гар", "меч", "спин" } },
            { "Gnar", new List<string> { "гнар", "гна", "йордл" } },
            { "Gragas", new List<string> { "грагас", "граг", "grag", "пьяница", "бочка" } },
            { "Graves", new List<string> { "грейвз", "грейв", "grav", "сигара" } },
            { "Gwen", new List<string> { "гвен", "гве", "ножницы" } },
            { "Hecarim", new List<string> { "Heca", "хека", "гекарим", "hec", "конь", "лошадь" } },
            { "Heimerdinger", new List<string> { "Heimer", "хаймер", "донгер", "heim", "турели" } },
            { "Illaoi", new List<string> { "иллаой", "илл", "illa", "кракен", "щупальца" } },
            { "Irelia", new List<string> { "Ire", "ирелия", "ире", "ирка", "лезвия" } },
            { "Ivern", new List<string> { "иверн", "иве", "дерево", "лес" } },
            { "Janna", new List<string> { "жанна", "жан", "jan", "ветер" } },
            { "JarvanIV", new List<string> { "J4", "джарван", "жарван", "jarv", "jar", "король" } },
            { "Jax", new List<string> { "джакс", "жакс", "лампа", "фонарь" } },
            { "Jayce", new List<string> { "джейс", "жейс", "молот" } },
            { "Jhin", new List<string> { "джин", "жин", "4", "четыре", "маска" } },
            { "Jinx", new List<string> { "джинкс", "жинкс", "jinx", "ракета" } },
            { "Kalista", new List<string> { "Kali", "кали", "калиста", "kali", "копья" } },
            { "Karma", new List<string> { "карма", "кар", "kar" } },
            { "Karthus", new List<string> { "Karth", "карт", "картус", "kart", "ульта", "р" } },
            { "Kassadin", new List<string> { "Kassa", "касса", "кас", "kass", "kassad", "пустота" } },
            { "Katarina", new List<string> { "Kata", "ката", "кат", "kat", "кинжалы" } },
            { "Kayle", new List<string> { "кейл", "kay", "ангел", "крылья" } },
            { "Kayn", new List<string> { "кейн", "кайн", "kay", "коса", "раст" } },
            { "Kennen", new List<string> { "кеннен", "кен", "ken", "йордл", "молния" } },
            { "KhaZix", new List<string> { "Khazix", "каzix", "кха", "хазикс", "kha", "жук", "пустота" } },
            { "Kindred", new List<string> { "киндред", "кин", "kind", "овца", "волк" } },
            { "Kled", new List<string> { "клед", "кле", "kled", "ящер" } },
            { "KogMaw", new List<string> { "Kog", "ког", "мав", "kog", "пустота", "рот" } },
            { "KSante", new List<string> { "Ksante", "ксанте", "санте", "кса", "ksante" } },
            { "Leblanc", new List<string> { "LB", "лб", "лебланк", "лебл", "клон" } },
            { "LeeSin", new List<string> { "Lee", "ли", "синь", "lee", "монах", "слепой" } },
            { "Leona", new List<string> { "леона", "лео", "leo", "солнце" } },
            { "Lillia", new List<string> { "лиллия", "лил", "lil", "олень" } },
            { "Lissandra", new List<string> { "Liss", "лисс", "лиссандра", "lissa", "лед" } },
            { "Lucian", new List<string> { "люциан", "люк", "luc", "луциан", "пистолеты" } },
            { "Lulu", new List<string> { "лулу", "лул", "lulu", "фея" } },
            { "Lux", new List<string> { "люкс", "лукс", "lux", "свет", "лазер" } },
            { "Malphite", new List<string> { "Malph", "малф", "малфит", "malph", "камень", "рок" } },
            { "Malzahar", new List<string> { "Malz", "малз", "малзахар", "malza", "пустота" } },
            { "Maokai", new List<string> { "маокай", "мао", "mao", "дерево" } },
            { "MasterYi", new List<string> { "Yi", "ии", "мастер", "yi", "меч", "альфа" } },
            { "Milio", new List<string> { "милио", "мил", "mil", "огонь" } },
            { "MissFortune", new List<string> { "MF", "мф", "фортуна", "miss", "мисс", "пираты" } },
            { "MonkeyKing", new List<string> { "Wukong", "вуконг", "ву", "monkey", "обезьяна", "палка" } },
            { "Mordekaiser", new List<string> { "Morde", "морд", "мордекайзер", "mord", "железо" } },
            { "Morgana", new List<string> { "Morg", "морг", "моргана", "морга", "корень" } },
            { "Naafiri", new List<string> { "наафири", "наа", "naa", "собаки" } },
            { "Nami", new List<string> { "нами", "нам", "nami", "рыба" } },
            { "Nasus", new List<string> { "насус", "нас", "nasus", "nass", "пес", "стаки" } },
            { "Nautilus", new List<string> { "Naut", "наут", "наутилус", "naut", "якорь" } },
            { "Neeko", new List<string> { "нико", "ник", "neeko", "хамелеон" } },
            { "Nidalee", new List<string> { "Nida", "нида", "нидали", "nid", "копье", "кошка" } },
            { "Nilah", new List<string> { "нила", "нил", "nilah", "вода" } },
            { "Nocturne", new List<string> { "Noc", "нок", "ноктюрн", "noct", "кошмар", "тень" } },
            { "Nunu", new List<string> { "nunu", "нуну", "нуна", "nun", "йети", "снежок" } },
            { "Olaf", new List<string> { "олаф", "ола", "olaf", "топоры", "викинг" } },
            { "Orianna", new List<string> { "Ori", "ори", "орианна", "ориана", "ori", "шар" } },
            { "Ornn", new List<string> { "орн", "орнн", "ornn", "кузнец", "баран" } },
            { "Pantheon", new List<string> { "Panth", "пант", "пантеон", "panth", "копье", "щит" } },
            { "Poppy", new List<string> { "поппи", "поп", "poppy", "молот" } },
            { "Pyke", new List<string> { "пайк", "пай", "pyke", "крюк" } },
            { "Qiyana", new List<string> { "Qi", "кияна", "киана", "qi", "обруч" } },
            { "Quinn", new List<string> { "квинн", "кви", "quinn", "птица" } },
            { "Rakan", new List<string> { "ракан", "рак", "rakan", "перья" } },
            { "Rammus", new List<string> { "раммус", "рам", "rammus", "еж", "шар", "ok" } },
            { "RekSai", new List<string> { "Reksai", "рексай", "рек", "rek", "туннели", "пустота" } },
            { "Rell", new List<string> { "релл", "рел", "rell", "железо" } },
            { "RenataGlasc", new List<string> { "Renata", "рената", "рен", "renata" } },
            { "Renekton", new List<string> { "Renek", "ренек", "рен", "renek", "крок", "крокодил" } },
            { "Rengar", new List<string> { "ренгар", "рен", "rengar", "кот", "лев", "куст" } },
            { "Riven", new List<string> { "ривен", "рив", "riven", "меч" } },
            { "Rumble", new List<string> { "рамбл", "рам", "rumble", "робот" } },
            { "Ryze", new List<string> { "райз", "рай", "ryze", "синий", "рунты" } },
            { "Samira", new List<string> { "Sam", "сэм", "самира", "сами", "sam", "стиль" } },
            { "Sejuani", new List<string> { "Sej", "седж", "седжуани", "sej", "свинья", "кабан" } },
            { "Senna", new List<string> { "сенна", "сен", "senna", "пушка" } },
            { "Seraphine", new List<string> { "Sera", "сера", "серафина", "sera", "сцена" } },
            { "Sett", new List<string> { "сетт", "сет", "sett", "кулаки" } },
            { "Shaco", new List<string> { "шако", "шак", "shaco", "клоун", "коробка" } },
            { "Shen", new List<string> { "шен", "shen", "ульта", "щит" } },
            { "Shyvana", new List<string> { "Shyv", "шив", "шивана", "shyv", "дракон", "огонь" } },
            { "Singed", new List<string> { "синджед", "синж", "singed", "газ", "яд" } },
            { "Sion", new List<string> { "сион", "си", "sion", "зомби" } },
            { "Sivir", new List<string> { "сивир", "сив", "sivir", "бумеранг" } },
            { "Skarner", new List<string> { "скарнер", "скар", "skarner", "скорпион", "шпили" } },
            { "Sona", new List<string> { "сона", "сон", "sona", "арфа" } },
            { "Soraka", new List<string> { "Raka", "рака", "сорака", "сора", "soraka", "банан", "коза" } },
            { "Swain", new List<string> { "свейн", "све", "swain", "ворон" } },
            { "Sylas", new List<string> { "сайлас", "сай", "sylas", "цепи" } },
            { "Syndra", new List<string> { "Synd", "синд", "синдра", "synd", "шары" } },
            { "TahmKench", new List<string> { "Tahm", "там", "кенч", "tahm", "жаба", "язык" } },
            { "Taliyah", new List<string> { "Tali", "тали", "талия", "tali", "камни", "стена" } },
            { "Talon", new List<string> { "талон", "тал", "talon", "паркур" } },
            { "Taric", new List<string> { "тарик", "тар", "taric", "гем", "кристалл" } },
            { "Teemo", new List<string> { "тимо", "тим", "teemo", "сатана", "грибы", "йордл" } },
            { "Thresh", new List<string> { "треш", "тре", "thresh", "цепь", "фонарь" } },
            { "Tristana", new List<string> { "Trist", "трист", "тристана", "tris", "пушка", "йордл" } },
            { "Trundle", new List<string> { "трандл", "тран", "trundle", "тролль", "столб" } },
            { "Tryndamere", new List<string> { "Trynd", "тринд", "триндамир", "trynd", "берсерк", "р" } },
            { "TwistedFate", new List<string> { "TF", "тф", "твист", "tf", "карты" } },
            { "Twitch", new List<string> { "твич", "тви", "twitch", "крыса", "яд" } },
            { "Udyr", new List<string> { "удир", "уди", "udyr", "стойки" } },
            { "Urgot", new List<string> { "ургот", "ург", "urgot", "краб" } },
            { "Varus", new List<string> { "варус", "вар", "varus", "лук", "стрелы" } },
            { "Vayne", new List<string> { "вейн", "вей", "vayne", "арбалет" } },
            { "Veigar", new List<string> { "Vei", "вей", "вейгар", "veig", "йордл", "клетка" } },
            { "Velkoz", new List<string> { "Velkoz", "велкоз", "вел", "vel", "кальмар", "лазеры", "пустота" } },
            { "Vex", new List<string> { "векс", "век", "vex", "тень" } },
            { "Vi", new List<string> { "ви", "vi", "кулаки" } },
            { "Viego", new List<string> { "виего", "вие", "viego", "король" } },
            { "Viktor", new List<string> { "Vik", "вик", "виктор", "vik", "лазер", "робот" } },
            { "Vladimir", new List<string> { "Vlad", "влад", "вампир", "vlad", "кровь", "лужа" } },
            { "Volibear", new List<string> { "Voli", "воли", "волибир", "voli", "медведь", "молния" } },
            { "Warwick", new List<string> { "WW", "вв", "варвик", "вар", "ww", "волк", "вой" } },
            { "Xayah", new List<string> { "зая", "заяша", "xayah", "перья" } },
            { "Xerath", new List<string> { "Xer", "ксерат", "ксер", "xerath", "молнии" } },
            { "XinZhao", new List<string> { "Xin", "ксин", "жао", "xin", "копье" } },
            { "Yasuo", new List<string> { "Yas", "ясуо", "ясик", "яс", "yas", "ветер", "0/10" } },
            { "Yone", new List<string> { "йоне", "йон", "yone", "меч" } },
            { "Yorick", new List<string> { "йорик", "йор", "yorick", "могилы" } },
            { "Yuumi", new List<string> { "юми", "юм", "yuumi", "кот", "книга" } },
            { "Zac", new List<string> { "зак", "zac", "слизь", "резина" } },
            { "Zed", new List<string> { "зед", "zed", "тень", "сюрикен" } },
            { "Zeri", new List<string> { "зери", "зер", "zeri", "молния" } },
            { "Ziggs", new List<string> { "зигс", "зиг", "ziggs", "бомба", "йордл" } },
            { "Zilean", new List<string> { "Zil", "зил", "зилеан", "zil", "время", "бомбы" } },
            { "Zoe", new List<string> { "зоя", "зо", "zoe", "звезда", "пузырь" } },
            { "Zyra", new List<string> { "зира", "зир", "zyra", "растения" } }
        };
        
        if (shortcuts.TryGetValue(englishName, out var shortcutList))
        {
            foreach (var shortcut in shortcutList)
            {
                aliases.Add(shortcut.ToLowerInvariant());
            }
        }
        
        return aliases;
    }
    
    public List<string> GetChampionLanes(string displayName)
    {
        if (!_championInfoCache.TryGetValue(displayName, out var info))
            return new List<string>();
        
        List<string> lanes = [];
        var tags = info.Tags;
        var name = info.EnglishName;
        
        // Маппинг на основе тегов и конкретных чемпионов
        // TOP
        if (tags.Contains("Tank") || tags.Contains("Fighter"))
        {
            lanes.Add("TOP");
        }
        
        // JUNGLE
        if (tags.Contains("Fighter") || tags.Contains("Assassin") || JungleChampions.Contains(name))
        {
            if (!lanes.Contains("JUNGLE"))
                lanes.Add("JUNGLE");
        }
        
        // MIDDLE
        if (tags.Contains("Mage") || tags.Contains("Assassin"))
        {
            lanes.Add("MIDDLE");
        }
        
        // BOTTOM (ADC)
        if (tags.Contains("Marksman"))
        {
            lanes.Add("BOTTOM");
        }
        
        // UTILITY (Support)
        if (tags.Contains("Support") || SupportChampions.Contains(name))
        {
            lanes.Add("UTILITY");
        }
        
        // Особые случаи
        if (BottomSpecialCases.Contains(name) && !lanes.Contains("BOTTOM"))
            lanes.Add("BOTTOM");
        
        return lanes;
    }

    private readonly Dictionary<string, string> _spellImageMapping = [];

    private static readonly string[] JungleChampions = 
    [
        "Ivern", "Nidalee", "Kindred", "Lillia", "Graves", "KhaZix", "RekSai", "Elise",
        "Evelynn", "MasterYi", "Shyvana", "Udyr", "Volibear", "Warwick", "Amumu",
        "Sejuani", "Zac", "Rammus", "Nunu", "Hecarim", "JarvanIV", "LeeSin",
        "XinZhao", "Vi", "Nocturne", "Rengar", "Kayn", "Ekko", "Fiddlesticks",
        "Viego", "Belveth", "Briar"
    ];

    private static readonly string[] SupportChampions = 
    [
        "Blitzcrank", "Thresh", "Bard", "Rakan", "Pyke", "Nautilus", "Leona",
        "Braum", "TahmKench", "Alistar", "Senna", "Milio", "Renata"
    ];

    private static readonly string[] BottomSpecialCases = ["Yasuo", "Yone"];

    public async Task<Dictionary<string, string>> GetSummonerSpellsAsync()
    {
        await _spellLoadLock.WaitAsync();
        try
        {
            if (_cachedSpells != null)
            {
                return _cachedSpells;
            }

            Dictionary<string, string> spells = [];
            _spellImageMapping.Clear();
            
            var version = await GetLatestVersionAsync();
            var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/ru_RU/summoner.json";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            foreach (var spell in doc.RootElement.GetProperty("data").EnumerateObject())
            {
                var imageFile = spell.Value.GetProperty("image").GetProperty("full").GetString() ?? $"{spell.Name}.png";
                var displayName = spell.Value.GetProperty("name").GetString() ?? spell.Name;
                
                spells[displayName] = spell.Value.GetProperty("key").GetString() ?? spell.Name;
                _spellImageMapping[displayName] = imageFile.Replace(".png", "");
            }
            
            _cachedSpells = spells;
            _logger.Info($"DataDragon: Загружено {spells.Count} заклинаний призывателя.");
            return _cachedSpells;
        }
        catch (Exception ex)
        {
            _logger.Error($"DataDragon: Ошибка загрузки заклинаний призывателя: {ex.Message}");
            return [];
        }
        finally
        {
            _spellLoadLock.Release();
        }
    }
    
    public string GetSummonerSpellImageFileName(string displayName)
    {
        if (_spellImageMapping.TryGetValue(displayName, out var imageFile))
            return imageFile;
        
        // Фоллбек: добавляем "Summoner" обратно
        return displayName.StartsWith("Summoner") ? displayName : $"Summoner{displayName}";
    }

    public string GetChampionImageUrl(string championName)
    {
        if (string.IsNullOrWhiteSpace(championName)) return string.Empty;
        return $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/champion/{championName}.png";
    }

    public string GetSummonerSpellImageUrl(string spellName)
    {
        if (string.IsNullOrWhiteSpace(spellName)) return string.Empty;
        return $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/spell/{spellName}.png";
    }
    
    public string GetRankIconUrl(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return string.Empty;
        
        var tierLower = tier.ToUpperInvariant();
        return $"https://ddragon.leagueoflegends.com/cdn/img/ranked-emblems/{tierLower}.png";
    }
}
