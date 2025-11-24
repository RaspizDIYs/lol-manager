using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LolManager.Models;

namespace LolManager.Services;

public interface IRunePagesStorage
{
    List<RunePage> LoadAll();
    void SaveAll(IEnumerable<RunePage> pages);
    void Save(RunePage page);
    void Delete(string pageName);
}

public class RunePagesStorage : IRunePagesStorage
{
    private readonly string _dataFilePath;
    private readonly ILogger? _logger;
    private List<RunePage>? _cachedPages;
    private DateTime _lastFileRead = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(2);

    public RunePagesStorage(ILogger? logger = null)
    {
        var roamingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LolManager");

        if (!Directory.Exists(roamingPath))
            Directory.CreateDirectory(roamingPath);

        _dataFilePath = Path.Combine(roamingPath, "rune-pages.json");
        _logger = logger;

        MigrateFromOldLocation();
    }

    private void MigrateFromOldLocation()
    {
        try
        {
            if (File.Exists(_dataFilePath))
                return;

            var localPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LolManager");
            var oldFilePath = Path.Combine(localPath, "rune-pages.json");

            if (!File.Exists(oldFilePath))
                return;

            File.Copy(oldFilePath, _dataFilePath, overwrite: true);

            var backupPath = oldFilePath + ".backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(oldFilePath, backupPath, overwrite: false);
            }

            _logger?.Info("Rune pages migrated to roaming AppData storage.");
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Rune pages migration failed: {ex.Message}");
        }
    }

    public List<RunePage> LoadAll()
    {
        try
        {
            var now = DateTime.Now;
            if (_cachedPages != null && now - _lastFileRead < _cacheExpiration)
            {
                return new List<RunePage>(_cachedPages);
            }

            if (!File.Exists(_dataFilePath))
            {
                _cachedPages = new List<RunePage>();
                _lastFileRead = now;
                return new List<RunePage>();
            }

            var json = File.ReadAllText(_dataFilePath);
            var pages = JsonSerializer.Deserialize<List<RunePage>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<RunePage>();

            _cachedPages = pages;
            _lastFileRead = now;
            
            return new List<RunePage>(pages);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to load rune pages: {ex.Message}");
            return new List<RunePage>();
        }
    }

    public void SaveAll(IEnumerable<RunePage> pages)
    {
        try
        {
            var list = pages.ToList();

            if (File.Exists(_dataFilePath))
            {
                var backupPath = _dataFilePath + ".bak";
                File.Copy(_dataFilePath, backupPath, overwrite: true);
            }

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_dataFilePath, json);

            _cachedPages = list;
            _lastFileRead = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to save rune pages: {ex.Message}");
        }
    }

    public void Save(RunePage page)
    {
        try
        {
            var list = LoadAll();
            var existingIdx = list.FindIndex(p => string.Equals(p.Name, page.Name, StringComparison.OrdinalIgnoreCase));
            
            if (existingIdx >= 0)
                list[existingIdx] = page;
            else
                list.Add(page);

            SaveAll(list);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to save rune page '{page.Name}': {ex.Message}");
        }
    }

    public void Delete(string pageName)
    {
        try
        {
            var list = LoadAll()
                .Where(p => !string.Equals(p.Name, pageName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            SaveAll(list);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to delete rune page '{pageName}': {ex.Message}");
        }
    }
}

