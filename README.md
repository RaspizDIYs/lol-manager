# LolManager

![Downloads](https://img.shields.io/github/downloads/RaspizDIYs/lol-manager/total?style=flat-square&logo=github)
![Latest Release](https://img.shields.io/github/v/release/RaspizDIYs/lol-manager?style=flat-square&logo=github)
![License](https://img.shields.io/github/license/RaspizDIYs/lol-manager?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet)

**League of Legends Account Manager** - автоматический вход в аккаунты LoL одним кликом.

## ⚡ Возможности

- **Автоматический вход** - UI Automation для заполнения формы входа
- **Безопасное хранение** - шифрование паролей Windows ProtectedData API
- **Современный интерфейс** - WPF-UI с темной темой
- **Экспорт/Импорт** - резервное копирование аккаунтов
- **Автообновления** - встроенная система обновлений

## 🚀 Установка

**Требования:** Windows 10/11, .NET 8.0 Runtime

Скачать: [**Releases**](../../releases) → `LolManager-Setup.exe`

## 💻 Использование

1. **Добавить аккаунт**: кнопка "Добавить аккаунт" → ввести логин/пароль
2. **Автовход**: выбрать аккаунт → кнопка "Войти" 
3. **Управление**: редактирование, удаление, экспорт аккаунтов

## 🛠️ Технологии

- **.NET 8.0 WPF** + **WPF-UI** + **MVVM**
- **FlaUI** для UI Automation
- **Velopack** для автообновлений

## 🔧 Сборка

```bash
git clone https://github.com/yourusername/LolManager.git
dotnet build --configuration Release
```

## 📄 Лицензия

Проект распространяется под [MIT License](LICENSE).

## ⚠️ Disclaimer

Не связано с Riot Games. Используйте на свой риск. Все данные храняться локально и никуда не передаются
