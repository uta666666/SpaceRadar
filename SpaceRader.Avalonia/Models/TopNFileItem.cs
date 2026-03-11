namespace SpaceRader.Avalonia.Models;

public class TopNFileItem
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public double BarWidthRatio { get; set; }
}
