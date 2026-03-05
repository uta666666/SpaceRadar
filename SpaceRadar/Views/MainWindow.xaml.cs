using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScottPlot.Plottables;
using SpaceRadar.Models;
using SpaceRadar.Utilities;
using SpaceRadar.ViewModels;

namespace SpaceRadar.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Pie? _pie;
    private ScottPlot.Color[] _originalSliceColors = [];
    private double _topNPanelHeight = 180;
    private bool _ignoreNextMouseLeftButtonDown = false;

    // Catppuccin Mocha palette
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
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        SetupPlot();

        _viewModel.DisplayChildren.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                FolderListBox.SelectedIndex = -1;
            }
            RefreshChart();
        };
        _viewModel.IsTopNVisible.Subscribe(UpdateTopNPanelVisibility);
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void UpdateTopNPanelVisibility(bool visible)
    {
        if (visible)
        {
            RootGrid.RowDefinitions[2].Height = new GridLength(5);
            RootGrid.RowDefinitions[3].MinHeight = 80;
            RootGrid.RowDefinitions[3].Height = new GridLength(_topNPanelHeight);
            Height += _topNPanelHeight + 5;
        }
        else
        {
            var currentPanelHeight = RootGrid.RowDefinitions[3].ActualHeight;
            var currentSplitterHeight = RootGrid.RowDefinitions[2].ActualHeight;
            if (currentPanelHeight > 0)
            {
                _topNPanelHeight = currentPanelHeight;
            }
            RootGrid.RowDefinitions[3].MinHeight = 0;
            RootGrid.RowDefinitions[2].Height = new GridLength(0);
            RootGrid.RowDefinitions[3].Height = new GridLength(0);
            if (currentPanelHeight > 0)
            {
                Height = Math.Max(400, Height - currentPanelHeight - currentSplitterHeight);
            }
        }
    }

    private void SetupPlot()
    {
        //ScottPlot.Fonts.Default = ScottPlot.Fonts.Detect("テスト");
        WpfPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181825");
        WpfPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        WpfPlot.Plot.Axes.Frameless();
        WpfPlot.Plot.HideAxesAndGrid();
        WpfPlot.Plot.Legend.IsVisible = false;
        WpfPlot.UserInputProcessor.IsEnabled = false;
        WpfPlot.SizeChanged += (_, _) => WpfPlot.Refresh();
        WpfPlot.MouseLeftButtonDown += WpfPlot_MouseLeftButtonDown;
        WpfPlot.MouseDoubleClick += WpfPlot_MouseDoubleClick;
        WpfPlot.Refresh();
    }

    private void RefreshChart()
    {
        WpfPlot.Plot.Clear();

        var children = _viewModel.DisplayChildren.ToList();
        if (children.Count == 0)
        {
            WpfPlot.Refresh();
            return;
        }

        var totalSize = children.Sum(c => c.Size);
        if (totalSize == 0)
        {
            WpfPlot.Refresh();
            return;
        }

        double[] values = children.Select(c => (double)c.Size).ToArray();
        var pie = WpfPlot.Plot.Add.Pie(values);
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

            var mediaColor = System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
            children[i].SliceColorBrush = new System.Windows.Media.SolidColorBrush(mediaColor);
        }

        _pie = pie;
        _originalSliceColors = pie.Slices.Select(s => s.Fill.Color).ToArray();

        WpfPlot.Plot.HideAxesAndGrid();
        WpfPlot.Plot.Axes.SetLimits(-1.2, 1.2, -1.2, 1.2);
        WpfPlot.Refresh();
    }

    private void FolderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pie == null || _originalSliceColors.Length == 0)
        {
            return;
        }

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
        WpfPlot.Refresh();
    }

    private int GetSliceIndexAt(MouseButtonEventArgs e)
    {
        if (_pie == null || _viewModel.DisplayChildren.Count == 0)
        {
            return -1;
        }

        var pos = e.GetPosition(WpfPlot);
        var pixel = new ScottPlot.Pixel((float)pos.X, (float)pos.Y);
        var coords = WpfPlot.Plot.GetCoordinates(pixel);

        double x = coords.X;
        double y = coords.Y;

        // パイの外側・中心付近のクリックは無視
        double dist = Math.Sqrt(x * x + y * y);
        if (dist > 1.15 || dist < 0.01)
        {
            return -1;
        }

        // ScottPlot 5 のデフォルト: Rotation=0 → 12時から時計回り
        // Math.Atan2 は反時計回り・右=0° なので変換する
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

    private void WpfPlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_ignoreNextMouseLeftButtonDown)
        {
            _ignoreNextMouseLeftButtonDown = false;
            return;
        }

        int index = GetSliceIndexAt(e);
        if (index < 0)
        {
            return;
        }

        FolderListBox.SelectedIndex = index;
        FolderListBox.ScrollIntoView(FolderListBox.SelectedItem);
    }

    private void WpfPlot_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        int index = GetSliceIndexAt(e);
        if (index < 0)
        {
            return;
        }

        var item = _viewModel.DisplayChildren[index];
        if (item.IsDirectory)
        {
            _ignoreNextMouseLeftButtonDown = true;
            _ = _viewModel.DrillDownAsync(item);
        }
    }

    private void FolderListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FolderListBox.SelectedItem is FolderItem item && item.IsDirectory)
        {
            _ = _viewModel.DrillDownAsync(item);
        }
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            FolderListBox.SelectedIndex = -1;
        }
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            _viewModel.IsDragOver.Value = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        // WPF では子要素に入っただけでも DragLeave が発火するため
        // ウィンドウ境界の外に出た場合のみオーバーレイを非表示にする
        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            _viewModel.IsDragOver.Value = false;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        _viewModel.IsDragOver.Value = false;

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        var folder = paths?.FirstOrDefault(System.IO.Directory.Exists);

        if (folder == null)
        {
            _viewModel.StatusText.Value = "フォルダーをドロップしてください";
            return;
        }

        _ = _viewModel.ScanDroppedFolderAsync(folder);
    }
}
