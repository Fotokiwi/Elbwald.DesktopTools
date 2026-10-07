using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.FileOperations;

public sealed class FileOperationSafetyCheckerTests
{
    [Fact]
    public void Check_CopyWithImpossibleReserve_IsBlocked()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "test-data");

        var destination = directory.GetPath(
            "copy/source.bin");

        var planner = new FileOperationPlanner();

        var options = new FileOperationSafetyOptions
        {
            MinimumFreeSpaceReserveBytes = long.MaxValue / 4,
            FreeSpaceReserveRatio = 0,
            MaximumRatioReserveBytes = 0
        };

        var checker = new FileOperationSafetyChecker(options);

        var plan = planner.CreatePlan([
            FileOperationRequest.Copy(source, destination)
        ]);

        var result = checker.Check(plan);

        Assert.False(result.IsSafe);
        Assert.Contains(
            result.Issues,
            issue => issue.Kind
                == FileOperationSafetyIssueKind.InsufficientFreeSpace);
    }

    [Fact]
    public void CalculateReserveBytes_UsesMinimumReserve()
    {
        var options = new FileOperationSafetyOptions
        {
            MinimumFreeSpaceReserveBytes = 1024,
            FreeSpaceReserveRatio = 0.01,
            MaximumRatioReserveBytes = 10_000
        };

        Assert.Equal(
            1024,
            options.CalculateReserveBytes(1000));
    }

    [Fact]
    public void CalculateReserveBytes_CapsRatioReserve()
    {
        var options = new FileOperationSafetyOptions
        {
            MinimumFreeSpaceReserveBytes = 100,
            FreeSpaceReserveRatio = 0.50,
            MaximumRatioReserveBytes = 1000
        };

        Assert.Equal(
            1000,
            options.CalculateReserveBytes(100_000));
    }
}
