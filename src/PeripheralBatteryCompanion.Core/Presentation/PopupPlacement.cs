using System.Drawing;

namespace PeripheralBatteryCompanion.Core.Presentation;

public static class PopupPlacement
{
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
