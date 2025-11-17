using System.Windows;
using Wpf.Ui.Controls;

namespace LolManager.Views;

public partial class PasswordInputWindow : FluentWindow
{
    public string? Password { get; private set; }
    
    public PasswordInputWindow()
    {
        InitializeComponent();
        Loaded += (s, e) => PasswordBox.Focus();
    }
    
    public static string? ShowDialog(string title, string message, Window? owner = null)
    {
        var window = new PasswordInputWindow();
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.Title = title;
        
        try
        {
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                window.Owner = owner;
            }
            else if (Application.Current?.MainWindow != null && 
                     Application.Current.MainWindow.IsLoaded && 
                     Application.Current.MainWindow.IsVisible &&
                     Application.Current.MainWindow != window)
            {
                window.Owner = Application.Current.MainWindow;
            }
        }
        catch { }
        
        if (window.ShowDialog() == true)
        {
            return window.Password;
        }
        
        return null;
    }
    
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(passwordBox.Password);
        }
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

