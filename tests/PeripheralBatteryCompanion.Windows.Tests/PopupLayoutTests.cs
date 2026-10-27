using System.Runtime.ExceptionServices;
using PeripheralBatteryCompanion.Core.Presentation;
using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

// Real WinForms controls, running on an STA thread. These tests need Windows, but no
// G HUB or tray icon. They do not prove shell click/focus ordering or physical DPI changes.
public sealed class PopupLayoutTests
{
    [Fact]
    public void PopupHasReadableWidthAndVisibleInformationAndButtons() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        Assert.False(form.AutoSize); // Reverting the circular form/child AutoSize contract fails here.
        Assert.True(form.ClientSize.Width >= PopupPlacement.ClientWidthForDpi(96));
        form.Display(new(45, "Battery: 45%", DateTimeOffset.UtcNow, ""));
        var work = Screen.PrimaryScreen!.WorkingArea;
        form.ShowNearTray(new Point(work.Right - 2, work.Bottom - 2));
        Application.DoEvents(); // Process the actual handle/DPI/layout messages; no sleep/retry.

        AssertReadableAndOnScreen(form);
        var labels = Descendants(form).OfType<Label>().Where(label => label.Visible).Select(label => label.Text).ToArray();
        Assert.Contains("G915 Keyboard", labels);
        Assert.Contains("Battery: 45%", labels);
        Assert.Contains("G305 Mouse", labels);
        Assert.Contains("Battery: Unknown", labels);
        AssertControlsReachable(form);
    });

    [Fact]
    public void LongUnavailableMessageWrapsWithoutCollapsingWidthOrLosingClose() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        var work = Screen.PrimaryScreen!.WorkingArea;
        form.ShowNearTray(new Point(work.Right - 2, work.Bottom - 2));
        Application.DoEvents();
        var width = form.Width;
        var message = string.Concat(Enumerable.Repeat("G HUB is temporarily unavailable. ", 12));
        form.Display(new(null, "Battery: Unavailable", DateTimeOffset.UtcNow, message));
        Application.DoEvents();
        Assert.Equal(width, form.Width);
        AssertReadableAndOnScreen(form);
        var status = Descendants(form).OfType<Label>().Single(label => label.Text == message);
        Assert.True(status.Height > status.Font.Height, "Long status should wrap to multiple lines.");
        AssertControlsReachable(form);

        form.Display(new(62, "Battery: 62%", DateTimeOffset.UtcNow, ""));
        Application.DoEvents();
        Assert.Equal(width, form.Width);
        AssertReadableAndOnScreen(form);
        AssertControlsReachable(form);
    });

    [Fact]
    public void ReopeningAtScreenEdgesKeepsOneFormInsideEachWorkingArea() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        form.Display(new(45, "Battery: 45%", DateTimeOffset.UtcNow, ""));
        foreach (var screen in Screen.AllScreens)
        {
            var work = screen.WorkingArea;
            foreach (var anchor in new[] { new Point(work.Left + 2, work.Top + 2), new Point(work.Right - 2, work.Bottom - 2) })
            {
                form.ShowNearTray(anchor);
                Application.DoEvents();
                Assert.True(work.Contains(form.Bounds), $"Popup {form.Bounds} must fit in {work}.");
                AssertReadableAndOnScreen(form);
                form.Hide();
                Assert.False(form.IsDisposed);
            }
        }
    });

    [Fact]
    public void HyperXReceiverAndBatteryRemainSeparateAndQueryIsBlockedAfterOptIn() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        form.DisplayHyperX(new("Receiver: Detected; headset connectivity: Unknown", "Battery: Unknown",
            "Last observed: Never", "Receiver detected; no battery query.", true, false,
            PeripheralBatteryCompanion.Core.HyperXBatteryProvider.LifetimeBlocker));
        var work = Screen.PrimaryScreen!.WorkingArea;
        form.ShowNearTray(new Point(work.Right - 2, work.Bottom - 2));
        Application.DoEvents();
        AssertReadableAndOnScreen(form);
        var controls = Descendants(form).ToArray();
        Assert.Contains(controls.OfType<Label>(), label => label.Text == "HyperX Cloud III Wireless");
        Assert.Contains(controls.OfType<Label>(), label => label.Text.Contains("headset connectivity: Unknown"));
        Assert.True(controls.OfType<CheckBox>().Single(box => box.Text == "Experimental HyperX queries").Checked);
        Assert.False(controls.OfType<Button>().Single(button => button.Text == "Query battery (experimental)").Enabled);
        Assert.DoesNotContain(typeof(BatteryPopupForm).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "PeripheralBatteryCompanion.HyperXBatteryPoc");
        AssertControlsReachable(form);
    });

    [Fact]
    public void HyperXStaleCandidateWrapsAndDoesNotChangeKeyboardDisplay() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        form.Display(new(45, "Battery: 45%", DateTimeOffset.UtcNow, ""));
        form.DisplayHyperX(new("Receiver: Absent; headset connectivity: Unknown", "Battery: Stale; last candidate 56% (not current)",
            "Last observed: previous observation", "Receiver not observed.\nSanitized diagnostics.", false, false,
            PeripheralBatteryCompanion.Core.HyperXBatteryProvider.LifetimeBlocker));
        var work = Screen.PrimaryScreen!.WorkingArea;
        form.ShowNearTray(new Point(work.Right - 2, work.Bottom - 2));
        Application.DoEvents();
        AssertReadableAndOnScreen(form);
        Assert.Contains(Descendants(form).OfType<Label>(), label => label.Text == "Battery: 45%");
        Assert.Contains(Descendants(form).OfType<Label>(), label => label.Text.Contains("not current"));
        AssertControlsReachable(form);
    });

    [Fact]
    public void HelperReadinessAndBusyStateGateQueryAndCancelWithoutStartingAnOperation() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        var queries = 0; var cancels = 0;
        form.HyperXQueryRequested += (_, _) => queries++;
        form.HyperXCancelRequested += (_, _) => cancels++;
        var controls = Descendants(form).OfType<Button>().ToArray();
        var query = controls.Single(button => button.Text == "Query battery (experimental)");
        var cancel = controls.Single(button => button.Text == "Cancel HyperX operation");
        Assert.False(query.Enabled); Assert.False(cancel.Enabled); Assert.Equal(0, queries);
        form.DisplayHyperX(new("Receiver: Detected; headset connectivity: Unknown", "Battery: Unknown", "Last observed: Never",
            "Simulation readiness passed; no battery query.", true, true, ""));
        Assert.True(query.Enabled);
        form.SetHyperXQuerying(true);
        Assert.False(query.Enabled); Assert.True(cancel.Enabled);
        form.SetHyperXQuerying(false);
        Assert.True(query.Enabled); Assert.False(cancel.Enabled);
        Assert.Equal(0, queries); Assert.Equal(0, cancels); // Display/busy transitions never invoke a query.
    });

    [Fact]
    public void AutomaticControlsDisplayStateWithoutPersistingOrQuerying() => OnSta(() =>
    {
        using var form = new BatteryPopupForm();
        var edits = 0; var queries = 0;
        form.HyperXMonitoringRequested += (_, _) => edits++;
        form.HyperXQueryRequested += (_, _) => queries++;
        form.DisplayHyperXMonitoring(new(true, 300, false, true, null, null,
            PeripheralBatteryCompanion.Core.HyperXCheckOrigin.Scheduled, 4, "Paused after failures"));
        var controls = Descendants(form).ToArray();
        Assert.True(controls.OfType<CheckBox>().Single(box => box.Text.StartsWith("Automatic HyperX")).Checked);
        Assert.Equal("300 seconds", controls.OfType<ComboBox>().Single().SelectedItem);
        Assert.True(controls.OfType<Button>().Single(button => button.Text == "Resume HyperX monitoring").Enabled);
        Assert.Contains(controls.OfType<Label>(), label => label.Text.Contains("Next check: None"));
        Assert.Equal(0, edits); Assert.Equal(0, queries);
        form.SetHyperXQuerying(true);
        Assert.False(controls.OfType<ComboBox>().Single().Enabled);
        Assert.True(controls.OfType<Button>().Single(button => button.Text == "Cancel HyperX operation").Enabled);
    });

    private static void AssertReadableAndOnScreen(BatteryPopupForm form)
    {
        var work = Screen.FromRectangle(form.Bounds).WorkingArea;
        var chromeWidth = form.Width - form.ClientSize.Width;
        var expectedWidth = Math.Min(PopupPlacement.ClientWidthForDpi(form.DeviceDpi), work.Width - chromeWidth);
        Assert.Equal(expectedWidth, form.ClientSize.Width);
        Assert.True(form.MinimumSize.Width >= form.Width);
        Assert.True(work.Contains(form.Bounds), $"Popup {form.Bounds} must fit in {work}.");
        Assert.False(form.HorizontalScroll.Visible, "Normal popup text must wrap rather than require horizontal scrolling.");
    }

    private static void AssertControlsReachable(Form form)
    {
        var leaves = Descendants(form).Where(control => control.Visible && control is Label or Button or BatteryLevelBar).ToArray();
        Assert.NotEmpty(leaves);
        foreach (var control in leaves)
        {
            Assert.True(control.Width > 0 && control.Height > 0, $"{control.Text} has empty bounds.");
            if (!form.VerticalScroll.Visible)
            {
                var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                Assert.True(form.ClientRectangle.Contains(bounds), $"Clipped control: {control.Text} at {bounds}.");
            }
        }
        // With a constrained screen/long diagnostic message, bottom controls must be reachable.
        var actions = Descendants(form).OfType<FlowLayoutPanel>().Single();
        form.ScrollControlIntoView(actions);
        foreach (var button in actions.Controls.OfType<Button>())
        {
            var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            Assert.True(form.ClientRectangle.Contains(bounds), $"Action must remain reachable: {button.Text}.");
        }
        form.AutoScrollPosition = Point.Empty;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Windows layout test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
