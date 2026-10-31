using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.EphemeralCapability;

internal readonly record struct O2ClearedBuffer(bool OutputObserved, bool Cleared);
internal static class Option2PrivateBuffer
{
    // No allocation, callbacks, logging or journal access before clearing the full owned span.
    internal static O2ClearedBuffer Clear(Span<byte> output, bool fillReturned, bool fillAttempted)
    {
        bool observed = fillReturned;
        try {
            if (fillAttempted) foreach (byte value in output) observed |= value != 0;
        } finally { CryptographicOperations.ZeroMemory(output); }
        bool cleared = true; foreach (byte value in output) cleared &= value == 0;
        return new(observed, cleared);
    }
}

// Pure ownership ledger used by Windows Key and fake-callback tests; no native API here.
internal sealed class Option2KeyOwnership
{
    private bool deleteAttempt;
    internal O2HandleState State { get; private set; } = O2HandleState.Owned;
    internal bool BeginRelease()
    {
        if (State != O2HandleState.Owned) return false;
        State = O2HandleState.Released;
        return true;
    }
    internal O2Deletion Delete(Func<int> nativeDelete, Action invalidateHandle)
    {
        if (State != O2HandleState.Owned || deleteAttempt) throw new InvalidOperationException();
        deleteAttempt = true;
        int code;
        try { code = nativeDelete(); }
        catch {
            State = O2HandleState.OwnershipUnknown;
            invalidateHandle(); // abandon unknown ownership; never risk a second free
            return new(null, State);
        }
        // Exception handling above covers ONLY the native call. Reporting/allocation cannot erase known consumption.
        if (code == 0) {
            State = O2HandleState.ConsumedByDelete;
            invalidateHandle();
        }
        return new(code, State);
    }
}
