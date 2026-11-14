using System.IO.Compression;

namespace PCPulse.Core.Security;

/// <summary>
/// Безопасная распаковка ZIP-архивов модулей с защитой от ZipSlip и символических ссылок.
/// </summary>
public static class ZipSafeExtractor
{
    public static async Task ExtractToDirectoryAsync(
        string zipPath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullDestination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestination);

        await using var archive = await ZipFile.OpenReadAsync(zipPath, cancellationToken).ConfigureAwait(false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.FullName))
                continue;

            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var cleanRelative = entry.FullName.TrimEnd('/', '\\');
            if (string.IsNullOrEmpty(cleanRelative))
                continue;

            var targetPath = PathGuard.ResolveSafeChildPath(fullDestination, cleanRelative);
            if (isDirectory)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            await using var sourceStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var targetStream = new FileStream(
                targetPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
        }
    }
}
