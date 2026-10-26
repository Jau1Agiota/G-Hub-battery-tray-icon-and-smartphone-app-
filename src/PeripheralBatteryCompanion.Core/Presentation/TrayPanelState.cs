namespace PeripheralBatteryCompanion.Core.Presentation;

// UI-thread state only; hiding the information panel never requests process exit.
public sealed class TrayPanelState
{
    public bool IsPanelVisible { get; private set; }
    public bool IsExitRequested { get; private set; }

    public void TogglePanel()
    {
        if (!IsExitRequested) IsPanelVisible = !IsPanelVisible;
    }

    public void OpenPanel()
    {
        if (!IsExitRequested) IsPanelVisible = true;
    }

    public void DismissPanel() => IsPanelVisible = false;
    public void OpenContextMenu() => DismissPanel();

    public void RequestExit()
    {
        IsExitRequested = true;
        IsPanelVisible = false;
    }
}
