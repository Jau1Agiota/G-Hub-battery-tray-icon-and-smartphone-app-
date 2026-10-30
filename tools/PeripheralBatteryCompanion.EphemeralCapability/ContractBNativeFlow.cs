namespace PeripheralBatteryCompanion.EphemeralCapability;

// Same fixed argument path in production and pure recorder tests. Implementations own returned resources.
internal interface IBNativeCalls
{
    bool IsOrdinaryUnelevatedUser();
    IResource OpenProvider(string name, int flags);
    IResource CreateKey(IResource provider, string algorithm, string? name, int legacySpec, int flags);
    void SetDword(IResource key, MetadataProperty property, uint value, int flags);
    void FinalizeKey(IResource key, int flags);
    string ReadText(IResource resource, MetadataProperty property);
    uint ReadDword(IResource key, MetadataProperty property);
    int QueryExport(IResource key, IntPtr wrappingKey, string blob, IntPtr parameters, int flags);
    int FillExport(IResource key, IntPtr wrappingKey, string blob, IntPtr parameters, Span<byte> output, int flags);
}
internal sealed class ContractBNativeFlow(IBNativeCalls calls) : IBApi
{
    internal static bool RetainedRead(MetadataProperty property) => property is MetadataProperty.ProviderName or
        MetadataProperty.KeyAlgorithmName or MetadataProperty.KeyAlgorithmGroup or MetadataProperty.KeyLength or
        MetadataProperty.KeyType or MetadataProperty.KeyUsage;
    public bool IsOrdinaryUnelevatedUser() => calls.IsOrdinaryUnelevatedUser();
    public IBProvider OpenSoftwareProvider() => new Provider(calls.OpenProvider(BConstruction.WindowsProvider, 0), calls);
    private sealed class Provider(IResource resource, IBNativeCalls calls) : IBProvider
    {
        public bool? ReleaseSucceeded => resource.ReleaseSucceeded;
        public int? ReleaseHResult => resource.ReleaseHResult;
        public void Dispose() => resource.Dispose();
        public IBKey CreateUnnamedP256()
        {
            var actualArguments = new BConstruction(BConstruction.WindowsProvider, 0, "ECDSA_P256", null, 0, 0);
            var key = calls.CreateKey(resource, actualArguments.Algorithm, actualArguments.KeyName, actualArguments.LegacySpec, actualArguments.KeyFlags);
            return new Key(key, resource, calls, actualArguments);
        }
    }
    private sealed class Key(IResource resource, IResource provider, IBNativeCalls calls, BConstruction arguments) : IBKey
    {
        private bool policyAttempt, policySet, usageAttempt, usageSet, finalizeAttempt, finalized, metadataAttempt,
            metadataRead, sizeAttempt, sized, fillAttempt;
        public BConstruction Construction => arguments;
        public bool? ReleaseSucceeded => resource.ReleaseSucceeded;
        public int? ReleaseHResult => resource.ReleaseHResult;
        public void Dispose() => resource.Dispose();
        public void SetExportPolicy()
        {
            if (policyAttempt) throw new InvalidOperationException(); policyAttempt = true;
            calls.SetDword(resource, MetadataProperty.KeyExportPolicy, 3, 0); policySet = true;
        }
        public void SetSigningUsage()
        {
            if (!policySet || usageAttempt) throw new InvalidOperationException(); usageAttempt = true;
            calls.SetDword(resource, MetadataProperty.KeyUsage, 2, 0); usageSet = true;
        }
        public void FinalizeOnce()
        {
            if (!usageSet || finalizeAttempt) throw new InvalidOperationException(); finalizeAttempt = true;
            calls.FinalizeKey(resource, 0); finalized = true;
        }
        public BMetadata ReadMetadata()
        {
            if (!finalized || metadataAttempt) throw new InvalidOperationException(); metadataAttempt = true;
            bool software = calls.ReadText(provider, MetadataProperty.ProviderName) == BConstruction.WindowsProvider;
            bool name = calls.ReadText(resource, MetadataProperty.KeyAlgorithmName) == "ECDSA_P256";
            bool group = calls.ReadText(resource, MetadataProperty.KeyAlgorithmGroup) == "ECDSA";
            int bits = MetadataProperties.ParseBitLength(calls.ReadDword(resource, MetadataProperty.KeyLength));
            uint type = calls.ReadDword(resource, MetadataProperty.KeyType);
            uint usage = calls.ReadDword(resource, MetadataProperty.KeyUsage);
            metadataRead = true;
            return new(software, name, group, bits, type, usage);
        }
        public int QueryExportSize()
        {
            if (!metadataRead || sizeAttempt) throw new InvalidOperationException(); sizeAttempt = true;
            int size = calls.QueryExport(resource, IntPtr.Zero, "ECCPRIVATEBLOB", IntPtr.Zero, 0);
            sized = size == CapabilityEngine.BlobSize;
            return size;
        }
        public int FillExport(Span<byte> output)
        {
            if (!sized || fillAttempt || output.Length != CapabilityEngine.BlobSize) throw new InvalidOperationException(); fillAttempt = true;
            return calls.FillExport(resource, IntPtr.Zero, "ECCPRIVATEBLOB", IntPtr.Zero, output, 0);
        }
    }
}
