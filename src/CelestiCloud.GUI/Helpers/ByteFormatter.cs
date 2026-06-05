namespace CelestiCloud.GUI.Helpers;

public static class ByteFormatter
{
    public static string Format(double bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (bytes >= 1024 && i < suffixes.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return $"{bytes:F1} {suffixes[i]}";
    }
}