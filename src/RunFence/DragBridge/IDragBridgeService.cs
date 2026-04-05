using RunFence.Core.Models;

namespace RunFence.DragBridge;

public interface IDragBridgeService : IDisposable
{
    void SetData(SessionContext session);
    void ApplySettings(AppSettings settings);
}