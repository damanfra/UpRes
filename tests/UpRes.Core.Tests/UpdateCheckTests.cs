using UpRes.Core;
using Xunit;

namespace UpRes.Core.Tests;

public class UpdateCheckTests
{
    private const string Sha = "97d6d17a909bb236197ae2801468629ad1da2b40";

    private static ReleaseInfo Release(string tag, string commit = Sha) =>
        new ReleaseInfo { Tag = tag, HtmlUrl = "https://github.com/damanfra/UpRes/releases/tag/" + tag, TargetCommitish = commit, PublishedAt = "2026-10-13T20:00:00Z" };

    [Fact]
    public void Parses_github_release_json_ignoring_extra_fields()
    {
        const string json = @"{""url"":""x"",""html_url"":""https://github.com/damanfra/UpRes/releases/tag/v0.3.0.5"",
            ""tag_name"":""v0.3.0.5"",""target_commitish"":""" + Sha + @""",""name"":""UpRes v0.3.0.5"",
            ""author"":{""login"":""github-actions[bot]"",""html_url"":""https://github.com/apps/github-actions""},
            ""published_at"":""2026-10-13T20:00:00Z"",""assets"":[{""name"":""UpRes.zip"",""size"":123}],""body"":""Commit: x""}";
        var r = UpdateCheck.ParseRelease(json);
        Assert.Equal("v0.3.0.5", r.Tag);
        Assert.Equal("https://github.com/damanfra/UpRes/releases/tag/v0.3.0.5", r.HtmlUrl);
        Assert.Equal(Sha, r.TargetCommitish);
    }

    [Fact]
    public void Same_commit_is_up_to_date_even_for_local_build()
        => Assert.Equal(UpdateStatus.UpToDate, UpdateCheck.Compare("0.3.0.0+97d6d17", Release("v0.3.0.5")).Status);

    [Fact]
    public void Same_commit_but_dirty_is_up_to_date_with_note()
    {
        var r = UpdateCheck.Compare("0.3.0.0+97d6d17-dirty", Release("v0.3.0.5"));
        Assert.Equal(UpdateStatus.UpToDate, r.Status);
        Assert.Contains("alterações locais", r.Message);
    }

    [Theory]
    [InlineData("0.3.0.4+abcdef1", "v0.3.0.5", UpdateStatus.UpdateAvailable)]
    [InlineData("0.2.0+b7cc87f", "v0.3.0.1", UpdateStatus.UpdateAvailable)]   // formato antigo (sem build number)
    [InlineData("0.3.0.9+abcdef1", "v0.3.0.5", UpdateStatus.LocalIsNewer)]
    [InlineData("0.4.0.0+abcdef1", "v0.3.0.50", UpdateStatus.LocalIsNewer)]
    [InlineData("0.3.0.5+abcdef1", "v0.3.0.5", UpdateStatus.UpToDate)]
    [InlineData("0.3.0.0+sem-git", "v0.3.0.5", UpdateStatus.UpdateAvailable)]
    [InlineData("lixo", "v0.3.0.5", UpdateStatus.Unknown)]
    [InlineData("0.3.0.0+abcdef1", "release-final", UpdateStatus.Unknown)]
    public void Compares_versions(string local, string tag, UpdateStatus expected)
        => Assert.Equal(expected, UpdateCheck.Compare(local, Release(tag)).Status);

    [Fact]
    public void Current_build_version_is_parseable()
        => Assert.True(UpdateCheck.TryParseLocal(BuildInfo.Version, out _, out _, out _));
}
