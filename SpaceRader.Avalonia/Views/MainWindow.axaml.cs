using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ScottPlot.Plottables;
using SpaceRader.Avalonia.Models;
using SpaceRader.Avalonia.Utilities;
using SpaceRader.Avalonia.ViewModels;

namespace SpaceRader.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private Pie? _pie;
    private ScottPlot.Color[] _originalSliceColors = [];

    private static readonly System.Drawing.Color[] SliceColors =
    [
        System.Drawing.Color.FromArgb(243, 139, 168), // Red
        System.Drawing.Color.FromArgb(250, 179, 135), // Peach
        System.Drawing.Color.FromArgb(249, 226, 175), // Yellow
        System.Drawing.Color.FromArgb(166, 227, 161), // Green
        System.Drawing.Color.FromArgb(148, 226, 213), // Teal
        System.Drawing.Color.FromArgb(137, 220, 235), // Sky
        System.Drawing.Color.FromArgb(116, 199, 236), // Sapphire
        System.Drawing.Color.FromArgb(137, 180, 250), // Blue
        System.Drawing.Color.FromArgb(180, 190, 254), // Lavender
        System.Drawing.Color.FromArgb(203, 166, 247), // Mauve
        System.Drawing.Color.FromArgb(245, 194, 231), // Pink
    ];

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(PickFolderAsync);
        DataContext = _viewModel;

        SetupPlot();

        _viewModel.DisplayChildren.CollectionChanged += (_, _) => RefreshChart();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "スキャンするフォルダーを選択してください",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private void SetupPlot()
    {
        AvaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181825");
        AvaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        AvaPlot.Plot.Axes.Frameless();
        AvaPlot.Plot.HideAxesAndGrid();
        AvaPlot.Plot.Legend.IsVisible = false;
        AvaPlot.UserInputProcessor.IsEnabled = false;
        AvaPlot.Refresh();
    }

    private void RefreshChart()
    {
        AvaPlot.Plot.Clear();

        var children = _viewModel.DisplayChildren.ToList();
        if (children.Count == 0)
        {
            AvaPlot.Refresh();
            return;
        }

        var totalSize = children.Sum(c => c.Size);
        if (totalSize == 0)
        {
            AvaPlot.Refresh();
            return;
        }

        double[] values = children.Select(c => (double)c.Size).ToArray();
        var pie = AvaPlot.Plot.Add.Pie(values);
        pie.SliceLabelDistance = 0;
        pie.LineWidth = 2;
        pie.LineColor = ScottPlot.Color.FromHex("#1E1E2E");
        pie.ExplodeFraction = 0.05;

        for (int i = 0; i < pie.Slices.Count && i < children.Count; i++)
        {
            var color = SliceColors[i % SliceColors.Length];
            pie.Slices[i].Fill = new ScottPlot.FillStyle
            {
                Color = ScottPlot.Color.FromARGB((uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B))
            };
            pie.Slices[i].LegendText = $"{children[i].Name}  ({FileSizeFormatter.FormatSize(children[i].Size)})";

            var avColor = Color.FromArgb(color.A, color.R, color.G, color.B);
            children[i].SliceColorBrush = new SolidColorBrush(avColor);
        }

        _pie = pie;
        _originalSliceColors = pie.Slices.Select(s => s.Fill.Color).ToArray();

        AvaPlot.Plot.HideAxesAndGrid();
        AvaPlot.Plot.Axes.SetLimits(-1.2, 1.2, -1.2, 1.2);
        AvaPlot.Refresh();
    }

    private void FolderListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_pie == null || _originalSliceColors.Length == 0) return;

        int selectedIndex = FolderListBox.SelectedIndex;
        for (int i = 0; i < _pie.Slices.Count && i < _originalSliceColors.Length; i++)
        {
            var original = _originalSliceColors[i];
            _pie.Slices[i].Fill = new ScottPlot.FillStyle
            {
                Color = selectedIndex < 0 || i == selectedIndex
                    ? original
                    : original.WithAlpha(0.25)
            };
        }
        AvaPlot.Refresh();
    }

    private void FolderListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FolderListBox.SelectedItem is FolderItem item && item.IsDirectory)
            _ = _viewModel.DrillDownAsync(item);
    }
}
