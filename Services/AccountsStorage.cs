using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LolManager.Models;
using Newtonsoft.Json;

namespace LolManager.Services;

public class AccountsStorage : IAccountsStorage
{
    private readonly string _dataFilePath;
    private List<AccountRecord>? _cachedAccounts;
    private DateTime _lastFileRead = DateTime.MinValue;

    public AccountsStorage()
    {
        // Используем ApplicationData (Roaming) для стабильности при обновлениях
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LolManager");
        Directory.CreateDirectory(appDir);
        _dataFilePath = Path.Combine(appDir, "accounts.json");
        
        // Пытаемся мигрировать данные из старого местоположения
        MigrateFromOldLocation();
    }
    
    private void MigrateFromOldLocation()
    {
        // Если новый файл уже существует, миграция не нужна
        if (File.Exists(_dataFilePath)) return;
        
        // Проверяем старое местоположение (LocalApplicationData)
        var oldAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LolManager");
        var oldFilePath = Path.Combine(oldAppDir, "accounts.json");
        
        if (File.Exists(oldFilePath))
        {
            try
            {
                // Копируем файл в новое местоположение
                File.Copy(oldFilePath, _dataFilePath, overwrite: true);
                
                // Создаем backup старого файла
                var backupPath = oldFilePath + ".backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(oldFilePath, backupPath);
                }
            }
            catch
            {
                // Если миграция не удалась, продолжаем работу без данных
            }
        }
    }

    public IEnumerable<AccountRecord> LoadAll()
    {
        if (!File.Exists(_dataFilePath)) return Enumerable.Empty<AccountRecord>();
        
        var fileInfo = new FileInfo(_dataFilePath);
        if (_cachedAccounts == null || fileInfo.LastWriteTime > _lastFileRead)
        {
            var json = File.ReadAllText(_dataFilePath);
            _cachedAccounts = JsonConvert.DeserializeObject<List<AccountRecord>>(json) ?? new List<AccountRecord>();
            _lastFileRead = fileInfo.LastWriteTime;
        }
        
        return _cachedAccounts.OrderBy(a => a.Username);
    }

    public void Save(AccountRecord account)
    {
        var list = LoadAll().ToList();
        var existingIdx = list.FindIndex(a => string.Equals(a.Username, account.Username, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0) list[existingIdx] = account; else list.Add(account);
        
        // Создаем backup перед сохранением
        if (File.Exists(_dataFilePath))
        {
            var backupPath = _dataFilePath + ".bak";
            File.Copy(_dataFilePath, backupPath, overwrite: true);
        }
        
        File.WriteAllText(_dataFilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
        
        // Обновляем кеш
        _cachedAccounts = list;
        _lastFileRead = DateTime.Now;
    }

    public void Delete(string username)
    {
        var list = LoadAll().Where(a => !string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)).ToList();
        File.WriteAllText(_dataFilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
        
        // Обновляем кеш
        _cachedAccounts = list;
        _lastFileRead = DateTime.Now;
    }

    public string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        var protectedBytes = Convert.FromBase64String(encrypted);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public void ExportAccounts(string filePath)
    {
        var accounts = LoadAll();
        var exportAccounts = accounts.Select(acc => new ExportAccountRecord
        {
            Username = acc.Username,
            Password = Unprotect(acc.EncryptedPassword),
            Note = acc.Note,
            CreatedAt = acc.CreatedAt
        }).ToList();
        
        var json = JsonConvert.SerializeObject(exportAccounts, Formatting.Indented);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    public void ImportAccounts(string filePath)
    {
        if (!File.Exists(filePath)) return;
        
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var importAccounts = JsonConvert.DeserializeObject<List<ExportAccountRecord>>(json);
        
        if (importAccounts == null) return;
        
        var existingAccounts = LoadAll().ToList();
        
        foreach (var importAcc in importAccounts)
        {
            var account = new AccountRecord
            {
                Username = importAcc.Username,
                EncryptedPassword = Protect(importAcc.Password),
                Note = importAcc.Note,
                CreatedAt = importAcc.CreatedAt
            };
            
            var existingIdx = existingAccounts.FindIndex(a => 
                string.Equals(a.Username, account.Username, StringComparison.OrdinalIgnoreCase));
            
            if (existingIdx >= 0)
            {
                existingAccounts[existingIdx] = account;
            }
            else
            {
                existingAccounts.Add(account);
            }
        }
        
        if (File.Exists(_dataFilePath))
        {
            var backupPath = _dataFilePath + ".bak";
            File.Copy(_dataFilePath, backupPath, overwrite: true);
        }
        
        File.WriteAllText(_dataFilePath, JsonConvert.SerializeObject(existingAccounts, Formatting.Indented));
        
        _cachedAccounts = existingAccounts;
        _lastFileRead = DateTime.Now;
    }
}


