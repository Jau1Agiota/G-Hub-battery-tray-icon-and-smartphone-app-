using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

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
            bool software = Native.ReadText(provider, MetadataProperty.ProviderName) == ProviderName;
            bool algorithm = Native.ReadText(handle, MetadataProperty.KeyAlgorithmName) == "ECDSA_P256" &&
                Native.ReadText(handle, MetadataProperty.KeyAlgorithmGroup) == "ECDSA";
            var ui = Native.ReadProperty(handle, MetadataProperty.KeyUiPolicy);
            if (!InteropContracts.ValidUiPolicyShape(ui, IntPtr.Size)) throw new MetadataReadFailure(MetadataProperty.KeyUiPolicy, MetadataReadError.MalformedValue);
            bool noUi = InteropContracts.NoUiFlags(ui, IntPtr.Size);
            // There is no documented universal IsEphemeral BOOL. Require successful empty Name readback,
            // together with NULL-name creation. Unsupported/not-found metadata never substitutes for evidence.
            bool unnamed = Native.ReadText(handle, MetadataProperty.KeyName).Length == 0;
            return new(software, algorithm, MetadataProperties.ParseBitLength(Native.ReadDword(handle, MetadataProperty.KeyLength)),
                Native.ReadDword(handle, MetadataProperty.KeyExportPolicy), Native.ReadDword(handle, MetadataProperty.KeyType) == 0,
                unnamed, noUi, Native.ReadDword(handle, MetadataProperty.KeyUsage) == 2);
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
    internal static unsafe byte[] ReadProperty(NCryptHandle handle, MetadataProperty property)
    {
        try {
            var output = new byte[512];
            int length, code;
            fixed (byte* pointer = output)
                code = GetProperty(handle, MetadataProperties.Describe(property).WireName, (IntPtr)pointer, output.Length, out length, 0);
            if (code != 0) throw new MetadataReadFailure(property, MetadataReadError.NativeStatus, code);
            if (length < 0 || length > output.Length) throw new MetadataReadFailure(property, MetadataReadError.MalformedValue);
            return output[..length];
        } catch (MetadataReadFailure) { throw; }
        catch { throw new MetadataReadFailure(property, MetadataReadError.UnexpectedException); }
    }
    internal static uint ReadDword(NCryptHandle handle, MetadataProperty property) =>
        MetadataProperties.ParseDword(property, ReadProperty(handle, property));
    internal static string ReadText(NCryptHandle handle, MetadataProperty property) =>
        MetadataProperties.ParseText(property, ReadProperty(handle, property));
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
