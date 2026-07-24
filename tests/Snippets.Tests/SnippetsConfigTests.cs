using Snippets.Core.Config;

namespace Snippets.Tests;

public sealed class SnippetsConfigTests
{
    [Fact]
    public void CreateDefault_matches_readme_storage_conventions()
    {
        var config = SnippetsConfig.CreateDefault(@"C:\Users\Alice", @"C:\Users\Alice\AppData\Local");

        Assert.Equal("snippets-v1", config.Schema);
        Assert.EndsWith(@"Documents\Snippets", config.Workspace.Root);
        Assert.EndsWith(@"Clips\AutoSave", config.Clips.AutoSave);
        Assert.EndsWith(@"Clips\Favorites", config.Clips.Favorites);
        Assert.EndsWith(@"Notes\Drafts", config.Notes.Drafts);
        Assert.Contains(config.Jobs.Items, job => job.Id == "clip-poll" && job.Action.Name == "clip.poll");
        Assert.Contains(config.Jobs.Items, job => job.Id == "clip-prune" && job.Trigger.Type == "startup");
    }
}
