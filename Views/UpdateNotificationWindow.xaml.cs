using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LolManager.Views
{
    [ObservableObject]
    public partial class UpdateNotificationWindow : Window
    {
        [ObservableProperty]
        private string _version = string.Empty;
        
        private readonly Func<Task>? _downloadAction;
        private readonly Action? _dismissAction;

        public UpdateNotificationWindow(string version, Func<Task>? downloadAction = null, Action? dismissAction = null)
        {
            _version = version;
            _downloadAction = downloadAction;
            _dismissAction = dismissAction;
            
            InitializeComponent();
            
            // Позиционируем в правом нижнем углу
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 20;
            Top = workingArea.Bottom - Height - 20;
            
            // Автоматически закрываем через 8 секунд
            var autoCloseTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            autoCloseTimer.Tick += (s, e) =>
            {
                autoCloseTimer.Stop();
                Close();
            };
            autoCloseTimer.Start();
        }

        [RelayCommand]
        private async Task Download()
        {
            try
            {
                if (_downloadAction != null)
                {
                    await _downloadAction.Invoke();
                }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке обновления: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void Dismiss()
        {
            try
            {
                _dismissAction?.Invoke();
                Close();
            }
            catch
            {
                Close();
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Немного уменьшаем прозрачность когда окно не активно
            Opacity = 0.8;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Восстанавливаем полную прозрачность при активации
            Opacity = 1.0;
        }
    }
}
