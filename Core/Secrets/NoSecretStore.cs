namespace OloopApp.Core.Secrets;

/// <summary>
/// The store of a system that has no place for secrets yet: it keeps nothing, so the user signs in
/// and unlocks again at every start. Better than a file that anybody could read.
/// </summary>
public sealed class NoSecretStore : ISecretStore
{
    public static NoSecretStore Instance { get; } = new();

    private NoSecretStore()
    {
    }

    public bool IsPersistent => false;

    public byte[]? Get(string name) => null;

    public void Set(string name, byte[] value)
    {
    }

    public void Delete(string name)
    {
    }
}
