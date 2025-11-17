using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
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
        CloseButton.Click += (s, e) => Close();
    }
}
