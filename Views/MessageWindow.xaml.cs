using System;
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
    
    // Удаляем кастомный DialogResult - используем встроенный WPF
    
    public MessageWindow()
    {
        InitializeComponent();
    }
    
    public static bool? Show(string message, string title = "Сообщение", MessageType messageType = MessageType.Information, MessageButtons buttons = MessageButtons.Ok, Window? owner = null)
    {
        var window = new MessageWindow();
        
        // Настройка владельца
        if (owner != null)
        {
            window.Owner = owner;
        }
        else
        {
            // Попытка найти главное окно приложения
            foreach (Window appWindow in Application.Current.Windows)
            {
                if (appWindow.GetType().Name == "MainWindow")
                {
                    window.Owner = appWindow;
                    break;
                }
            }
        }
        
        // Настройка содержимого
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.Title = title;
        
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
}
