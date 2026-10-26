using System.Drawing;

namespace PeripheralBatteryCompanion.Core.Presentation;

public static class PopupPlacement
{
    // A docked layout must not be the authority for the window's preferred width.
    public static int ClientWidthForDpi(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return checked((int)Math.Round(360d * dpi / 96, MidpointRounding.AwayFromZero));
    }

    public static Size FitSize(Size preferred, Size workingArea)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workingArea.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workingArea.Height);
        return new(Math.Clamp(preferred.Width, 1, workingArea.Width),
            Math.Clamp(preferred.Height, 1, workingArea.Height));
    }

    // Use the clicked tray monitor, including negative coordinates and non-bottom taskbars.
    // The native adapter also constrains the form's MaximumSize to the working area.
    public static Point NearTray(Rectangle screen, Rectangle work, Size popup, Point anchor, int gap)
    {
        int x = anchor.X - popup.Width / 2;
        int y = anchor.Y - popup.Height - gap;
        if (work.Bottom < screen.Bottom) y = work.Bottom - popup.Height - gap;
        else if (work.Top > screen.Top) y = work.Top + gap;
        else if (work.Left > screen.Left)
        {
            x = work.Left + gap;
            y = anchor.Y - popup.Height / 2;
        }
        else if (work.Right < screen.Right)
        {
            x = work.Right - popup.Width - gap;
            y = anchor.Y - popup.Height / 2;
        }
        else if (y < work.Top) y = anchor.Y + gap; // Auto-hidden taskbar: use click position.

        return new(Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - popup.Width)),
            Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - popup.Height)));
    }
}
