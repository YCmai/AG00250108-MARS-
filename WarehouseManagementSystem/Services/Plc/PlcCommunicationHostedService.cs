using Microsoft.Extensions.Hosting;

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC通信托管服务，用于管理PLC通信服务的生命周期
    /// </summary>
    public class PlcCommunicationHostedService : IHostedService, IDisposable
    {
        private readonly IPlcCommunicationService _plcCommunicationService;
        private readonly ILogger<PlcCommunicationHostedService> _logger;
        
        // 2023-05-04修改：添加状态监控任务
        private Task _monitorTask;
        private CancellationTokenSource _monitorCts;
        private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(30); // 每30秒检查一次
        
        // 记录上次重置时间，避免频繁重置
        private Dictionary<string, DateTime> _lastResetTimes = new Dictionary<string, DateTime>();
        private readonly TimeSpan _minResetInterval = TimeSpan.FromMinutes(3); // 同一IP最少间隔3分钟重置

        // 记录上次服务重启时间
        private DateTime _lastServiceRestartTime = DateTime.MinValue;
        private readonly TimeSpan _minServiceRestartInterval = TimeSpan.FromMinutes(5); // 最少5分钟重启一次

        public PlcCommunicationHostedService(
            IPlcCommunicationService plcCommunicationService,
            ILogger<PlcCommunicationHostedService> logger)
        {
            _plcCommunicationService = plcCommunicationService ?? throw new ArgumentNullException(nameof(plcCommunicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("正在启动PLC通信托管服务...");
                
                // 尝试重置服务锁状态，防止信号量问题
                try
                {
                    _logger.LogInformation("尝试重置PLC通信服务锁状态...");
                    await _plcCommunicationService.ResetServiceLockAsync();
                }
                catch (SemaphoreFullException ex)
                {
                    _logger.LogError(ex, "服务锁重置时发生信号量已满异常，将尝试强制重置");
                    
                    // 信号量已满时，强制重置服务（在新线程中执行，防止阻塞当前线程）
                    ThreadPool.QueueUserWorkItem(async _ => 
                    {
                        try
                        {
                            // 等待一段时间，确保其他操作完成
                            await Task.Delay(1000);
                            
                            // 停止再启动服务
                            try
                            {
                                await _plcCommunicationService.StopServiceAsync();
                            }
                            catch (Exception stopEx)
                            {
                                _logger.LogError(stopEx, "停止服务失败，将直接尝试启动");
                            }
                            
                            // 启动服务
                            await _plcCommunicationService.StartServiceAsync();

                            
                            _logger.LogInformation("服务已在遇到信号量问题后重新启动成功");
                        }
                        catch (Exception restartEx)
                        {
                            _logger.LogError(restartEx, "信号量重置后重启服务失败");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "重置服务锁状态失败，继续启动");
                }
                
                await _plcCommunicationService.StartServiceAsync();
                
                // 2023-05-04修改：启动连接状态监控任务
                _monitorCts = new CancellationTokenSource();
                _monitorTask = MonitorConnectionStatusAsync(_monitorCts.Token);
                
                _logger.LogInformation("PLC通信托管服务已启动");
            }
            catch (SemaphoreFullException ex)
            {
                _logger.LogError(ex, "启动PLC通信托管服务时发生信号量已满异常");
                
                // 尝试在延迟后再次启动（在新线程中执行，防止阻塞当前线程）
                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    try
                    {
                        _logger.LogInformation("将在5秒后尝试重新启动服务...");
                        await Task.Delay(5000);
                        
                        // 尝试重置信号量
                        await _plcCommunicationService.ResetServiceLockAsync();
                        
                        // 再次启动服务
                        await _plcCommunicationService.StartServiceAsync();
                        
                        // 启动连接状态监控任务
                        _monitorCts = new CancellationTokenSource();
                        _monitorTask = MonitorConnectionStatusAsync(_monitorCts.Token);
                        
                        _logger.LogInformation("PLC通信托管服务已在延迟后重新启动");
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "延迟后重新启动服务失败");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动PLC通信托管服务失败");
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("正在停止PLC通信托管服务...");
                
                // 2023-05-04修改：停止监控任务
                if (_monitorCts != null)
                {
                    _monitorCts.Cancel();
                    try
                    {
                        if (_monitorTask != null)
                        {
                            // 等待监控任务完成，但设置超时
                            await Task.WhenAny(_monitorTask, Task.Delay(5000));
                        }
                    }
                    catch { }
                }
                
                await _plcCommunicationService.StopServiceAsync();
                _logger.LogInformation("PLC通信托管服务已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止PLC通信托管服务失败");
            }
        }
        
        /// <summary>
        /// 2023-05-04新增：监控PLC连接状态
        /// </summary>
        private async Task MonitorConnectionStatusAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PLC连接状态监控任务已启动");
            
            // 记录连续错误次数
            Dictionary<int, int> deviceErrorCounts = new Dictionary<int, int>();
            int maxErrorsBeforeRestart = 3; // 连续3次错误就重启服务
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 添加超时控制，避免长时间阻塞
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    
                    try
                    {
                        // 检查所有PLC设备的连接状态
                        var statusDict = await _plcCommunicationService.GetServiceStatusAsync().WaitAsync(linkedCts.Token);
                        
                        if (statusDict != null && statusDict.Count > 0)
                        {
                            // 分析连接状态，查找异常的连接
                            var offlineDevices = statusDict.Where(kv => !kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
                            
                            if (offlineDevices.Any())
                            {
                                _logger.LogWarning("检测到 {Count} 个PLC设备连接离线", offlineDevices.Count);
                                
                                // 获取需要重置的IP地址
                                bool needServiceRestart = false;
                                
                                foreach (var deviceId in offlineDevices.Keys)
                                {
                                    try
                                    {
                                        // 增加设备错误计数
                                        if (!deviceErrorCounts.ContainsKey(deviceId))
                                        {
                                            deviceErrorCounts[deviceId] = 0;
                                        }
                                        deviceErrorCounts[deviceId]++;
                                        
                                        // 如果错误次数超过阈值，标记需要重启服务
                                        if (deviceErrorCounts[deviceId] >= maxErrorsBeforeRestart)
                                        {
                                            _logger.LogWarning("设备 {DeviceId} 连续 {Count} 次出现连接问题，将重启服务", 
                                                deviceId, deviceErrorCounts[deviceId]);
                                            needServiceRestart = true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "处理设备 {DeviceId} 连接问题时发生错误", deviceId);
                                    }
                                }
                                
                                // 如果需要重启服务
                                if (needServiceRestart && ShouldRestartService())
                                {
                                    try
                                    {
                                        _logger.LogWarning("检测到严重的PLC连接问题，正在重启PLC通信服务...");
                                        await _plcCommunicationService.RestartServiceAsync();
                                        _logger.LogInformation("PLC通信服务重启成功");
                                        
                                        // 重置所有设备的错误计数
                                        deviceErrorCounts.Clear();
                                        
                                        // 记录最后重启时间
                                        _lastServiceRestartTime = DateTime.Now;
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "重启PLC通信服务失败");
                                    }
                                }
                            }
                            else
                            {
                                // 所有设备在线，重置错误计数
                                deviceErrorCounts.Clear();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        _logger.LogWarning("获取PLC设备状态超时，跳过本次检查");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "监控PLC连接状态时发生错误");
                }
                
                // 等待下一次检查
                try
                {
                    await Task.Delay(_monitorInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break; // 任务取消
                }
            }
            
            _logger.LogInformation("PLC连接状态监控任务已停止");
        }
        
        /// <summary>
        /// 2023-05-04新增：判断是否为连接被强制关闭的异常
        /// </summary>
        private bool IsConnectionForciblyClosedException(Exception ex)
        {
            if (ex == null) return false;
            
            // 检查异常消息
            if (ex.Message.Contains("远程主机强迫关闭了一个现有的连接") || 
                ex.Message.Contains("connection was forcibly closed") ||
                ex.Message.Contains("10054") ||  // 添加Socket错误代码
                ex.Message.Contains("Unable to write data to the transport connection")) 
                return true;
            
            // 检查内部异常
            if (ex.InnerException != null)
            {
                return IsConnectionForciblyClosedException(ex.InnerException);
            }
            
            // 检查是否是S7.Net.PlcException类型
            if (ex.GetType().Name == "PlcException" && ex.InnerException != null)
            {
                if (ex.InnerException.GetType().Name == "IOException")
                    return true;
            }
            
            // 检查是否是SocketException
            if (ex.GetType().Name == "SocketException")
            {
                // 尝试获取错误代码属性
                try {
                    var errorCodeProp = ex.GetType().GetProperty("ErrorCode");
                    if (errorCodeProp != null)
                    {
                        var errorCode = errorCodeProp.GetValue(ex);
                        if (errorCode != null && errorCode.ToString() == "10054")
                            return true;
                    }
                    
                    // 另一种方式：检查异常号属性
                    var nativeProp = ex.GetType().GetProperty("NativeErrorCode");
                    if (nativeProp != null)
                    {
                        var nativeCode = nativeProp.GetValue(ex);
                        if (nativeCode != null && nativeCode.ToString() == "10054")
                            return true;
                    }
                }
                catch {
                    // 忽略反射错误
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 2023-05-04新增：从异常中提取IP地址
        /// </summary>
        private string ExtractIpAddressFromException(Exception ex)
        {
            try
            {
                // 默认尝试从异常消息中提取IP地址
                var message = ex.Message + (ex.InnerException != null ? " " + ex.InnerException.Message : "");
                
                // 使用正则表达式查找IP地址
                var match = System.Text.RegularExpressions.Regex.Match(message, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
                if (match.Success)
                {
                    return match.Value;
                }
                
                return null;
            }
            catch
            {
                return null; // 提取失败则返回null
            }
        }
        
        /// <summary>
        /// 2023-05-04新增：判断是否应该重置连接
        /// </summary>
        private bool ShouldResetConnection(string ipAddress)
        {
            if (_lastResetTimes.TryGetValue(ipAddress, out DateTime lastReset))
            {
                // 如果上次重置时间太近，不再重置
                return (DateTime.Now - lastReset) > _minResetInterval;
            }
            
            // 没有重置记录，应该重置
            return true;
        }
        
        /// <summary>
        /// 2023-05-04新增：判断是否应该重启服务
        /// </summary>
        private bool ShouldRestartService()
        {
            // 检查距离上次重启是否已经超过最小间隔
            return (DateTime.Now - _lastServiceRestartTime) > _minServiceRestartInterval;
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 释放监控任务的资源
            if (_monitorCts != null)
            {
                _monitorCts.Dispose();
                _monitorCts = null;
            }
        }

        
    }
} 