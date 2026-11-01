using System.Collections.Immutable;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

public enum ReplayDisposition { Rejected, Advanced, Duplicate, NewPublisher, HistoryFull }
public interface IReplayAuthorizationJournal : IAuthorizationJournalSession
{
    ReplayHistory? ReadReplay(long authorizationGeneration);
    AuthorizationCommitOutcome CommitReplay(long authorizationGeneration, long expectedVersion, ReplayHistory next);
}

public sealed class ReplayHighWater
{
    public Guid Instance { get; }
    public long Revision { get; }
    public ImmutableArray<byte> Digest { get; }
    internal ReplayHighWater(Guid instance, long revision, ReadOnlySpan<byte> digest)
    { Instance = instance; Revision = revision; Digest = ImmutableArray.CreateRange(digest.ToArray()); }
    public override string ToString() => "ReplayHighWater (redacted)";
}

/// <summary>Immutable bounded metadata only; no telemetry or secret cache. Earlier instances are retired permanently.</summary>
public sealed class ReplayHistory
{
    public const int MaximumInstances = 64;
    public const int MaximumBytes = 4096;
    public long AuthorizationGeneration { get; }
    public long Version { get; }
    public ImmutableArray<ReplayHighWater> Instances { get; }
    public ImmutableArray<byte> LastNonceDigest { get; }
    private ReplayHistory(long generation, long version, ImmutableArray<ReplayHighWater> instances, ReadOnlySpan<byte> nonce)
    { AuthorizationGeneration = generation; Version = version; Instances = instances; LastNonceDigest = ImmutableArray.CreateRange(nonce.ToArray()); }
    internal static ReplayHistory Empty(long generation) => new(generation, 0, [], []);

    public bool TryAdvance(Guid instance, long revision, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> nonceDigest,
        out ReplayHistory? next, out ReplayDisposition disposition)
    {
        next = null; disposition = ReplayDisposition.Rejected;
        if (instance == Guid.Empty || revision < 0 || digest.Length != 32 || nonceDigest.Length != 32 || Version == long.MaxValue ||
            (LastNonceDigest.Length == 32 && CryptographicOperations.FixedTimeEquals(LastNonceDigest.AsSpan(), nonceDigest))) return false;
        int index = -1;
        for (int i = 0; i < Instances.Length; i++) if (Instances[i].Instance == instance) { index = i; break; }
        var entries = Instances;
        if (index >= 0)
        {
            if (index != entries.Length - 1 || revision < entries[index].Revision) return false;
            if (revision == entries[index].Revision)
            {
                if (!CryptographicOperations.FixedTimeEquals(digest, entries[index].Digest.AsSpan())) return false;
                disposition = ReplayDisposition.Duplicate;
            }
            else { entries = entries.SetItem(index, new(instance, revision, digest)); disposition = ReplayDisposition.Advanced; }
        }
        else
        {
            if (entries.Length == MaximumInstances) { disposition = ReplayDisposition.HistoryFull; return false; }
            entries = entries.Add(new(instance, revision, digest)); disposition = ReplayDisposition.NewPublisher;
        }
        next = new(AuthorizationGeneration, Version + 1, entries, nonceDigest);
        return true;
    }

    internal bool IsSuccessorOf(ReplayHistory current)
    {
        if (Instances.IsEmpty || !current.TryAdvance(Instances[^1].Instance, Instances[^1].Revision, Instances[^1].Digest.AsSpan(),
            LastNonceDigest.AsSpan(), out var expected, out _) || expected is null) return false;
        return Encode().AsSpan().SequenceEqual(expected.Encode());
    }
    internal byte[] Encode()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(AuthorizationGeneration); writer.Write(Version); writer.Write(Instances.Length);
        writer.Write(LastNonceDigest.Length); writer.Write(LastNonceDigest.AsSpan());
        foreach (var entry in Instances) { writer.Write(entry.Instance.ToByteArray()); writer.Write(entry.Revision); writer.Write(entry.Digest.AsSpan()); }
        return stream.ToArray();
    }
    internal static ReplayHistory? Decode(byte[] bytes)
    {
        if (bytes.Length is < 24 or > MaximumBytes) return null;
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream);
        long generation = reader.ReadInt64(), version = reader.ReadInt64(); int count = reader.ReadInt32(), nonceLength = reader.ReadInt32();
        if (generation < 1 || version < 0 || count is < 0 or > MaximumInstances ||
            nonceLength != (count == 0 ? 0 : 32) || (count == 0 ? version != 0 : version < count) ||
            stream.Length - stream.Position != nonceLength + count * 56) return null;
        byte[] nonce = reader.ReadBytes(nonceLength); var entries = ImmutableArray.CreateBuilder<ReplayHighWater>(count);
        var ids = new HashSet<Guid>();
        for (int i = 0; i < count; i++)
        {
            Guid id = new(reader.ReadBytes(16)); long revision = reader.ReadInt64(); byte[] digest = reader.ReadBytes(32);
            if (id == Guid.Empty || !ids.Add(id) || revision < 0) return null;
            entries.Add(new(id, revision, digest));
        }
        return new(generation, version, entries.MoveToImmutable(), nonce);
    }
    public override string ToString() => "ReplayHistory (redacted)";
}
