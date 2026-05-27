using Ignore;

namespace CelestiCloud.Core.Filtering;

public class IgnoreFilter
{
    private readonly Ignore.Ignore _ignore;

    public IgnoreFilter(IEnumerable<string> patterns)
    {
        _ignore = new Ignore.Ignore();
        _ignore.Add(patterns);
    }

    /// <summary>
    /// Checks if a file should be ignored based on the patterns.
    /// </summary>
    /// <param name="localRootPath">The base directory being synced (e.g. C:\Docs)</param>
    /// <param name="fullFilePath">The full path of the file (e.g. C:\Docs\Sub\file.txt)</param>
    public bool ShouldIgnore(string localRootPath, string fullFilePath)
    {
        // Gitignore syntax ALWAYS evaluates relative paths, using forward slashes.
        string relativePath = Path.GetRelativePath(localRootPath, fullFilePath);
        string normalizedPath = relativePath.Replace('\\', '/');

        return _ignore.IsIgnored(normalizedPath);
    }
}
