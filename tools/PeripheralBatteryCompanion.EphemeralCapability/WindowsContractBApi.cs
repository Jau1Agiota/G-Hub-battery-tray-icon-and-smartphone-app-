using System.Runtime.Versioning;

namespace PeripheralBatteryCompanion.EphemeralCapability;

[SupportedOSPlatform("windows")]
internal static class WindowsContractBApi
{
    internal static IBApi Create() => new ContractBNativeFlow(new Calls());
    private sealed class Resource(NCryptHandle handle) : IResource
    {
        internal NCryptHandle Handle => handle;
        public bool? ReleaseSucceeded => handle.ReleaseSucceeded;
        public int? ReleaseHResult => handle.ReleaseHResult;
        public void Dispose() => handle.Dispose();
    }
    private sealed class Calls : IBNativeCalls
    {
        private static NCryptHandle Handle(IResource resource) => ((Resource)resource).Handle;
        public bool IsOrdinaryUnelevatedUser() => new WindowsCapabilityApi().IsOrdinaryUnelevatedUser();
        public IResource OpenProvider(string name, int flags)
        {
            if (name != BConstruction.WindowsProvider || flags != 0) throw new InvalidOperationException();
            int code = Native.OpenProvider(out var handle, name, flags);
            Native.CheckCreated(code, handle, Phase.ReleaseProvider);
            return new Resource(handle);
        }
        public IResource CreateKey(IResource provider, string algorithm, string? name, int legacySpec, int flags)
        {
            if (algorithm != "ECDSA_P256" || name is not null || legacySpec != 0 || flags != 0) throw new InvalidOperationException();
            int code = Native.CreateKey(Handle(provider), out var key, algorithm, name, legacySpec, flags);
            Native.CheckCreated(code, key, Phase.ReleaseKey);
            return new Resource(key);
        }
        public void SetDword(IResource key, MetadataProperty property, uint value, int flags)
        {
            if (flags != 0 || !(property == MetadataProperty.KeyExportPolicy && value == 3 ||
                property == MetadataProperty.KeyUsage && value == 2)) throw new InvalidOperationException();
            Native.SetDword(Handle(key), MetadataProperties.Describe(property).WireName, value);
        }
        public void FinalizeKey(IResource key, int flags)
        {
            if (flags != 0) throw new InvalidOperationException();
            Native.Check(Native.FinalizeKey(Handle(key), flags));
        }
        public string ReadText(IResource resource, MetadataProperty property)
        {
            if (property is not (MetadataProperty.ProviderName or MetadataProperty.KeyAlgorithmName or MetadataProperty.KeyAlgorithmGroup))
                throw new InvalidOperationException();
            return Native.ReadText(Handle(resource), property);
        }
        public uint ReadDword(IResource key, MetadataProperty property)
        {
            if (property is not (MetadataProperty.KeyLength or MetadataProperty.KeyType or MetadataProperty.KeyUsage))
                throw new InvalidOperationException();
            return Native.ReadDword(Handle(key), property);
        }
        private static void ExportArguments(IntPtr wrappingKey, string blob, IntPtr parameters, int flags)
        {
            if (wrappingKey != IntPtr.Zero || blob != "ECCPRIVATEBLOB" || parameters != IntPtr.Zero || flags != 0)
                throw new InvalidOperationException();
        }
        public int QueryExport(IResource key, IntPtr wrappingKey, string blob, IntPtr parameters, int flags)
        {
            ExportArguments(wrappingKey, blob, parameters, flags);
            Native.Check(Native.ExportKey(Handle(key), wrappingKey, blob, parameters, IntPtr.Zero, 0, out int length, flags));
            return length;
        }
        public unsafe int FillExport(IResource key, IntPtr wrappingKey, string blob, IntPtr parameters, Span<byte> output, int flags)
        {
            ExportArguments(wrappingKey, blob, parameters, flags);
            if (output.Length != CapabilityEngine.BlobSize) throw new InvalidOperationException();
            fixed (byte* pointer = output) {
                Native.Check(Native.ExportKey(Handle(key), wrappingKey, blob, parameters, (IntPtr)pointer, output.Length, out int length, flags));
                return length;
            }
        }
    }
}
