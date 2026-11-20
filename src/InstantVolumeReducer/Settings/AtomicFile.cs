using System.Text.Json;

namespace InstantVolumeReducer.Settings;

/// <summary>
/// Crash-safe file writes for the app's JSON files (settings and the saved reduction state).
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Serializes <paramref name="value"/> to a temporary file next to <paramref name="path"/>, which then
    /// replaces it. A crash or full disk while writing can't leave a half-written file behind. Creates
    /// the folder if needed.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file isn't writable.</exception>
    public static void WriteJson<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Deletes the file if it exists; returns <c>false</c> only if it exists and couldn't be deleted.</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
