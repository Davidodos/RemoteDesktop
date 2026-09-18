namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Ob ffmpeg da ist — beim Start gefragt, damit <c>h264</c> nur dann unter den
/// Fähigkeiten steht, wenn es auch geliefert werden kann. Vorher versuchte die
/// App es bei jedem Rechner und meldete danach einen Rückfall, den niemand
/// angefordert hatte.
/// </summary>
public static class FfmpegLocator
{
    public static bool IsAvailable(string configuredPath) =>
        IsAvailable(configuredPath, Environment.GetEnvironmentVariable("PATH"));

    /// <param name="searchPath">
    /// Der Suchpfad, in dem ein nackter Name wie <c>ffmpeg</c> gesucht wird.
    /// </param>
    public static bool IsAvailable(string configuredPath, string? searchPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        if (Path.IsPathRooted(configuredPath) || configuredPath.Contains(Path.DirectorySeparatorChar))
        {
            return File.Exists(configuredPath);
        }

        var names = configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? [configuredPath]
            : new[] { configuredPath, configuredPath + ".exe" };

        foreach (var folder in (searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    if (File.Exists(Path.Combine(folder, name)))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // Ein unbrauchbarer Eintrag im Suchpfad ist nicht unser Problem.
                }
            }
        }

        return false;
    }
}
