using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Documents;
using LolManager.ViewModels;

namespace LolManager.Views.Pages;

public partial class InformationPage : UserControl
{
    public InformationPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }
    
    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        
        if (e.NewValue is MainViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }
    
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ChangelogText))
        {
            UpdateChangelogRichTextBox();
        }
    }
    
    private void UpdateChangelogRichTextBox()
    {
        if (DataContext is MainViewModel viewModel && !string.IsNullOrEmpty(viewModel.ChangelogText))
        {
            var converter = new Converters.MarkdownToFlowDocumentConverter();
            ChangelogRichTextBox.Document = (FlowDocument)converter.Convert(viewModel.ChangelogText, typeof(FlowDocument), null!, System.Globalization.CultureInfo.CurrentCulture);
        }
    }
}

