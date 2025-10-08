# LolManager

![Downloads](https://img.shields.io/github/downloads/RaspizDIYs/lol-manager/total?style=flat-square&logo=github)
![Latest Release](https://img.shields.io/github/v/release/RaspizDIYs/lol-manager?style=flat-square&logo=github)
![License](https://img.shields.io/github/license/RaspizDIYs/lol-manager?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet)

**League of Legends Account Manager** - автоматический вход в аккаунты LoL одним кликом.

## ⚡ Возможности

- **Автоматический вход** - UI Automation для заполнения формы входа и автоматический запуск Лиги
- **Автопринятие матчей** - автоматическое принятие готовности к игре через LCU API
- **Автоматизация выбора** - автопик/бан чемпионов, выбор заклинаний призывателя
- **Безопасное хранение** - шифрование паролей Windows ProtectedData API
- **Современный интерфейс** - WPF-UI с темной темой
- **Умный поиск чемпионов** - поиск по русским и английским названиям + популярные сокращения
- **Экспорт/Импорт** - резервное копирование аккаунтов
- **Автообновления** - встроенная система обновлений
- **Единый экземпляр** - предотвращение запуска нескольких копий приложения

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

### Аккаунты:
1. **Добавить аккаунт**: кнопка "Добавить аккаунт" → ввести логин/пароль
2. **Автовход**: выбрать аккаунт → кнопка "Войти" (автоматически запустит Лигу)
3. **Однопотендный выбор**: можно выбрать только один аккаунт для входа
4. **Управление**: редактирование, удаление, экспорт аккаунтов

### Автоматизация:
1. **Автопринятие**: глобальный переключатель на странице аккаунтов
2. **Настройка матчей**: страница "Автоматизация" → выбор чемпионов для пика/бана
3. **Выбор заклинаний**: автовыбор заклинаний призывателя по популярности
4. **Поиск чемпионов**: умный поиск по названиям и сокращениям (мф, вв, etc)
5. **Автосохранение**: все настройки сохраняются автоматически при изменении

## 🛠️ Технологии

- **.NET 8.0 WPF** + **WPF-UI** + **MVVM**
- **LCU API** для автоматизации в League of Legends
- **Data Dragon API** для актуальных данных чемпионов и заклинаний
- **FlaUI** для UI Automation входа в Riot Client
- **WebSocket** для реального времени событий лиги
- **Velopack** для автообновлений с delta пакетами
- **GitHub Actions** для автоматической сборки и релизов
- **Multithreading** с Interlocked операциями для предотвращения race conditions

## 🔧 Сборка

```bash
git clone https://github.com/RaspizDIYs/lol-manager.git
dotnet build --configuration Release
```

## 📄 Лицензия

Проект распространяется под [MIT License](LICENSE).

## ⚠️ Disclaimer

Не связано с Riot Games. Используйте на свой риск. Все данные храняться локально и никуда не передаются
