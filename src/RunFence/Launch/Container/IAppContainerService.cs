namespace RunFence.Launch.Container;

/// <summary>
/// Composed interface for the full AppContainer service.
/// Use <see cref="IAppContainerProfileService"/> when only profile/identity operations are needed,
/// or <see cref="IAppContainerLaunchService"/> when only launch/traverse/COM operations are needed.
/// </summary>
public interface IAppContainerService : IAppContainerProfileService, IAppContainerLaunchService;