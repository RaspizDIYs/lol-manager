using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LolManager.Models;
using LolManager.Services;
using LolManager.ViewModels;

namespace LolManager.Views;

public partial class RunePageEditorWindow
{
    private readonly RunePageEditorViewModel _viewModel;

    public RunePageEditorWindow(RuneDataService runeDataService, RunePage? existingPage = null)
    {
        InitializeComponent();
        
        _viewModel = new RunePageEditorViewModel(runeDataService, existingPage);
        DataContext = _viewModel;
        
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName?.StartsWith("Selected") == true)
            {
                UpdateStatusText();
            }
        };
        
        Loaded += (s, e) =>
        {
            UpdateStatusText();
        };
    }

    #region Event Handlers for Path Selection
    
    private void PrimaryPath_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is RunePath path)
        {
            _viewModel.SelectedPrimaryPath = path;
        }
    }

    private void SecondaryPath_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is RunePath path)
        {
            _viewModel.SelectedSecondaryPath = path;
        }
    }
    
    #endregion
    
    #region Event Handlers for Primary Runes
    
    private void KeystoneRune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedKeystone = rune;
        }
    }

    private void PrimarySlot1Rune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedPrimarySlot1 = rune;
        }
    }

    private void PrimarySlot2Rune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedPrimarySlot2 = rune;
        }
    }

    private void PrimarySlot3Rune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedPrimarySlot3 = rune;
        }
    }
    
    #endregion
    
    #region Event Handlers for Secondary Runes

    private void SecondarySlot1Rune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.TrySelectSecondaryRune(1, rune);
            UpdateSecondaryRuneButtons();
        }
    }

    private void SecondarySlot2Rune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.TrySelectSecondaryRune(2, rune);
            UpdateSecondaryRuneButtons();
        }
    }

    private void SecondarySlot3Rune_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.TrySelectSecondaryRune(3, rune);
            UpdateSecondaryRuneButtons();
        }
    }
    
    #endregion
    
    #region Event Handlers for Stat Mods

    private void StatMod1_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedStatMod1 = rune;
        }
    }

    private void StatMod2_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedStatMod2 = rune;
        }
    }

    private void StatMod3_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is Rune rune)
        {
            _viewModel.SelectedStatMod3 = rune;
        }
    }
    
    #endregion

    #region Status Updates
    
    private void UpdateStatusText()
    {
        if (StatusText == null) return;
        
        try
        {
            if (_viewModel.CanSave())
            {
                StatusText.Text = "Готово к сохранению ✓";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorSuccessBrush");
            }
            else
            {
                var missing = GetMissingFields();
                StatusText.Text = $"Не заполнено: {missing}";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
            }
        }
        catch
        {
            StatusText.Text = "Заполните все поля для сохранения";
        }
    }
    
    private string GetMissingFields()
    {
        var missing = new List<string>();
        
        if (string.IsNullOrWhiteSpace(_viewModel.PageName))
            missing.Add("название");
        
        if (_viewModel.SelectedPrimaryPath == null)
            missing.Add("основной путь");
        
        if (_viewModel.SelectedSecondaryPath == null)
            missing.Add("дополнительный путь");
        
        if (_viewModel.SelectedKeystone == null)
            missing.Add("краеугольный камень");
        
        if (_viewModel.SelectedPrimarySlot1 == null || 
            _viewModel.SelectedPrimarySlot2 == null || 
            _viewModel.SelectedPrimarySlot3 == null)
            missing.Add("основные руны");
        
        int secondaryCount = 0;
        if (_viewModel.SelectedSecondarySlot1 != null) secondaryCount++;
        if (_viewModel.SelectedSecondarySlot2 != null) secondaryCount++;
        if (_viewModel.SelectedSecondarySlot3 != null) secondaryCount++;
        
        if (secondaryCount != 2)
            missing.Add("дополнительные руны (2 из 3)");
        
        if (_viewModel.SelectedStatMod1 == null || 
            _viewModel.SelectedStatMod2 == null || 
            _viewModel.SelectedStatMod3 == null)
            missing.Add("статистика");
        
        return string.Join(", ", missing);
    }
    
    #endregion

    #region Button Handlers
    
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanSave())
        {
            var missing = GetMissingFields();
            var message = $"Заполните все обязательные поля:\n{missing}";
            
            var messageWindow = new MessageWindow();
            messageWindow.Title = "Ошибка сохранения";
            messageWindow.MessageText.Text = $"Заполните все обязательные поля:\n{missing}";
            messageWindow.Owner = this;
            messageWindow.ShowDialog();
            return;
        }

        _viewModel.DialogResult = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    #endregion
    
    #region Helper Methods
    
    private void UpdateSecondaryRuneButtons()
    {
        // Обновляем состояние RadioButton для вторичных рун
        UpdateSecondarySlotButtons("SecondarySlot1", _viewModel.SelectedSecondarySlot1);
        UpdateSecondarySlotButtons("SecondarySlot2", _viewModel.SelectedSecondarySlot2);
        UpdateSecondarySlotButtons("SecondarySlot3", _viewModel.SelectedSecondarySlot3);
    }
    
    private void UpdateSecondarySlotButtons(string groupName, Rune? selectedRune)
    {
        var buttons = FindVisualChildren<RadioButton>(this)
            .Where(rb => rb.GroupName == groupName);
            
        foreach (var button in buttons)
        {
            if (button.Content is Rune rune)
            {
                button.IsChecked = selectedRune != null && selectedRune.Equals(rune);
            }
        }
    }
    
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T)
                {
                    yield return (T)child;
                }

                if (child != null)
                {
                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }
    }
    
    #endregion

    public RunePage GetSavedPage() => _viewModel.Save();
}