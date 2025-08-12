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
}


