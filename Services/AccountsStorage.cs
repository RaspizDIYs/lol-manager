using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LolManager.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LolManager.Services;

public class AccountsStorage : IAccountsStorage
{
    private readonly string _dataFilePath;
    private List<AccountRecord>? _cachedAccounts;
    private DateTime _lastFileRead = DateTime.MinValue;
    private readonly ILogger? _logger;

    public AccountsStorage() : this(null) { }

    public AccountsStorage(ILogger? logger = null)
    {
        _logger = logger;
        
        // Используем ApplicationData (Roaming) для стабильности при обновлениях
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LolManager");
        Directory.CreateDirectory(appDir);
        _dataFilePath = Path.Combine(appDir, "accounts.json");
        
        _logger?.Info("AccountsStorage initialized with data path: " + _dataFilePath);
        
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

    public void ExportAccounts(string filePath, IEnumerable<AccountRecord>? selectedAccounts = null)
    {
        var accountsToExport = selectedAccounts ?? LoadAll();
        ExportAccountsEncrypted(filePath, accountsToExport, null);
    }

    public void ExportAccounts(string filePath, string password, IEnumerable<AccountRecord>? selectedAccounts = null)
    {
        var accountsToExport = selectedAccounts ?? LoadAll();
        ExportAccountsEncrypted(filePath, accountsToExport, password);
    }

    private void ExportAccountsEncrypted(string filePath, IEnumerable<AccountRecord> accounts, string? password)
    {
        var exportAccounts = accounts.Select(acc => new ExportAccountRecord
        {
            Username = acc.Username,
            Password = Unprotect(acc.EncryptedPassword),
            Note = acc.Note,
            CreatedAt = acc.CreatedAt
        }).ToList();

        var accountsJson = JsonConvert.SerializeObject(exportAccounts, Formatting.Indented);
        
        EncryptedExportData exportData;
        
        if (!string.IsNullOrEmpty(password))
        {
            var salt = GenerateRandomSalt();
            var iv = GenerateRandomIV();
            var encryptedAccounts = EncryptDataAes(accountsJson, password, salt, iv);
            
            exportData = new EncryptedExportData
            {
                Version = 3,
                AppName = "LolManager",
                ExportedAt = DateTime.UtcNow,
                EncryptedAccounts = encryptedAccounts,
                Salt = Convert.ToBase64String(salt),
                IV = Convert.ToBase64String(iv)
            };
        }
        else
        {
            var salt = GenerateRandomSalt();
            var encryptedAccounts = EncryptDataLegacy(accountsJson, salt);
            
            exportData = new EncryptedExportData
            {
                Version = 2,
                AppName = "LolManager",
                ExportedAt = DateTime.UtcNow,
                EncryptedAccounts = encryptedAccounts,
                Salt = Convert.ToBase64String(salt)
            };
        }
        
        var finalJson = JsonConvert.SerializeObject(exportData, Formatting.Indented);
        File.WriteAllText(filePath, finalJson, Encoding.UTF8);
    }

    private byte[] GenerateRandomSalt()
    {
        var salt = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }

    private byte[] GenerateRandomIV()
    {
        var iv = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(iv);
        return iv;
    }

    private string EncryptDataAes(string data, string password, byte[] salt, byte[] iv)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
        
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;
        
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
        aes.Key = pbkdf2.GetBytes(32);
        
        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        {
            csEncrypt.Write(dataBytes, 0, dataBytes.Length);
        }
        
        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    private string DecryptDataAes(string encryptedData, string password, byte[] salt, byte[] iv)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedData);
        
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;
        
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
        aes.Key = pbkdf2.GetBytes(32);
        
