using System.Windows;
using LolManager.Services;

namespace LolManager.Views;

public partial class UpdateWindow : Window
{
    private readonly IUpdateService _updateService;

    public UpdateWindow(IUpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;
        
        LoadChangelog();
        SetupEventHandlers();
    }

    private async void LoadChangelog()
    {
        try
        {
            var changelog = await _updateService.GetChangelogAsync();
            ChangelogTextBox.Text = changelog;
        }
        catch
        {
            ChangelogTextBox.Text = "Не удалось загрузить changelog";
        }
    }

    private void SetupEventHandlers()
    {
        UpdateButton.Click += async (s, e) => await UpdateNow();
        SkipButton.Click += (s, e) => Close();
    }

    private async Task UpdateNow()
    {
        try
        {
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Обновление...";
            
            var success = await _updateService.UpdateAsync();
            if (success)
            {
                MessageBox.Show("Обновление завершено. Приложение будет перезапущено.", 
                              "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show("Не удалось выполнить обновление.", 
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "Обновить сейчас";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при обновлении: {ex.Message}", 
                          "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "Обновить сейчас";
        }
    }
}
