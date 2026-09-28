using Snet.Yolo.Server;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class TrainingStoragePathTests
{
    [Theory]
    [InlineData("yolo")]
    [InlineData("anomalib")]
    public void OwnerDirectory_UsesUsernameDirectlyUnderAlgorithm(string algorithm)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "train", algorithm);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "snet")), TrainingStoragePath.OwnerDirectory(root, "snet"));
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("weights")]
    [InlineData("STATUSES")]
    [InlineData("scripts")]
    [InlineData("../outside")]
    public void OwnerDirectory_DoesNotUseSharedNamesOrEscapeRoot(string owner)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "train", "yolo"));
        var directory = TrainingStoragePath.OwnerDirectory(root, owner);
        Assert.Equal(root, Path.GetDirectoryName(directory));
        Assert.NotEqual(owner.ToLowerInvariant(), Path.GetFileName(directory));
        Assert.Equal(directory, TrainingStoragePath.OwnerDirectory(root, owner));
    }

    [Fact]
    public void AnomalibProjectRoot_HasNoUsersLayer()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "train", "anomalib", "snet", "p1")),
            AnomalibModelRegistry.ProjectRoot("snet", "p1"));
    }

    [Fact]
    public void CudaLibraries_FindAnomalibEnvironmentWithoutYoloEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-training-path-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "train", "anomalib", ".env", "lib", "python3.13", "site-packages", "nvidia", "cudnn", "lib");
        try
        {
            Directory.CreateDirectory(library);
            Assert.Contains(library, CudaRuntimeLibraries.VirtualEnvironmentLibraryDirectories(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
