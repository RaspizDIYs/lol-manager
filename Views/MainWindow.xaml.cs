using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;

namespace LolManager.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && sender is PasswordBox pb)
        {
            vm.NewPassword = pb.Password;
        }
    }

    
}


