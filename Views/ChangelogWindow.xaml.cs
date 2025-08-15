using System.Windows;
using LolManager.Services;

namespace LolManager.Views;

public partial class ChangelogWindow : Window
{
    private readonly IUpdateService _updateService;

    public ChangelogWindow(IUpdateService updateService)
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
        CloseButton.Click += (s, e) => Close();
    }
}
