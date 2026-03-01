using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpaceRadar.Models;

public class FolderItem : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public List<FolderItem> Children { get; set; } = new();
    public FolderItem? Parent { get; set; }

    private System.Windows.Media.Brush? _sliceColorBrush;
    public System.Windows.Media.Brush? SliceColorBrush
    {
        get => _sliceColorBrush;
        set { _sliceColorBrush = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
