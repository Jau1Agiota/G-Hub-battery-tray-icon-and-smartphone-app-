using PeripheralBatteryCompanion.Core.TransportSecurity;

namespace PeripheralBatteryCompanion.NativeAcceptance;

// This wrapper is not referenced or compiled by production startup. Real I/O stays in shared storage.
internal sealed class JournalSeams(IAuthorizationJournalStorage storage, Action<string> phase, string? fault = null) : IAuthorizationJournalStorage
{
    internal bool InjectionFired { get; private set; }
    public IAuthorizationJournalFiles AcquireExclusive() => new Files(storage.AcquireExclusive(), phase, fault, () => InjectionFired = true);
    private sealed class Files(IAuthorizationJournalFiles inner, Action<string> phase, string? fault, Action injected) : IAuthorizationJournalFiles
    {
        private bool _injected;
        private string? _afterFlush;
        private void Inject(string operation)
        { if (!_injected && operation == fault) { _injected = true; injected(); throw new IOException("Injected owned-fixture failure."); } }
        public IReadOnlyList<string> Inventory() => inner.Inventory();
        public byte[]? Read(string name, int maximumBytes) => inner.Read(name, maximumBytes);
        public void CreateNewAndFlush(string name, byte[] bytes)
        {
            Inject("write"); inner.CreateNewAndFlush(name, bytes);
            if (name is "initialized.bin" or "pending.bin") _afterFlush = name == "initialized.bin" ? "witness" : "pending";
        }
        public void Replace(string temporaryName, string destinationName)
        { Inject("replace"); inner.Replace(temporaryName, destinationName); phase(destinationName == "state.bin" ? "state" : "checkpoint"); }
        public void DeleteExisting(string name)
        { inner.DeleteExisting(name); _afterFlush = name == "pending.bin" ? "intent-removed" : "session-removed"; }
        public void FlushDirectory()
        { Inject("flush"); inner.FlushDirectory(); if (_afterFlush is { } point) { _afterFlush = null; phase(point); } }
        public void Dispose() { Inject("close"); inner.Dispose(); }
    }
}
