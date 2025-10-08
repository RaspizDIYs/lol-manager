using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace LolManager.Views.Pages;

public partial class SpyPage : UserControl
{
    public SpyPage()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Игнорируем ошибки открытия ссылки
        }
    }
}
