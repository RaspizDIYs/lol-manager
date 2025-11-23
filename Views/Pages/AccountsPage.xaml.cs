using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LolManager.Models;

namespace LolManager.Views.Pages;

public partial class AccountsPage : UserControl
{
    private AccountRecord? _draggedItem;
    private Point _dragStartPoint;
    private DataGridRow? _dragOverRow;

    public AccountsPage()
    {
        InitializeComponent();
    }

    private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.Item is AccountRecord account)
        {
            _draggedItem = account;
            _dragStartPoint = e.GetPosition(null);
        }
    }

    private void DataGridRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
        {
            var currentPoint = e.GetPosition(null);
            var diff = _dragStartPoint - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is DataGridRow row)
                {
                    row.Opacity = 0.5;
                    DragDrop.DoDragDrop(row, _draggedItem, DragDropEffects.Move);
                    row.Opacity = 1.0;
                    _draggedItem = null;
                }
            }
        }
    }

    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
        if (sender is DataGridRow targetRow && 
            targetRow.Item is AccountRecord targetAccount &&
            e.Data.GetData(typeof(AccountRecord)) is AccountRecord draggedAccount &&
            draggedAccount != targetAccount)
        {
            var viewModel = DataContext as ViewModels.MainViewModel;
            if (viewModel != null)
            {
                var accounts = viewModel.Accounts;
                var draggedIndex = accounts.IndexOf(draggedAccount);
                var targetIndex = accounts.IndexOf(targetAccount);

                if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
                {
                    accounts.Move(draggedIndex, targetIndex);
                    viewModel.SaveAccountsOrder();
                }
            }
        }
        
        if (_dragOverRow != null)
        {
            _dragOverRow.Background = Brushes.Transparent;
            _dragOverRow = null;
        }
        _draggedItem = null;
        e.Handled = true;
    }

    private void DataGridRow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(AccountRecord)) is AccountRecord)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            
            if (sender is DataGridRow row && row != _dragOverRow)
            {
                if (_dragOverRow != null)
                {
                    _dragOverRow.Background = Brushes.Transparent;
                }
                _dragOverRow = row;
                row.Background = new SolidColorBrush(Color.FromArgb(100, 99, 102, 241)); // Полупрозрачный фиолетовый
            }
        }
    }

    private void DataGridRow_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is DataGridRow row && row == _dragOverRow)
        {
            row.Background = Brushes.Transparent;
            _dragOverRow = null;
        }
    }
}

