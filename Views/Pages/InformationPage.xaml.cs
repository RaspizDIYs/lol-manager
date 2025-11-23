using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows.Documents;
using System.Windows.Media;
using LolManager.ViewModels;

namespace LolManager.Views.Pages;

public partial class InformationPage : UserControl
{
    public InformationPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }
    
    private void DiscordNickMejaikin_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText("mejaikin");
            
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowToast("Ник скопирован в буфер обмена");
        }
        catch
        {
        }
    }
    
    private void DiscordNickSpellov_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText("spellq");
            
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowToast("Ник скопирован в буфер обмена");
        }
        catch
        {
        }
    }
    
    private void DiscordNickShpinat_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText("shp1n4t");
            
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowToast("Ник скопирован в буфер обмена");
        }
        catch
        {
        }
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

