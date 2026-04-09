using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;
using System.Data;
using WarehouseManagementSystem.Service.Io;
using WarehouseManagementSystem.Db;
using Dapper;

public class IOProcessorService : BackgroundService
{
    private readonly ILogger<IOProcessorService> _logger;
    private readonly IOAGVTaskProcessor _ioAgvTaskProcessor;
    private readonly IDatabaseService _db;
    private readonly ConcurrentDictionary<string, Task> _ipProcessingTasks = new();
    private readonly TimeSpan _signalUpdateInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _taskProcessInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _deviceCheckInterval = TimeSpan.FromSeconds(5); // 每5秒检查一次设备列表
    private readonly TimeSpan _networkCheckInterval = TimeSpan.FromSeconds(10); // 每10秒检查一次网络连接
    
    // 添加性能统计字典
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _signalProcessingTimes = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _taskProcessingTimes = new();
    private readonly int _maxStatsCount = 100; // 保留最近100次的处理时间
    
    // 记录当前处理的设备IP
    private readonly ConcurrentDictionary<string, bool> _activeDevices = new();
    // 记录设备的网络状态
    private readonly ConcurrentDictionary<string, bool> _deviceNetworkStatus = new();
    
    // 添加清理配置
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6); // 每6小时清理一次
    private DateTime _lastCleanupTime = DateTime.MinValue;
    
    // 清理配置参数
    private readonly int _completedTaskRetentionDays = 1; // 已完成任务保留天数
    private readonly int _pendingTaskRetentionDays = 7;   // 待处理任务保留天数
    private readonly int _maxCleanupBatchSize = 1000;     // 每次清理的最大批次大小

    public IOProcessorService(
        ILogger<IOProcessorService> logger,
        IOAGVTaskProcessor ioAgvTaskProcessor,
        IDatabaseService db)
    {
        _logger = logger;
        _ioAgvTaskProcessor = ioAgvTaskProcessor;
        _db = db;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IO处理服务已启动");
        
        // 启动性能统计日志任务
        _ = LogPerformanceStatsAsync(stoppingToken);
        
        // 启动网络连接检查任务
        _ = NetworkMonitoringAsync(stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 定期清理过期任务
                if ((DateTime.Now - _lastCleanupTime) >= _cleanupInterval)
                {
                    await CleanupExpiredTasksAsync();
                    _lastCleanupTime = DateTime.Now;
                }
                
                // 1. 获取所有需要处理的设备IP
                var deviceIps = await _ioAgvTaskProcessor.GetAllDeviceIpsAsync();
                
                // 记录新发现的设备
                bool hasNewDevices = false;
                foreach (var ip in deviceIps)
                {
                    if (!_activeDevices.ContainsKey(ip))
                    {
                        _logger.LogInformation("发现新的IO设备: {DeviceIP}", ip);
                        _activeDevices[ip] = true;
                        _deviceNetworkStatus[ip] = false; // 初始状态设为未连接，等待网络检查
                        hasNewDevices = true;
                        
                        // 立即检查新设备的网络连接
                        bool isConnected = await CheckNetworkConnectionAsync(ip);
                        _deviceNetworkStatus[ip] = isConnected;
                        _logger.LogInformation("设备 {DeviceIP} 的网络连接状态: {Status}", 
                            ip, isConnected ? "在线" : "离线");
                    }
                }
                
                // 移除不再活跃的设备
                var inactiveDevices = _activeDevices.Keys.Except(deviceIps).ToList();
                foreach (var ip in inactiveDevices)
                {
                    _logger.LogInformation("IO设备已移除或禁用: {DeviceIP}", ip);
                    _activeDevices.TryRemove(ip, out _);
                    _deviceNetworkStatus.TryRemove(ip, out _);
                }
                
                if (hasNewDevices || inactiveDevices.Any())
                {
                    _logger.LogInformation("当前活跃IO设备: {Count}个", deviceIps.Count);
                }
                
                // 2. 为每个IP创建单独的处理线程
                foreach (var ip in deviceIps)
                {
                    // 检查网络连接状态，如果离线则跳过处理
                    if (_deviceNetworkStatus.TryGetValue(ip, out bool isConnected) && !isConnected)
                    {
                        continue; // 跳过网络连接不可用的设备
                    }
                    
                    // 检查该IP的处理任务是否已存在且正在运行
                    string deviceTaskKey = $"{ip}_device";
                    if (!_ipProcessingTasks.TryGetValue(deviceTaskKey, out var deviceTask) || deviceTask.IsCompleted)
                    {
                        _logger.LogInformation("为设备 {DeviceIP} 创建处理任务", ip);
                        // 创建并启动新的设备处理任务
                        var processingTask = ProcessDeviceAsync(ip, stoppingToken);
                        
                        // 注册任务完成后的清理操作
                        _ipProcessingTasks[deviceTaskKey] = processingTask.ContinueWith(t => 
                        {
                            // 处理未捕获的异常
                            if (t.IsFaulted && t.Exception != null)
                            {
                                _logger.LogError(t.Exception.InnerException ?? t.Exception, 
                                    "处理IP {IpAddress} 的任务时发生未捕获的异常", ip);
                            }
                        }, TaskContinuationOptions.ExecuteSynchronously);
                    }
                }
                
                // 等待一段时间再检查设备列表
                await Task.Delay(_deviceCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IO处理服务循环发生错误");
                await Task.Delay(1000, stoppingToken);
            }
        }
        
        _logger.LogInformation("IO处理服务已停止");
    }
    
    /// <summary>
    /// 清理过期的IO任务数据
    /// </summary>
    private async Task CleanupExpiredTasksAsync()
    {
        try
        {
            _logger.LogInformation("开始清理过期IO任务数据...");
            
            using var conn = _db.CreateConnection();
            int totalDeletedCount = 0;
            
            // 1. 清理已完成且超过保留期的任务
            int completedDeletedCount = await CleanupCompletedTasksAsync(conn);
            totalDeletedCount += completedDeletedCount;
            
            // 2. 清理超过保留期的待处理任务（防止任务堆积）
            int pendingDeletedCount = await CleanupPendingTasksAsync(conn);
            totalDeletedCount += pendingDeletedCount;
            
            // 3. 可选：清理其他相关历史数据表（如果有的话）
            int otherDeletedCount = await CleanupOtherHistoricalDataAsync(conn);
            totalDeletedCount += otherDeletedCount;
            
            if (totalDeletedCount > 0)
            {
                _logger.LogInformation("IO任务清理完成，共清理 {TotalCount} 条记录", totalDeletedCount);
            }
            else
            {
                _logger.LogDebug("IO任务清理完成，没有需要清理的记录");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期IO任务失败");
        }
    }
    
    /// <summary>
    /// 清理已完成的过期任务
    /// </summary>
    private async Task<int> CleanupCompletedTasksAsync(IDbConnection conn)
    {
        try
        {
            var expirationDate = DateTime.Now.AddDays(-_completedTaskRetentionDays);
            
            // 分批删除，避免一次性删除过多数据
            int totalDeleted = 0;
            int batchDeleted;
            
            do
            {
                batchDeleted = await conn.ExecuteAsync(@"
                    DELETE TOP(@BatchSize) FROM RCS_IOAGV_Tasks
                    WHERE Status = 'Completed' 
                      AND CompletedTime < @ExpirationDate",
                    new { 
                        BatchSize = _maxCleanupBatchSize, 
                        ExpirationDate = expirationDate 
                    });
                
                totalDeleted += batchDeleted;
                
                // 如果还有数据需要删除，等待一小段时间避免阻塞
                if (batchDeleted == _maxCleanupBatchSize)
                {
                    await Task.Delay(100);
                }
            } while (batchDeleted == _maxCleanupBatchSize);
            
            if (totalDeleted > 0)
            {
                _logger.LogInformation("已清理 {DeletedCount} 条已完成且超过{RetentionDays}天的IO任务", 
                    totalDeleted, _completedTaskRetentionDays);
            }
            
            return totalDeleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理已完成IO任务失败");
            return 0;
        }
    }
    
    /// <summary>
    /// 清理过期的待处理任务
    /// </summary>
    private async Task<int> CleanupPendingTasksAsync(IDbConnection conn)
    {
        try
        {
            var expirationDate = DateTime.Now.AddDays(-_pendingTaskRetentionDays);
            
            int deletedCount = await conn.ExecuteAsync(@"
                DELETE FROM RCS_IOAGV_Tasks
                WHERE Status = 'Pending' 
                  AND CreatedTime < @ExpirationDate",
                new { ExpirationDate = expirationDate });
            
            if (deletedCount > 0)
            {
                _logger.LogInformation("已清理 {DeletedCount} 条超过{RetentionDays}天的待处理IO任务", 
                    deletedCount, _pendingTaskRetentionDays);
            }
            
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理待处理IO任务失败");
            return 0;
        }
    }
    
    /// <summary>
    /// 清理其他相关历史数据（预留扩展点）
    /// </summary>
    private async Task<int> CleanupOtherHistoricalDataAsync(IDbConnection conn)
    {
        try
        {
            // 这里可以添加清理其他相关历史数据的逻辑
            // 例如：清理IO信号的历史记录、清理设备连接日志等
            
            // 示例：如果有IO信号历史表，可以在这里清理
            // int signalHistoryDeleted = await conn.ExecuteAsync(@"
            //     DELETE FROM RCS_IOSignalHistory 
            //     WHERE RecordTime < @ExpirationDate",
            //     new { ExpirationDate = DateTime.Now.AddDays(-30) });
            
            return 0; // 目前没有其他需要清理的数据
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理其他历史数据失败");
            return 0;
        }
    }
    
    /// <summary>
    /// 网络连接监控任务
    /// </summary>
    private async Task NetworkMonitoringAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("网络连接监控任务已启动");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 检查所有活跃设备的网络连接
                foreach (var ip in _activeDevices.Keys)
                {
                    bool previousStatus = _deviceNetworkStatus.GetValueOrDefault(ip, false);
                    bool currentStatus = await CheckNetworkConnectionAsync(ip);
                    
                    // 更新网络状态
                    _deviceNetworkStatus[ip] = currentStatus;
                    
                    // 如果状态发生变化，记录日志
                    if (previousStatus != currentStatus)
                    {
                        if (currentStatus)
                        {
                            _logger.LogInformation("设备 {DeviceIP} 的网络连接已恢复", ip);
                        }
                        else
                        {
                            _logger.LogWarning("设备 {DeviceIP} 的网络连接已断开", ip);
                        }
                    }
                }
                
                // 等待一段时间再检查
                await Task.Delay(_networkCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "网络连接监控任务发生错误");
                await Task.Delay(1000, stoppingToken);
            }
        }
        
        _logger.LogInformation("网络连接监控任务已停止");
    }
    
    /// <summary>
    /// 检查网络连接状态
    /// </summary>
    private async Task<bool> CheckNetworkConnectionAsync(string ipAddress)
    {
        try
        {
            using Ping pinger = new Ping();
            PingReply reply = await pinger.SendPingAsync(IPAddress.Parse(ipAddress), 1000);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查IP {IpAddress} 的网络连接时出错", ipAddress);
            return false;
        }
    }
    
    /// <summary>
    /// 定期记录性能统计信息
    /// </summary>
    private async Task LogPerformanceStatsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 每30秒记录一次性能统计
                await Task.Delay(30000, stoppingToken);
                
                foreach (var entry in _signalProcessingTimes)
                {
                    var ip = entry.Key;
                    var times = entry.Value.ToArray();
                    if (times.Length > 0)
                    {
                        double avgTime = times.Average();
                        double maxTime = times.Max();
                        double minTime = times.Min();
                        
                        _logger.LogInformation(
                            "设备 {DeviceIP} 信号处理性能统计 - 平均: {AvgTime}ms, 最小: {MinTime}ms, 最大: {MaxTime}ms, 样本数: {Count}",
                            ip, avgTime.ToString("F2"), minTime, maxTime, times.Length);
                    }
                }
                
                foreach (var entry in _taskProcessingTimes)
                {
                    var ip = entry.Key;
                    var times = entry.Value.ToArray();
                    if (times.Length > 0)
                    {
                        double avgTime = times.Average();
                        double maxTime = times.Max();
                        double minTime = times.Min();
                        
                        _logger.LogInformation(
                            "设备 {DeviceIP} 任务处理性能统计 - 平均: {AvgTime}ms, 最小: {MinTime}ms, 最大: {MaxTime}ms, 样本数: {Count}",
                            ip, avgTime.ToString("F2"), minTime, maxTime, times.Length);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录性能统计信息时发生错误");
            }
        }
    }
    
    /// <summary>
    /// 处理单个设备的所有操作（串行执行信号读取和任务处理）
    /// </summary>
    private async Task ProcessDeviceAsync(string deviceIp, CancellationToken stoppingToken)
    {
        _logger.LogInformation("开始处理设备 {DeviceIP} 的IO操作", deviceIp);
        
        // 确保该IP有性能统计队列
        var signalProcessingTimes = _signalProcessingTimes.GetOrAdd(deviceIp, _ => new ConcurrentQueue<long>());
        var taskProcessingTimes = _taskProcessingTimes.GetOrAdd(deviceIp, _ => new ConcurrentQueue<long>());
        
        while (!stoppingToken.IsCancellationRequested && _activeDevices.ContainsKey(deviceIp))
        {
            try
            {
                // 1. 先处理任务（优先级更高）
                var taskStopwatch = Stopwatch.StartNew();
                try
                {
                   // _logger.LogDebug("设备 {DeviceIP} 开始处理任务", deviceIp);
                    await _ioAgvTaskProcessor.ProcessTasksForDevice(deviceIp);
                    
                    // 记录处理时间
                    taskStopwatch.Stop();
                    long taskElapsedMs = taskStopwatch.ElapsedMilliseconds;
                    
                    // 添加到性能统计队列
                    taskProcessingTimes.Enqueue(taskElapsedMs);
                    // 保持队列大小在限制范围内
                    while (taskProcessingTimes.Count > _maxStatsCount && taskProcessingTimes.TryDequeue(out _)) { }
                    
                    //_logger.LogDebug("设备 {DeviceIP} 任务处理完成，耗时: {ElapsedMs}ms", deviceIp, taskElapsedMs);
                    
                    // 如果处理时间过长，记录警告
                    if (taskElapsedMs > 1000)
                    {
                        _logger.LogWarning("设备 {DeviceIP} 任务处理耗时过长: {ElapsedMs}ms", deviceIp, taskElapsedMs);
                    }
                }
                catch (Exception ex)
                {
                    taskStopwatch.Stop();
                    _logger.LogError(ex, "处理设备 {DeviceIP} 的IO任务失败，耗时: {ElapsedMs}ms", 
                        deviceIp, taskStopwatch.ElapsedMilliseconds);
                }
                
                // 任务处理后等待一段时间
                await Task.Delay(_taskProcessInterval, stoppingToken);
                
                // 2. 再处理信号更新（优先级较低）
                var signalStopwatch = Stopwatch.StartNew();
                try
                {
                    //_logger.LogDebug("设备 {DeviceIP} 开始更新信号", deviceIp);
                    await _ioAgvTaskProcessor.UpdateIOSignalsForDevice(deviceIp);
                    
                    // 记录处理时间
                    signalStopwatch.Stop();
                    long signalElapsedMs = signalStopwatch.ElapsedMilliseconds;
                    
                    // 添加到性能统计队列
                    signalProcessingTimes.Enqueue(signalElapsedMs);
                    // 保持队列大小在限制范围内
                    while (signalProcessingTimes.Count > _maxStatsCount && signalProcessingTimes.TryDequeue(out _)) { }
                    
                   // _logger.LogDebug("设备 {DeviceIP} 信号更新完成，耗时: {ElapsedMs}ms", deviceIp, signalElapsedMs);
                    
                    // 如果处理时间过长，记录警告
                    if (signalElapsedMs > 1000)
                    {
                        _logger.LogWarning("设备 {DeviceIP} 信号更新耗时过长: {ElapsedMs}ms", deviceIp, signalElapsedMs);
                    }
                }
                catch (Exception ex)
                {
                    signalStopwatch.Stop();
                    _logger.LogError(ex, "更新设备 {DeviceIP} 的IO信号失败，耗时: {ElapsedMs}ms", 
                        deviceIp, signalStopwatch.ElapsedMilliseconds);
                }
                
                // 信号更新后等待一段时间
                await Task.Delay(_signalUpdateInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理设备 {DeviceIP} 的IO操作时发生未捕获的异常", deviceIp);
                await Task.Delay(1000, stoppingToken); // 错误后延长等待时间
            }
        }
        
        _logger.LogInformation("设备 {DeviceIP} 的IO处理任务已停止", deviceIp);
    }
}