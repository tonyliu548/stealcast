using System;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace stealcast
{
    /// <summary>
    /// 基于 DXGI Desktop Duplication API 的高性能显存截屏捕获器
    /// </summary>
    public class DxgiCapturer : IDisposable
    {
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _deviceContext;
        private IDXGIFactory1? _factory;
        private IDXGIAdapter1? _adapter;
        private IDXGIOutput1? _output1;
        private IDXGIOutputDuplication? _outputDuplication;
        private ID3D11Texture2D? _stagingTexture;

        private readonly object _lock = new();
        private bool _isInitialized;

        // DXGI 错误码定义
        private const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
        private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
        private const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);

        /// <summary>
        /// 初始化 DXGI 桌面复制环境
        /// </summary>
        public bool Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized) return true;

                try
                {
                    Cleanup();

                    // 1. 创建 D3D11 设备与上下文
                    var result = D3D11.D3D11CreateDevice(
                        null!,
                        DriverType.Hardware,
                        DeviceCreationFlags.None,
                        null!,
                        out _device,
                        out _deviceContext
                    );

                    if (result.Failure)
                    {
                        Log.Error("创建 Direct3D11 设备失败: {Result}", result);
                        return false;
                    }

                    // 2. 创建 DXGI 1.1 工厂
                    result = DXGI.CreateDXGIFactory1(out _factory);
                    if (result.Failure || _factory == null)
                    {
                        Log.Error("创建 DXGI 工厂失败: {Result}", result);
                        return false;
                    }

                    // 3. 获取主适配器（显卡）
                    result = _factory.EnumAdapters1(0, out _adapter);
                    if (result.Failure || _adapter == null)
                    {
                        Log.Error("枚举 DXGI 适配器失败: {Result}", result);
                        return false;
                    }

                    // 4. 获取主显示器输出
                    result = _adapter.EnumOutputs(0, out var output);
                    if (result.Failure || output == null)
                    {
                        Log.Error("枚举 DXGI 输出设备失败: {Result}", result);
                        return false;
                    }

                    // 5. 转换为 IDXGIOutput1（桌面复制 API 的入口）
                    _output1 = output.QueryInterface<IDXGIOutput1>();
                    output.Dispose(); // 释放原 output

                    if (_output1 == null)
                    {
                        Log.Error("查询 IDXGIOutput1 接口失败");
                        return false;
                    }

                    // 6. 创建桌面复制接口
                    try
                    {
                        _outputDuplication = _output1.DuplicateOutput(_device!);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "创建 DXGI 桌面复制接口失败，请确保没有在安全桌面（如 UAC 或锁屏界面）且主屏显示正常");
                        return false;
                    }

                    _isInitialized = true;
                    Log.Information("DXGI 桌面复制捕获器初始化成功，分辨率为: {Width}x{Height}", 
                        _output1.Description.DesktopCoordinates.Right - _output1.Description.DesktopCoordinates.Left,
                        _output1.Description.DesktopCoordinates.Bottom - _output1.Description.DesktopCoordinates.Top);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "初始化 DXGI 捕获器时发生未捕获的异常");
                    Cleanup();
                    return false;
                }
            }
        }

        /// <summary>
        /// 捕获主显示器单帧画面，并压缩为 JPEG 字节数组
        /// </summary>
        /// <param name="quality">JPEG 导出质量 (1-100)</param>
        /// <returns>压缩后的 JPEG 字节数组，捕获失败时返回 null</returns>
        public byte[]? CaptureFrame(int quality)
        {
            lock (_lock)
            {
                if (!_isInitialized && !Initialize())
                {
                    return null;
                }

                IDXGIResource? desktopResource = null;
                ID3D11Texture2D? screenTexture = null;

                try
                {
                    // 尝试抓取下一帧，设置超时为 100ms
                    var result = _outputDuplication!.AcquireNextFrame(100, out var frameInfo, out desktopResource);

                    if (result.Code == DXGI_ERROR_WAIT_TIMEOUT)
                    {
                        // 屏幕无变化，直接返回 null 即可，无需重新初始化
                        return null;
                    }

                    if (result.Failure)
                    {
                        // 发生错误，可能是由于分辨率更改或安全桌面导致连接丢失
                        Log.Warning("AcquireNextFrame 失败: {Result}，准备重试并重置状态", result);
                        _isInitialized = false;
                        return null;
                    }

                    // 成功获取帧，获取 2D 纹理对象
                    screenTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    if (screenTexture == null)
                    {
                        Log.Warning("无法获取帧纹理接口");
                        return null;
                    }

                    var desc = screenTexture.Description;
                    int width = (int)desc.Width;
                    int height = (int)desc.Height;

                    // 延迟或重新创建 CPU 暂存纹理（Staging Texture），保证尺寸合适且可读
                    if (_stagingTexture == null || 
                        _stagingTexture.Description.Width != (uint)width || 
                        _stagingTexture.Description.Height != (uint)height)
                    {
                        _stagingTexture?.Dispose();
                        
                        var stagingDesc = new Texture2DDescription
                        {
                            Width = (uint)width,
                            Height = (uint)height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm, // 桌面复制通常是 BGRA 格式
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Staging,
                            BindFlags = BindFlags.None,
                            CPUAccessFlags = CpuAccessFlags.Read
                        };

                        _stagingTexture = _device!.CreateTexture2D(stagingDesc);
                        Log.Information("创建了新的暂存纹理，尺寸: {Width}x{Height}", width, height);
                    }

                    // 将显存中捕获的屏幕纹理复制到 CPU 可读的暂存纹理中
                    _deviceContext!.CopyResource(_stagingTexture, screenTexture);

                    // 映射（Map）暂存纹理以获取 CPU 内存指针
                    var mapResult = _deviceContext.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mappedResource);
                    if (mapResult.Failure)
                    {
                        Log.Warning("映射暂存纹理失败: {Result}", mapResult);
                        return null;
                    }

                    byte[] jpegBytes;
                    try
                    {
                        // 使用 SkiaSharp 直接引用映射内存进行无拷贝图片编码，极大提高性能
                        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using (var bitmap = new SKBitmap())
                        {
                            // 使用映射出来的指针直接初始化 Skia 像素，避免多余的内存拷贝
                            bitmap.InstallPixels(imageInfo, mappedResource.DataPointer, (int)mappedResource.RowPitch);
                            
                            using (var image = SKImage.FromBitmap(bitmap))
                            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, quality))
                            {
                                jpegBytes = data.ToArray();
                            }
                        }
                    }
                    finally
                    {
                        // 必须解除映射
                        _deviceContext.Unmap(_stagingTexture, 0);
                    }

                    return jpegBytes;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "捕获帧并压缩时发生异常");
                    // 出现异常，重置状态
                    _isInitialized = false;
                    return null;
                }
                finally
                {
                    // 必须释放帧，否则 DXGI 将停止投递新帧
                    if (screenTexture != null)
                    {
                        screenTexture.Dispose();
                    }
                    if (desktopResource != null)
                    {
                        desktopResource.Dispose();
                    }

                    if (_isInitialized && _outputDuplication != null)
                    {
                        try
                        {
                            _outputDuplication.ReleaseFrame();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "ReleaseFrame 发生异常，可能桌面复制通道已丢失，准备重置");
                            _isInitialized = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 清理所有 DXGI 与 Direct3D 资源
        /// </summary>
        public void Cleanup()
        {
            lock (_lock)
            {
                _isInitialized = false;

                _stagingTexture?.Dispose();
                _stagingTexture = null;

                _outputDuplication?.Dispose();
                _outputDuplication = null;

                _output1?.Dispose();
                _output1 = null;

                _adapter?.Dispose();
                _adapter = null;

                _factory?.Dispose();
                _factory = null;

                _deviceContext?.Dispose();
                _deviceContext = null;

                _device?.Dispose();
                _device = null;

                Log.Debug("DXGI 截屏捕获器资源已清理释放");
            }
        }

        public void Dispose()
        {
            Cleanup();
            GC.SuppressFinalize(this);
        }
    }
}
