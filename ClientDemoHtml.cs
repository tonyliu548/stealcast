namespace stealcast
{
    /// <summary>
    /// 内置的客户端网页 HTML 源码实体类
    /// </summary>
    public static class ClientDemoHtml
    {
        public static readonly string HtmlContent = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <title>StealthCaster 社区版 - 投屏接收端</title>
    <style>
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body, html {
            width: 100%;
            height: 100%;
            overflow: hidden;
            background-color: #000000;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
        }

        /* 整体布局：垂直排列 */
        .viewport {
            display: flex;
            flex-direction: column;
            width: 100vw;
            height: 100vh;
        }

        /* 图像显示区域：占据全部剩余空间 */
        .screen-area {
            flex: 1;
            display: flex;
            align-items: center;
            justify-content: center;
            position: relative;
            background-color: #050505;
            overflow: hidden;
            width: 100%;
        }

        /* 投屏图像：最大化自适应，保持纵横比 */
        #screenImage {
            width: 100%;
            height: 100%;
            object-fit: contain;
            display: none;
            transition: opacity 0.15s ease;
        }

        /* 占位提示 */
        .placeholder {
            text-align: center;
            color: #4b5563;
            display: flex;
            flex-direction: column;
            gap: 12px;
            padding: 20px;
            z-index: 2;
        }

        .placeholder-icon {
            font-size: 4rem;
            animation: pulse 2s infinite;
        }

        @keyframes pulse {
            0% { transform: scale(1); opacity: 0.6; }
            50% { transform: scale(1.05); opacity: 1; }
            100% { transform: scale(1); opacity: 0.6; }
        }

        .placeholder p {
            font-size: 1.1rem;
            color: #9ca3af;
        }

        .placeholder span {
            font-size: 0.85rem;
            color: #6b7280;
        }

        /* 信息栏：置于最下方 */
        .info-bar {
            height: 40px;
            background-color: #0f172a;
            border-top: 1px solid #1e293b;
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 20px;
            color: #94a3b8;
            font-family: Consolas, Monaco, monospace;
            font-size: 0.85rem;
            z-index: 10;
        }

        .info-item {
            display: flex;
            align-items: center;
            gap: 6px;
        }

        /* 状态指示灯 */
        .status-dot {
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background-color: #ef4444;
            box-shadow: 0 0 6px #ef4444;
            display: inline-block;
            transition: all 0.3s ease;
        }

        .status-dot.connected {
            background-color: #10b981;
            box-shadow: 0 0 8px #10b981;
        }

        .status-dot.connecting {
            background-color: #f59e0b;
            box-shadow: 0 0 8px #f59e0b;
            animation: blink 1s infinite alternate;
        }

        @keyframes blink {
            from { opacity: 0.4; }
            to { opacity: 1; }
        }
    </style>
</head>
<body>

    <div class="viewport">
        <!-- 图像展示区域（最大化） -->
        <div class="screen-area">
            <img id="screenImage" alt="屏幕投屏画面">
            <div class="placeholder" id="placeholder">
                <div class="placeholder-icon">📺</div>
                <p>等待连接并按下全局快捷键</p>
                <span>网页版已启动自动连接，在电脑上按下注册的快捷键即刻显示画面</span>
            </div>
        </div>

        <!-- 信息栏（置于最下方） -->
        <div class="info-bar">
            <div class="info-item">
                <span class="status-dot" id="statusDot"></span>
                <span id="statusText">正在自动连接...</span>
            </div>
            <div class="info-item" style="display: flex; gap: 20px;">
                <span id="frameCount">帧数: 0</span>
                <span id="frameSize">大小: 0 KB</span>
                <span id="latency">更新时间: 无</span>
            </div>
        </div>
    </div>

    <script>
        const statusDot = document.getElementById('statusDot');
        const statusText = document.getElementById('statusText');
        const screenImage = document.getElementById('screenImage');
        const placeholder = document.getElementById('placeholder');
        const frameCountSpan = document.getElementById('frameCount');
        const frameSizeSpan = document.getElementById('frameSize');
        const latencySpan = document.getElementById('latency');

        let ws = null;
        let frameCount = 0;
        let reconnectTimer = null;

        function initWebSocket() {
            if (ws) {
                try { ws.close(); } catch(e) {}
            }

            statusDot.className = 'status-dot connecting';
            statusText.innerText = '正在连接投屏通道...';

            // 自动根据当前网页地址构建 WebSocket 地址 (例如 ws://192.168.1.100:28499/ws)
            const protocol = window.location.protocol === 'https:' ? 'wss://' : 'ws://';
            const wsUrl = `${protocol}${window.location.host}/ws`;

            ws = new WebSocket(wsUrl);
            ws.binaryType = 'arraybuffer'; // 接收二进制字节流

            ws.onopen = () => {
                statusDot.className = 'status-dot connected';
                statusText.innerText = '已连接';
                console.log('WebSocket 连接成功');
                if (reconnectTimer) {
                    clearInterval(reconnectTimer);
                    reconnectTimer = null;
                }
            };

            ws.onmessage = (event) => {
                const arrayBuffer = event.data;
                const blob = new Blob([arrayBuffer], { type: 'image/jpeg' });
                const urlCreator = window.URL || window.webkitURL;
                const imageUrl = urlCreator.createObjectURL(blob);

                // 替换并释放之前的 ObjectURL，防止浏览器内存泄漏
                const oldUrl = screenImage.src;
                screenImage.src = imageUrl;
                if (oldUrl && oldUrl.startsWith('blob:')) {
                    urlCreator.revokeObjectURL(oldUrl);
                }

                screenImage.style.display = 'block';
                placeholder.style.display = 'none';

                frameCount++;
                frameCountSpan.innerText = `帧数: ${frameCount}`;
                frameSizeSpan.innerText = `大小: ${(arrayBuffer.byteLength / 1024).toFixed(2)} KB`;
                
                const now = new Date();
                const pad = (n) => n.toString().padStart(2, '0');
                const timeStr = `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.${now.getMilliseconds().toString().substring(0, 2)}`;
                latencySpan.innerText = `时间: ${timeStr}`;
            };

            ws.onclose = () => {
                statusDot.className = 'status-dot';
                statusText.innerText = '连接已断开，尝试重连...';
                startReconnect();
            };

            ws.onerror = () => {
                statusDot.className = 'status-dot';
                statusText.innerText = '连接出错，尝试重连...';
                startReconnect();
            };
        }

        function startReconnect() {
            if (!reconnectTimer) {
                reconnectTimer = setInterval(() => {
                    console.log('正在尝试重新建立连接...');
                    initWebSocket();
                }, 2000); // 每2秒尝试重连一次
            }
        }

        // 页面加载完成后立即初始化
        window.addEventListener('load', () => {
            initWebSocket();
        });
    </script>
</body>
</html>
""";
    }
}
