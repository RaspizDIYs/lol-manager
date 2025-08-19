using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace LolManager.Views.Pages;

public partial class AddAccountPage : UserControl
{
    public AddAccountPage()
    {
        InitializeComponent();
    }
    
    private void PasswordBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox && DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.NewPassword = passwordBox.Password;
        }
    }
}
