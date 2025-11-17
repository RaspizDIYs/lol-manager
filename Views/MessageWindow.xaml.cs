using System;
using System.Diagnostics;
using System.Text;
using System.Web;
using System.Windows;
using Wpf.Ui.Controls;

namespace LolManager.Views;

public partial class MessageWindow : FluentWindow
{
    public enum MessageType
    {
        Information,
        Warning,
        Error,
        Success,
        Question
    }
    
    public enum MessageButtons
    {
        Ok,
        OkCancel,
        YesNo
    }
    
    private string _fullErrorMessage = "";
    
    public MessageWindow()
    {
        InitializeComponent();
    }
    
    public static bool? Show(string message, string title = "Сообщение", MessageType messageType = MessageType.Information, MessageButtons buttons = MessageButtons.Ok, Window? owner = null)
    {
        var window = new MessageWindow();
        
        // Настройка владельца - только если окно в нормальном состоянии
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
        catch (Exception)
        {
            // Если не удалось установить Owner - игнорируем, окно откроется без родителя
        }
        
        // Настройка содержимого
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.Title = title;
        window._fullErrorMessage = message;
        
        // Настройка иконки и цвета в зависимости от типа сообщения
        switch (messageType)
        {
            case MessageType.Information:
                window.MessageIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Info24;
                break;
            case MessageType.Warning:
                window.MessageIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning24;
                break;
            case MessageType.Error:
                window.MessageIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle24;
                window.CopyButton.Visibility = Visibility.Visible;
                window.ReportButton.Visibility = Visibility.Visible;
                break;
            case MessageType.Success:
                window.MessageIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                break;
            case MessageType.Question:
                window.MessageIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.QuestionCircle24;
                break;
        }
        
        // Настройка кнопок
        switch (buttons)
        {
            case MessageButtons.Ok:
                window.CancelButton.Visibility = Visibility.Collapsed;
                window.OkButton.Content = "OK";
                break;
            case MessageButtons.OkCancel:
                window.CancelButton.Visibility = Visibility.Visible;
                window.OkButton.Content = "OK";
                window.CancelButton.Content = "Отмена";
                break;
            case MessageButtons.YesNo:
                window.CancelButton.Visibility = Visibility.Visible;
                window.OkButton.Content = "Да";
                window.CancelButton.Content = "Нет";
                break;
        }
        
        // Показ окна
        return window.ShowDialog();
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = true;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        Close();
    }
    
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_fullErrorMessage);
            
            // Временно меняем текст кнопки для подтверждения
            var originalContent = CopyButton.Content;
            CopyButton.Content = "Скопировано!";
            CopyButton.IsEnabled = false;
            
            // Возвращаем исходный текст через 1.5 секунды
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            timer.Tick += (s, args) =>
            {
                CopyButton.Content = originalContent;
                CopyButton.IsEnabled = true;
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            // В случае ошибки копирования просто показываем сообщение
            System.Diagnostics.Debug.WriteLine($"Ошибка копирования в буфер обмена: {ex.Message}");
        }
    }
    
    private void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var title = "Ошибка в приложении";
            var body = CreateIssueBody();
            
            // Используем реальный репозиторий GitHub 
            var url = $"https://github.com/RaspizDIYs/LolManager/issues/new?title={Uri.EscapeDataString(title)}&body={body}";
            
            // Альтернативный подход для открытия URL
            try
            {
                System.Diagnostics.Process.Start("cmd", $"/c start \"\" \"{url}\"");
            }
            catch
            {
                // Если cmd не работает, пробуем explorer
                System.Diagnostics.Process.Start("explorer", url);
            }
            
            // Временно меняем текст кнопки для подтверждения
            var originalContent = ReportButton.Content;
            ReportButton.Content = "Открыто в браузере";
            ReportButton.IsEnabled = false;
            
            // Возвращаем исходный текст через 2 секунды
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, args) =>
            {
                ReportButton.Content = originalContent;
                ReportButton.IsEnabled = true;
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при открытии браузера: {ex.Message}");
            // Fallback - копируем URL в буфер обмена
            try
            {
                var title = "Ошибка в приложении";
                var body = CreateIssueBodyPlain(); // Используем версию без URL encoding для копирования
                var url = $"https://github.com/RaspizDIYs/LolManager/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
                Clipboard.SetText(url);
                
                ReportButton.Content = "URL скопирован";
                ReportButton.IsEnabled = false;
                
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, args) =>
                {
                    ReportButton.Content = "Сообщить разработчику";
                    ReportButton.IsEnabled = true;
                    timer.Stop();
                };
                timer.Start();
            }
            catch
            {
                // Если и это не сработало - ничего не делаем
            }
        }
    }
    
    private string CreateIssueBody()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Описание ошибки");
        sb.AppendLine(_fullErrorMessage);
        sb.AppendLine();
        sb.AppendLine("## Системная информация");
        sb.AppendLine($"- ОС: {Environment.OSVersion}");
        sb.AppendLine($"- .NET: {Environment.Version}");
        sb.AppendLine($"- Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("## Шаги для воспроизведения");
        sb.AppendLine("1. ");
        sb.AppendLine("2. ");
        sb.AppendLine("3. ");
        sb.AppendLine();
        sb.AppendLine("## Ожидаемое поведение");
        sb.AppendLine("");
        sb.AppendLine();
        sb.AppendLine("## Фактическое поведение");
        sb.AppendLine("");
        
        return Uri.EscapeDataString(sb.ToString());
    }
    
    private string CreateIssueBodyPlain()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Описание ошибки");
        sb.AppendLine(_fullErrorMessage);
        sb.AppendLine();
        sb.AppendLine("## Системная информация");
        sb.AppendLine($"- ОС: {Environment.OSVersion}");
        sb.AppendLine($"- .NET: {Environment.Version}");
        sb.AppendLine($"- Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("## Шаги для воспроизведения");
        sb.AppendLine("1. ");
        sb.AppendLine("2. ");
        sb.AppendLine("3. ");
        sb.AppendLine();
        sb.AppendLine("## Ожидаемое поведение");
        sb.AppendLine("");
        sb.AppendLine();
        sb.AppendLine("## Фактическое поведение");
        sb.AppendLine("");
        
        return sb.ToString();
    }
}
