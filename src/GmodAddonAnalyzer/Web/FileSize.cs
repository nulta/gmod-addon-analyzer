namespace GmodAddonAnalyzer.Web;

public static class FileSize
{
    public static string Humanize(long bytes)
    {
        return bytes switch
        {
            < 0 => "0 B",
            0 => "0 B",
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }
}