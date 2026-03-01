namespace SpaceRadar.Utilities;

public static class FileSizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";

        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{(long)size} {Units[unitIndex]}"
            : $"{size:F2} {Units[unitIndex]}";
    }
}
