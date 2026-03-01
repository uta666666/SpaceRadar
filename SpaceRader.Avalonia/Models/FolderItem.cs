using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SpaceRader.Avalonia.Models;

public partial class FolderItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public List<FolderItem> Children { get; set; } = [];
    public FolderItem? Parent { get; set; }

    public string Icon => IsDirectory ? "📁" : "📄";

    [ObservableProperty]
    private IBrush? _sliceColorBrush;
}
