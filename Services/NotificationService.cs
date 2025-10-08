using System;
using System.Threading.Tasks;
using System.Windows;
using LolManager.Views;

namespace LolManager.Services
{
    public class NotificationService
    {

        public async Task ShowUpdateNotificationAsync(string version, Action? downloadAction = null, Action? dismissAction = null)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var notification = new UpdateNotificationWindow(version, downloadAction, dismissAction);
                    notification.Show();
                });
            }
            catch
            {
                // Безопасная обработка ошибок без логирования, так как это системный сервис
            }
        }

        public void ShowError(string title, string message)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageWindow.Show(message, title, MessageWindow.MessageType.Error);
                });
            }
            catch
            {
                // Безопасная обработка ошибок UI
            }
        }

        public void ShowInfo(string title, string message)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageWindow.Show(message, title, MessageWindow.MessageType.Information);
                });
            }
            catch
            {
                // Безопасная обработка ошибок UI
            }
        }
    }
}
