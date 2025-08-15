using CommunityToolkit.Mvvm.ComponentModel;

namespace LolManager.Models;

public partial class LogFilters : ObservableObject
{
    [ObservableProperty]
    private bool showLogin = true;
    
    [ObservableProperty]
    private bool showHttp = true;
    
    [ObservableProperty]
    private bool showUi = true;
    
    [ObservableProperty]
    private bool showProcess = true;
    
    [ObservableProperty]
    private bool showInfo = true;
    
    [ObservableProperty]
    private bool showWarning = true;
    
    [ObservableProperty]
    private bool showError = true;
    
    [ObservableProperty]
    private bool showDebug = false; // Скрыт по умолчанию
}
