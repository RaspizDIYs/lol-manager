using System.Windows;
using System.Windows.Controls;
using LolManager.ViewModels;
using LolManager.Services;

namespace LolManager.Views.Pages;

public partial class AutomationPage : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(AutomationViewModel), typeof(AutomationPage), new PropertyMetadata(null));

    public AutomationViewModel? ViewModel
    {
        get => (AutomationViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private bool _isInitialized = false;

    public AutomationPage()
    {
        InitializeComponent();
        
        // Инициализируем при первом показе страницы
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || _isInitialized) return;
        
        _isInitialized = true;
        
        try
        {
            var app = (App)Application.Current;
            var logger = app.GetService<ILogger>();
            var settingsService = app.GetService<ISettingsService>();
            var dataDragonService = app.GetService<DataDragonService>();
            var autoAcceptService = app.GetService<AutoAcceptService>();
            
            // Создаем ViewModel и устанавливаем как свойство
            ViewModel = new AutomationViewModel(logger, settingsService, dataDragonService, autoAcceptService);
            
            logger.Info("AutomationPage initialized successfully");
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing AutomationPage: {ex.Message}");
        }
    }
}
