using System.Security.Cryptography;

namespace PCPulse.Core.Security;

/// <summary>
/// Проверка контрольных сумм SHA-256 для загруженных пакетов и установщиков.
/// </summary>
public static class Sha256Verifier
{
    /// <summary>
    /// Извлекает 64-символьную шестнадцатеричную строку SHA-256 из файла <c>.sha256</c> (формат <c>&lt;hash&gt;</c> или <c>&lt;hash&gt;  &lt;filename&gt;</c>).
    /// </summary>
    public static string? TryParseExpectedHash(string? sha256Content)
    {
        if (string.IsNullOrWhiteSpace(sha256Content))
            return null;

        var firstLine = sha256Content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        var token = firstLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (token.Length != 64)
            return null;

        foreach (var ch in token)
        {
            if (!Uri.IsHexDigit(ch))
                return null;
        }

        return token.ToLowerInvariant();
    }

    public static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> VerifyFileAsync(
        string filePath,
        string sha256Content,
        CancellationToken cancellationToken = default)
    {
        var expected = TryParseExpectedHash(sha256Content);
        if (expected is null || !File.Exists(filePath))
            return false;

        var actual = await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual),
            System.Text.Encoding.ASCII.GetBytes(expected));
    }
}
