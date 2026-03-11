# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**LolManager** — десктопный менеджер аккаунтов League of Legends на .NET 8.0 WPF. Автоматизирует вход в Riot Client, принятие матчей, выбор чемпионов/рун/скинов через LCU API.

**Версия:** 0.2.17 | **Ветки:** `master` (stable), `develop` (beta)

## Build Commands

```bash
dotnet restore LolManager.csproj
dotnet build LolManager.csproj -c Release
dotnet run --project LolManager.csproj

# Публикация self-contained (как в CI)
dotnet publish LolManager.csproj -c Release -o publish -r win-x64 --self-contained true
```

Тестов в проекте нет. Проверка — только `dotnet build -c Release`.

## Release Process

- **master**: при пуше CI проверяет что `<Version>` в `LolManager.csproj` увеличена относительно последнего stable релиза, затем публикует через Velopack (`vpk pack/upload --channel stable`).
- **develop**: CI автоматически инкрементирует beta-версию (формат `X.Y.Z-beta.N`), коммитит с `[skip ci]` и публикует через Velopack (`--channel beta`).
- Перед пушем в master нужно вручную обновить `<Version>` в `.csproj`.

## Architecture

### Паттерн: MVVM + ручной DI

```
Views (XAML) → ViewModels (CommunityToolkit.Mvvm) → Services → Models
```

DI-контейнер — `Dictionary<Type, object>` в `App.xaml.cs`. Сервисы создаются вручную в `RegisterServices()` и получаются через `((App)Application.Current).GetService<T>()`. Это не стандартный `IServiceProvider` — регистрация только по интерфейсу или конкретному типу.

### Ключевые сервисы

| Сервис | Интерфейс | Назначение |
|--------|-----------|-----------|
| `RiotClientService` | `IRiotClientService` | LCU API, WebSocket, авто-логин через FlaUI |
| `AutoAcceptService` | — | Авто-принятие матча (WebSocket/Polling/UIA) |
| `AccountsStorage` | `IAccountsStorage` | Хранение аккаунтов, шифрование паролей (DPAPI) |
| `SettingsService` | `ISettingsService` | JSON-настройки в `%LOCALAPPDATA%/LolManager/` |
| `DataDragonService` | — | Данные чемпионов/рун/заклинаний из Data Dragon API |
| `CustomizationService` | — | Кастомизация скинов через LCU |
| `RevealService` | — | Мониторинг состояния игры в реальном времени |
| `UpdateService` | `IUpdateService` | Авто-обновления через Velopack |
| `RunePagesStorage` | `IRunePagesStorage` | Хранение пользовательских страниц рун |
| `FileLogger` | `ILogger` | Логирование в `%LOCALAPPDATA%/LolManager/debug.log` |

### ViewModels

- `MainViewModel` — центральный хаб: управление аккаунтами, навигация, координация сервисов
- `AutomationViewModel` — настройки авто-выбора чемпиона/бана/заклинаний
- `CustomizationViewModel` — выбор скинов и хром
- `RunePageEditorViewModel` — редактор страниц рун
- `SettingsPageViewModel` — настройки приложения

### Pages (табированный интерфейс в MainWindow)

`AccountsPage` → `AddAccountPage` → `AutomationPage` → `CustomizationPage` → `InformationPage` → `SpyPage` → `SettingsPage` → `LogsPage`

## Key Technical Details

### LCU API (League Client Update)
`RiotClientService` обнаруживает lockfile Riot Client, извлекает порт/токен и делает HTTP-запросы к `https://127.0.0.1:{port}/lol/...` с Basic Auth. WebSocket (`wss://`) используется для событий в реальном времени.

### Авто-логин
Использует `FlaUI` (UIA3) для автоматического заполнения формы логина Riot Client. Это хрупкий механизм — изменения в UI Riot Client могут его сломать.

### Хранение данных
- Пароли: `System.Security.Cryptography.ProtectedData` (Windows DPAPI), `%APPDATA%/Roaming/LolManager/`
- Настройки/логи: `%LOCALAPPDATA%/LolManager/`

### Авто-принятие матча
`AutoAcceptService` поддерживает три метода: `WebSocket` (предпочтительный), `Polling` (резервный), `UIA` (через UI Automation). Метод настраивается в `AutomationSettings.AutoAcceptMethod`.

### Одиночный экземпляр
Приложение использует `Mutex` + `EventWaitHandle` для ограничения до одного экземпляра. Второй запуск отправляет IPC-сигнал для показа уже запущенного окна.

### Конвертеры
24 WPF-конвертера в `/Converters/`. Наиболее сложные: `MarkdownToFlowDocumentConverter` (changelog), `ImageUrlConverter` (загрузка изображений).

## NuGet Dependencies

- `CommunityToolkit.Mvvm` 8.2.2 — ObservableObject, RelayCommand
- `WPF-UI` 4.0.3 — современная тёмная тема (Wpf.Ui namespace)
- `FlaUI.Core` / `FlaUI.UIA3` 4.0.0 — UI Automation
- `Velopack` 0.0.1298 — авто-обновления
- `H.NotifyIcon.Wpf` 2.1.4 — системный трей
- `Newtonsoft.Json` 13.0.3 — сериализация
- `System.Security.Cryptography.ProtectedData` 8.0.0 — шифрование DPAPI
