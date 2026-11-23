using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Hospitality.Pos.Desktop.ViewModels;

public interface IRefreshable
{
    void Refresh();
}

public partial class SelectableChip : ObservableObject
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}

public partial class NavItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public Geometry? Icon { get; init; }
    [ObservableProperty] private string _badge = "";
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    partial void OnBadgeChanged(string value) => OnPropertyChanged(nameof(HasBadge));
}
