namespace RunFence.UI.Forms;

public sealed class ContextHelpSnapshotRenderer
{
    public Bitmap? CaptureFormSnapshot(
        ContextHelpForm form,
        ContextHelpButton button,
        ContextHelpOverlay overlay,
        IReadOnlyCollection<IContextHelpSnapshotParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(participants);

        if (form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0)
            return null;

        bool restoreOverlayVisibility = overlay.Parent == form && overlay.Visible;
        bool restoreButtonVisibility = button.Visible;
        try
        {
            PrepareSnapshotParticipants(participants);
            if (restoreOverlayVisibility)
                overlay.Visible = false;
            if (!restoreButtonVisibility)
                button.Visible = true;

            var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            Cursor.Hide();
            try
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, form.ClientSize);
            }
            finally
            {
                Cursor.Show();
            }

            return bitmap;
        }
        catch
        {
            try
            {
                var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(form.BackColor);

                foreach (Control child in form.Controls.Cast<Control>())
                {
                    if (child == overlay || child == button)
                        continue;

                    DrawVisibleControl(graphics, child);
                }

                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            if (!button.IsDisposed)
                button.Visible = restoreButtonVisibility;
            if (restoreOverlayVisibility && !overlay.IsDisposed)
                overlay.Visible = true;
        }
    }

    private static void PrepareSnapshotParticipants(IReadOnlyCollection<IContextHelpSnapshotParticipant> participants)
    {
        foreach (var participant in participants)
            participant.PrepareForContextHelpSnapshot();
    }

    private static void DrawVisibleControl(Graphics graphics, Control control)
    {
        if (!control.Visible || control.Width <= 0 || control.Height <= 0)
            return;

        using var childBitmap = new Bitmap(control.Width, control.Height);
        using (var childGraphics = Graphics.FromImage(childBitmap))
        {
            childGraphics.Clear(control.BackColor == Color.Transparent
                ? control.Parent?.BackColor ?? SystemColors.Control
                : control.BackColor);
        }

        control.DrawToBitmap(childBitmap, new Rectangle(Point.Empty, control.Size));
        graphics.DrawImageUnscaled(childBitmap, control.Location);
    }
}
