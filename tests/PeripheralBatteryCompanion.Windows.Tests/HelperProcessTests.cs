using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.HyperXHelperProtocol;
using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

// These tests run real Windows process/pipe/Job APIs, but restrict the helper to NO-HID simulation modes.
public sealed class HelperProcessTests
{
    [Fact]
    public async Task AtomicJobContainmentNormalExitAndForcedExitPassWithoutHid()
    {
        var factory = new SimulationOnlyFactory(HelperPath());
        await using var transport = new HyperXHelperTransport(factory);
        Assert.True(await transport.PrepareAsync(true).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(new[] { HelperMode.Simulate, HelperMode.SimulateHold }, factory.Modes);
        Assert.True(transport.HasVerifiedLongRunningLifetime);
        Assert.Contains("forced exit observed", transport.Diagnostics);
        Assert.DoesNotContain("arm attempted=True", transport.Diagnostics);
        Assert.False(WindowsHelperChild.HasUnconfirmedExit);
    }

    [Fact]
    public async Task UnarmedSimulationCanBeTerminatedAndAlwaysObservesExit()
    {
        var factory = new SimulationOnlyFactory(HelperPath());
        var nonce = Guid.NewGuid().ToString("N");
        await using var child = await factory.StartAsync(HelperMode.SimulateHold, nonce, default);
        Assert.True(child.Contained);
        var ready = HelperProtocol.Decode<HelperReady>((await child.ReadFrameAsync(default).WaitAsync(TimeSpan.FromSeconds(5)))!);
        Assert.True(HelperProtocol.ValidReady(ready, nonce, HelperMode.SimulateHold));
        child.RequestTermination(); // No Start command; execution delegate cannot run.
        Assert.True(await child.WaitForExitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(child.ExitCode);
    }

    [Fact]
    public async Task OwnerExitClosesNonInheritedJobAndKillsUnarmedNoHidChild()
    {
        var factory = new SimulationOnlyFactory(HelperPath());
        var nonce = Guid.NewGuid().ToString("N");
        await using var child = await factory.StartAsync(HelperMode.SimulateHold, nonce, default);
        Assert.True(child.Contained);
        var frame = await child.ReadFrameAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        Assert.IsType<WindowsHelperChild>(child).OwnerExiting(); // Actual owner-exit path, no Start/arm command.
        Assert.True(await child.WaitForExitAsync(TimeSpan.FromSeconds(2))); // Before the helper's 5-second unarmed watchdog.
    }

    [Fact]
    public void Win64ProcessInteropLayoutsMatchTheSdk()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(144, Marshal.SizeOf<ProcessNative.ExtendedLimits>());
        Assert.Equal(104, Marshal.SizeOf<ProcessNative.Startup>());
        Assert.Equal(112, Marshal.SizeOf<ProcessNative.StartupEx>());
        Assert.Equal(24, Marshal.SizeOf<ProcessNative.ProcessInfo>());
    }

    private static string HelperPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PeripheralBatteryCompanion.sln"))) root = root.Parent;
        Assert.NotNull(root);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var path = Path.Combine(root.FullName, "src", "PeripheralBatteryCompanion.HyperXHelper", "bin", configuration,
            "net10.0", "win-x64", "PeripheralBatteryCompanion.HyperXHelper.exe");
        Assert.True(File.Exists(path), "Build the solution before the no-HID helper process tests.");
        return path;
    }
    private sealed class SimulationOnlyFactory(string executable) : IHyperXHelperProcessFactory
    {
        private readonly HyperXHelperProcessFactory _inner = new(executable);
        public List<HelperMode> Modes = [];
        public bool IsAvailable => _inner.IsAvailable;
        public Task<IHyperXHelperProcess> StartAsync(HelperMode mode, string nonce, CancellationToken token)
        {
            if (mode == HelperMode.NativeOneShot) throw new InvalidOperationException("Hardware requests prohibited in tests");
            Modes.Add(mode); return _inner.StartAsync(mode, nonce, token);
        }
    }
}
