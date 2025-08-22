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

### 📥 Скачать последнюю версию:

[![Скачать Setup](https://img.shields.io/badge/📦_Скачать_Setup-Latest-blue?style=for-the-badge&logo=windows)](https://github.com/RaspizDIYs/lol-manager/releases/latest/download/LolManager-stable-Setup.exe)

[![Все релизы](https://img.shields.io/badge/📋_Все_релизы-GitHub-lightgrey?style=for-the-badge&logo=github)](https://github.com/RaspizDIYs/lol-manager/releases)

> ℹ️ **Если ссылка не работает**: перейдите на страницу [Все релизы](https://github.com/RaspizDIYs/lol-manager/releases) и скачайте файл `LolManager-stable-Setup.exe` из последнего стабильного релиза.

### 🔧 Установка:

- **LolManager-stable-Setup.exe** - автоматическая установка с автообновлениями

### 📦 Дополнительные файлы в релизах:

- `.nupkg` файлы - пакеты Velopack (для обновлений)
- `RELEASES` - индекс для системы обновлений  
- `releases.stable.json` - метаданные релизов

### 🔄 Каналы обновлений:

- **Stable** ⭐ - стабильные релизы, тестированные версии (по умолчанию)
- **Beta** 🧪 - ранний доступ к новым функциям *(переключите в настройках приложения)*

> 📌 **Важно**: Ссылка `/latest` всегда ведет на **последний стабильный релиз**. Beta версии помечены как `pre-release` и доступны в разделе [Все релизы](https://github.com/RaspizDIYs/lol-manager/releases).

*После установки приложение будет автоматически проверять и скачивать обновления согласно выбранному каналу*

## 💻 Использование

1. **Добавить аккаунт**: кнопка "Добавить аккаунт" → ввести логин/пароль
2. **Автовход**: выбрать аккаунт → кнопка "Войти" 
3. **Управление**: редактирование, удаление, экспорт аккаунтов

## 🛠️ Технологии

- **.NET 8.0 WPF** + **WPF-UI** + **MVVM**
- **FlaUI** для UI Automation 
- **Velopack** для автообновлений с delta пакетами
- **GitHub Actions** для автоматической сборки и релизов

## 🔧 Сборка

```bash
git clone https://github.com/RaspizDIYs/lol-manager.git
dotnet build --configuration Release
```

## 📄 Лицензия

Проект распространяется под [MIT License](LICENSE).

## ⚠️ Disclaimer

Не связано с Riot Games. Используйте на свой риск. Все данные храняться локально и никуда не передаются
