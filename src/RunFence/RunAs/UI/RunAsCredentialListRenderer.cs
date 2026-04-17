using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.RunAs.UI.Forms;
using RunFence.UI;

namespace RunFence.RunAs.UI;

/// <summary>
/// Handles owner-draw rendering for the credential list box in <see cref="RunAsDialog"/>.
/// Caches icons to avoid repeated GDI allocations.
/// </summary>
/// <remarks>
/// The 3 static <see cref="Image"/> fields (<c>_iconKeyStored</c>, <c>_iconKeyAdHoc</c>,
/// <c>_iconContainer</c>) live for process lifetime. This is acceptable: RunFence is a
/// single-instance desktop app, these GDI objects are tiny (16×16 bitmaps), and the
/// alternative (per-instance Image objects) would require IDisposable plumbing in a class
/// whose instances are created per-dialog invocation, making cleanup complex and fragile.
/// </remarks>
public class RunAsCredentialListRenderer
{
    private static Image? _iconKeyStored;
    private static Image? _iconKeyAdHoc;
    private static Image? _iconContainer;

    private static Image GetIconKeyStored() =>
        _iconKeyStored ??= UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0xB8, 0x8A, 0x00), 16);

    private static Image GetIconKeyAdHoc() =>
        _iconKeyAdHoc ??= UiIconFactory.CreateToolbarIcon("\U0001F512", Color.FromArgb(0xA0, 0xA0, 0xA0), 16);

    private static Image GetIconContainer() =>
        _iconContainer ??= UiIconFactory.CreateToolbarIcon("\U0001F4E6", Color.FromArgb(0x33, 0x66, 0xCC), 16);

    public void Attach(ListBox listBox)
    {
        listBox.DrawItem += OnDrawItem;
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        var listBox = (ListBox)sender!;
        if (e.Index < 0 || e.Index >= listBox.Items.Count)
            return;

        var item = listBox.Items[e.Index];
        bool isSelected = (e.State & DrawItemState.Selected) != 0;

        using var backBrush = new SolidBrush(isSelected ? SystemColors.Highlight : SystemColors.Window);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var textColor = isSelected ? SystemColors.HighlightText : SystemColors.WindowText;
        const int iconAreaWidth = 22;
        var textBounds = e.Bounds with { X = e.Bounds.X + iconAreaWidth, Width = e.Bounds.Width - iconAreaWidth };
        var textFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        switch (item)
        {
            case CredentialDisplayItem credItem:
            {
                var icon = credItem.HasStoredCredential ? GetIconKeyStored() : GetIconKeyAdHoc();
                e.Graphics.DrawImage(icon, e.Bounds.X + 3, e.Bounds.Y + (e.Bounds.Height - icon.Height) / 2);
                TextRenderer.DrawText(e.Graphics, credItem.ToString(), listBox.Font, textBounds, textColor, textFlags);
                break;
            }
            case AppContainerDisplayItem containerItem:
            {
                var icon = GetIconContainer();
                e.Graphics.DrawImage(icon, e.Bounds.X + 3, e.Bounds.Y + (e.Bounds.Height - icon.Height) / 2);
                TextRenderer.DrawText(e.Graphics, containerItem.ToString(), listBox.Font, textBounds, textColor, textFlags);
                break;
            }
            case ContainerSeparatorItem:
                TextRenderer.DrawText(e.Graphics, item.ToString(), listBox.Font, e.Bounds,
                    SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                break;
            default:
                TextRenderer.DrawText(e.Graphics, item.ToString(), listBox.Font, textBounds, textColor, textFlags);
                break;
        }

        e.DrawFocusRectangle();
    }
}