using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using Serilog;

namespace stealcast
{
    /// <summary>
    /// stealcast 社区版主图形界面，采用现代暗色主题设计
    /// </summary>
    public class MainForm : Form
    {
        private readonly AppConfig _config;
        private readonly KestrelWebServer _server;
        private readonly DxgiCapturer _capturer;
        private readonly HotKeyManager _hotKeyManager;

        private NotifyIcon? _notifyIcon;
        private Label? _lblStatus;
        private Label? _lblHotKey;
        private Label? _lblPort;
        private FlowLayoutPanel? _pnlIpList;
        private Button? _btnOpenConfig;
        private Button? _btnRestart;
        private Button? _btnHide;

        private bool _reallyExit = false;

        public MainForm(AppConfig config, KestrelWebServer server, DxgiCapturer capturer, HotKeyManager hotKeyManager)
        {
            _config = config;
            _server = server;
            _capturer = capturer;
            _hotKeyManager = hotKeyManager;

            InitializeFormDesign();
            InitializeTrayIcon();
            LoadNetworkAddresses();
            BindEvents();
        }

        /// <summary>
        /// 程序化构建 UI 界面，免去 Designer 文件，保持极简结构
        /// </summary>
        private void InitializeFormDesign()
        {
            // 窗口基础属性
            this.Text = "stealcast 社区版";
            this.Size = new Size(520, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Icon = SystemIcons.Application;
            this.BackColor = Color.FromArgb(15, 23, 42); // 深蓝色背景 (#0f172a)
            this.ForeColor = Color.FromArgb(248, 250, 252); // 亮色文本 (#f8fafc)
            this.Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. 顶部标题
            var lblTitle = new Label
            {
                Text = "stealcast Community Edition",
                Location = new Point(20, 20),
                Size = new Size(480, 30),
                Font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(96, 165, 250) // 浅蓝色强调色 (#60a5fa)
            };
            this.Controls.Add(lblTitle);

            // 分割线
            var pnlDivider = new Panel
            {
                Location = new Point(20, 60),
                Size = new Size(464, 1),
                BackColor = Color.FromArgb(51, 65, 85) // 灰色分割线 (#334155)
            };
            this.Controls.Add(pnlDivider);

            // 2. 信息面板 (Status Panel)
            var pnlStatusGroup = new Panel
            {
                Location = new Point(20, 75),
                Size = new Size(464, 80),
                BackColor = Color.FromArgb(30, 41, 59), // 卡片底色 (#1e293b)
                Padding = new Padding(15)
            };
            
            _lblStatus = new Label
            {
                Text = "● 投屏服务运行中",
                Location = new Point(15, 12),
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(16, 185, 129) // 绿色状态 (#10b981)
            };
            pnlStatusGroup.Controls.Add(_lblStatus);

            _lblHotKey = new Label
            {
                Text = $"全局快捷键：{_config.HotKey}",
                Location = new Point(15, 45),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlStatusGroup.Controls.Add(_lblHotKey);

            _lblPort = new Label
            {
                Text = $"服务端口：{_config.Port} (HTTP+WS)",
                Location = new Point(230, 45),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlStatusGroup.Controls.Add(_lblPort);

            this.Controls.Add(pnlStatusGroup);

            // 3. 局域网网页地址提示
            var lblUrlTitle = new Label
            {
                Text = "网页端连接地址（局域网内的设备可在浏览器打开此链接）：",
                Location = new Point(20, 170),
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            this.Controls.Add(lblUrlTitle);

            // 4. IP 链接列表滚动面板
            _pnlIpList = new FlowLayoutPanel
            {
                Location = new Point(20, 195),
                Size = new Size(464, 110),
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(10)
            };
            this.Controls.Add(_pnlIpList);

            // 5. 底部操作按钮
            _btnOpenConfig = CreateStyledButton("打开配置目录", new Point(20, 325), new Size(120, 34));
            _btnRestart = CreateStyledButton("重启服务", new Point(150, 325), new Size(100, 34));
            _btnHide = CreateStyledButton("最小化到托盘", new Point(344, 325), new Size(140, 34));

            this.Controls.Add(_btnOpenConfig);
            this.Controls.Add(_btnRestart);
            this.Controls.Add(_btnHide);
        }

        /// <summary>
        /// 创建带有现代暗色样式的按钮
        /// </summary>
        private Button CreateStyledButton(string text, Point location, Size size)
        {
            var btn = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.FromArgb(248, 250, 252),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(71, 85, 105);
            return btn;
        }

        /// <summary>
        /// 初始化系统托盘图标
        /// </summary>
        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "stealcast 社区版",
                Visible = true
            };

            // 右键菜单
            var contextMenu = new ContextMenuStrip();
            var itemShow = new ToolStripMenuItem("显示主界面", null, (s, e) => ShowForm());
            var itemExit = new ToolStripMenuItem("完全退出程序", null, (s, e) => ExitApplication());
            contextMenu.Items.Add(itemShow);
            contextMenu.Items.Add(itemExit);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // 双击托盘显示主界面
            _notifyIcon.DoubleClick += (s, e) => ShowForm();
        }

        /// <summary>
        /// 加载并显示本机所有物理网卡的局域网 IP 与网页访问链接
        /// </summary>
        private void LoadNetworkAddresses()
        {
            if (_pnlIpList == null) return;
            _pnlIpList.Controls.Clear();

            var ips = GetLocalIPv4Addresses();
            foreach (var ip in ips)
            {
                string url = $"http://{ip}:{_config.Port}/";
                
                var lnk = new LinkLabel
                {
                    Text = url,
                    AutoSize = true,
                    Font = new Font("Consolas", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
                    LinkColor = Color.FromArgb(96, 165, 250),
                    ActiveLinkColor = Color.FromArgb(191, 219, 254),
                    VisitedLinkColor = Color.FromArgb(96, 165, 250),
                    Margin = new Padding(0, 4, 0, 4)
                };

                lnk.LinkClicked += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("无法打开链接: {Message}", ex.Message);
                    }
                };

                _pnlIpList.Controls.Add(lnk);
            }
        }

        /// <summary>
        /// 获取本机物理网卡的所有局域网 IPv4 地址
        /// </summary>
        private List<string> GetLocalIPv4Addresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 仅获取已启动的且非回环/非虚拟网卡的物理网卡
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                        !ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
                    {
                        var props = ni.GetIPProperties();
                        foreach (var addr in props.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                list.Add(addr.Address.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "获取本地局域网 IP 地址时发生异常");
            }

            // 无论如何，回环地址总是可用的
            list.Add("127.0.0.1");
            return list;
        }

        /// <summary>
        /// 绑定按钮与表单事件
        /// </summary>
        private void BindEvents()
        {
            // 最小化到托盘按钮
            _btnHide!.Click += (s, e) => HideForm();

            // 打开配置目录按钮
            _btnOpenConfig!.Click += (s, e) =>
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "打开配置目录失败");
                }
            };

