using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace PeripheralBatteryCompanion.EphemeralCapability;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCapabilityApi : ICapabilityApi
{
    internal const string ProviderName = "Microsoft Software Key Storage Provider";
    public bool IsOrdinaryUnelevatedUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return Environment.UserInteractive && identity.User is not null && identity.IsAuthenticated &&
            !identity.IsSystem && !identity.IsAnonymous && !identity.IsGuest &&
            !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public IProvider OpenSoftwareProvider()
    {
        int code = Native.OpenProvider(out var handle, ProviderName, 0);
        Native.CheckCreated(code, handle, Phase.ReleaseProvider);
        return new Provider(handle);
    }

    private sealed class Provider(NCryptHandle handle) : IProvider
    {
        public bool? ReleaseSucceeded => handle.ReleaseSucceeded;
        public int? ReleaseHResult => handle.ReleaseHResult;
        public void Dispose() => handle.Dispose();
        public IKey CreateUnnamedP256()
        {
            // NULL name, user flags zero, no overwrite or machine flags; never call NCryptOpenKey/DeleteKey.
            int code = Native.CreateKey(handle, out var key, "ECDSA_P256", null, 0, 0);
            Native.CheckCreated(code, key, Phase.ReleaseKey);
            return new Key(key, handle);
        }
    }
    private sealed class Key(NCryptHandle handle, NCryptHandle provider) : IKey
    {
        private bool configured, finalized;
        public bool? ReleaseSucceeded => handle.ReleaseSucceeded;
        public int? ReleaseHResult => handle.ReleaseHResult;
        public void Dispose() => handle.Dispose();
        public unsafe void ConfigureExportableSigningOnly()
        {
            if (configured || finalized) throw new InvalidOperationException();
            Native.SetDword(handle, "Export Policy", 3); // AllowExport | AllowPlaintextExport, no archiving.
            Native.SetDword(handle, "Key Usage", 2); // signing only
            var ui = new NativeUiPolicy { Version = 1, Flags = 0 }; // no strong-key UI requested
            Native.Check(Native.SetProperty(handle, "UI Policy", (IntPtr)(&ui), sizeof(NativeUiPolicy), 0));
            configured = true;
        }
        public void FinalizeOnce()
        {
            if (!configured || finalized) throw new InvalidOperationException();
            finalized = true; // no retry even if native finalization fails
            Native.Check(Native.FinalizeKey(handle, 0));
        }
        public KeyMetadata ReadMetadata()
        {
            if (!finalized) throw new InvalidOperationException();
            bool software = Native.ReadText(provider, "Name") == ProviderName;
            bool algorithm = Native.ReadText(handle, "Algorithm Name") == "ECDSA_P256" &&
                Native.ReadText(handle, "Algorithm Group") == "ECDSA";
            var ui = Native.ReadProperty(handle, "UI Policy");
            if (!InteropContracts.ValidUiPolicyShape(ui, IntPtr.Size)) throw new InvalidOperationException();
            bool noUi = InteropContracts.NoUiFlags(ui, IntPtr.Size);
            // There is no documented universal IsEphemeral BOOL. Require successful empty Name readback,
            // together with NULL-name creation. Unsupported/not-found metadata never substitutes for evidence.
            bool unnamed = Native.ReadText(handle, "Name").Length == 0;
            return new(software, algorithm, checked((int)Native.ReadDword(handle, "Length")),
                Native.ReadDword(handle, "Export Policy"), Native.ReadDword(handle, "Key Type") == 0,
                unnamed, noUi, Native.ReadDword(handle, "Key Usage") == 2);
        }
        public int QueryEccPrivateSize()
        {
            Native.Check(Native.ExportKey(handle, IntPtr.Zero, "ECCPRIVATEBLOB", IntPtr.Zero,
                IntPtr.Zero, 0, out int length, 0));
            return length;
        }
        public unsafe int FillEccPrivate(Span<byte> output)
        {
            // Pin caller span directly. No byte-array marshaling/copies, wrapping key, parameter list, fallback or flags.
            fixed (byte* pointer = output) {
                Native.Check(Native.ExportKey(handle, IntPtr.Zero, "ECCPRIVATEBLOB", IntPtr.Zero,
                    (IntPtr)pointer, output.Length, out int length, 0));
                return length;
            }
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class NCryptHandle : CheckedReleaseHandle
{
    public NCryptHandle() { }
    protected override int ReleaseNativeHandle(IntPtr value) => Native.FreeObject(value);
}

[SupportedOSPlatform("windows")]
internal static class Native
{
    internal static void Check(int code) { if (code != 0) throw new NativeFailure(code); }
    internal static void CheckCreated(int code, NCryptHandle handle, Phase releasePhase)
    {
        if (InteropContracts.Acquisition(code, !handle.IsInvalid) == AcquisitionDecision.AcceptNewHandle) return;
        bool hadHandle = !handle.IsInvalid;
        handle.Dispose();
        if (hadHandle) throw new AcquisitionFailure(code, releasePhase, handle.ReleaseSucceeded == true, handle.ReleaseHResult);
        if (code == 0) throw new InvalidOperationException();
        throw new NativeFailure(code);
    }
    internal static unsafe void SetDword(NCryptHandle key, string property, uint value) =>
        Check(SetProperty(key, property, (IntPtr)(&value), sizeof(uint), 0));
    // Public metadata only, fixed bound; never retrieve unique/key-file names or copy any private blob here.
    internal static unsafe byte[] ReadProperty(NCryptHandle handle, string property)
    {
        var output = new byte[512];
        int length;
        fixed (byte* pointer = output) Check(GetProperty(handle, property, (IntPtr)pointer, output.Length, out length, 0));
        if (length < 0 || length > output.Length) throw new InvalidOperationException();
        return output[..length];
    }
    internal static uint ReadDword(NCryptHandle handle, string property)
    {
        var bytes = ReadProperty(handle, property);
        if (bytes.Length != 4) throw new InvalidOperationException();
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
    internal static string ReadText(NCryptHandle handle, string property)
    {
        var bytes = ReadProperty(handle, property);
        if (bytes.Length == 0) return "";
        if (bytes.Length < 2 || bytes.Length % 2 != 0 || bytes[^1] != 0 || bytes[^2] != 0) throw new InvalidOperationException();
        return Encoding.Unicode.GetString(bytes, 0, bytes.Length - 2);
    }
    [DllImport("ncrypt.dll", EntryPoint = "NCryptOpenStorageProvider", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int OpenProvider(out NCryptHandle provider, string name, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptCreatePersistedKey", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int CreateKey(NCryptHandle provider, out NCryptHandle key, string algorithm, string? name, int legacySpec, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptSetProperty", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SetProperty(NCryptHandle handle, string property, IntPtr input, int length, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptGetProperty", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetProperty(NCryptHandle handle, string property, IntPtr output, int capacity, out int length, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptFinalizeKey")]
    internal static extern int FinalizeKey(NCryptHandle key, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptExportKey", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int ExportKey(NCryptHandle key, IntPtr wrappingKey, string blobType, IntPtr parameters,
        IntPtr output, int capacity, out int length, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptFreeObject")]
    internal static extern int FreeObject(IntPtr handle);
}
