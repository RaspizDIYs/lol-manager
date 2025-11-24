using System.Windows;
using System.Windows.Controls;
using LolManager.ViewModels;
using LolManager.Services;
using System.Collections.Generic;

namespace LolManager.Views.Pages;

public partial class AutomationPage : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(AutomationViewModel), typeof(AutomationPage), new PropertyMetadata(null));

    public AutomationViewModel? ViewModel
    {
        get => (AutomationViewModel?)GetValue(ViewModelProperty);
        private set => SetValue(ViewModelProperty, value);
    }

    public AutomationPage()
    {
        InitializeComponent();
        
        // Инициализируем ViewModel сразу в конструкторе
        InitializeViewModel();
        
        // Простая инициализация без сложной логики
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void InitializeViewModel()
    {
        if (ViewModel == null)
        {
            try
            {
                var app = (App)Application.Current;
                var logger = app.GetService<ILogger>();
                var settingsService = app.GetService<ISettingsService>();
                var dataDragonService = app.GetService<DataDragonService>();
                var autoAcceptService = app.GetService<AutoAcceptService>();
                var runeDataService = app.GetService<RuneDataService>();
                var riotClientService = app.GetService<IRiotClientService>() as RiotClientService;
                var runePagesStorage = app.GetService<IRunePagesStorage>();
                var bindingService = app.GetService<BindingService>();

                ViewModel = new AutomationViewModel(logger!, settingsService!, dataDragonService!, autoAcceptService!, runeDataService!, riotClientService!, runePagesStorage!, bindingService);
                
                logger.Info("AutomationPage ViewModel initialized successfully");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AutomationPage ViewModel: {ex.Message}");
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Синхронизируем состояние автоматизации с MainViewModel
        if (DataContext is MainViewModel mainViewModel && ViewModel != null)
        {
            ViewModel.IsAutomationEnabled = mainViewModel.IsAutomationEnabled;
            
            mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            ViewModel.PropertyChanged += AutomationViewModel_PropertyChanged;
        }
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAutomationEnabled) && sender is MainViewModel mainViewModel)
        {
            if (ViewModel != null && ViewModel.IsAutomationEnabled != mainViewModel.IsAutomationEnabled)
            {
                ViewModel.IsAutomationEnabled = mainViewModel.IsAutomationEnabled;
            }
        }
    }

    private void AutomationViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutomationViewModel.IsAutomationEnabled) && sender is AutomationViewModel automationViewModel)
        {
            if (DataContext is MainViewModel mainViewModel && mainViewModel.IsAutomationEnabled != automationViewModel.IsAutomationEnabled)
            {
                mainViewModel.IsAutomationEnabled = automationViewModel.IsAutomationEnabled;
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Отписываемся от событий
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        }
        
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= AutomationViewModel_PropertyChanged;
        }
        
        // Закрываем все дочерние окна при выгрузке страницы
        CloseAllChildWindows();
    }

    private void CloseAllChildWindows()
    {
        try
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                var windowsToClose = new List<Window>();
                
                foreach (Window window in Application.Current.Windows)
                {
                    if (window != mainWindow && window.IsVisible)
                    {
                        if (window.GetType().Name.Contains("RunePageEditor") || 
                            window.Owner == mainWindow)
                        {
                            windowsToClose.Add(window);
                        }
                    }
                }
                
                foreach (var window in windowsToClose)
                {
                    try
                    {
                        window.Close();
                        System.Diagnostics.Debug.WriteLine($"Closed window: {window.GetType().Name}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error closing window {window.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error closing child windows: {ex.Message}");
        }
    }
}
