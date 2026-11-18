using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace OloopApp.Core.Secrets;

/// <summary>
/// The Credential Manager of Windows, through advapi32. An item is a generic credential whose
/// target is <c>service/name</c>; Windows encrypts it with the key of the signed-in user, so
/// another user of the machine cannot read it. It is kept for this machine only (it does not
/// roam with a domain profile).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const uint TypeGeneric = 1; // CRED_TYPE_GENERIC
    private const uint PersistLocalMachine = 2; // CRED_PERSIST_LOCAL_MACHINE
    private const int ErrorNotFound = 1168; // ERROR_NOT_FOUND

    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE: what one credential can hold.</summary>
    private const int MaxBlob = 5 * 512;

    private readonly string _service;

    public WindowsCredentialSecretStore(string service) => _service = service;

    public bool IsPersistent => true;

    public byte[]? Get(string name)
    {
        if (!CredRead(Target(name), TypeGeneric, 0, out var credential))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
                return null;
            throw Failure("read", error);
        }

        try
        {
            var item = Marshal.PtrToStructure<Credential>(credential);
            var bytes = new byte[item.CredentialBlobSize];
            if (bytes.Length > 0)
                Marshal.Copy(item.CredentialBlob, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            CredFree(credential);
        }
    }

    public void Set(string name, byte[] value)
    {
        if (value.Length > MaxBlob)
            throw new IOException($"The Credential Manager keeps at most {MaxBlob} bytes per secret.");

        var blob = Marshal.AllocHGlobal(Math.Max(value.Length, 1));
        try
        {
            Marshal.Copy(value, 0, blob, value.Length);
            var item = new Credential
            {
                Type = TypeGeneric,
                TargetName = Target(name),
                CredentialBlobSize = (uint)value.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = _service,
            };
            if (!CredWrite(ref item, 0))
                throw Failure("write", Marshal.GetLastWin32Error());
        }
        finally
        {
            // The copy in unmanaged memory held the secret: clear it before it is freed.
            Marshal.Copy(new byte[value.Length], 0, blob, value.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string name)
    {
        if (CredDelete(Target(name), TypeGeneric, 0))
            return;

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw Failure("delete", error);
    }

    private string Target(string name) => _service + "/" + name;

    private static IOException Failure(string what, int error)
        => new($"The Credential Manager could not {what} the secret: {new Win32Exception(error).Message} (error {error}).");

    // ---------------- Native ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
