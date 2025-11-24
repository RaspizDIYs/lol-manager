using System.Collections.Generic;
using Wpf.Ui.Controls;
using LolManager.ViewModels;

namespace LolManager.Views;

public partial class BindingEditorWindow : FluentWindow
{
    private readonly BindingEditorViewModel _viewModel;

    public BindingEditorWindow(BindingEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BindingEditorViewModel.DialogResult))
            {
                DialogResult = _viewModel.DialogResult;
                Close();
            }
        };
    }

    public Dictionary<string, string> GetBindings()
    {
        return _viewModel.GetBindings();
    }
}

