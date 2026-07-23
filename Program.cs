using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Serilog;

namespace stealcast
{
    internal static class Program
    {
        private static DxgiCapturer? _capturer;
        private static KestrelWebServer? _server;
        private static HotKeyManager? _hotKeyManager;
        private static HotKeyManager? _guiHotKeyManager;

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // 1. 初始化 Windows Forms 样式
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 2. 初始化 Serilog 日志（Windows 窗体没有控制台，直接记录在本地 logs/ 文件中）
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "stealcast-.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, 
                    rollingInterval: RollingInterval.Day, 
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== stealcast 社区版启动 ===");

            // 3. 加载配置文件
            AppConfig config = LoadOrCreateConfig();

            try
            {
                // 4. 初始化 Kestrel 混合服务器并启动
                _server = new KestrelWebServer(config.Port);
                _server.Start();

                // 5. 初始化 DXGI 显存屏幕捕获器
                _capturer = new DxgiCapturer();
                if (!_capturer.Initialize())
                {
                    Log.Error("DXGI 截屏捕获器初始化失败，请确保您在主显示器桌面环境中运行此程序。");
                }

                // 6. 初始化全局快捷键监听器并绑定捕获流
                _hotKeyManager = new HotKeyManager(config.HotKey);
                _hotKeyManager.HotKeyPressed += () =>
                {
                    Log.Debug("检测到全局快捷键按下，开始捕捉帧并广播");
                    try
                    {
                        var jpegBytes = _capturer.CaptureFrame(config.JpegQuality);
                        if (jpegBytes != null && jpegBytes.Length > 0)
                        {
                            // 异步进行 WebSocket 广播，不阻塞快捷键消息泵线程
                            _ = _server.BroadcastBinaryAsync(jpegBytes);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "全局快捷键捕获并广播逻辑中发生异常");
                    }
                };

                // 启动快捷键监听线程
                _hotKeyManager.Start();

                // 7. 运行 Windows Forms UI
                using (var mainForm = new MainForm(config, _server, _capturer, _hotKeyManager))
                {
                    // 绑定用于显示/隐藏主界面的全局快捷键
                    _guiHotKeyManager = new HotKeyManager(config.ShowGuiHotKey, 9998);
                    _guiHotKeyManager.HotKeyPressed += () =>
                    {
                        Log.Information("触发显示/隐藏界面快捷键...");
                        mainForm.ToggleVisibility();
                    };
                    _guiHotKeyManager.Start();

                    Application.Run(mainForm);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用程序崩溃");
                MessageBox.Show($"应用程序发生严重错误，即将关闭！\n\n详细信息：{ex.Message}", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cleanup();
                Log.Information("=== stealcast 社区版已关闭 ===");
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// 加载或创建本地 config.json 配置文件
        /// </summary>
        private static AppConfig LoadOrCreateConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            AppConfig config;

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var parsed = JsonSerializer.Deserialize<AppConfig>(json);
                    if (parsed != null)
                    {
                        config = parsed;
                        Log.Information("已成功从本地 config.json 加载配置参数");
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "读取 config.json 出错，将重新生成默认配置");
                }
            }

            config = new AppConfig();
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, json);
                Log.Information("已创建默认的 config.json 配置文件");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "写入 config.json 配置文件失败");
            }

            return config;
        }

        /// <summary>
        /// 程序完全退出时释放所有系统级资源
        /// </summary>
        private static void Cleanup()
        {
            Log.Information("正在销毁所有模块资源...");

            _hotKeyManager?.Dispose();
            _hotKeyManager = null;

            _guiHotKeyManager?.Dispose();
            _guiHotKeyManager = null;

            _capturer?.Dispose();
            _capturer = null;

            _server?.Dispose();
            _server = null;
        }
    }
}
