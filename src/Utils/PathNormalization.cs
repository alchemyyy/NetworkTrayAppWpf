using System.IO;

namespace NetworkTrayAppWPF.Utils;

/// <summary>
/// Shared path canonicalization for install, shortcut, and running-exe comparisons.
/// </summary>
public static class PathNormalization
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }
}
