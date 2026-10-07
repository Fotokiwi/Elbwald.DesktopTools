using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.FileOperations;

public sealed class FileOperationPlannerTests
{
    [Fact]
    public void CreatePlan_ValidMove_IsExecutable()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("source/photo.jpg");
        var destination = directory.GetPath("sorted/photo.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        Assert.True(plan.CanExecute);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(1, plan.MoveCount);
        Assert.Equal(0, plan.CopyCount);

        var operation = Assert.Single(plan.Operations);
        Assert.Equal(Path.GetFullPath(source), operation.SourcePath);
        Assert.Equal(Path.GetFullPath(destination), operation.DestinationPath);
    }

    [Fact]
    public void CreatePlan_ExistingDestination_BlocksExecution()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("source.jpg");
        var destination = directory.CreateFile("destination.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        Assert.False(plan.CanExecute);
        Assert.Contains(
            plan.Conflicts,
            conflict => conflict.Kind
                == FileOperationConflictKind.DestinationAlreadyExists);
    }

    [Fact]
    public void CreatePlan_MissingSource_BlocksExecution()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.GetPath("missing.jpg");
        var destination = directory.GetPath("destination.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        Assert.False(plan.CanExecute);
        Assert.Contains(
            plan.Conflicts,
            conflict => conflict.Kind
                == FileOperationConflictKind.SourceFileMissing);
    }

    [Fact]
    public void CreatePlan_SameSourceAndDestination_BlocksExecution()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("photo.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, source)
        ]);

        Assert.False(plan.CanExecute);
        Assert.Contains(
            plan.Conflicts,
            conflict => conflict.Kind
                == FileOperationConflictKind.SameSourceAndDestination);
    }

    [Fact]
    public void CreatePlan_DuplicateDestination_BlocksExecution()
    {
        using var directory = new TemporaryDirectory();
        var sourceOne = directory.CreateFile("one.jpg");
        var sourceTwo = directory.CreateFile("two.jpg");
        var destination = directory.GetPath("sorted/photo.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(sourceOne, destination),
            FileOperationRequest.Move(sourceTwo, destination)
        ]);

        var conflict = Assert.Single(
            plan.Conflicts.Where(
                item => item.Kind
                    == FileOperationConflictKind.DuplicateDestination));

        Assert.False(plan.CanExecute);
        Assert.Equal(1, conflict.OperationIndex);
        Assert.Equal(0, conflict.RelatedOperationIndex);
    }

    [Fact]
    public void CreatePlan_SourceUsedMultipleTimes_BlocksExecution()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("source.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Copy(
                source,
                directory.GetPath("copy-one.jpg")),
            FileOperationRequest.Copy(
                source,
                directory.GetPath("copy-two.jpg"))
        ]);

        Assert.False(plan.CanExecute);
        Assert.Contains(
            plan.Conflicts,
            conflict => conflict.Kind
                == FileOperationConflictKind.SourceUsedMultipleTimes);
    }

    [Fact]
    public void CreatePlan_DestinationIsAnotherSource_BlocksExecution()
    {
        using var directory = new TemporaryDirectory();
        var sourceOne = directory.CreateFile("one.jpg");
        var sourceTwo = directory.CreateFile("two.jpg");
        var destinationTwo = directory.GetPath("three.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(sourceOne, sourceTwo),
            FileOperationRequest.Move(sourceTwo, destinationTwo)
        ]);

        Assert.False(plan.CanExecute);
        Assert.Contains(
            plan.Conflicts,
            conflict => conflict.Kind
                == FileOperationConflictKind.DestinationIsAnotherSource);
    }

    [Fact]
    public void CreatePlan_InvalidPath_IsReportedAsConflict()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("destination.jpg");
        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Move("\0", destination)
        ]);

        Assert.False(plan.CanExecute);
        Assert.Contains(
            plan.Conflicts,
            conflict => conflict.Kind
                == FileOperationConflictKind.InvalidSourcePath);
    }
}
