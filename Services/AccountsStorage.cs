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

    public AccountsStorage()
    {
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LolManager");
        Directory.CreateDirectory(appDir);
        _dataFilePath = Path.Combine(appDir, "accounts.json");
    }

    public IEnumerable<AccountRecord> LoadAll()
    {
        if (!File.Exists(_dataFilePath)) return Enumerable.Empty<AccountRecord>();
        var json = File.ReadAllText(_dataFilePath);
        var list = JsonConvert.DeserializeObject<List<AccountRecord>>(json) ?? new List<AccountRecord>();
        return list.OrderBy(a => a.Username);
    }

    public void Save(AccountRecord account)
    {
        var list = LoadAll().ToList();
        var existingIdx = list.FindIndex(a => string.Equals(a.Username, account.Username, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0) list[existingIdx] = account; else list.Add(account);
        File.WriteAllText(_dataFilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
    }

    public void Delete(string username)
    {
        var list = LoadAll().Where(a => !string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)).ToList();
        File.WriteAllText(_dataFilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
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
}


