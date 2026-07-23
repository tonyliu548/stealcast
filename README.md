# stealcast 开源社区版 (Community Edition)

stealcast 社区版是一款基于 **.NET 8** 开发的 Windows 高性能屏幕图像投屏服务端。该版本专为开源社区学习、二次开发及私有部署而设计，遵循 MIT 开源协议，不包含任何商业授权限制、反调试或混淆逻辑，代码结构极简。

在最新版本中，普通用户只需双击一个 `.exe` 即可同时提供 HTTP 网页端托管与 WebSocket 二进制串流服务。

> [!TIP]
> **🚀 进阶与高级版本支持以下功能：**
> *   **自定义快捷截图按钮**：支持更高级的屏幕抓取热键及按键绑定。
> *   **接入 AI 大模型识别**：捕获到的屏幕图片可一键上传至 AI 大模型进行图文识别（OCR）、分析与理解。
> *   **绝对静默后台运行**：支持运行期间在任务栏、桌面、甚至系统托盘中**完全隐藏图标**，保障隐私。
> *   **图片放大与缩小**：针对手机界面进行图片放大与缩小，更方便观察
> *   *如有定制化二次开发需求或需要获取高级版本，请邮件联系作者：[782940061@qq.com](mailto:782940061@qq.com)*

---

## 核心设计与特性

*   **桌面 GUI 界面**: 采用精致的深色暗黑系（Slate Blue）风格。启动后免去黑窗口，在主窗体上直观列出电脑的所有**局域网 IP 与网页版访问地址**。
*   **同端口混合服务端 (Kestrel)**: 运行时**无需管理员权限**即可绑定端口。
    *   在端口根目录（如 `http://192.168.1.100:28499/`）直接向浏览器提供接收端网页。
    *   在同一端口下的 `/ws` 路径建立 WebSocket 帧广播通道。
*   **自适应最大化网页端**:
    *   去除了外框卡片限制，屏幕投屏图像自动以 `object-fit: contain` **最大化填满**浏览器可视区域。
    *   **信息栏置底**：接收帧数、图像大小及延迟刷新时间等状态面板被优雅地固定在网页的最底部。
    *   **免输入自动连接**：网页端搭载了智能自检测逻辑，打开链接即刻**自动建立 WebSocket 连接**并等待图像流，无需用户手动输入 IP 或点击连接。
*   **系统托盘守护**:
    *   支持关闭窗口时**自动隐藏到 Windows 系统托盘**继续在后台提供投屏服务，防止用户误触关闭。
    *   双击托盘图标或通过托盘右键菜单可以快速唤起主界面或完全退出。

---

## 项目结构

```text
stealcast/
│
├── config.json                     # 配置文件 (启动时若不存在会自动生成)
├── stealcast.csproj               # 项目工程文件 (包含 SDK.Web、UseWindowsForms、Unsafe)
├── Program.cs                      # 程序入口与生命周期编排，初始化并运行 Windows Forms
├── AppConfig.cs                    # 配置信息的数据模型类
├── MainForm.cs                     # 主图形窗口类，负责本机多局域网 IP 获取、托盘集成和 UI 交互
├── KestrelWebServer.cs             # 基于原生 Kestrel 的高性能同端口 HTTP+WS 服务端 (免管理员权限)
├── ClientDemoHtml.cs               # 内置网页静态字符串类 (内置自适应最大化和信息栏置底网页源码)
├── HotKeyManager.cs                # Win32 全局快捷键注册与监听器 (运行在独立线程的消息队列中)
└── README.md                       # 说明文档
```

---

## 配置文件说明 (`config.json`)

在程序首次启动或找不到配置文件时，会在同级目录下自动生成默认的 `config.json`：

```json
{
  "Port": 28499,
  "HotKey": "Ctrl+Alt+S",
  "ShowGuiHotKey": "Ctrl+Alt+H",
  "JpegQuality": 80
}
```

