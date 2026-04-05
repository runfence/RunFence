namespace RunFence.UI;

/// <summary>
/// Shared helper for skipping separator items in list/combo controls that mix selectable items
/// with non-selectable visual dividers (e.g. <see cref="ContainerSeparatorItem"/>).
/// </summary>
public static class SeparatorSkipHelper
{
    /// <summary>
    /// When the current selection is a separator item, jumps to the adjacent selectable item
    /// in the direction the user was navigating. Updates <paramref name="lastIndex"/> when
    /// navigation settles on a non-separator item.
    /// </summary>
    /// <param name="selectedItem">The currently selected item.</param>
    /// <param name="selectedIndex">The currently selected index.</param>
    /// <param name="itemCount">The total number of items in the control.</param>
    /// <param name="setSelectedIndex">Callback to set the selected index.</param>
    /// <param name="lastIndex">The previously selected index, used to infer navigation direction.</param>
    /// <returns>
    /// <c>true</c> if the selected item was a separator and navigation was redirected
    /// (caller should return early from its handler); <c>false</c> if the item is selectable.
    /// </returns>
    public static bool HandleSeparatorSkip(
        object? selectedItem, int selectedIndex, int itemCount,
        Action<int> setSelectedIndex, ref int lastIndex)
    {
        if (selectedItem is not ContainerSeparatorItem)
        {
            lastIndex = selectedIndex;
            return false;
        }

        var current = selectedIndex;
        var navigatingDown = current > lastIndex;
        if (navigatingDown)
        {
            var next = current + 1;
            if (next < itemCount)
                setSelectedIndex(next);
        }
        else
        {
            var prev = current - 1;
            if (prev >= 0)
                setSelectedIndex(prev);
        }

        return true;
    }
}