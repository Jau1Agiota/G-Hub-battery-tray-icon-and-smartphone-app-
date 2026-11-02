using System.Net;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class LanPreviewSettingsForm : Form
{
    private readonly ComboBox _address = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Value = 8765, Width = 100 };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    internal LanPreviewSettingsForm(LanPreviewServer? active, Func<IPAddress, int, string> start, Func<Task> stop)
    {
        Text = "LAN preview"; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(460, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), AutoScroll = true };
        foreach (var address in LanPreviewServer.LocalAddresses()) _address.Items.Add(address);
        if (_address.Items.Count > 0) _address.SelectedIndex = 0;
        var enable = new Button { Text = "Enable for this session", AutoSize = true };
        var disable = new Button { Text = "Stop preview", AutoSize = true };
        enable.Click += (_, _) =>
        {
            if (_address.SelectedItem is not IPAddress address) { _status.Text = "No assigned private IPv4 interface is available."; return; }
            _status.Text = start(address, (int)_port.Value);
        };
        disable.Click += async (_, _) => { await stop(); _status.Text = "Preview is off."; };
        _status.Text = active is null ? "Preview is off. Choose your home LAN interface. No firewall rule is created." : $"http://{active.Endpoint}{LanPreviewServer.Path}";
        layout.Controls.AddRange([new Label { Text = LanPreviewServer.Label, AutoSize = true },
            new Label { Text = "Private IPv4 interface (select explicitly)", AutoSize = true }, _address,
            new Label { Text = "Port", AutoSize = true }, _port, enable, disable, _status]);
        Controls.Add(layout);
    }
}