*   **Port**: 服务端监听端口（默认 `28499`）。请确保此端口未被其他程序占用。
*   **HotKey**: 触发屏幕捕获与广播的全局快捷键。例如：`Ctrl+Alt+S`，`Alt+F9`，`Ctrl+Shift+P`。
*   **ShowGuiHotKey**: 触发主窗口“显示/隐藏”切换的全局快捷键（默认 `Ctrl+Alt+H`）。当程序在后台静默运行或缩在托盘时，按下该快捷键可以瞬时唤起或隐藏主窗口，非常方便。
*   **JpegQuality**: JPEG 图像的压缩质量（范围 `1 - 100`，默认 `80`）。

---

## 编译与运行指南

### 准备工作

*   操作系统：Windows 10 或 Windows 11 (DXGI 专属)
*   开发环境：[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 编译步骤

打开 PowerShell 或 CMD，导航到项目根目录（包含 `stealcast.csproj` 的目录）：

1.  **还原依赖并编译**
    ```powershell
    dotnet build -c Release
    ```

2.  **发布免安装单文件应用程序** (输出直接保存至 publish 文件夹)
    ```powershell
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish
    ```
    发布成功后的打包程序位于根目录下的 `publish/` 文件夹中。

### 运行方式

1.  运行 `publish/stealcast.exe`。
2.  桌面将弹出一个精致的暗黑色系主窗口，展示“● 投屏服务运行中”，并在中间区域列出当前电脑的所有局域网 IP 链接。
3.  点击界面上的任意局域网链接，可以直接在默认浏览器中打开。
4.  在局域网内任意手机、平板或其他电脑上，输入界面展示的任意地址（例如 `http://192.168.1.100:28499`），即可看到投屏接收网页，网页会自动连接到服务端。
5.  在服务端电脑上按下注册的快捷键（例如 `Ctrl+Alt+S`），所有连接的浏览器将瞬间更新为最新画面！图片会自动全屏最大化显示，统计状态信息显示在最底部。

---

## 常见问题与排障

*   **如何隐藏与退出程序？**
    *   **常规托盘隐藏**：点击界面右上角的 `[X]` 或最小化按钮 `[_]`，程序将自动最小化并隐藏在右下角系统托盘中。双击托盘图标即可重新呼出。
    *   **彻底退出程序**：在右下角托盘图标上点击鼠标右键，选择 **“完全退出程序”**。
*   **连接失败了怎么办？**
    *   确保客户端（如您的手机）与运行服务端的电脑处于**同一个局域网（Wi-Fi）**下。
    *   检查 Windows **防火墙设置**，确保本程序配置的端口（默认 28499）允许局域网流量入站。
    *   程序会实时在程序同目录下的 `logs/` 文件夹中记录日志，如有异常可打开日志排查。

---

## 支持与打赏 (Support & Donate)

如果您觉得这个项目对您有所帮助，或者在学习、二次开发中有所收获，欢迎进行打赏支持！您的鼓励是开源持续维护的最大动力：

<p align="center">
  <img src="打赏图片.jpg" alt="微信打赏收款码" width="280" />
</p>

---

## 技术标签与搜索关键词 (Keywords & Tags)

为了便于开源社区及搜索引擎（SEO）收录，本项目涵盖以下核心技术主题与搜索热词：

*   **开发语言与框架**：`C#`、`C# .NET 8`、`Windows Forms`、`ASP.NET Core Kestrel`、`Web API`、`WebSocket`。
*   **图形采集与渲染**：`DirectX`、`DXGI`、`Desktop Duplication API`、`DesktopDuplication`、`Vortice.DirectX`、`SkiaSharp`、`Image Compression`、`JPEG`。
*   **投屏与流媒体技术**：`stealcast`、`screen-share`、`screen-casting`、`screencast`、`realtime-streaming`、`low-latency`、`web-screencast`、`LAN-cast`。
*   **中文搜索热词**：`C#局域网投屏`、`C#屏幕共享`、`高性能屏幕广播`、`免管理员权限投屏`、`显存截屏`、`WinForms托盘后台运行`、`C#零拷贝压缩`、`开源投屏服务端`、`极简网页投屏`。

---

## 开源协议

本项目基于 **MIT 协议** 开源。任何人均可自由地克隆、修改、商业分发或集成到闭源产品中。
