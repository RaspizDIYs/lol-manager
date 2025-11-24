using System.Windows.Controls;
using System.ComponentModel;
using Wpf.Ui.Controls;

namespace LolManager.Views.Pages;

public partial class AddAccountPage : UserControl
{
    private bool _isUpdatingPassword = false;
    
    public AddAccountPage()
    {
        InitializeComponent();
        Loaded += AddAccountPage_Loaded;
        DataContextChanged += AddAccountPage_DataContextChanged;
    }
    
    private void AddAccountPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        UpdatePasswordBox();
    }
    
    private void AddAccountPage_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.MainViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        
        if (e.NewValue is ViewModels.MainViewModel newViewModel)
        {
            newViewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdatePasswordBox();
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.NewPassword) || 
            e.PropertyName == nameof(ViewModels.MainViewModel.IsEditMode))
        {
            UpdatePasswordBox();
        }
    }
    
    private void UpdatePasswordBox()
    {
        if (_isUpdatingPassword) return;
        
        if (DataContext is ViewModels.MainViewModel viewModel && PasswordBox != null)
        {
            _isUpdatingPassword = true;
            try
            {
                PasswordBox.Password = viewModel.NewPassword ?? string.Empty;
            }
            finally
            {
                _isUpdatingPassword = false;
            }
        }
    }
    
    private void PasswordBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isUpdatingPassword) return;
        
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox && DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.NewPassword = passwordBox.Password;
        }
    }
}
