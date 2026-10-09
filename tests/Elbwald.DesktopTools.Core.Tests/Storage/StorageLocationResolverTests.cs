using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Storage;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Storage;

public sealed class StorageLocationResolverTests
{
    [Fact]
    public void CaptureStoresStableVolumeIdAndRelativePath()
    {
        using var temp = new TemporaryDirectory();
        var mount = Path.Combine(temp.RootPath, "volume");
        var folder = Path.Combine(mount, "Import", "Waiting");
        Directory.CreateDirectory(folder);

        var provider = new FakeStorageVolumeProvider(
            new StorageVolumeInfo("volume-123", mount));
        var resolver = new StorageLocationResolver(provider);

        var location = resolver.Capture(folder);

        Assert.Equal("volume-123", location.VolumeId);
        Assert.Equal("Import/Waiting", location.RelativePath);
        Assert.Equal(Path.GetFullPath(mount), Path.GetFullPath(location.LastKnownMountPath!));
    }

    [Fact]
    public void ResolveUsesCurrentMountPathForSameVolumeId()
    {
        using var temp = new TemporaryDirectory();
        var currentMount = Path.Combine(temp.RootPath, "new-mount");
        var library = Path.Combine(currentMount, "Photos");
        Directory.CreateDirectory(library);

        var provider = new FakeStorageVolumeProvider(
            new StorageVolumeInfo("stable-id", currentMount));
        var resolver = new StorageLocationResolver(provider);
        var location = new StorageLocation(
            "stable-id",
            "Photos",
            LastKnownMountPath: Path.Combine(temp.RootPath, "old-mount"),
            LastKnownAbsolutePath: Path.Combine(temp.RootPath, "old-mount", "Photos"));

        var resolution = resolver.Resolve(location);

        Assert.Equal(StorageLocationStatus.Available, resolution.Status);
        Assert.Equal(Path.GetFullPath(library), Path.GetFullPath(resolution.ResolvedPath!));
    }

    [Fact]
    public void ResolveDoesNotTrustExistingLastKnownPathWhenVolumeIsMissing()
    {
        using var temp = new TemporaryDirectory();
        var misleadingPath = Path.Combine(temp.RootPath, "Photos");
        Directory.CreateDirectory(misleadingPath);

        var resolver = new StorageLocationResolver(
            new FakeStorageVolumeProvider());
        var location = new StorageLocation(
            "missing-volume",
            "Photos",
            LastKnownMountPath: temp.RootPath,
            LastKnownAbsolutePath: misleadingPath);

        var resolution = resolver.Resolve(location);

        Assert.Equal(StorageLocationStatus.VolumeUnavailable, resolution.Status);
        Assert.Null(resolution.ResolvedPath);
    }

    [Fact]
    public void ResolveFailsClosedWhenVolumeIdIsAmbiguous()
    {
        using var temp = new TemporaryDirectory();
        var mountA = Path.Combine(temp.RootPath, "a");
        var mountB = Path.Combine(temp.RootPath, "b");
        Directory.CreateDirectory(mountA);
        Directory.CreateDirectory(mountB);

        var provider = new FakeStorageVolumeProvider(
            new StorageVolumeInfo("duplicate", mountA),
            new StorageVolumeInfo("duplicate", mountB));
        var resolver = new StorageLocationResolver(provider);

        var resolution = resolver.Resolve(
            new StorageLocation("duplicate", string.Empty));

        Assert.Equal(StorageLocationStatus.AmbiguousVolume, resolution.Status);
        Assert.Null(resolution.ResolvedPath);
    }

    [Fact]
    public void ResolveRejectsTraversalInStoredRelativePath()
    {
        using var temp = new TemporaryDirectory();
        var mount = Path.Combine(temp.RootPath, "volume");
        Directory.CreateDirectory(mount);

        var provider = new FakeStorageVolumeProvider(
            new StorageVolumeInfo("stable", mount));
        var resolver = new StorageLocationResolver(provider);

        var resolution = resolver.Resolve(
            new StorageLocation("stable", "../outside"));

        Assert.Equal(StorageLocationStatus.InvalidConfiguration, resolution.Status);
        Assert.Null(resolution.ResolvedPath);
    }

    private sealed class FakeStorageVolumeProvider
        : IStorageVolumeProvider
    {
        private readonly IReadOnlyList<StorageVolumeInfo> _volumes;

        public FakeStorageVolumeProvider(params StorageVolumeInfo[] volumes)
        {
            _volumes = volumes;
        }

        public StorageVolumeInfo? FindVolumeForPath(string absolutePath)
        {
            var fullPath = Path.GetFullPath(absolutePath);

            return _volumes
                .Where(volume =>
                {
                    var mount = Path.GetFullPath(volume.MountPath);
                    return fullPath == mount
                           || fullPath.StartsWith(
                               mount.TrimEnd(Path.DirectorySeparatorChar)
                               + Path.DirectorySeparatorChar,
                               OperatingSystem.IsWindows()
                                   ? StringComparison.OrdinalIgnoreCase
                                   : StringComparison.Ordinal);
                })
                .OrderByDescending(volume => volume.MountPath.Length)
                .FirstOrDefault();
        }

        public IReadOnlyList<StorageVolumeInfo> GetAvailableVolumes()
        {
            return _volumes;
        }
    }
}
