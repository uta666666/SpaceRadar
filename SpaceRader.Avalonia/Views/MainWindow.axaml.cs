using System.Collections.Specialized;
using System.IO;
using Avalonia;
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
    private double _topNPanelHeight = 180;
    private bool _ignoreNextPointerPressed;

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

        _viewModel.DisplayChildren.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                FolderListBox.SelectedIndex = -1;
            }
            RefreshChart();
        };
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsTopNVisible))
            {
                UpdateTopNPanelVisibility(_viewModel.IsTopNVisible);
            }
        };
        AddHandler(DragDrop.DragEnterEvent, Window_DragEnter);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DragLeaveEvent, Window_DragLeave);
        AddHandler(DragDrop.DropEvent, Window_Drop);
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
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void SetupPlot()
    {
        AvaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181825");
        AvaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        AvaPlot.Plot.Axes.Frameless();
        AvaPlot.Plot.HideAxesAndGrid();
        AvaPlot.Plot.Legend.IsVisible = false;
        AvaPlot.UserInputProcessor.IsEnabled = false;
        AvaPlot.PointerPressed += AvaPlot_PointerPressed;
        AvaPlot.DoubleTapped += AvaPlot_DoubleTapped;
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
        pie.SliceLabelDistance = 0.6;
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
            string sizeText = FileSizeFormatter.FormatSize(children[i].Size);
            pie.Slices[i].LegendText = $"{children[i].Name}  ({sizeText})";

            double sliceRatio = (double)children[i].Size / totalSize;
            pie.Slices[i].Label = sliceRatio >= 0.05
                ? $"{sizeText}\n[{sliceRatio:P1}]"
                : string.Empty;
            pie.Slices[i].LabelFontSize = 11;
            pie.Slices[i].LabelBold = true;
            pie.Slices[i].LabelFontColor = ScottPlot.Color.FromHex("#1E1E2E");

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
        {
            _ = _viewModel.DrillDownAsync(item);
        }
    }

    private int GetSliceIndexAt(Point pos)
    {
        if (_pie == null || _viewModel.DisplayChildren.Count == 0)
        {
            return -1;
        }

        var pixel = new ScottPlot.Pixel((float)pos.X, (float)pos.Y);
        var coords = AvaPlot.Plot.GetCoordinates(pixel);

        double x = coords.X;
        double y = coords.Y;

        double dist = Math.Sqrt(x * x + y * y);
        if (dist > 1.15 || dist < 0.01)
        {
            return -1;
        }

        double mathAngleDeg = Math.Atan2(y, x) * 180.0 / Math.PI;
        double chartAngle = ((90.0 - mathAngleDeg) % 360.0 + 360.0) % 360.0;

        double total = _pie.Slices.Sum(s => s.Value);
        double cumulative = 0;

        for (int i = 0; i < _pie.Slices.Count; i++)
        {
            double sliceAngle = _pie.Slices[i].Value / total * 360.0;
            if (chartAngle < cumulative + sliceAngle)
            {
                return i;
            }
            cumulative += sliceAngle;
        }
        return -1;
    }

    private void AvaPlot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_ignoreNextPointerPressed)
        {
            _ignoreNextPointerPressed = false;
            return;
        }

        var pos = e.GetPosition(AvaPlot);
        int index = GetSliceIndexAt(pos);
        if (index < 0)
        {
            return;
        }

        FolderListBox.SelectedIndex = index;
        FolderListBox.ContainerFromIndex(index)?.BringIntoView();
    }

    private void AvaPlot_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(AvaPlot);
        int index = GetSliceIndexAt(pos);
        if (index < 0)
        {
            return;
        }

        var item = _viewModel.DisplayChildren[index];
        if (item.IsDirectory)
        {
            _ignoreNextPointerPressed = true;
            _ = _viewModel.DrillDownAsync(item);
        }
    }

    private void UpdateTopNPanelVisibility(bool visible)
    {
        var rootGrid = (Grid)Content!;
        if (visible)
        {
            rootGrid.RowDefinitions[2].Height = new GridLength(5);
            rootGrid.RowDefinitions[3].MinHeight = 80;
            rootGrid.RowDefinitions[3].Height = new GridLength(_topNPanelHeight);
            Height += _topNPanelHeight + 5;
        }
        else
        {
            double currentPanelHeight = rootGrid.RowDefinitions[3].ActualHeight;
            double currentSplitterHeight = rootGrid.RowDefinitions[2].ActualHeight;
            if (currentPanelHeight > 0)
            {
                _topNPanelHeight = currentPanelHeight;
            }
            rootGrid.RowDefinitions[3].MinHeight = 0;
            rootGrid.RowDefinitions[2].Height = new GridLength(0);
            rootGrid.RowDefinitions[3].Height = new GridLength(0);
            if (currentPanelHeight > 0)
            {
                Height = Math.Max(400, Height - currentPanelHeight - currentSplitterHeight);
            }
        }
    }

    private void Window_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            _viewModel.IsDragOver = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void Window_DragLeave(object? sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X > ClientSize.Width || pos.Y > ClientSize.Height)
        {
            _viewModel.IsDragOver = false;
        }
    }

    private void Window_Drop(object? sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        var folder = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null && Directory.Exists(p));
        if (folder == null)
        {
            _viewModel.StatusText = "フォルダーをドロップしてください";
            return;
        }

        _ = _viewModel.ScanDroppedFolderAsync(folder);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            FolderListBox.SelectedIndex = -1;
        }
    }
}
