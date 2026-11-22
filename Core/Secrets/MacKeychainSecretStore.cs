using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace OloopApp.Core.Secrets;

/// <summary>
/// The login keychain of macOS, through Security.framework. The <c>security</c> tool is not used
/// on purpose: it takes the secret as an argument, and every process of the user can read the
/// arguments of the others. An item is a generic password of one service (the app) and one account
/// (the name of the secret); only the app that made it may read it without asking.
///
/// A build that is not signed has no stable identity, so macOS may ask once after each new build
/// whether the app may use the item. That is the keychain doing its job.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int Success = 0;
    private const int ItemNotFound = -25300; // errSecItemNotFound
    private const int DuplicateItem = -25299; // errSecDuplicateItem
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    private readonly string _service;

    public MacKeychainSecretStore(string service) => _service = service;

    public bool IsPersistent => true;

    public byte[]? Get(string name)
    {
        using var refs = new Refs();
        var query = refs.Query(_service, name);
        refs.Set(query, Constant("kSecReturnData"), Constant("kCFBooleanTrue"));
        refs.Set(query, Constant("kSecMatchLimit"), Constant("kSecMatchLimitOne"));

        var status = SecItemCopyMatching(query, out var result);
        if (status == ItemNotFound)
            return null;
        if (status != Success)
            throw Failure("read", status);

        try
        {
            var length = (int)CFDataGetLength(result);
            var bytes = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, length);
            return bytes;
        }
        finally
        {
            CFRelease(result);
        }
    }

    public void Set(string name, byte[] value)
    {
        using var refs = new Refs();
        var data = refs.Data(value);

        var add = refs.Query(_service, name);
        refs.Set(add, Constant("kSecValueData"), data);
        var status = SecItemAdd(add, IntPtr.Zero);
        if (status == Success)
            return;
        if (status != DuplicateItem)
            throw Failure("write", status);

        var change = refs.NewDictionary();
        refs.Set(change, Constant("kSecValueData"), data);
        status = SecItemUpdate(refs.Query(_service, name), change);
        if (status != Success)
            throw Failure("update", status);
    }

    public void Delete(string name)
    {
        using var refs = new Refs();
        var status = SecItemDelete(refs.Query(_service, name));
        if (status != Success && status != ItemNotFound)
            throw Failure("delete", status);
    }

    private static IOException Failure(string what, int status)
        => new($"The keychain could not {what} the secret (status {status}).");

    // ---------------- Constants of the frameworks ----------------

    // The names of the dictionary keys are not numbers but symbols of the library: a pointer to a
    // CFString that the framework owns. They are read once and never released.
    private static readonly Dictionary<string, IntPtr> Constants = [];

    private static IntPtr Constant(string symbol)
    {
        lock (Constants)
        {
            if (!Constants.TryGetValue(symbol, out var value))
            {
                var library = NativeLibrary.Load(symbol.StartsWith("kCF", StringComparison.Ordinal) ? CoreFoundation : Security);
                value = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, symbol));
                Constants[symbol] = value;
            }

            return value;
        }
    }

    // ---------------- Objects that have to be released ----------------

    /// <summary>The Core Foundation objects of one call; released together.</summary>
    private sealed class Refs : IDisposable
    {
        private readonly List<IntPtr> _owned = [];

        public IntPtr NewDictionary()
        {
            var core = NativeLibrary.Load(CoreFoundation);
            var dictionary = CFDictionaryCreateMutable(
                IntPtr.Zero, 0,
                NativeLibrary.GetExport(core, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(core, "kCFTypeDictionaryValueCallBacks"));
            return Own(dictionary);
        }

        /// <summary>A query for the item of this service and account.</summary>
        public IntPtr Query(string service, string account)
        {
            var query = NewDictionary();
            Set(query, Constant("kSecClass"), Constant("kSecClassGenericPassword"));
            Set(query, Constant("kSecAttrService"), Text(service));
            Set(query, Constant("kSecAttrAccount"), Text(account));
            return query;
        }

        public IntPtr Text(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return Own(CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, Utf8, false));
        }

        public IntPtr Data(byte[] bytes) => Own(CFDataCreate(IntPtr.Zero, bytes, bytes.Length));

        public void Set(IntPtr dictionary, IntPtr key, IntPtr value) => CFDictionarySetValue(dictionary, key, value);

        private IntPtr Own(IntPtr reference)
        {
            if (reference == IntPtr.Zero)
                throw new IOException("The keychain could not be prepared (out of memory?).");

            _owned.Add(reference);
            return reference;
        }

        public void Dispose()
        {
            foreach (var reference in _owned)
                CFRelease(reference);
        }
    }

    // ---------------- Native functions ----------------

    [DllImport(Security)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(Security)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(Security)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(Security)]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundation)]
    private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte[] bytes, nint count, uint encoding, [MarshalAs(UnmanagedType.I1)] bool isExternalRepresentation);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
