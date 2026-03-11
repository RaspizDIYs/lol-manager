using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace LolManager.Services;

/// <summary>
/// Сервис миграции с WPF LolManager на Tauri RustLM.
/// При запуске проверяет наличие RustLM и при необходимости скачивает и устанавливает его.
/// </summary>
public static class MigrationService
{
    private const string RustLmExeName = "RustLM.exe";
    private const string InstallerFileName = "RustLM_0.1.0_x64-setup.exe";
    private const string DownloadUrl = "https://github.com/RaspizDIYs/rustlm/releases/latest/download/RustLM_0.1.0_x64-setup.exe";
    private const string MigrationDeclinedFlag = "migration_declined";

    /// <summary>
    /// Tauri NSIS installer использует identifier (com.rustlm.app) для пути установки,
    /// а НЕ productName.
    /// </summary>
    private static readonly string RustLmInstallPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "com.rustlm.app",
        RustLmExeName);

    /// <summary>
    /// Проверяет наличие RustLM. Если установлен — запускает и возвращает true (сигнал на выход).
    /// Если нет — предлагает установить.
    /// Возвращает true если нужно закрыть LolManager.
    /// </summary>
    public static async Task<bool> TryMigrateAsync(ILogger? logger)
    {
        try
        {
            // 1. Если RustLM уже установлен — просто запускаем
            if (File.Exists(RustLmInstallPath))
            {
                logger?.Info("[MIGRATION] RustLM найден, запускаем и выходим");
                LaunchRustLm(logger);
                return true;
            }

            // 2. Если пользователь ранее отказался — не спрашиваем снова
            var flagPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LolManager", MigrationDeclinedFlag);
            if (File.Exists(flagPath))
            {
                logger?.Info("[MIGRATION] Пользователь ранее отказался от миграции");
                return false;
            }

            // 3. Спрашиваем пользователя
            var result = MessageBox.Show(
                "Доступна новая версия менеджера — RustLM!\n\n" +
                "Она быстрее, современнее и полностью совместима с вашими данными.\n\n" +
                "Установить RustLM сейчас?\n\n" +
                "(Если нет — вы продолжите пользоваться текущей версией)",
                "Обновление до RustLM",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                logger?.Info("[MIGRATION] Пользователь отказался от миграции");
                try { File.WriteAllText(flagPath, DateTime.UtcNow.ToString("o")); } catch { }
                return false;
            }

            // 3. Скачиваем инсталлятор
            logger?.Info("[MIGRATION] Начинаем скачивание RustLM...");
            var installerPath = await DownloadInstallerAsync(logger);
            if (installerPath == null)
            {
                MessageBox.Show(
                    "Не удалось скачать установщик RustLM.\nПопробуйте позже или скачайте вручную с GitHub.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            // 4. Запускаем тихую установку
            logger?.Info("[MIGRATION] Запускаем установку...");
            var installed = await RunInstallerAsync(installerPath, logger);

            // Удаляем скачанный инсталлятор
            try { File.Delete(installerPath); } catch { }

            if (!installed || !File.Exists(RustLmInstallPath))
            {
                logger?.Error("[MIGRATION] Установка не удалась");
                MessageBox.Show(
                    "Установка RustLM не завершилась успешно.\nВы продолжите работу в текущей версии.",
                    "Ошибка установки",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            // 5. Запускаем RustLM
            logger?.Info("[MIGRATION] Установка завершена, запускаем RustLM");
            LaunchRustLm(logger);
            return true;
        }
        catch (Exception ex)
        {
            logger?.Error($"[MIGRATION] Ошибка миграции: {ex.Message}");
            return false;
        }
    }

    private static async Task<string?> DownloadInstallerAsync(ILogger? logger)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), InstallerFileName);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);

            logger?.Info($"[MIGRATION] Инсталлятор скачан: {tempPath}");
            return tempPath;
        }
        catch (Exception ex)
        {
            logger?.Error($"[MIGRATION] Ошибка скачивания: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> RunInstallerAsync(string installerPath, ILogger? logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/S", // NSIS тихая установка
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            if (process == null) return false;

            // Ждём завершения до 3 минут
            await Task.Run(() => process.WaitForExit(180_000));

            var exitCode = process.HasExited ? process.ExitCode : -1;
            logger?.Info($"[MIGRATION] Инсталлятор завершён с кодом: {exitCode}");

            return exitCode == 0;
        }
        catch (Exception ex)
        {
            logger?.Error($"[MIGRATION] Ошибка запуска инсталлятора: {ex.Message}");
            return false;
        }
    }

    private static void LaunchRustLm(ILogger? logger)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RustLmInstallPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger?.Error($"[MIGRATION] Ошибка запуска RustLM: {ex.Message}");
        }
    }
}
