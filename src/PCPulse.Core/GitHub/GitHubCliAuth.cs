using System.Diagnostics;
using System.Net.Http.Headers;

namespace PCPulse.Core.GitHub;

/// <summary>Reads an existing GitHub CLI or Git credential without storing credentials in PCPulse.</summary>
public static class GitHubCliAuth
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);

    public static async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var environmentToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (string.IsNullOrWhiteSpace(environmentToken))
            environmentToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
            return environmentToken.Trim();

        var ghToken = await RunCommandAsync("gh", ["auth", "token"], null, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(ghToken))
            return ghToken.Trim();

        var credential = await RunCommandAsync(
            "git",
            ["credential", "fill"],
            "protocol=https\nhost=github.com\n\n",
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credential))
            return null;

        foreach (var line in credential.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("password=", StringComparison.Ordinal))
                return line["password=".Length..].Trim();
        }

        return null;
    }

    internal static HttpRequestMessage CreateApiRequest(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        return request;
    }

    private static async Task<string?> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (standardInput is not null)
        {
            process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            process.StartInfo.Environment["GCM_INTERACTIVE"] = "never";
        }

        try
        {
            if (!process.Start())
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
