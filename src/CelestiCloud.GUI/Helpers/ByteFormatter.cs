using System;
using System.Globalization;
using System.Text.RegularExpressions;

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

    public static int ParseToKbps(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;

        // Clean up input (e.g., "1.5 mb" -> "1.5mb")
        string cleanInput = input.Trim().ToLowerInvariant().Replace(" ", "");

        // Regular expression to extract the numeric part and the optional unit
        var match = Regex.Match(cleanInput, @"^([0-9]+(?:\.[0-9]+)?)(kb|mb|gb)?(?:/s|/sec|s)?$");
        if (!match.Success) return 0;

        string numberPart = match.Groups[1].Value;
        string unitPart = match.Groups[2].Value;

        if (!double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedValue))
        {
            return 0;
        }

        // Apply fallback rules if no unit is specified
        if (string.IsNullOrEmpty(unitPart))
        {
            // Rule: Anything with a decimal point (like 1.2) is treated as MB. 
            // Plain integers (like 500) are treated as KB.
            unitPart = numberPart.Contains('.') ? "mb" : "kb";
        }

        double kbpsValue = unitPart switch
        {
            "mb" => parsedValue * 1024,
            "gb" => parsedValue * 1024 * 1024,
            _ => parsedValue // "kb" or default
        };

        return (int)Math.Round(kbpsValue);
    }

    public static string FormatKbps(int kbps)
    {
        if (kbps <= 0) return "Unlimited";

        if (kbps >= 1024 * 1024)
        {
            double gb = (double)kbps / (1024 * 1024);
            return $"{gb:0.#} GB/s";
        }
        if (kbps >= 1024)
        {
            double mb = (double)kbps / 1024;
            return $"{mb:0.#} MB/s";
        }

        return $"{kbps} KB/s";
    }
}