using System.Collections.Concurrent;
using System.Drawing;
using Moq;
using RunFence.Core;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundPrivilegeMarkerRuntimeTests
{
    [Fact]
    public async Task StartFalse_StillStartsListenerAndClassification()
    {
        var worker = new FakeClassificationWorker();
        using var runtime = CreateRuntime(worker, out var listener, out _);
        var pendingResult = worker.CreatePendingResult();

        runtime.Start(false, true);
        var request = Assert.Single(worker.Requests);
        var completedSignal = worker.AwaitNextClassificationCompletion();

        Assert.Equal(1, listener.StartCount);
        Assert.False(completedSignal.IsCompleted);

        pendingResult.SetResult(ForegroundPrivilegeClassificationResult.Hidden(request));
        await completedSignal;
    }

    [Fact]
    public async Task StateChanged_SeesCurrentStateAlreadyUpdated()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out _, out _);
        ForegroundPrivilegeMarkerState? observed = null;

        runtime.StateChanged += state =>
        {
            observed = state;
            Assert.Same(state, runtime.CurrentState);
        };

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();

        Assert.NotNull(observed);
        Assert.True(observed!.IsActive);
    }

    [Fact]
    public async Task ForegroundChanged_RefreshesUsingListenerWindow()
    {
        var worker = new FakeClassificationWorker();
        using var runtime = CreateRuntime(worker, out var listener, out _);

        runtime.Start(true, true);
        _ = await worker.AwaitNextClassificationRequest();
        await worker.AwaitNextClassificationCompletion();

        listener.RaiseForegroundChanged((IntPtr)30);
        var eventRequest = await worker.AwaitNextClassificationRequest();
        await worker.AwaitNextClassificationCompletion();

        Assert.Equal((IntPtr)30, eventRequest.TrackedWindowHandle);
        Assert.Equal((uint)40, eventRequest.PrivilegeSubjectProcessId);
    }

    [Fact]
    public async Task SetMarkerWindowEnabledFalse_HidesMarkerWithoutPublishingInactive()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out _, out var markerWindow);

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();
        var hideCountBeforeDisable = markerWindow.HideCount;

        runtime.SetMarkerWindowEnabled(false);

        Assert.True(runtime.CurrentState.IsActive);
        Assert.Equal(hideCountBeforeDisable + 1, markerWindow.HideCount);
    }

    [Fact]
    public async Task Start_WhenAlreadyStarted_UpdatesMarkerWindowSettingWithoutReclassifying()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out _, out var markerWindow);

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();
        var requestCount = worker.Requests.Count;
        var hideCountBeforeSecondStart = markerWindow.HideCount;

        runtime.Start(false, true);

        Assert.Equal(requestCount, worker.Requests.Count);
        Assert.Equal(hideCountBeforeSecondStart + 1, markerWindow.HideCount);
        Assert.True(runtime.CurrentState.IsActive);
    }

    [Fact]
    public async Task MoveSizeStarted_HidesMarkerForTrackedWindow()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out var listener, out var markerWindow);

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();
        var hideCountBeforeEvent = markerWindow.HideCount;

        listener.RaiseMoveSizeStarted((IntPtr)10);

        Assert.Equal(hideCountBeforeEvent + 1, markerWindow.HideCount);
    }

    [Fact]
    public async Task MoveSizeEnded_RendersCurrentVisibleClassificationAgain()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out var listener, out var markerWindow);

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();
        listener.RaiseMoveSizeStarted((IntPtr)10);
        var showCountBeforeEvent = markerWindow.ShowCount;

        listener.RaiseMoveSizeEnded((IntPtr)10);

        Assert.Equal(showCountBeforeEvent + 1, markerWindow.ShowCount);
    }

    [Fact]
    public async Task LocationChanged_RerendersOnlyForTrackedWindow()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out var listener, out var markerWindow);

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();
        var showCountBeforeOtherWindow = markerWindow.ShowCount;

        listener.RaiseLocationChanged((IntPtr)11);

        Assert.Equal(showCountBeforeOtherWindow, markerWindow.ShowCount);

        listener.RaiseLocationChanged((IntPtr)10);

        Assert.Equal(showCountBeforeOtherWindow + 1, markerWindow.ShowCount);
    }

    [Fact]
    public async Task Stop_PublishesInactiveBeforeShutdownCompletes()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });
        using var runtime = CreateRuntime(worker, out var listener, out _);
        var states = new List<ForegroundPrivilegeMarkerState>();
        runtime.StateChanged += states.Add;

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();

        runtime.Stop();

        Assert.Equal(1, listener.StopCount);
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);
        Assert.Contains(states, state => state.IsActive);
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, states[^1]);
        Assert.Throws<InvalidOperationException>(() => runtime.Start(true, true));
        Assert.Throws<InvalidOperationException>(() => runtime.SetMarkerWindowEnabled(true));
        Assert.Throws<InvalidOperationException>(() => runtime.SetMarkerWindowEnabledWhenFullscreen(true));
        Assert.Throws<InvalidOperationException>(() => runtime.RefreshForegroundWindow());
        Assert.Throws<InvalidOperationException>(() => runtime.RequestReclassification());
    }

    [Fact]
    public async Task Stop_BeforePendingClassificationCompletes_DoesNotPublishActiveState()
    {
        var worker = new FakeClassificationWorker();
        var pendingResult = worker.CreatePendingResult();
        using var runtime = CreateRuntime(worker, out _, out var markerWindow);
        var states = new List<ForegroundPrivilegeMarkerState>();
        runtime.StateChanged += states.Add;

        runtime.Start(true, true);
        var request = await worker.AwaitNextClassificationRequest();
        var statesBeforeStop = states.Count;
        var showCountBeforeStop = markerWindow.ShowCount;

        runtime.Stop();

        pendingResult.SetResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });
        await worker.AwaitNextClassificationCompletion();

        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);
        Assert.All(states.Skip(statesBeforeStop), state => Assert.False(state.IsActive));
        Assert.Equal(showCountBeforeStop, markerWindow.ShowCount);
        Assert.Throws<InvalidOperationException>(() => runtime.Start(true, true));
        Assert.Throws<InvalidOperationException>(() => runtime.SetMarkerWindowEnabled(true));
        Assert.Throws<InvalidOperationException>(() => runtime.SetMarkerWindowEnabledWhenFullscreen(true));
        Assert.Throws<InvalidOperationException>(() => runtime.RefreshForegroundWindow());
        Assert.Throws<InvalidOperationException>(() => runtime.RequestReclassification());
    }

    [Fact]
    public async Task Dispose_BeforePendingClassificationCompletes_DoesNotPublishActiveState()
    {
        var worker = new FakeClassificationWorker();
        var pendingResult = worker.CreatePendingResult();
        var runtime = CreateRuntime(worker, out _, out var markerWindow);
        int statesBeforeDispose;
        int showCountBeforeDispose;
        ForegroundPrivilegeClassificationRequest request;
        var states = new List<ForegroundPrivilegeMarkerState>();

        runtime.StateChanged += states.Add;
        runtime.Start(true, true);
        request = await worker.AwaitNextClassificationRequest();
        statesBeforeDispose = states.Count;
        showCountBeforeDispose = markerWindow.ShowCount;

        runtime.Dispose();
        pendingResult.SetResult(
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });
        await worker.AwaitNextClassificationCompletion();

        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);
        Assert.All(states.Skip(statesBeforeDispose), state => Assert.False(state.IsActive));
        Assert.Equal(showCountBeforeDispose, markerWindow.ShowCount);
    }

    [Fact]
    public async Task Stop_AfterClassificationQueuesPostedResult_DoesNotPublishActiveState()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });
        var dispatcher = new QueuedDispatcher();
        using var runtime = CreateRuntime(worker, out _, out var markerWindow, dispatcher);
        var states = new List<ForegroundPrivilegeMarkerState>();
        runtime.StateChanged += states.Add;

        runtime.Start(true, true);
        await dispatcher.AwaitPostedActionQueued();
        Assert.Equal(1, dispatcher.PostedActionCount);

        var statesBeforeStop = states.Count;
        var showCountBeforeStop = markerWindow.ShowCount;

        runtime.Stop();
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);

        dispatcher.DrainPostedActions();

        Assert.All(states.Skip(statesBeforeStop), state => Assert.False(state.IsActive));
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);
        Assert.Equal(showCountBeforeStop, markerWindow.ShowCount);
    }

    [Fact]
    public async Task Dispose_AfterClassificationQueuesPostedResult_DoesNotPublishActiveState()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("app.exe", "sid")
            });
        var dispatcher = new QueuedDispatcher();
        using var runtime = CreateRuntime(worker, out _, out var markerWindow, dispatcher);
        var states = new List<ForegroundPrivilegeMarkerState>();
        runtime.StateChanged += states.Add;

        runtime.Start(true, true);
        await dispatcher.AwaitPostedActionQueued();
        Assert.Equal(1, dispatcher.PostedActionCount);

        var statesBeforeDispose = states.Count;
        var showCountBeforeDispose = markerWindow.ShowCount;

        runtime.Dispose();
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);

        dispatcher.DrainPostedActions();

        Assert.All(states.Skip(statesBeforeDispose), state => Assert.False(state.IsActive));
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);
        Assert.Equal(showCountBeforeDispose, markerWindow.ShowCount);
    }

    [Fact]
    public void Dispose_WhenNeverStarted_DisposesCoordinatorOwnedResources()
    {
        var worker = new FakeClassificationWorker();
        var runtime = CreateRuntime(worker, out var listener, out var markerWindow);

        runtime.Dispose();

        Assert.Equal(1, listener.DisposeCount);
        Assert.Equal(1, markerWindow.DisposeCount);
        Assert.Same(ForegroundPrivilegeMarkerState.Inactive, runtime.CurrentState);
    }

    [Fact]
    public async Task Dispose_UnsubscribesMoveSizeAndLocationEvents()
    {
        var worker = new FakeClassificationWorker();
        worker.SetNextImmediateResult(request =>
            ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated)
            with
            {
                Metadata = new ForegroundPrivilegeMarkerMetadata("chrome.exe", "sid")
            });
        var runtime = CreateRuntime(worker, out var listener, out var markerWindow);

        runtime.Start(true, true);
        await worker.AwaitNextClassificationCompletion();

        runtime.Dispose();
        var showCountAfterDispose = markerWindow.ShowCount;
        var hideCountAfterDispose = markerWindow.HideCount;
        listener.RaiseMoveSizeStarted((IntPtr)10);
        listener.RaiseMoveSizeEnded((IntPtr)10);
        listener.RaiseLocationChanged((IntPtr)10);

        Assert.Equal(showCountAfterDispose, markerWindow.ShowCount);
        Assert.Equal(hideCountAfterDispose, markerWindow.HideCount);
    }

    private static ForegroundPrivilegeMarkerRuntime CreateRuntime(
        FakeClassificationWorker worker,
        out FakeWinEventListener listener,
        out FakeMarkerWindow markerWindow,
        IForegroundMarkerThreadDispatcher? dispatcher = null)
    {
        dispatcher ??= new InlineDispatcher();
        listener = new FakeWinEventListener();
        markerWindow = new FakeMarkerWindow();
        var resolver = new Mock<IForegroundWindowResolver>();
        resolver.Setup(r => r.GetForegroundWindow())
            .Returns(new ForegroundWindowInfo((IntPtr)10, 20, "TestWindow"));
        resolver.Setup(r => r.GetWindowInfo((IntPtr)30))
            .Returns(new ForegroundWindowInfo((IntPtr)30, 40, "EventWindow"));
        resolver.Setup(r => r.GetWindowInfo(It.Is<IntPtr>(hwnd => hwnd != (IntPtr)30)))
            .Returns((IntPtr hwnd) => new ForegroundWindowInfo(hwnd, 20, "TestWindow"));
        var boundsReader = new FakeBoundsReader();
        var coordinator = new ForegroundPrivilegeRefreshCoordinator(
            resolver.Object,
            Mock.Of<IProcessCreationTimeReader>(),
            boundsReader,
            new ForegroundShellWindowFilter(Mock.Of<IProcessImagePathReader>()),
            markerWindow,
            Mock.Of<ILoggingService>());
        return new ForegroundPrivilegeMarkerRuntime(dispatcher, listener, coordinator, worker);
    }

    private sealed class InlineDispatcher : IForegroundMarkerThreadDispatcher
    {
        public bool IsCurrentThread => true;
        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();

        public void Dispose()
        {
        }
    }

    private sealed class QueuedDispatcher : IForegroundMarkerThreadDispatcher
    {
        private readonly object queueLock = new();
        private readonly Queue<Action> postedActions = new();
        private readonly Queue<TaskCompletionSource<bool>> postedActionSignals = new();
        private bool disposed;

        public bool IsCurrentThread => true;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int PostedActionCount
        {
            get
            {
                lock (queueLock)
                {
                    return postedActions.Count;
                }
            }
        }

        public void Start()
        {
            StartCount++;
        }

        public void Stop()
        {
            StopCount++;
        }

        public void Post(Action action)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            TaskCompletionSource<bool>? signal = null;
            lock (queueLock)
            {
                postedActions.Enqueue(action);
                if (postedActionSignals.TryDequeue(out var queuedSignal))
                    signal = queuedSignal;
            }

            if (signal is not null)
                signal.TrySetResult(true);
        }

        public Task AwaitPostedActionQueued()
        {
            lock (queueLock)
            {
                if (postedActions.Count > 0)
                    return Task.CompletedTask;

                var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                postedActionSignals.Enqueue(signal);
                return signal.Task;
            }
        }

        public void DrainPostedActions()
        {
            while (TryDequeuePostedAction(out var action))
                action();
        }

        private bool TryDequeuePostedAction(out Action action)
        {
            lock (queueLock)
            {
                return postedActions.TryDequeue(out action!);
            }
        }

        public void Invoke(Action action)
        {
            action();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            DisposeCount++;
        }
    }

    private sealed class FakeClassificationWorker : IForegroundPrivilegeClassificationWorker
    {
        private readonly ConcurrentQueue<Func<ForegroundPrivilegeClassificationRequest, ForegroundPrivilegeClassificationResult>> immediateResults =
            new();
        private readonly ConcurrentQueue<TaskCompletionSource<ForegroundPrivilegeClassificationResult>> pendingResults = new();
        private readonly ConcurrentQueue<TaskCompletionSource<bool>> completionSignals = new();
        private readonly ConcurrentQueue<TaskCompletionSource<ForegroundPrivilegeClassificationRequest>> requestSignals = new();

        public ConcurrentQueue<ForegroundPrivilegeClassificationRequest> Requests { get; } = new();

        public FakeClassificationWorker()
        {
            SetNextImmediateResult(request => ForegroundPrivilegeClassificationResult.Hidden(request));
        }

        public void SetNextImmediateResult(
            Func<ForegroundPrivilegeClassificationRequest, ForegroundPrivilegeClassificationResult> resultFactory)
        {
            while (immediateResults.TryDequeue(out _))
            {
            }

            immediateResults.Enqueue(resultFactory);
        }

        public TaskCompletionSource<ForegroundPrivilegeClassificationResult> CreatePendingResult()
        {
            var completionSource = new TaskCompletionSource<ForegroundPrivilegeClassificationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pendingResults.Enqueue(completionSource);
            return completionSource;
        }

        public Task<ForegroundPrivilegeClassificationRequest> AwaitNextClassificationRequest()
        {
            if (Requests.TryDequeue(out var queuedRequest))
                return Task.FromResult(queuedRequest);

            var signal = new TaskCompletionSource<ForegroundPrivilegeClassificationRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            requestSignals.Enqueue(signal);
            return signal.Task;
        }

        public Task AwaitNextClassificationCompletion()
        {
            if (!completionSignals.TryDequeue(out var signal))
                return Task.FromException(new InvalidOperationException("No classification completion signal is pending."));

            return signal.Task;
        }

        public Task<ForegroundPrivilegeClassificationResult> ClassifyAsync(
            ForegroundPrivilegeClassificationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            if (requestSignals.TryDequeue(out var requestSignal))
                requestSignal.TrySetResult(request);

            Task<ForegroundPrivilegeClassificationResult> resultTask;
            if (pendingResults.TryDequeue(out var pendingResult))
            {
                resultTask = pendingResult.Task;
            }
            else if (immediateResults.TryDequeue(out var immediateResult))
            {
                resultTask = Task.FromResult(immediateResult(request));
            }
            else
            {
                resultTask = Task.FromResult(ForegroundPrivilegeClassificationResult.Hidden(request));
            }

            var completionSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completionSignals.Enqueue(completionSignal);
            _ = resultTask.ContinueWith(
                static (task, state) =>
                {
                    var signal = (TaskCompletionSource<bool>)state!;
                    if (task.IsCompletedSuccessfully)
                        signal.TrySetResult(true);
                    else if (task.IsCanceled)
                        signal.TrySetCanceled();
                    else
                        signal.TrySetException(task.Exception ?? new AggregateException("Classification failed."));
                },
                completionSignal,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return resultTask;
        }
    }

    private sealed class FakeWinEventListener : IForegroundWinEventListener
    {
        public event Action<IntPtr>? ForegroundChanged;
        public event Action<IntPtr>? MoveSizeStarted;
        public event Action<IntPtr>? MoveSizeEnded;
        public event Action<IntPtr>? LocationChanged;

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Start() => StartCount++;

        public void Stop() => StopCount++;

        public void Dispose()
        {
            DisposeCount++;
        }

        public void RaiseForegroundChanged(IntPtr hwnd) => ForegroundChanged?.Invoke(hwnd);

        public void RaiseMoveSizeStarted(IntPtr hwnd) => MoveSizeStarted?.Invoke(hwnd);

        public void RaiseMoveSizeEnded(IntPtr hwnd) => MoveSizeEnded?.Invoke(hwnd);

        public void RaiseLocationChanged(IntPtr hwnd) => LocationChanged?.Invoke(hwnd);
    }

    private sealed class FakeBoundsReader : IForegroundWindowBoundsReader
    {
        public IntPtr ResolveTrackedTopLevelWindow(IntPtr hwnd) => hwnd;

        public bool TryGetVisibleBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = new Rectangle(20, 20, 300, 200);
            return true;
        }

        public bool IsFullscreen(IntPtr hwnd, Rectangle bounds) => false;

        public bool ShouldRenderInsideLeftEdge(Rectangle bounds) => false;
    }

    private sealed class FakeMarkerWindow : IForegroundMarkerWindow
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Show(IntPtr targetWindow, Rectangle bounds, bool renderInsideLeftEdge, Color color) => ShowCount++;

        public void Hide() => HideCount++;

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