        using var decryptor = aes.CreateDecryptor();
        using var msDecrypt = new MemoryStream(encryptedBytes);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt, Encoding.UTF8);
        
        return srDecrypt.ReadToEnd();
    }

    private string EncryptDataLegacy(string data, byte[] salt)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
        
        var saltedData = new byte[salt.Length + dataBytes.Length];
        Array.Copy(salt, 0, saltedData, 0, salt.Length);
        Array.Copy(dataBytes, 0, saltedData, salt.Length, dataBytes.Length);
        
        var encryptedBytes = ProtectedData.Protect(saltedData, optionalEntropy: salt, scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    private string DecryptDataLegacy(string encryptedData, byte[] salt)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedData);
        var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: salt, scope: DataProtectionScope.CurrentUser);
        
        var dataBytes = new byte[decryptedBytes.Length - salt.Length];
        Array.Copy(decryptedBytes, salt.Length, dataBytes, 0, dataBytes.Length);
        
        return Encoding.UTF8.GetString(dataBytes);
    }

    public void ImportAccounts(string filePath)
    {
        ImportAccounts(filePath, null);
    }

    public void ImportAccounts(string filePath, string? password)
    {        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Файл импорта не найден: {filePath}");
        }
        
        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Файл импорта пуст");
            }
            
            var importAccounts = ParseImportFile(json, password);
            
            if (importAccounts == null || !importAccounts.Any())
            {
                throw new InvalidOperationException("Файл импорта не содержит аккаунтов или имеет неверный формат");
            }
            
            var existingAccounts = LoadAll().ToList();
            int updatedCount = 0;
            int addedCount = 0;
            
            foreach (var importAcc in importAccounts)
            {
                var account = new AccountRecord
                {
                    Username = importAcc.Username,
                    EncryptedPassword = Protect(importAcc.Password),
                    Note = importAcc.Note ?? string.Empty,
                    CreatedAt = importAcc.CreatedAt
                };
                
                var existingIdx = existingAccounts.FindIndex(a => 
                    string.Equals(a.Username, account.Username, StringComparison.OrdinalIgnoreCase));
                
                if (existingIdx >= 0)
                {
                    existingAccounts[existingIdx] = account;
                    updatedCount++;
                }
                else
                {
                    existingAccounts.Add(account);
                    addedCount++;
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
            
            _logger?.Info($"Импорт: добавлено {addedCount}, обновлено {updatedCount} аккаунтов");
        }
        catch (Exception ex)
        {
            _logger?.Error($"Ошибка импорта: {ex.Message}");
            throw;
        }
    }

    private List<ExportAccountRecord>? ParseImportFile(string json, string? password)
    {
        try
        {
            JToken jsonToken = JToken.Parse(json);
            
            if (jsonToken is JObject jsonObj)
            {
                if (jsonObj.ContainsKey("Version") && jsonObj.ContainsKey("EncryptedAccounts"))
                {
                    return ParseEncryptedFormat(jsonObj, password);
                }
                else
                {
                    throw new InvalidOperationException("Неподдерживаемый формат файла");
                }
            }
            else if (jsonToken is JArray jsonArray)
            {
                if (jsonArray.Count > 0)
                {
                    var firstItem = jsonArray[0];
                    
                    if (firstItem["Note"] != null)
                    {
                        return JsonConvert.DeserializeObject<List<ExportAccountRecord>>(json);
                    }
                    else
                    {
                        var legacyAccounts = JsonConvert.DeserializeObject<List<LegacyExportAccountRecord>>(json);
                        return legacyAccounts?.Select(legacy => new ExportAccountRecord
                        {
                            Username = legacy.Username,
                            Password = legacy.Password,
                            Note = string.Empty,
                            CreatedAt = legacy.CreatedAt
                        }).ToList();
                    }
                }
                else
                {
                    return new List<ExportAccountRecord>();
                }
            }
            else
            {
                throw new InvalidOperationException($"Неподдерживаемый формат JSON");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Неверный формат файла: {ex.Message}");
        }
    }

    private List<ExportAccountRecord>? ParseEncryptedFormat(JObject jsonObj, string? password)
    {
        try
        {
            var exportData = jsonObj.ToObject<EncryptedExportData>();
            if (exportData == null) 
            {
                return null;
            }
            
            if (string.IsNullOrEmpty(exportData.EncryptedAccounts) || string.IsNullOrEmpty(exportData.Salt))
            {
                throw new InvalidOperationException("Поврежденный зашифрованный файл");
            }
            
            var salt = Convert.FromBase64String(exportData.Salt);
            string decryptedJson;
            
            if (exportData.Version == 3)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("Для расшифровки файла версии 3 требуется пароль");
                }
                
                if (string.IsNullOrEmpty(exportData.IV))
                {
                    throw new InvalidOperationException("Поврежденный зашифрованный файл: отсутствует IV");
                }
                
                var iv = Convert.FromBase64String(exportData.IV);
                decryptedJson = DecryptDataAes(exportData.EncryptedAccounts, password, salt, iv);
            }
            else if (exportData.Version == 2)
            {
                decryptedJson = DecryptDataLegacy(exportData.EncryptedAccounts, salt);
            }
            else
            {
                throw new InvalidOperationException($"Неподдерживаемая версия зашифрованного файла: {exportData.Version}");
            }
            
            return JsonConvert.DeserializeObject<List<ExportAccountRecord>>(decryptedJson);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Неверный пароль или поврежденный файл");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Не удалось расшифровать файл: {ex.Message}");
        }
    }
}


