using Snippets.Core.Config;
using Snippets.Core.Jobs;

namespace Snippets.Tests;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_executes_registered_tool_action()
    {
        var registry = new ToolRegistry();
        registry.Register("clip.prune", (args, _) => Task.FromResult<string?>($"max={args["maxAutoSave"]}"));
        var runner = new JobRunner(registry);
        var job = JobConfig.CreateTool(
            "clip-prune",
            "prune clips",
            JobTriggerConfig.Startup(),
            "clip.prune",
            new Dictionary<string, string> { ["maxAutoSave"] = "100" });

        var result = await runner.RunOnceAsync(job);

        Assert.True(result.Succeeded);
        Assert.Equal("max=100", result.Output);
    }
}
