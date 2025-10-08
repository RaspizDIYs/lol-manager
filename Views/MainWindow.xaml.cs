using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Media.Animation;
using System.Windows.Media;
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
    private System.Windows.Controls.MenuItem? _trayAutomationMenu;
    private ILogger? _logger;

    public MainWindow()
    {
        InitializeComponent();
        
        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F12)
            {
                _logger?.Info("[WINDOW] F12 нажата - восстановление окна");
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        };
        
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
            UpdateAutomationCheckbox();
            
            // Устанавливаем начальное состояние навигации
            UpdateNavigationButtonStates(ViewModel?.SelectedTabIndex ?? 0);
            
            var app = (App)Application.Current;
            if (app.TrayIcon == null)
            {
                _logger?.Warning("[WINDOW] ВНИМАНИЕ: Трей-иконка не инициализирована!");
            }
        };
        
        Closing += Window_Closing;
        StateChanged += Window_StateChanged;
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
        _trayAutomationMenu = new System.Windows.Controls.MenuItem { Header = "Автоматизация", IsCheckable = true };
        _trayAutomationMenu.Click += TrayAutomation_Click;

        contextMenu.Items.Add(_trayLoginMenu);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(_trayAutoAcceptMenu);
        contextMenu.Items.Add(_trayAutomationMenu);
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
        else if (e.PropertyName == nameof(MainViewModel.IsAutomationEnabled))
        {
            UpdateAutomationCheckbox();
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

    private void UpdateAutomationCheckbox()
    {
        if (ViewModel != null && _trayAutomationMenu != null)
        {
            _trayAutomationMenu.IsChecked = ViewModel.IsAutomationEnabled;
            _trayAutomationMenu.Click -= TrayAutomation_Click;
            _trayAutomationMenu.Click += TrayAutomation_Click;
        }
    }

    private void TrayAutoAccept_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsAutoAcceptEnabled = !ViewModel.IsAutoAcceptEnabled;
        }
    }

    private void TrayAutomation_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsAutomationEnabled = !ViewModel.IsAutomationEnabled;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _logger?.Info($"[WINDOW] Window_Closing вызван, _isRealClose={_isRealClose}");
        
        if (!_isRealClose)
        {
            e.Cancel = true;
            _logger?.Info("[WINDOW] Показываем меню выбора действия");
            
            CloseActionPopup.IsOpen = true;
        }
        else
        {
            _logger?.Info("[WINDOW] Реальное закрытие приложения");
        }
    }
    
    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        _logger?.Info("[WINDOW] Выбрано сворачивание в трей");
        CloseActionPopup.IsOpen = false;
        
        var app = (App)Application.Current;
        
        if (app.TrayIcon == null)
        {
            _logger?.Warning("[WINDOW] Трей-иконка не доступна, минимизация в панель задач");
            WindowState = WindowState.Minimized;
            return;
        }
        
        try
        {
            app.TrayIcon.ForceCreate();
            Hide();
            _logger?.Info("[WINDOW] Окно скрыто в трей");
        }
        catch (Exception ex)
        {
            _logger?.Error($"[WINDOW] Ошибка скрытия в трей: {ex.Message}");
            WindowState = WindowState.Minimized;
        }
    }
    
    private void ExitAppButton_Click(object sender, RoutedEventArgs e)
    {
        _logger?.Info("[WINDOW] Выбрано полное закрытие приложения");
        CloseActionPopup.IsOpen = false;
        _isRealClose = true;
        Application.Current.Shutdown();
    }

    private void Window_StateChanged(object? sender, System.EventArgs e)
    {
        _logger?.Info($"[WINDOW] Window_StateChanged: {WindowState}");
        if (WindowState == WindowState.Minimized)
        {
            var app = (App)Application.Current;
            if (app.TrayIcon != null)
            {
                _logger?.Info("[WINDOW] Минимизация - сворачиваем в трей");
                try
                {
                    Hide();
                }
                catch (Exception ex)
                {
                    _logger?.Error($"[WINDOW] Ошибка скрытия при минимизации: {ex.Message}");
                }
            }
            else
            {
                _logger?.Warning("[WINDOW] Минимизация - трей недоступен, остаемся в панели задач");
            }
        }
    }
    
    public void ShowUpdateNotification(string version, Action downloadAction)
    {
        Dispatcher.Invoke(() =>
        {
            var toast = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(250, 20, 20, 22)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
            
            toast.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.6,
                ShadowDepth = 0,
                BlurRadius = 15
            };
            
            var mainStack = new StackPanel { Orientation = Orientation.Horizontal };
            
            var messageText = new System.Windows.Controls.TextBlock
            {
                Text = "Доступно обновление",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            
            var downloadBtn = new System.Windows.Controls.Button
            {
                Content = "Скачать",
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            
            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "✕",
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 100, 100, 100)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 3, 6, 3),
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            mainStack.Children.Add(messageText);
            mainStack.Children.Add(downloadBtn);
            mainStack.Children.Add(closeBtn);
            toast.Child = mainStack;
            
            NotificationContainer.Child = toast;
            NotificationContainer.IsHitTestVisible = true;
            
            closeBtn.Click += (s, args) => 
            {
                NotificationContainer.Child = null;
                NotificationContainer.IsHitTestVisible = false;
            };
            
            downloadBtn.Click += (s, args) => 
            {
                NotificationContainer.Child = null;
                NotificationContainer.IsHitTestVisible = false;
                downloadAction?.Invoke();
            };
            
            toast.Opacity = 0;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            
            var autoCloseTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            autoCloseTimer.Tick += (s, args) =>
            {
                autoCloseTimer.Stop();
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fadeOut.Completed += (s2, e2) => 
                {
                    NotificationContainer.Child = null;
                    NotificationContainer.IsHitTestVisible = false;
                };
                toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            autoCloseTimer.Start();
        });
    }

    private void BurgerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var menuBorder = MenuBorder;
            var currentWidth = menuBorder.Width;
            var isExpanded = currentWidth > 80;

            var targetWidth = isExpanded ? 80 : 250;
            
            // Добавляем анимацию поворота иконки
            var rotateAnimation = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            if (isExpanded)
            {
                // Сворачиваем - поворачиваем на 180 градусов (стрелка вправо)
                rotateAnimation.To = 180;
            }
            else
            {
                // Разворачиваем - возвращаем в исходное положение (стрелка влево)
                rotateAnimation.To = 0;
            }

            BurgerIconRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
            
            // Ищем анимации в ресурсах MenuBorder, а не всего окна
            var animationKey = isExpanded ? "CollapseAnimation" : "ExpandAnimation";
            var storyboard = menuBorder.FindResource(animationKey) as System.Windows.Media.Animation.Storyboard;
            
            if (storyboard != null)
            {
                storyboard.Begin();
            }
            else
            {
                // Fallback: просто меняем ширину напрямую без анимации
                _logger?.Warning($"[WINDOW] Анимация {animationKey} не найдена, меняем ширину напрямую");
                menuBorder.Width = targetWidth;
            }

            // Скрываем/показываем текст кнопок и заголовок
            AccountsText.Visibility = targetWidth > 80 ? Visibility.Visible : Visibility.Collapsed;
            SettingsText.Visibility = targetWidth > 80 ? Visibility.Visible : Visibility.Collapsed;
            LogsText.Visibility = targetWidth > 80 ? Visibility.Visible : Visibility.Collapsed;
            InformationText.Visibility = targetWidth > 80 ? Visibility.Visible : Visibility.Collapsed;
            AutomationText.Visibility = targetWidth > 80 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger?.Error($"[WINDOW] Ошибка при сворачивании/разворачивании панели: {ex}");
            Views.MessageWindow.Show($"Ошибка при сворачивании панели: {ex.Message}\n\nДетали:\n{ex}", "Ошибка", Views.MessageWindow.MessageType.Error);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag != null)
        {
            if (int.TryParse(button.Tag.ToString(), out int tabIndex))
            {
                if (ViewModel != null)
                {
                    ViewModel.SelectedTabIndex = tabIndex;
                }
                
                // Обновляем визуальные состояния кнопок
                UpdateNavigationButtonStates(tabIndex);
            }
        }
    }

    private void UpdateNavigationButtonStates(int selectedIndex)
    {
        // Сбрасываем все кнопки к стандартному виду
        ResetButtonStyle(AccountsButton);
        ResetButtonStyle(SettingsButton);
        ResetButtonStyle(LogsButton);
        ResetButtonStyle(InformationButton);
        ResetButtonStyle(AutomationButton);
        if (SpyButton.Visibility == Visibility.Visible)
            ResetButtonStyle(SpyButton);

        // Выделяем активную кнопку
        var buttons = new[] { AccountsButton, SettingsButton, LogsButton, InformationButton, AutomationButton, SpyButton };
        if (selectedIndex >= 0 && selectedIndex < buttons.Length)
        {
            if (selectedIndex == 5 && SpyButton.Visibility == Visibility.Visible)
            {
                SetActiveButtonStyle(SpyButton);
            }
            else if (selectedIndex < 5)
            {
                SetActiveButtonStyle(buttons[selectedIndex]);
            }
        }
    }

    private void ResetButtonStyle(System.Windows.Controls.Button button)
    {
        if (button is Wpf.Ui.Controls.Button wpfUiButton)
        {
            wpfUiButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            // Применяем стиль для обычных кнопок
            wpfUiButton.Style = (Style)FindResource("AnimatedNavButton");
        }
        
        button.Background = System.Windows.Media.Brushes.Transparent;
        button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
    }

    private void SetActiveButtonStyle(System.Windows.Controls.Button button)
    {
        if (button is Wpf.Ui.Controls.Button wpfUiButton)
        {
            // Применяем стиль для выбранной кнопки
            wpfUiButton.Style = (Style)FindResource("SelectedNavButtonStyle");
        }
    }

    public void ShowToast(string message, string icon = "✓", string iconColor = "#4CAF50")
    {
        try
        {
            // Очистить предыдущие уведомления
            NotificationContainer.Child = null;

            var toast = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 40, 40, 40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            toast.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.3,
                ShadowDepth = 0,
                BlurRadius = 12
            };

            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var iconText = new System.Windows.Controls.TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var messageText = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            stackPanel.Children.Add(iconText);
            stackPanel.Children.Add(messageText);
            toast.Child = stackPanel;

            NotificationContainer.Child = toast;

            // Анимация появления
            toast.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Таймер для скрытия
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (s2, e2) => NotificationContainer.Child = null;
                toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            timer.Start();
        }
        catch
        {
            // Безопасная обработка ошибок
        }
    }
}


