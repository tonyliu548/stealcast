using System;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace stealcast
{
    /// <summary>
    /// Windows 全局快捷键管理器，运行在独立线程的 Win32 消息循环中
    /// </summary>
    public class HotKeyManager : IDisposable
    {
        // Win32 API 导入
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        // 常量定义
        private const uint WM_HOTKEY = 0x0312;
        private const uint WM_QUIT = 0x0012;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000; // 防止按住不放时连续触发

        private readonly string _hotkeyString;
        private readonly int _hotkeyId;
        private Thread? _messageLoopThread;
        private uint _threadId;
        private bool _isRegistered;
        private readonly ManualResetEventSlim _threadStartedEvent = new(false);

        /// <summary>
        /// 当全局快捷键被按下时触发的事件
        /// </summary>
        public event Action? HotKeyPressed;

        public HotKeyManager(string hotkeyString, int hotkeyId = 9999)
        {
            _hotkeyString = hotkeyString;
            _hotkeyId = hotkeyId;
        }

        /// <summary>
        /// 启动后台线程并注册全局快捷键
        /// </summary>
        public void Start()
        {
            if (_messageLoopThread != null) return;

            _messageLoopThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "HotKeyMessageLoopThread"
            };
            _messageLoopThread.Start();

            // 等待线程启动并尝试注册快捷键
            _threadStartedEvent.Wait();

            if (!_isRegistered)
            {
                throw new InvalidOperationException($"全局快捷键 '{_hotkeyString}' 注册失败，可能已被其他程序占用。");
            }
        }

        /// <summary>
        /// 消息循环线程主函数
        /// </summary>
        private void MessageLoop()
        {
            _threadId = GetCurrentThreadId();

            if (!ParseHotKey(_hotkeyString, out uint modifiers, out uint vk))
            {
                Log.Error("无法解析快捷键配置: {HotKey}", _hotkeyString);
                _threadStartedEvent.Set();
                return;
            }

            // 在该线程上注册快捷键（hWnd 设为 IntPtr.Zero，消息将投递到当前线程的消息队列）
            _isRegistered = RegisterHotKey(IntPtr.Zero, _hotkeyId, modifiers | MOD_NOREPEAT, vk);
            if (_isRegistered)
            {
                Log.Information("全局快捷键 '{HotKey}' 注册成功，ID: {Id}", _hotkeyString, _hotkeyId);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                Log.Error("全局快捷键注册失败，错误代码: {Error}", error);
            }

            // 通知主线程：注册操作已完成（无论成功与否）
            _threadStartedEvent.Set();

            if (!_isRegistered) return;

            // Win32 消息循环
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY && (int)msg.wParam == _hotkeyId)
                {
                    Log.Debug("全局快捷键触发: {HotKey}", _hotkeyString);
                    try
                    {
                        HotKeyPressed?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "执行快捷键回调事件时发生异常");
                    }
                }
                else if (msg.message == WM_QUIT)
                {
                    break;
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // 线程退出前注销快捷键
            UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            Log.Information("全局快捷键已注销，ID: {Id}", _hotkeyId);
        }

        /// <summary>
        /// 解析快捷键字符串（例如 "Ctrl+Alt+S"）为 Win32 修饰符和虚拟键码
        /// </summary>
        private bool ParseHotKey(string hotkey, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;

            if (string.IsNullOrWhiteSpace(hotkey)) return false;

            string[] parts = hotkey.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(part)) continue;

                // 判断是否为修饰键
                if (part == "CTRL" || part == "CONTROL")
                {
                    modifiers |= MOD_CONTROL;
                }
                else if (part == "ALT")
                {
                    modifiers |= MOD_ALT;
                }
                else if (part == "SHIFT")
                {
                    modifiers |= MOD_SHIFT;
                }
                else if (part == "WIN" || part == "WINDOWS")
                {
                    modifiers |= MOD_WIN;
                }
                // 判断是否为主键
                else
                {
                    if (part.Length == 1)
                    {
                        vk = part[0];
                    }
                    else if (part.StartsWith("F") && int.TryParse(part.AsSpan(1), out int fVal) && fVal >= 1 && fVal <= 12)
                    {
                        vk = (uint)(0x70 + (fVal - 1)); // F1 = 0x70
                    }
                    else if (part == "SPACE")
                    {
                        vk = 0x20;
                    }
                    else if (part == "ENTER" || part == "RETURN")
                    {
                        vk = 0x0D;
                    }
                    else if (part == "TAB")
                    {
                        vk = 0x09;
                    }
                    else if (part == "ESC" || part == "ESCAPE")
                    {
                        vk = 0x1B;
                    }
                    else
                    {
                        Log.Warning("未知的按键: {Part}", part);
                        return false;
                    }
                }
            }

            return vk != 0;
        }

        public void Dispose()
        {
            if (_messageLoopThread != null && _isRegistered)
            {
                // 向后台线程消息队列发送 WM_QUIT 消息使其退出循环
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                if (!_messageLoopThread.Join(2000))
                {
                    Log.Warning("快捷键消息循环线程未能优雅退出，正在强制终止...");
                }
                _messageLoopThread = null;
            }
            _threadStartedEvent.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
