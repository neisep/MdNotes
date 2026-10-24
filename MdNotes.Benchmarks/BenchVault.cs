using MdNotes.Services;
using MdNotes.Tests;

namespace MdNotes.Benchmarks;

/// <summary>
/// A generated vault in a temp folder, of the shape <see cref="VaultShape"/> writes, with an open
/// <see cref="VaultService"/> over it. Writing five thousand notes costs a few seconds, so this belongs
/// in a [GlobalSetup] and is shared by every iteration.
/// </summary>
internal sealed class BenchVault : IDisposable
{
    public string Root { get; }
    public VaultService Service { get; }
    public BenchSettings Settings { get; } = new();

    public BenchVault(int notes, int averageKb = 2, int seed = 1)
    {
        Root = Directory.CreateTempSubdirectory("mdnotes-micro-").FullName;
        VaultShape.Write(Root, new VaultShape.Spec(notes, averageKb, seed));
        Service = new VaultService(Settings);
        Service.Open(Root);
    }

    public void Dispose()
    {
        Service.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
