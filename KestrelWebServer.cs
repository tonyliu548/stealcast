using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace stealcast
{
    /// <summary>
    /// 基于 ASP.NET Core Kestrel 的高性能、零权限混合 Web/WS 服务端
    /// </summary>
    public class KestrelWebServer : IDisposable
    {
        private readonly int _port;
        private WebApplication? _app;
        private readonly ConcurrentDictionary<WebSocket, byte> _sockets = new();
        private CancellationTokenSource? _cts;

        public KestrelWebServer(int port)
        {
            _port = port;
        }

        /// <summary>
        /// 启动 Kestrel 混合服务器
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            
            var builder = WebApplication.CreateBuilder();
            
            // 屏蔽 ASP.NET Core 默认日志以避免冗余的系统事件记录
            builder.Logging.ClearProviders();

            // 配置 Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                // 监听任意 IP（无需管理员权限即可绑定 0.0.0.0）
                options.ListenAnyIP(_port);
            });

            _app = builder.Build();

            // 1. 启用 WebSocket 中间件支持
            _app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120)
            });

            // 2. 路由：WebSocket 处理连接
            _app.Map("/ws", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    _sockets.TryAdd(webSocket, 0);
                    Log.Information("新的 WebSocket 客户端已连接: {Remote}", context.Connection.RemoteIpAddress);

                    var buffer = new byte[1024];
                    try
                    {
                        // 挂起线程以维持连接生命周期
                        while (webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                        {
                            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", _cts.Token);
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略连接断开导致的读取异常
                    }
                    finally
                    {
                        _sockets.TryRemove(webSocket, out _);
                        Log.Information("WebSocket 客户端已断开: {Remote}", context.Connection.RemoteIpAddress);
                    }
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });

            // 3. 路由：HTTP 首页请求，直接返回内置的网页源码
            _app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(ClientDemoHtml.HtmlContent, _cts.Token);
            });

            // 异步开启 Web 服务
            _app.StartAsync(_cts.Token);
            Log.Information("Kestrel 混合 Web/WebSocket 服务端成功运行于端口: {Port}", _port);
        }

        /// <summary>
        /// 向所有当前活跃的客户端广播二进制 JPEG 图片帧数据
        /// </summary>
        /// <param name="jpegBytes">图片字节流</param>
        public async Task BroadcastBinaryAsync(byte[] jpegBytes)
        {
            if (jpegBytes == null || jpegBytes.Length == 0 || _sockets.IsEmpty) return;

            Log.Debug("正在向 {Count} 个客户端广播图片帧 ({Bytes} 字节)...", _sockets.Count, jpegBytes.Length);

            var activeSockets = _sockets.Keys;
            var tasks = new List<Task>();

            foreach (var socket in activeSockets)
            {
                if (socket.State == WebSocketState.Open)
                {
                    tasks.Add(SendToSocketAsync(socket, jpegBytes));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private async Task SendToSocketAsync(WebSocket socket, byte[] data)
        {
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning("WebSocket 帧数据发送失败: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// 关闭服务器并清理连接
        /// </summary>
        public void Dispose()
        {
            _cts?.Cancel();
            
            // 关闭所有客户端连接
            foreach (var socket in _sockets.Keys)
            {
                try
                {
                    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server Shutdown", CancellationToken.None).Wait(1000);
                    socket.Dispose();
                }
                catch { }
            }
            _sockets.Clear();

            if (_app != null)
            {
                try
                {
                    _app.StopAsync(CancellationToken.None).Wait(2000);
                    _app.DisposeAsync().AsTask().Wait(2000);
                }
                catch { }
                _app = null;
            }

            _cts?.Dispose();
            _cts = null;
            
            Log.Information("Kestrel 混合 Web 服务端已停止并注销");
            GC.SuppressFinalize(this);
        }
    }
}
