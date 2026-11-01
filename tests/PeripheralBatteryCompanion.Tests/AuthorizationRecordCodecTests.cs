using System.Text;
using System.Text.Json.Nodes;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class AuthorizationRecordCodecTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static EnrollmentIdentity Identity => new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "https://192.168.1.10:8443", new string('a', 64), [1, 2, 3], Now, Now.AddDays(90));
    private static AuthorizationRecord Record => new(Identity, Guid.Parse("22222222-2222-4222-8222-222222222222"),
        Guid.Parse("33333333-3333-4333-8333-333333333333"), new string('b', 64), new string('c', 64),
        1, Now, Now.AddDays(90), false);

    [Fact]
    public void RoundTripPreservesBindingsAndTombstoneWithoutPersistingConsentOrBearer()
    {
        foreach (var original in new[] { Record, Record.Tombstone() })
        {
            byte[] bytes = AuthorizationRecordCodec.Encode(original);
            Assert.True(AuthorizationRecordCodec.TryDecode(bytes, Identity, out var decoded));
            Assert.Equal(bytes, AuthorizationRecordCodec.Encode(decoded!));
            Assert.Equal(original.Revoked, decoded!.Revoked);
            Assert.DoesNotContain("bearer", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("consent", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(original.TokenSha256, original.ToString());
        }
    }

    [Theory]
    [InlineData("version", "2")]
    [InlineData("scope", "\"admin\"")]
    [InlineData("generation", "0")]
    [InlineData("generation", "1.5")]
    [InlineData("issuedAt", "-9223372036854775808")]
    [InlineData("expiresAt", "9223372036854775807")]
    [InlineData("revoked", "\"false\"")]
    [InlineData("phoneId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("leafDer", "null")]
    [InlineData("leafDer", "\" AQID\"")]
    [InlineData("tokenSha256", "null")]
    [InlineData("endpoint", "null")]
    public void MalformedOrSubstitutedFieldsAreRejected(string field, string replacement)
    {
        var json = JsonNode.Parse(AuthorizationRecordCodec.Encode(Record))!.AsObject();
        json[field] = JsonNode.Parse(replacement);
        Assert.False(AuthorizationRecordCodec.TryDecode(Encoding.UTF8.GetBytes(json.ToJsonString()), Identity, out var record));
        Assert.Null(record);
    }

    [Fact]
    public void DuplicateUnknownMissingAndEscapedDuplicateKeysAreRejected()
    {
        string json = Encoding.UTF8.GetString(AuthorizationRecordCodec.Encode(Record));
        foreach (string malformed in new[]
        {
            json.Insert(1, "\"version\":1,"), json.Insert(1, "\"\\u0076ersion\":1,"),
            json.Insert(1, "\"extra\":true,"), json.Replace("\"scope\":\"snapshot.read\",", "", StringComparison.Ordinal),
            json + "{}", "[]", "{\"version\":{\"nested\":{\"value\":1}}}"
        })
            Assert.False(AuthorizationRecordCodec.TryDecode(Encoding.UTF8.GetBytes(malformed), Identity, out _));
    }

    [Fact]
    public void OversizeBomAndInvalidUtf8FailBeforeParsing()
    {
        byte[] valid = AuthorizationRecordCodec.Encode(Record);
        Assert.False(AuthorizationRecordCodec.TryDecode(new byte[AuthorizationRecordCodec.MaxBytes + 1], Identity, out _));
        Assert.False(AuthorizationRecordCodec.TryDecode(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(valid).ToArray(), Identity, out _));
        Assert.False(AuthorizationRecordCodec.TryDecode(new byte[] { 0xFF }.Concat(valid).ToArray(), Identity, out _));
    }

    [Fact]
    public void ExactLeafAndIdentityGenerationCannotBeSubstitutedEvenWhenPinMatches()
    {
        var changedLeaf = new EnrollmentIdentity(Identity.IdentityId, Identity.Endpoint, Identity.SpkiSha256,
            [1, 2, 4], Identity.LeafNotBefore, Identity.LeafNotAfter);
        Assert.False(AuthorizationRecordCodec.TryDecode(AuthorizationRecordCodec.Encode(Record), changedLeaf, out _));
        var changedId = new EnrollmentIdentity(Guid.Parse("44444444-4444-4444-8444-444444444444"), Identity.Endpoint,
            Identity.SpkiSha256, Identity.LeafDer.AsSpan(), Identity.LeafNotBefore, Identity.LeafNotAfter);
        Assert.False(AuthorizationRecordCodec.TryDecode(AuthorizationRecordCodec.Encode(Record), changedId, out _));
    }

    [Fact]
    public void MaximumPublicLeafFitsBoundAndInputBuffersAreNotRetained()
    {
        byte[] leaf = new byte[4096]; leaf[0] = 1;
        var identity = new EnrollmentIdentity(Identity.IdentityId, Identity.Endpoint, Identity.SpkiSha256, leaf,
            Identity.LeafNotBefore, Identity.LeafNotAfter);
        leaf[0] = 2;
        Assert.Equal(1, identity.LeafDer[0]);
        var record = new AuthorizationRecord(identity, Record.EnrollmentId, Record.PhoneId, Record.TokenSha256,
            Record.TranscriptSha256, 1, Now, Now.AddDays(90), false);
        byte[] bytes = AuthorizationRecordCodec.Encode(record);
        Assert.InRange(bytes.Length, 1, AuthorizationRecordCodec.MaxBytes);
        Assert.True(AuthorizationRecordCodec.TryDecode(bytes, identity, out _));
        Assert.Throws<ArgumentException>(() => new EnrollmentIdentity(Identity.IdentityId, Identity.Endpoint,
            Identity.SpkiSha256, new byte[4097], Now, Now.AddDays(90)));
    }
}
