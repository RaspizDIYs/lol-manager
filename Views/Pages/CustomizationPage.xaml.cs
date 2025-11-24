using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LolManager.ViewModels;
using LolManager.Services;

namespace LolManager.Views.Pages;

public partial class CustomizationPage : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(CustomizationViewModel), typeof(CustomizationPage), new PropertyMetadata(null));

    public CustomizationViewModel? ViewModel
    {
        get => (CustomizationViewModel?)GetValue(ViewModelProperty);
        private set => SetValue(ViewModelProperty, value);
    }

    public CustomizationPage()
    {
        InitializeComponent();
        InitializeViewModel();
    }

    private void InitializeViewModel()
    {
        if (ViewModel == null)
        {
            try
            {
                var app = (App)Application.Current;
                var logger = app.GetService<ILogger>();
                var customizationService = app.GetService<CustomizationService>();
                var dataDragonService = app.GetService<DataDragonService>();
                var riotClientService = app.GetService<IRiotClientService>();

                ViewModel = new CustomizationViewModel(logger!, customizationService!, dataDragonService!, riotClientService!);
                
                logger.Info("CustomizationPage ViewModel initialized successfully");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing CustomizationPage ViewModel: {ex.Message}");
            }
        }
    }

    private void BackgroundChampion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is string championName && ViewModel != null)
        {
            ViewModel.SelectedChampionForBackground = championName;
        }
    }
    
    private void BackgroundSkin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.SkinInfo skin && ViewModel != null)
        {
            ViewModel.SelectedChampionForBackground = skin.ChampionName;
            ViewModel.SelectedSkinForBackground = skin;
        }
    }

    private void Image_ImageFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Image image)
        {
            image.Source = null;
        }
    }

    private void SkinsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ViewModel?.HasMoreSkins != true)
        {
            return;
        }

        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 80)
        {
            if (ViewModel.LoadMoreSkinsCommand.CanExecute(null))
            {
                ViewModel.LoadMoreSkinsCommand.Execute(null);
            }
        }
    }
}