            // 重启服务按钮
            _btnRestart!.Click += (s, e) =>
            {
                try
                {
                    _server.Dispose();
                    _server.Start();
                    
                    _capturer.Cleanup();
                    _capturer.Initialize();

                    LoadNetworkAddresses();
                    MessageBox.Show("投屏服务端及 DXGI 显存捕获通道已重新初始化！", "服务重启成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "重启服务失败");
                    MessageBox.Show($"重启服务失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }

        /// <summary>
        /// 隐藏窗口到系统托盘
        /// </summary>
        private void HideForm()
        {
            this.Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(2000, "stealcast 社区版", "投屏服务已最小化到托盘，在后台静默运行中！", ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// 从系统托盘恢复窗口显示
        /// </summary>
        private void ShowForm()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true; // 显示界面时，确保托盘图标显示
            }
        }



        /// <summary>
        /// 彻底退出应用程序，释放所有组件
        /// </summary>
        private void ExitApplication()
        {
            _reallyExit = true;
            this.Close();
        }

        /// <summary>
        /// 切换窗口的显示与隐藏状态，线程安全
        /// </summary>
        public void ToggleVisibility()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ToggleVisibility));
                return;
            }

            if (this.Visible)
            {
                HideForm();
            }
            else
            {
                ShowForm();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                HideForm();
            }
            base.OnResize(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 当用户点击 X 关闭按钮时，最小化到托盘，而不是完全退出程序
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideForm();
            }
            else
            {
                // 彻底退出时销毁托盘组件
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            base.OnFormClosing(e);
        }
    }
}
