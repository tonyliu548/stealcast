using System.Text.Json;

namespace stealcast
{
    /// <summary>
    /// 应用程序配置实体类
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// WebSocket 监听端口（默认 28499）
        /// </summary>
        public int Port { get; set; } = 28499;

        /// <summary>
        /// 触发截图的全局快捷键组合字符串（如 "Ctrl+Alt+S"）
        /// </summary>
        public string HotKey { get; set; } = "Ctrl+Alt+S";

        /// <summary>
        /// 触发显示/隐藏 GUI 窗口的全局快捷键组合字符串（如 "Ctrl+Alt+H"）
        /// </summary>
        public string ShowGuiHotKey { get; set; } = "Ctrl+Alt+H";

        /// <summary>
        /// 导出 JPEG 图片的质量（1-100，默认 80）
        /// </summary>
        public int JpegQuality { get; set; } = 80;
    }
}
