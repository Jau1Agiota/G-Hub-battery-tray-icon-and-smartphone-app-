using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.EphemeralCapability;

internal static class Option2Engine
{
    internal static O2Result Run(O2Permit? permit, IO2Calls calls, IO2Journals journals, Func<int, byte[]>? allocate = null)
    {
        if (permit is null || !permit.Consume()) return new(Outcome.NotRun, MemoryOutcome.NotAllocated, O2Cleanup.NoKeyCreationAttempted, [new(O2Phase.Consent, Outcome.NotRun)]);
        var plan = permit.Plan;
        var ownership = new Option2Ownership();
        var evidence = new List<O2Evidence> { new(O2Phase.Consent, Outcome.Pass) };
        var outcome = Outcome.Inconclusive; // No unconditional policy-enforcement success exists.
        var cleanup = O2Cleanup.NoKeyCreationAttempted;
        var memory = MemoryOutcome.NotAllocated;
        var phase = O2Phase.Owner;
        IResource? provider = null; IO2Key? created = null, reopened = null; IO2Journal? journal = null;
        byte[]? binding = null, output = null;
        bool creationAttempt = false, bound = false, journalUnsafe = false, fillAttempted = false, fillReturned = false, deferredRejectionRecord = false;
        void Pass(string category = "None") => evidence.Add(new(phase, Outcome.Pass, category));
        void Check(bool valid) { if (!valid) throw new InvalidOperationException(); }
        void Record(O2Stage stage, byte[]? publicBinding = null)
        {
            try { journal!.Append(stage, publicBinding); } catch { journalUnsafe = true; ownership.MarkUncertain(); throw; }
        }
        void VerifyJournal()
        {
            try { if (!journal!.Verify(plan, binding!)) { journalUnsafe = true; throw new InvalidOperationException(); } }
            catch { journalUnsafe = true; ownership.MarkUncertain(); throw; }
        }
        void ExactIdentity(IO2Key key)
        {
            try {
                Check(calls.ReadText(provider!, MetadataProperty.ProviderName) == O2Plan.Provider);
                Check(calls.ReadText(key, MetadataProperty.KeyName) == plan.KeyName);
                Check(calls.ReadDword(key, MetadataProperty.KeyType) == 0);
                Check(calls.PublicSpki(key).AsSpan().SequenceEqual(binding));
            } catch { ownership.MarkUncertain(); throw; }
        }
        void CloseKey(ref IO2Key? key, O2Phase releasePhase)
        {
            var owned = key; key = null;
            if (owned is null) return;
            if (owned.State == O2HandleState.ConsumedByDelete) {
                evidence.Add(new(releasePhase, Outcome.Pass, "HandleConsumedBySuccessfulDeleteNoFree")); return;
            }
            try {
                owned.Dispose();
                bool released = owned.ReleaseSucceeded == true;
                evidence.Add(new(releasePhase, released ? Outcome.Pass : Outcome.Fail, released ? "OwnedHandleReleasedNotKeyDeletion" : "ReleaseUnconfirmed", owned.ReleaseHResult));
                if (!released) outcome = Outcome.Fail;
            } catch { outcome = Outcome.Fail; evidence.Add(new(releasePhase, Outcome.Fail, "ReleaseExceptionUnconfirmed")); }
        }
        try {
            Check(calls.OrdinaryOwner()); Pass();
            phase = O2Phase.Provider; provider = calls.OpenProvider(O2Plan.Provider, 0); Pass();
            phase = O2Phase.Preflight; Check(!calls.ExactKeyExists(provider, plan.KeyName, 0)); Pass("FreshExactNameOnlyNoEnumeration");
            phase = O2Phase.Journal; journal = journals.CreateFresh(plan); Record(O2Stage.Planned); Pass();
            phase = O2Phase.Create; ownership.BeginCreate(); creationAttempt = true; cleanup = O2Cleanup.OrphanUnconfirmed;
            created = calls.Create(provider, O2Plan.Algorithm, plan.KeyName, 0, 0); ownership.Created(created.State); Record(O2Stage.Created); Pass();
            phase = O2Phase.SetExportPolicy; calls.SetDword(created, MetadataProperty.KeyExportPolicy, 0, 0); Pass("Requested0SetReturnedSuccessNotReadback");
            phase = O2Phase.SetSigningUsage; calls.SetDword(created, MetadataProperty.KeyUsage, 2, 0); Pass();
            phase = O2Phase.SetUiPolicy; calls.SetUiPolicy(created, 1, 0, 0); Pass("PersistedUi1Flags0NotNoDialogGuarantee");
            phase = O2Phase.Configure; ownership.Configured(); Pass("GenerationSettingsCompleteNotReadback");
            phase = O2Phase.Finalize; ownership.BeginFinalize(); calls.FinalizeKey(created, 0); ownership.Finalized(); Record(O2Stage.Finalized); Pass();
            phase = O2Phase.Binding; ownership.BeginBinding(); binding = calls.PublicSpki(created); Check(O2Binding.Valid(binding));
            Record(O2Stage.Bound, binding); bound = true; ownership.BindingFlushed();
            VerifyJournal(); ExactIdentity(created); ownership.BindingVerified(); Pass("DurableBindingAndIndependentLiveIdentityVerified");
            phase = O2Phase.CloseCreated;
            ownership.BeginCreationRelease();
            var initial = created; CloseKey(ref created, O2Phase.ReleaseCreated);
            ownership.CreationReleased(initial.ReleaseSucceeded == true); Pass();
            phase = O2Phase.Reopen; ownership.BeginReopen(); reopened = calls.Reopen(provider, plan.KeyName, 0, 0);
            ownership.Reopened(reopened.State); Record(O2Stage.Reopened); Pass();
            phase = O2Phase.Identity; ExactIdentity(reopened); ownership.ReopenedVerified(); Pass();
            phase = O2Phase.Metadata;
            Check(calls.ReadText(reopened, MetadataProperty.KeyAlgorithmName) == O2Plan.Algorithm);
            Check(calls.ReadText(reopened, MetadataProperty.KeyAlgorithmGroup) == "ECDSA");
            Check(calls.ReadDword(reopened, MetadataProperty.KeyLength) == 256);
            Check(calls.ReadDword(reopened, MetadataProperty.KeyExportPolicy) == 0);
            Check(calls.ReadDword(reopened, MetadataProperty.KeyUsage) == 2);
            var uiPolicy = calls.ReadUiPolicy(reopened);
            if (!InteropContracts.ValidUiPolicyShape(uiPolicy, IntPtr.Size)) throw new MetadataReadFailure(MetadataProperty.KeyUiPolicy, MetadataReadError.MalformedValue);
            Check(InteropContracts.NoUiFlags(uiPolicy, IntPtr.Size));
            Record(O2Stage.Validated); Pass("ActualPersistedPolicyReadback0NotEnforcementProof");
            phase = O2Phase.Possession;
            byte[] challenge = calls.FreshChallenge(); byte[]? hash = null;
            try {
                Check(challenge.Length == 32); hash = SHA256.HashData(challenge);
                var signature = calls.SignHash(reopened, hash, 0);
                Check(signature.Length == 64 && calls.VerifyHash(binding, hash, signature));
            } finally { CryptographicOperations.ZeroMemory(challenge); if (hash is not null) CryptographicOperations.ZeroMemory(hash); }
            Record(O2Stage.PossessionVerified); Pass("SigningPossessionOnlyNotExportDenial");
            phase = O2Phase.JournalGate; VerifyJournal(); Pass();
            Record(O2Stage.ExportAttempted);
            phase = O2Phase.Sizing;
            Check(calls.QueryPrivate(reopened, "ECCPRIVATEBLOB", IntPtr.Zero, IntPtr.Zero, 0) == CapabilityEngine.BlobSize); Pass("SizingReturned104NoPrivateBytesYet");
            phase = O2Phase.Fill; output = (allocate ?? (n => new byte[n]))(CapabilityEngine.BlobSize);
            memory = MemoryOutcome.Unconfirmed; Check(output.Length == CapabilityEngine.BlobSize);
            fillAttempted = true;
            int returned = calls.FillPrivate(reopened, "ECCPRIVATEBLOB", IntPtr.Zero, IntPtr.Zero, output, 0);
            fillReturned = true;
            outcome = Outcome.Fail; evidence.Add(new(phase, Outcome.Fail, returned == CapabilityEngine.BlobSize && CapabilityEngine.ValidStructure(output)
                ? "PrivateExportReturnedNonexportabilityClaimFailed" : "UnexpectedOutputOrMalformedLength"));
        } catch (Exception error) {
            ownership.OperationFailed();
            int? code = error is NativeFailure native ? native.Code : error is AcquisitionFailure partial ? partial.Code : error is MetadataReadFailure read ? read.Code : null;
            bool rejection = phase is O2Phase.Sizing or O2Phase.Fill && code is unchecked((int)0x80090029) or unchecked((int)0x8009000A) or unchecked((int)0x8009000B) or unchecked((int)0x80090010);
            bool inconclusive = rejection || code is unchecked((int)0x80090029) or unchecked((int)0x80090022);
            outcome = inconclusive ? Outcome.Inconclusive : Outcome.Fail;
            evidence.Add(new(phase, outcome, rejection ? "ExportRejectionObservedCauseUnestablished" : code is not null ? "NativeStatus" :
                error is MetadataReadFailure ? "MalformedOrUnavailableMetadata" : "ValidationOrUnexpectedException", code,
                error is MetadataReadFailure property ? property.Property : null));
            if (error is AcquisitionFailure acquired) {
                evidence.Add(new(acquired.ReleasePhase == Phase.ReleaseProvider ? O2Phase.ReleaseProvider : phase == O2Phase.Reopen ? O2Phase.ReleaseReopened : O2Phase.ReleaseCreated,
                    acquired.Released ? Outcome.Pass : Outcome.Fail, "PartialAcquisitionNotPersistentKeyCleanup", acquired.ReleaseCode));
                if (!acquired.Released) outcome = Outcome.Fail;
            }
            if (rejection && journal is not null && !journalUnsafe) {
                if (output is not null) deferredRejectionRecord = true;
                else { try { Record(O2Stage.ExportRejected); } catch { outcome = Outcome.Fail; } }
            }
        } finally {
            try {
                if (output is not null) {
                    // Any nonzero data after a failed fill is unexpected output, not denial proof. Never log bytes.
                    var clearedBuffer = Option2PrivateBuffer.Clear(output, fillReturned, fillAttempted);
                    bool observedOutput = clearedBuffer.OutputObserved;
                    memory = clearedBuffer.Cleared ? MemoryOutcome.ClearedFullCapacity : MemoryOutcome.Unconfirmed;
                    if (observedOutput) { outcome = Outcome.Fail; evidence.Add(new(O2Phase.Fill, Outcome.Fail, "OutputObservedEvenIfNativeCallRejected")); }
                    evidence.Add(new(O2Phase.Zeroization, clearedBuffer.Cleared ? Outcome.Pass : Outcome.Fail));
                    if (!clearedBuffer.Cleared) outcome = Outcome.Fail;
                    // Never hold uncleared private output across journal I/O/flush, including rejected partial fill.
                    if (journal is not null && !journalUnsafe) {
                        try {
                            if (deferredRejectionRecord) Record(O2Stage.ExportRejected);
                            if (observedOutput) Record(O2Stage.UnexpectedOutput);
                        } catch { outcome = Outcome.Fail; }
                    }
                }
            } finally {
                try {
                    var live = reopened ?? created;
                    if (creationAttempt) {
                        cleanup = O2Cleanup.Unconfirmed;
                        Check(bound && !journalUnsafe && binding is not null && live is not null && ownership.CleanupEligible(live.State));
                        phase = O2Phase.CleanupValidate; VerifyJournal(); ExactIdentity(live!); evidence.Add(new(phase, Outcome.Pass));
                        ownership.BeginDelete(live!.State); Record(O2Stage.DeleteAttempted);
                        phase = O2Phase.Delete;
                        var deleted = calls.Delete(live!, 0);
                        if (deleted.Code != 0 || deleted.State != O2HandleState.ConsumedByDelete || live!.State != O2HandleState.ConsumedByDelete)
                            evidence.Add(new(phase, Outcome.Fail, "DeleteFailureOrOwnershipUnconfirmedNoRetry", deleted.Code));
                        ownership.Deleted(deleted, live!.State);
                        evidence.Add(new(phase, Outcome.Pass, "ExactOwnedDeleteAndHandleConsumption", 0));
                        phase = O2Phase.CleanupRecord; Record(O2Stage.Deleted); Record(O2Stage.Completed);
                        cleanup = O2Cleanup.ExactDeleteAndJournalConfirmed; evidence.Add(new(phase, Outcome.Pass));
                    }
                } catch {
                    ownership.MarkUncertain();
                    outcome = Outcome.Fail;
                    cleanup = bound ? O2Cleanup.Unconfirmed : O2Cleanup.OrphanUnconfirmed;
                    evidence.Add(new(phase is O2Phase.Delete or O2Phase.CleanupRecord ? phase : O2Phase.CleanupValidate, Outcome.Fail, "OrphanOrCleanupUnconfirmedNoRecovery"));
                    if (journal is not null && !journalUnsafe) { try { Record(O2Stage.CleanupUnconfirmed); } catch { } }
                } finally {
                    try { CloseKey(ref reopened, O2Phase.ReleaseReopened); }
                    finally {
                        try { CloseKey(ref created, O2Phase.ReleaseCreated); }
                        finally {
                            void Close(IResource? resource, O2Phase p)
                            {
                                if (resource is null) return;
                                try { resource.Dispose(); if (resource.ReleaseSucceeded != true) throw new InvalidOperationException(); evidence.Add(new(p, Outcome.Pass, "ResourceReleasedNotPersistentKeyDeletion", resource.ReleaseHResult)); }
                                catch { outcome = Outcome.Fail; if (creationAttempt) cleanup = O2Cleanup.Unconfirmed; evidence.Add(new(p, Outcome.Fail, "ResourceReleaseUnconfirmed")); }
                            }
                            try { Close(provider, O2Phase.ReleaseProvider); } finally { Close(journal, O2Phase.JournalClose); }
                        }
                    }
                }
            }
        }
        return new(outcome, memory, cleanup, evidence, ownership.State);
    }
}
