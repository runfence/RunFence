using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Infrastructure;

/// <summary>
/// Intercepts paste keystrokes targeted at restricted-job processes and rewrites clipboard
/// HGLOBAL data inside the target process before replaying the paste input.
/// </summary>
public sealed class ClipboardPasteInterceptService : IRequiresInitialization, IDisposable
{
    private const uint SyntheticPasteMarker = 0x52464350u;
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02u;

    private readonly ClipboardPasteKeyDecision _pasteKeyDecision;
    private readonly IClipboardPasteTargetResolver _targetResolver;
    private readonly IClipboardFormatReader _clipboardFormatReader;
    private readonly IRemoteProcessInjector _remoteProcessInjector;
    private readonly ISyntheticInputSender _syntheticInputSender;
    private readonly IClipboardPasteWorkScheduler _workScheduler;
    private readonly ILowLevelHookApi _hookApi;
    private readonly ILoggingService _log;
    private readonly WindowNative.LowLevelKeyboardProc _hookProc;

    private IntPtr _hook;
    private bool _suppressingPasteKeyUp;
    private int _pasteVkToSuppress;
    private volatile bool _injectionInProgress;

    public ClipboardPasteInterceptService(
        ClipboardPasteKeyDecision pasteKeyDecision,
        IClipboardPasteTargetResolver targetResolver,
        IClipboardFormatReader clipboardFormatReader,
        IRemoteProcessInjector remoteProcessInjector,
        ISyntheticInputSender syntheticInputSender,
        IClipboardPasteWorkScheduler workScheduler,
        ILowLevelHookApi hookApi,
        ILoggingService log)
    {
        _pasteKeyDecision = pasteKeyDecision;
        _targetResolver = targetResolver;
        _clipboardFormatReader = clipboardFormatReader;
        _remoteProcessInjector = remoteProcessInjector;
        _syntheticInputSender = syntheticInputSender;
        _workScheduler = workScheduler;
        _hookApi = hookApi;
        _log = log;
        _hookProc = HookCallback;
    }

    public void Initialize()
    {
        if (_hook != IntPtr.Zero)
            return;

        _hook = _hookApi.InstallKeyboardHook(_hookProc);
        if (_hook == IntPtr.Zero)
            _log.Warn("ClipboardPasteInterceptService: Failed to install keyboard hook.");
        else
            _log.Info("ClipboardPasteInterceptService: Keyboard hook installed.");
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
            return;

        _hookApi.Unhook(_hook);
        _hook = IntPtr.Zero;
        _log.Info("ClipboardPasteInterceptService: Keyboard hook removed.");
    }

    internal IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNext(nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<WindowNative.KBDLLHOOKSTRUCT>(lParam);
        if (info.dwExtraInfo == (UIntPtr)SyntheticPasteMarker || (info.flags & LLKHF_LOWER_IL_INJECTED) != 0)
            return CallNext(nCode, wParam, lParam);

        uint message = (uint)wParam;
        if (message == WindowNative.WM_KEYUP && info.vkCode == _pasteVkToSuppress && _suppressingPasteKeyUp)
        {
            _suppressingPasteKeyUp = false;
            return (IntPtr)1;
        }

        ClipboardPasteKind pasteKind = _pasteKeyDecision.Classify(message, info.vkCode);
        if (pasteKind == ClipboardPasteKind.None)
            return CallNext(nCode, wParam, lParam);

        var targetResolution = _targetResolver.Resolve();
        if (!targetResolution.ShouldIntercept)
            return CallNext(nCode, wParam, lParam);

        var target = targetResolution.Target;
        if (_injectionInProgress)
        {
            _log.Debug($"ClipboardPasteInterceptService: Paste on pid {target.TargetProcessId} - injection already in progress, suppressing.");
            return (IntPtr)1;
        }

        string combo = pasteKind == ClipboardPasteKind.ShiftInsert ? "Shift+Ins" : "Ctrl+V";
        string pidDesc = target.TargetProcessId != target.ForegroundProcessId
            ? $"pid {target.TargetProcessId} (cmd pid {target.ForegroundProcessId})"
            : $"pid {target.TargetProcessId}";
        _log.Info($"ClipboardPasteInterceptService: Intercepting {combo} for {pidDesc} (clipboard owner pid {target.ClipboardOwnerProcessId}).");

        _suppressingPasteKeyUp = true;
        _pasteVkToSuppress = (int)info.vkCode;
        _injectionInProgress = true;
        try
        {
            _workScheduler.Run(() => HandlePaste(target, pasteKind));
        }
        catch (Exception ex)
        {
            _injectionInProgress = false;
            _log.Error($"ClipboardPasteInterceptService: Failed to schedule clipboard injection for pid {target.TargetProcessId}.", ex);
            _syntheticInputSender.SendPaste(pasteKind);
        }

        return (IntPtr)1;
    }

    private void HandlePaste(ClipboardPasteTarget target, ClipboardPasteKind pasteKind)
    {
        try
        {
            var formats = _clipboardFormatReader.ReadGlobalMemoryFormats();
            if (formats.Count == 0)
            {
                string combo = pasteKind == ClipboardPasteKind.ShiftInsert ? "Shift+Ins" : "Ctrl+V";
                _log.Info($"ClipboardPasteInterceptService: No HGLOBAL clipboard formats found - not sending synthetic {combo}.");
                return;
            }

            _log.Info($"ClipboardPasteInterceptService: Read {formats.Count} clipboard format(s): [{string.Join(", ", formats.Select(f => f.Format))}].");
            _remoteProcessInjector.TryInjectClipboardData(target.TargetProcessId, target.HWnd, formats);
            _syntheticInputSender.SendPaste(pasteKind);
        }
        catch (Exception ex)
        {
            _log.Error($"ClipboardPasteInterceptService: Unexpected error in HandlePaste for pid {target.TargetProcessId}.", ex);
            _syntheticInputSender.SendPaste(pasteKind);
        }
        finally
        {
            _injectionInProgress = false;
            _log.Debug("ClipboardPasteInterceptService: Injection lock released.");
        }
    }

    private IntPtr CallNext(int nCode, IntPtr wParam, IntPtr lParam) =>
        _hookApi.CallNextHook(_hook, nCode, wParam, lParam);

}
