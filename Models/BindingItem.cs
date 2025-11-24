using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LolManager.Models;

public class BindingItem : INotifyPropertyChanged
{
    private string _key = string.Empty;
    private string _value = string.Empty;

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string DisplayName => GetDisplayName(_key);

    private static string GetDisplayName(string key)
    {
        return key switch
        {
            "evtChampionSpell1" => "Q",
            "evtChampionSpell2" => "W",
            "evtChampionSpell3" => "E",
            "evtChampionSpell4" => "R",
            "evtUseItem1" => "Предмет 1",
            "evtUseItem2" => "Предмет 2",
            "evtUseItem3" => "Предмет 3",
            "evtUseItem4" => "Предмет 4",
            "evtUseItem5" => "Предмет 5",
            "evtUseItem6" => "Предмет 6",
            "evtCastSpell1" => "Заклинание 1",
            "evtCastSpell2" => "Заклинание 2",
            "evtPlayerAttackMove" => "Атака-движение",
            "evtPlayerAttackMoveClick" => "Атака-движение (клик)",
            "evtPlayerMoveClick" => "Движение",
            "evtPlayerSelectClick" => "Выбор",
            "evtPlayerSelectOnlySelf" => "Выбор только себя",
            "evtPlayerSelectOnlySelfAndPet" => "Выбор себя и питомца",
            "evtPlayerStop" => "Стоп",
            "evtPlayerHoldPosition" => "Удержание позиции",
            "evtCameraSnapPlayer" => "Камера на игрока",
            "evtCameraLock" => "Блокировка камеры",
            "evtShowCharacterMenu" => "Меню персонажа",
            "evtShowScoreboard" => "Таблица лидеров",
            "evtShowChat" => "Чат",
            "evtPing" => "Пинг",
            "evtPingFriendly" => "Пинг союзника",
            "evtPingEnemy" => "Пинг врага",
            "evtPingDanger" => "Пинг опасности",
            "evtPingOnMyWay" => "Пинг \"Иду\"",
            "evtPingAssistMe" => "Пинг \"Помоги\"",
            "evtPingRetreat" => "Пинг \"Отступление\"",
            "evtPingVision" => "Пинг \"Видимость\"",
            "evtPingMissingEnemy" => "Пинг \"Враг отсутствует\"",
            "evtPingEnemyMissing" => "Пинг \"Враг отсутствует\"",
            "evtPingEnemyVision" => "Пинг \"Вражеская видимость\"",
            "evtPingEnemyMissingPing" => "Пинг \"Враг отсутствует\"",
            "evtPingEnemyVisionPing" => "Пинг \"Вражеская видимость\"",
            "evtPingEnemyMissingPingAlt" => "Пинг \"Враг отсутствует\" (Alt)",
            "evtPingEnemyVisionPingAlt" => "Пинг \"Вражеская видимость\" (Alt)",
            "evtPingEnemyMissingPingCtrl" => "Пинг \"Враг отсутствует\" (Ctrl)",
            "evtPingEnemyVisionPingCtrl" => "Пинг \"Вражеская видимость\" (Ctrl)",
            "evtPingEnemyMissingPingShift" => "Пинг \"Враг отсутствует\" (Shift)",
            "evtPingEnemyVisionPingShift" => "Пинг \"Вражеская видимость\" (Shift)",
            "evtPingEnemyMissingPingAltCtrl" => "Пинг \"Враг отсутствует\" (Alt+Ctrl)",
            "evtPingEnemyVisionPingAltCtrl" => "Пинг \"Вражеская видимость\" (Alt+Ctrl)",
            "evtPingEnemyMissingPingAltShift" => "Пинг \"Враг отсутствует\" (Alt+Shift)",
            "evtPingEnemyVisionPingAltShift" => "Пинг \"Вражеская видимость\" (Alt+Shift)",
            "evtPingEnemyMissingPingCtrlShift" => "Пинг \"Враг отсутствует\" (Ctrl+Shift)",
            "evtPingEnemyVisionPingCtrlShift" => "Пинг \"Вражеская видимость\" (Ctrl+Shift)",
            "evtPingEnemyMissingPingAltCtrlShift" => "Пинг \"Враг отсутствует\" (Alt+Ctrl+Shift)",
            "evtPingEnemyVisionPingAltCtrlShift" => "Пинг \"Вражеская видимость\" (Alt+Ctrl+Shift)",
            _ => key
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(DisplayName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

