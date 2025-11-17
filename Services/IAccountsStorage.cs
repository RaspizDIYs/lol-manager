using System.Collections.Generic;
using LolManager.Models;

namespace LolManager.Services;

public interface IAccountsStorage
{
    IEnumerable<AccountRecord> LoadAll();
    void Save(AccountRecord account);
    void Delete(string username);

    string Protect(string plain);
    string Unprotect(string encrypted);
    
    void ExportAccounts(string filePath, IEnumerable<AccountRecord>? selectedAccounts = null);
    void ExportAccounts(string filePath, string password, IEnumerable<AccountRecord>? selectedAccounts = null);
    void ImportAccounts(string filePath);
    void ImportAccounts(string filePath, string? password);
}


