// Mockingbird-specific: low-level keyboard hook listening for a double-tap of RCtrl.
// Built on top of the same Win32 SetWindowsHookEx primitive as
// WhisperHeim/Services/Hotkey/GlobalHotkeyService.cs (@ 911bff0) but with different
// gesture semantics (double-tap, not chord). Per ADR 0006 we copy-and-modify
// rather than share a library. main-033 switched the watched key from
// VK_LCONTROL to VK_RCONTROL — Right Ctrl sits next to the cursor keys on
// Marco's split keyboard, so the gesture is reachable with the right hand
// without leaving the home row.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Mockingbird.Services.Hotkey;

/// <summary>
/// Detects a "double tap" of a configured virtual key (default: Right Control)
/// within a short window (default: 400 ms). Emits <see cref="DoubleTapped"/>
/// when the second key-down arrives in time. Uses a low-level keyboard hook so
/// the gesture works even when mockingbird's window is not focused.
/// </summary>
public sealed class DoubleTapDetector : IDisposable
{
    private readonly int _virtualKey;
    private readonly TimeSpan _window;
    private readonly ILogger<DoubleTapDetector> _logger;

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private bool _disposed;

    private DateTime _lastTapAt = DateTime.MinValue;
    private bool _keyIsDown;

    public event EventHandler? DoubleTapped;

    public DoubleTapDetector(ILogger<DoubleTapDetector> logger, int virtualKey = NativeMethods.VK_RCONTROL, int windowMs = 400)
    {
        _logger = logger;
        _virtualKey = virtualKey;
        _window = TimeSpan.FromMilliseconds(windowMs);
    }

    public bool Register()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookId != IntPtr.Zero) return true;

        _hookProc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(module.ModuleName!),
            0);

        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogWarning("DoubleTapDetector hook install failed (Win32 0x{Err:X8})", err);
            return false;
        }

        _logger.LogInformation("DoubleTapDetector listening for double-tap of vk=0x{Vk:X2} within {Ms}ms",
            _virtualKey, (int)_window.TotalMilliseconds);
        return true;
    }

    public void Unregister()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();

                if (vk == _virtualKey)
                {
                    if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                    {
                        if (!_keyIsDown)
                        {
                            _keyIsDown = true;
                            var now = DateTime.UtcNow;
                            if (now - _lastTapAt <= _window)
                            {
                                _lastTapAt = DateTime.MinValue; // consume so triple-tap doesn't double fire
                                DoubleTapped?.Invoke(this, EventArgs.Empty);
                            }
                            else
                            {
                                _lastTapAt = now;
                            }
                        }
                    }
                    else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                    {
                        _keyIsDown = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoubleTapDetector hook callback error");
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
