using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.EphemeralCapability;

[SupportedOSPlatform("windows")]
internal sealed class WindowsOption2Api : IO2Calls
{
    private class Resource(NCryptHandle handle) : IResource
    {
        internal NCryptHandle Handle => handle;
        public virtual bool? ReleaseSucceeded => handle.ReleaseSucceeded;
        public virtual int? ReleaseHResult => handle.ReleaseHResult;
        public virtual void Dispose() => handle.Dispose();
    }
    private sealed class Key(NCryptHandle handle) : Resource(handle), IO2Key
    {
        private readonly object sync = new();
        private readonly Option2KeyOwnership ownership = new();
        public O2HandleState State => ownership.State;
        public override bool? ReleaseSucceeded => State == O2HandleState.ConsumedByDelete ? true : State == O2HandleState.OwnershipUnknown ? false : base.ReleaseSucceeded;
        public override int? ReleaseHResult => State == O2HandleState.ConsumedByDelete ? 0 : State == O2HandleState.OwnershipUnknown ? null : base.ReleaseHResult;
        public override void Dispose()
        {
            lock (sync) {
                if (!ownership.BeginRelease()) return;
                base.Dispose();
            }
        }
        internal O2Deletion DeleteOnce(int flags)
        {
            lock (sync) {
                if (flags != 0) throw new InvalidOperationException();
                return ownership.Delete(() => Option2Native.DeleteKey(Handle, flags), Handle.SetHandleAsInvalid);
            }
        }
    }
    private static NCryptHandle Handle(IResource resource) => ((Resource)resource).Handle;
    public bool OrdinaryOwner() => new WindowsCapabilityApi().IsOrdinaryUnelevatedUser();
    public IResource OpenProvider(string name, int flags)
    {
        if (name != O2Plan.Provider || flags != 0) throw new InvalidOperationException();
        int code = Native.OpenProvider(out var handle, name, flags); Native.CheckCreated(code, handle, Phase.ReleaseProvider);
        return new Resource(handle);
    }
    private static void ExactName(string name)
    {
        const string prefix = "PBC-A22C6-Option2-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !O2Permit.ValidId(name[prefix.Length..])) throw new InvalidOperationException();
    }
    public bool ExactKeyExists(IResource provider, string name, int flags)
    {
        ExactName(name); if (flags != 0) throw new InvalidOperationException();
        int code = Option2Native.OpenKey(Handle(provider), out var key, name, 0, flags);
        if (O2Plan.DefinitiveMissing(code, key.IsInvalid)) { key.Dispose(); return false; }
        Native.CheckCreated(code, key, Phase.ReleaseKey);
        key.Dispose(); if (key.ReleaseSucceeded != true) throw new InvalidOperationException();
        return true; // Existing exact key: refuse before journal/key mutation. Never adopt or delete it.
    }
    public IO2Key Create(IResource provider, string algorithm, string name, int legacy, int flags)
    {
        ExactName(name); if (algorithm != O2Plan.Algorithm || legacy != 0 || flags != 0) throw new InvalidOperationException();
        int code = Native.CreateKey(Handle(provider), out var key, algorithm, name, legacy, flags);
        Native.CheckCreated(code, key, Phase.ReleaseKey); return new Key(key);
    }
    public IO2Key Reopen(IResource provider, string name, int legacy, int flags)
    {
        ExactName(name); if (legacy != 0 || flags != 0) throw new InvalidOperationException();
        int code = Option2Native.OpenKey(Handle(provider), out var key, name, legacy, flags);
        Native.CheckCreated(code, key, Phase.ReleaseKey); return new Key(key);
    }
    public void SetDword(IO2Key key, MetadataProperty property, uint value, int flags)
    {
        if (flags != 0 || !(property == MetadataProperty.KeyExportPolicy && value == 0 || property == MetadataProperty.KeyUsage && value == 2)) throw new InvalidOperationException();
        Native.SetDword(Handle(key), MetadataProperties.Describe(property).WireName, value);
    }
    public unsafe void SetUiPolicy(IO2Key key, uint version, uint flags, int callFlags)
    {
        if (version != 1 || flags != 0 || callFlags != 0) throw new InvalidOperationException();
        var policy = new NativeUiPolicy { Version = version, Flags = flags };
        Native.Check(Native.SetProperty(Handle(key), "UI Policy", (IntPtr)(&policy), sizeof(NativeUiPolicy), callFlags));
    }
    public void FinalizeKey(IO2Key key, int flags) { if (flags != 0) throw new InvalidOperationException(); Native.Check(Native.FinalizeKey(Handle(key), flags)); }
    public string ReadText(IResource resource, MetadataProperty property) => Native.ReadText(Handle(resource), property);
    public uint ReadDword(IO2Key key, MetadataProperty property) => Native.ReadDword(Handle(key), property);
    public byte[] ReadUiPolicy(IO2Key key) => Native.ReadProperty(Handle(key), MetadataProperty.KeyUiPolicy);
    public unsafe byte[] PublicSpki(IO2Key key)
    {
        Native.Check(Native.ExportKey(Handle(key), IntPtr.Zero, "ECCPUBLICBLOB", IntPtr.Zero, IntPtr.Zero, 0, out int length, 0));
        if (length != 72) throw new InvalidOperationException();
        var data = new byte[72];
        fixed (byte* pointer = data) Native.Check(Native.ExportKey(Handle(key), IntPtr.Zero, "ECCPUBLICBLOB", IntPtr.Zero, (IntPtr)pointer, data.Length, out length, 0));
        if (length != 72 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x31534345 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 32) throw new InvalidOperationException();
        var spki = new byte[91]; O2Binding.Prefix.CopyTo(spki, 0); data.AsSpan(8, 64).CopyTo(spki.AsSpan(27));
        return spki; // Public coordinates only, never private scalar or ECCPRIVATEBLOB.
    }
    public byte[] FreshChallenge() => RandomNumberGenerator.GetBytes(32);
    public unsafe byte[] SignHash(IO2Key key, ReadOnlySpan<byte> hash, int flags)
    {
        if (hash.Length != 32 || flags != 0) throw new InvalidOperationException();
        fixed (byte* input = hash) {
            Native.Check(Option2Native.SignHash(Handle(key), IntPtr.Zero, (IntPtr)input, hash.Length, IntPtr.Zero, 0, out int length, flags));
            if (length != 64) throw new InvalidOperationException();
            var signature = new byte[64];
            fixed (byte* output = signature) Native.Check(Option2Native.SignHash(Handle(key), IntPtr.Zero, (IntPtr)input, hash.Length, (IntPtr)output, signature.Length, out length, flags));
            if (length != 64) throw new InvalidOperationException(); return signature;
        }
    }
    public bool VerifyHash(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
    {
        if (!O2Binding.Valid(spki) || hash.Length != 32 || signature.Length != 64) return false;
        using var publicKey = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = spki.Slice(27, 32).ToArray(), Y = spki.Slice(59, 32).ToArray() } });
        return publicKey.VerifyHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
    private static void PrivateArguments(string blob, IntPtr wrap, IntPtr parameters, int flags)
    {
        if (blob != "ECCPRIVATEBLOB" || wrap != IntPtr.Zero || parameters != IntPtr.Zero || flags != 0) throw new InvalidOperationException();
    }
    public int QueryPrivate(IO2Key key, string blob, IntPtr wrap, IntPtr parameters, int flags)
    {
        PrivateArguments(blob, wrap, parameters, flags);
        Native.Check(Native.ExportKey(Handle(key), wrap, blob, parameters, IntPtr.Zero, 0, out int length, flags)); return length;
    }
    public unsafe int FillPrivate(IO2Key key, string blob, IntPtr wrap, IntPtr parameters, Span<byte> output, int flags)
    {
        PrivateArguments(blob, wrap, parameters, flags); if (output.Length != 104) throw new InvalidOperationException();
        fixed (byte* pointer = output) { Native.Check(Native.ExportKey(Handle(key), wrap, blob, parameters, (IntPtr)pointer, output.Length, out int length, flags)); return length; }
    }
    public O2Deletion Delete(IO2Key key, int flags) => ((Key)key).DeleteOnce(flags);
}
[SupportedOSPlatform("windows")]
internal static class Option2Native
{
    [DllImport("ncrypt.dll", EntryPoint = "NCryptOpenKey", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int OpenKey(NCryptHandle provider, out NCryptHandle key, string name, int legacy, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptDeleteKey", ExactSpelling = true)]
    internal static extern int DeleteKey(NCryptHandle key, int flags);
    [DllImport("ncrypt.dll", EntryPoint = "NCryptSignHash", ExactSpelling = true)]
    internal static extern int SignHash(NCryptHandle key, IntPtr padding, IntPtr hash, int hashLength, IntPtr signature, int capacity, out int length, int flags);
}
