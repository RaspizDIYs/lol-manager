using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using Wpf.Ui.Controls;
using LolManager.ViewModels;
using System.Threading.Tasks;
using H.NotifyIcon;
using LolManager.Services;

namespace LolManager.Views;

public partial class MainWindow : FluentWindow
{
    private bool _isRealClose = false;
    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private System.Windows.Controls.MenuItem? _trayLoginMenu;
    private System.Windows.Controls.MenuItem? _trayAutoAcceptMenu;
    private ILogger? _logger;

    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += (s, e) =>
        {
            _logger = ((App)Application.Current).GetService<ILogger>();
            _logger?.Info("[WINDOW] MainWindow загружено");
            
            SetupTrayMenuItems();
            
            if (ViewModel != null)
            {
                ViewModel.Accounts.CollectionChanged += (_, __) => UpdateTrayAccountsMenu();
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            UpdateTrayAccountsMenu();
            UpdateAutoAcceptCheckbox();
        };
    }

    private void SetupTrayMenuItems()
    {
        var app = (App)Application.Current;
        if (app.TrayIcon?.ContextMenu == null) return;

        _logger?.Info("[WINDOW] Настройка меню трея");

        var contextMenu = app.TrayIcon.ContextMenu;
        contextMenu.Items.Clear();

        _trayLoginMenu = new System.Windows.Controls.MenuItem { Header = "Войти" };
        _trayAutoAcceptMenu = new System.Windows.Controls.MenuItem { Header = "Автопринятие", IsCheckable = true };
        _trayAutoAcceptMenu.Click += TrayAutoAccept_Click;

        contextMenu.Items.Add(_trayLoginMenu);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(_trayAutoAcceptMenu);
        contextMenu.Items.Add(new Separator());

        var showItem = new System.Windows.Controls.MenuItem { Header = "Открыть" };
        showItem.Click += (s, e) =>
        {
            _logger?.Info("[WINDOW] Трей: Открыть окно");
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        contextMenu.Items.Add(showItem);

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Закрыть" };
        exitItem.Click += (s, e) =>
        {
            _logger?.Info("[WINDOW] Трей: Закрыть приложение");
            _isRealClose = true;
            Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        _logger?.Info("[WINDOW] Меню трея настроено");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAutoAcceptEnabled))
        {
            UpdateAutoAcceptCheckbox();
        }
    }

    private void UpdateAutoAcceptCheckbox()
    {
        if (ViewModel != null && _trayAutoAcceptMenu != null)
        {
            _trayAutoAcceptMenu.IsChecked = ViewModel.IsAutoAcceptEnabled;
            _trayAutoAcceptMenu.Click -= TrayAutoAccept_Click;
            _trayAutoAcceptMenu.Click += TrayAutoAccept_Click;
        }
    }

    private void UpdateTrayAccountsMenu()
    {
        if (_trayLoginMenu == null) return;
        
        _trayLoginMenu.Items.Clear();
        
        if (ViewModel?.Accounts == null || !ViewModel.Accounts.Any())
        {
            var emptyItem = new System.Windows.Controls.MenuItem { Header = "Нет аккаунтов", IsEnabled = false };
            _trayLoginMenu.Items.Add(emptyItem);
            return;
        }

        foreach (var account in ViewModel.Accounts)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = account.Username,
                Tag = account
            };
            menuItem.Click += async (s, e) =>
            {
                if (s is System.Windows.Controls.MenuItem mi && mi.Tag is Models.AccountRecord acc)
                {
                    ViewModel.SelectedAccount = acc;
                    if (ViewModel.LoginSelectedCommand.CanExecute(null))
                    {
                        await Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ViewModel.LoginSelectedCommand.Execute(null);
                            });
                        });
                    }
                }
            };
            _trayLoginMenu.Items.Add(menuItem);
        }
    }

    private void TrayAutoAccept_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsAutoAcceptEnabled = !ViewModel.IsAutoAcceptEnabled;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _logger?.Info($"[WINDOW] Window_Closing вызван, _isRealClose={_isRealClose}");
        
        if (!_isRealClose)
        {
            e.Cancel = true;
            _logger?.Info("[WINDOW] Отменяем закрытие, сворачиваем в трей");
            
            var app = (App)Application.Current;
            _logger?.Info($"[WINDOW] TrayIcon существует: {app.TrayIcon != null}");
            _logger?.Info($"[WINDOW] TrayIcon.Visibility: {app.TrayIcon?.Visibility}");
            
            Hide();
            _logger?.Info("[WINDOW] Окно скрыто, проверяем трей...");
            
            // Форсируем обновление трея
            if (app.TrayIcon != null)
            {
                app.TrayIcon.ForceCreate();
                _logger?.Info("[WINDOW] TrayIcon.ForceCreate() вызван");
            }
        }
        else
        {
            _logger?.Info("[WINDOW] Реальное закрытие приложения");
        }
    }

    private void Window_StateChanged(object? sender, System.EventArgs e)
    {
        _logger?.Info($"[WINDOW] Window_StateChanged: {WindowState}");
        if (WindowState == WindowState.Minimized)
        {
            _logger?.Info("[WINDOW] Минимизация - сворачиваем в трей");
            Hide();
        }
    }
}


