using System;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

// No constructor, initializer, fixture, discovery data, teardown or input loader.
// D3.3A incident: 11 certificate-generation + two secret-sample cases ran outside
// authorization. Do not use legacy runners/negative filters or remove this skip
// merely to obtain a passing result. Gate B and Gate E are NOT authorized.
public sealed class W1OfflineLifecycleTests
{
    [Fact(Skip = "NOT RUN: Gate E unapproved; no permit issuer or owner input route exists.")]
    public void SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore()
    {
        // Defense in depth if the skip is accidentally removed: no implicit plan,
        // environment/file configuration, consent default or executable native fixture.
        var result = W1NativeLifecycle.Execute(null, null, DateTimeOffset.UtcNow);
        Assert.Equal("NOT_RUN", result.Outcome);
        Assert.False(result.NativeEntered);
    }
}
