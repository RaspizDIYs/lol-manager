using System;
using System.Runtime.InteropServices;
using System.Windows;
using LolManager.Services;
using Wpf.Ui.Controls;

namespace LolManager.Views;

public partial class InfoWindow : FluentWindow
{
    private readonly IUpdateService _updateService;

    public InfoWindow(IUpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;
        
        LoadSystemInfo();
        SetupEventHandlers();
    }

    private void LoadSystemInfo()
    {
        try
        {
            // Версия приложения
            VersionTextBlock.Text = _updateService.CurrentVersion;
            
            // Информация о системе
            OSTextBlock.Text = $"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}";
            RuntimeTextBlock.Text = RuntimeInformation.FrameworkDescription;
            ArchTextBlock.Text = RuntimeInformation.OSArchitecture.ToString();
        }
        catch
        {
            OSTextBlock.Text = "Не удалось получить информацию";
            RuntimeTextBlock.Text = "Не удалось получить информацию";
            ArchTextBlock.Text = "Не удалось получить информацию";
        }
    }

    private void SetupEventHandlers()
    {
        ViewChangelogButton.Click += async (s, e) => await ShowChangelog();
        CloseButton.Click += (s, e) => Close();
    }

    private async System.Threading.Tasks.Task ShowChangelog()
    {
        try
        {
            var changelogWindow = new ChangelogWindow(_updateService);
            changelogWindow.Owner = this;
            changelogWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Не удалось открыть changelog: {ex.Message}", 
                                         "Ошибка", 
                                         System.Windows.MessageBoxButton.OK, 
                                         System.Windows.MessageBoxImage.Error);
        }
    }
}
