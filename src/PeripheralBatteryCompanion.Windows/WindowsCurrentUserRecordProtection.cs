using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PeripheralBatteryCompanion.Core.TransportSecurity;

namespace PeripheralBatteryCompanion.Windows;

/// <summary>
/// SOURCE-ONLY native adapter. No registration, startup call or enable switch exists.
/// The unconditional admission hold must remain until a separately reviewed revision
/// and exact native execution plan are approved. No filesystem persistence is implemented.
/// </summary>
internal sealed class WindowsCurrentUserRecordProtection
{
    internal byte[] Protect(AuthorizationRecord record)
    {
        RequireNativeApproval();
        byte[] plaintext = AuthorizationRecordCodec.Encode(record);
        try { return Transform(plaintext, Binding(record.Identity), protect: true); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal AuthorizationRecord Unprotect(byte[] ciphertext, EnrollmentIdentity expectedIdentity)
    {
        RequireNativeApproval();
        if (ciphertext is null || ciphertext.Length is < 1 or > 16384) throw new ArgumentException("Invalid protected record size.");
        byte[] plaintext = Transform(ciphertext.ToArray(), Binding(expectedIdentity), protect: false);
        try
        {
            if (!AuthorizationRecordCodec.TryDecode(plaintext, expectedIdentity, out var record) || record is null)
                throw new CryptographicException("Protected record binding rejected.");
            return record;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal byte[] ProtectJournalEnvelope(byte[] plaintext, EnrollmentIdentity identity)
    {
        RequireNativeApproval();
        if (plaintext is null || plaintext.Length is < 1 or > DurableAuthorizationJournal.MaximumPlaintextBytes)
            throw new ArgumentException("Journal envelope bound rejected.");
        return Transform(plaintext, Binding(identity), protect: true);
    }

    internal byte[] UnprotectJournalEnvelope(byte[] ciphertext, EnrollmentIdentity identity)
    {
        RequireNativeApproval();
        if (ciphertext is null || ciphertext.Length is < 1 or > DurableAuthorizationJournal.MaximumCiphertextBytes)
            throw new ArgumentException("Journal ciphertext bound rejected.");
        return Transform(ciphertext.ToArray(), Binding(identity), protect: false);
    }

    private static void RequireNativeApproval() => throw new InvalidOperationException(
        "GATE E BLOCKED: CurrentUser record protection requires separately approved native execution and provenance.");

    // Optional entropy is context binding, not a secret or a rollback-resistant anchor.
    private static byte[] Binding(EnrollmentIdentity identity) => Encoding.ASCII.GetBytes(FormattableString.Invariant(
        $"PBC-AUTHORIZATION-1\n{identity.IdentityId:D}\n{identity.Endpoint}\n{identity.SpkiSha256}\n{identity.LeafNotBefore.ToUnixTimeSeconds()}\n{identity.LeafNotAfter.ToUnixTimeSeconds()}\n{Convert.ToBase64String(identity.LeafDer.AsSpan())}\n"));

    private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        // Only called after the unconditional admission hold. No native method is
        // invoked by type construction or static initialization.
        if (!OperatingSystem.IsWindows() || input.Length is < 1 || input.Length > (protect ? DurableAuthorizationJournal.MaximumPlaintextBytes : 16384))
            throw new InvalidOperationException("Protected record operation refused.");
        GCHandle inputPin = default, entropyPin = default;
        NativeBlob output = default;
        byte[]? result = null;
        bool operationFailed = false, cleanupUnconfirmed = false;
        try
        {
            inputPin = GCHandle.Alloc(input, GCHandleType.Pinned);
            entropyPin = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            var source = new NativeBlob { Length = input.Length, Data = inputPin.AddrOfPinnedObject() };
            var binding = new NativeBlob { Length = entropy.Length, Data = entropyPin.AddrOfPinnedObject() };
            const uint uiForbidden = 1; // No LOCAL_MACHINE flag, prompts, description or audit payload.
            bool success = protect
                ? CryptProtectData(ref source, IntPtr.Zero, ref binding, IntPtr.Zero, IntPtr.Zero, uiForbidden, out output)
                : CryptUnprotectData(ref source, IntPtr.Zero, ref binding, IntPtr.Zero, IntPtr.Zero, uiForbidden, out output);
            if (!success || output.Data == IntPtr.Zero || output.Length is < 1 || output.Length > (protect ? 16384 : DurableAuthorizationJournal.MaximumPlaintextBytes))
                operationFailed = true;
            else
            {
                result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, result.Length);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { operationFailed = true; }
        finally
        {
            try
            {
                if (output.Data != IntPtr.Zero)
                {
                    try
                    {
                        if (output.Length is > 0 and <= 16384) Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                        else cleanupUnconfirmed = true;
                    }
                    catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { cleanupUnconfirmed = true; }
                    finally { if (LocalFree(output.Data) != IntPtr.Zero) cleanupUnconfirmed = true; }
                }
            }
            finally
            {
                try { if (entropyPin.IsAllocated) entropyPin.Free(); }
                finally { if (inputPin.IsAllocated) inputPin.Free(); }
            }
        }
        if (operationFailed || cleanupUnconfirmed || result is null)
        {
            if (result is not null) CryptographicOperations.ZeroMemory(result);
            throw new NativeRecordProtectionFailure(operationFailed, cleanupUnconfirmed);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref NativeBlob input, IntPtr description, ref NativeBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out NativeBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref NativeBlob input, IntPtr description, ref NativeBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out NativeBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal sealed class NativeRecordProtectionFailure(bool operationFailed, bool cleanupUnconfirmed)
    : CryptographicException("Protected record operation did not establish success.")
{
    internal bool OperationFailed { get; } = operationFailed;
    internal bool CleanupUnconfirmed { get; } = cleanupUnconfirmed;
}
