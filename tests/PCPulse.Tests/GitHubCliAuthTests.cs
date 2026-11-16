using PCPulse.Core.GitHub;
using Xunit;

namespace PCPulse.Tests;

public sealed class GitHubCliAuthTests
{
    [Fact]
    public void CreateApiRequest_AttachesTokenOnlyToGitHubApi()
    {
        using var apiRequest = GitHubCliAuth.CreateApiRequest("https://api.github.com/repos/owner/repo/releases", " test-token ");
        using var assetRequest = GitHubCliAuth.CreateApiRequest("https://github.com/owner/repo/releases/download/v1/file.zip", "test-token");
        using var otherHostRequest = GitHubCliAuth.CreateApiRequest("https://example.test/releases", "test-token");

        Assert.Equal("Bearer", apiRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", apiRequest.Headers.Authorization?.Parameter);
        Assert.Null(assetRequest.Headers.Authorization);
        Assert.Null(otherHostRequest.Headers.Authorization);
    }

    [Fact]
    public void CreateApiRequest_DoesNotAttachTokenToHttpOrLookalikeHost()
    {
        using var httpRequest = GitHubCliAuth.CreateApiRequest("http://api.github.com/repos/owner/repo", "test-token");
        using var lookalikeRequest = GitHubCliAuth.CreateApiRequest("https://api.github.com.example.test/repos/owner/repo", "test-token");

        Assert.Null(httpRequest.Headers.Authorization);
        Assert.Null(lookalikeRequest.Headers.Authorization);
    }
}
