using System.Windows;
using System.Windows.Documents;
using LolManager.Services;
using System;
using System.Threading.Tasks;

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
            ParseAndDisplayMarkdown(changelog);
        }
        catch
        {
            var document = new FlowDocument();
            document.Blocks.Add(new Paragraph(new Run("Не удалось загрузить changelog")));
            ChangelogRichTextBox.Document = document;
        }
    }

    private void ParseAndDisplayMarkdown(string markdown)
    {
        var converter = new Converters.MarkdownToFlowDocumentConverter();
        ChangelogRichTextBox.Document = (FlowDocument)converter.Convert(markdown, typeof(FlowDocument), null!, System.Globalization.CultureInfo.CurrentCulture);
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
                System.Windows.MessageBox.Show("Обновление завершено. Приложение будет перезапущено.", 
                              "Обновление", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                System.Windows.MessageBox.Show("Не удалось выполнить обновление.", 
                              "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "Обновить сейчас";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка при обновлении: {ex.Message}", 
                          "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "Обновить сейчас";
        }
    }
}
