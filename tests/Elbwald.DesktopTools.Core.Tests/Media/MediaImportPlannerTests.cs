using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Core.Media.Importing;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaImportPlannerTests
{
    [Fact]
    public async Task CreatePlanAllowsNewChildDestinationBelowExistingImportRoot()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.RootPath, "source");
        var importRoot = Path.Combine(temp.RootPath, "import");
        var destination = Path.Combine(importRoot, "2026-10-11 - Urlaub");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(importRoot);
        await File.WriteAllTextAsync(Path.Combine(source, "bild.jpg"), "test");

        var planner = new MediaImportPlanner();
        var plan = await planner.CreatePlanAsync(source, destination);

        Assert.True(plan.IsComplete);
        Assert.Equal(1, plan.CopyCount);
        Assert.Equal(destination, plan.DestinationRoot);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(
            MediaImportPlanItemState.ReadyToCopy,
            Assert.Single(plan.Items).State);
    }

    [Fact]
    public async Task CreatePlanBlocksMissingDestinationWhenParentIsMissing()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.RootPath, "source");
        var destination = Path.Combine(temp.RootPath, "missing-root", "child");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "bild.jpg"), "test");

        var planner = new MediaImportPlanner();
        var plan = await planner.CreatePlanAsync(source, destination);

        Assert.False(plan.IsComplete);
        Assert.NotEmpty(plan.Issues);
    }
    [Fact]
    public async Task CreatePlanBlocksDestinationThatIsAFile()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.RootPath, "source");
        var destination = Path.Combine(temp.RootPath, "import-target");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "bild.jpg"), "test");
        await File.WriteAllTextAsync(destination, "not-a-directory");

        var planner = new MediaImportPlanner();
        var plan = await planner.CreatePlanAsync(source, destination);

        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Issues, issue =>
            issue.Message.Contains("Ordner", StringComparison.OrdinalIgnoreCase));
    }

}
