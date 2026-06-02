using Ignore;

namespace CelestiCloud.Core.Filtering;

public class IgnoreFilter
{
    private readonly Ignore.Ignore _ignore;

    public IgnoreFilter(IEnumerable<string> patterns)
    {
        _ignore = new Ignore.Ignore();

        var sanitizedPatterns = patterns
            .Select(p => p.Replace('\\', '/'))
            .ToList();

        foreach (var pattern in sanitizedPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;

            try
            {
                _ignore.Add(pattern);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IgnoreFilter] Dropped invalid ignore pattern '{pattern}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Checks if a file should be ignored based on the patterns.
    /// </summary>
    /// <param name="localRootPath">The base directory being synced (e.g. C:\Docs)</param>
    /// <param name="fullFilePath">The full path of the file (e.g. C:\Docs\Sub\file.txt)</param>
    public bool ShouldIgnore(string localRootPath, string fullFilePath)
    {
        try
        {
            // Gitignore syntax ALWAYS evaluates relative paths using forward slashes.
            string relativePath = System.IO.Path.GetRelativePath(localRootPath, fullFilePath);
            string normalizedPath = relativePath.Replace('\\', '/');

            return _ignore.IsIgnored(normalizedPath);
        }
        catch
        {
            return false;
        }
    }
}
