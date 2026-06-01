using System;

using RunFence.Apps.Shortcuts;
using RunFence.Launch;

namespace RunFence.Apps.UI;

public sealed class HandlerPathIconProbe(IExecutableIconCountReader executableIconCountReader) : IHandlerPathIconProbe
{
    public HandlerPathIconPresence GetIconPresence(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return HandlerPathIconPresence.Unknown;

        if (!LaunchFileExtensionRules.IsSupportedHandlerSuggestionExtension(Path.GetExtension(path)))
            return HandlerPathIconPresence.Unknown;

        try
        {
            if (!executableIconCountReader.TryGetIconCount(path, out var iconCount))
                return HandlerPathIconPresence.Unknown;

            return iconCount > 0 ? HandlerPathIconPresence.HasIcon : HandlerPathIconPresence.NoIcon;
        }
        catch (Exception)
        {
            return HandlerPathIconPresence.Unknown;
        }
    }
}
