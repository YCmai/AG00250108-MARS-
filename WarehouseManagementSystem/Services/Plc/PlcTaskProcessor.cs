using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dapper;
using WarehouseManagementSystem.Db;
using WarehouseManagementSystem.Models.PLC;
using Client100.Entity;
using System.Collections.Concurrent;

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC任务处理器，用于处理AutoPlcTask表中的写入任务
    /// </summary>
    public class PlcTaskProcessor : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlcTaskProcessor> _logger;
        private readonly CancellationTokenSource _cts = new();
        private Task _processingTask;
        
        // 处理间隔配置
        private const int ProcessingInterval = 200; // 毫秒
        private const int ErrorRetryInterval = 1000; // 毫秒
        private const int MaxTasksPerBatch = 50; // 每批处理的最大任务数
        
        private readonly ConcurrentDictionary<string, Task> _ipProcessingTasks = new();
        
        public PlcTaskProcessor(
            IServiceProvider serviceProvider,
            ILogger<PlcTaskProcessor> logger) 
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }
        
        /// <summary>
        /// 启动任务处理器
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PLC任务处理器正在启动...");
            _processingTask = ProcessTasksAsync();
            _logger.LogInformation("PLC任务处理器已启动");
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// 停止任务处理器
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PLC任务处理器正在停止...");
            _cts.Cancel();
            
            if (_processingTask != null)
            {
                await Task.WhenAny(_processingTask, Task.Delay(5000));
            }
            
            _logger.LogInformation("PLC任务处理器已停止");
        }
        
        /// <summary>
        /// 任务处理循环
        /// </summary>
        private async Task ProcessTasksAsync()
        {
            _logger.LogInformation("PLC任务处理循环已启动");
            
            // 清理过期任务的计时器
            DateTime lastCleanupTime = DateTime.MinValue;
            const int cleanupIntervalHours = 6;
            
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 定期清理过期任务
                    if ((DateTime.Now - lastCleanupTime).TotalHours >= cleanupIntervalHours)
                    {
                        using var cleanupScope = _serviceProvider.CreateScope();
                        var cleanupDbService = cleanupScope.ServiceProvider.GetRequiredService<IDatabaseService>();
                        await CleanupExpiredTasksAsync(cleanupDbService);
                        lastCleanupTime = DateTime.Now;
                    }
                    
                    // 1. 获取服务和待处理任务
                    using var scope = _serviceProvider.CreateScope();
                    var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    var plcService = scope.ServiceProvider.GetRequiredService<IPlcCommunicationService>();

                    var pendingTasks = await GetPendingTasksWithIpAsync(dbService);
                    if (!pendingTasks.Any())
                    {
                        // 没有待处理任务，等待下一个周期
                        await Task.Delay(ProcessingInterval, _cts.Token);
                        continue;
                    }

                    // 2. 按IP地址分组任务
                    var tasksByIp = pendingTasks
                        .Where(t => !string.IsNullOrEmpty(t.PlcType))
                        .GroupBy(t => t.PlcType)
                        .ToList();

                    // 3. 为每个IP地址创建处理任务
                    foreach (var ipGroup in tasksByIp)
                    {
                        var ip = ipGroup.Key;
                        
                        // 检查该IP的处理任务是否已存在且正在运行
                        if (_ipProcessingTasks.TryGetValue(ip, out var existingTask) && !existingTask.IsCompleted)
                        {
                            // 已有任务正在处理该IP的命令，跳过
                            continue;
                        }
                        
                        // 创建并启动新的处理任务
                        var ipTasks = ipGroup.ToList();
                        var processingTask = ProcessIpTasksAsync(ip, ipTasks, dbService, plcService);
                        
                        // 注册任务完成后的清理操作
                        _ipProcessingTasks[ip] = processingTask.ContinueWith(t => 
                        {
                            // 任务完成后从字典中移除
                            _ipProcessingTasks.TryRemove(ip, out _);
                            
                            // 处理未捕获的异常
                            if (t.IsFaulted && t.Exception != null)
                            {
                                _logger.LogError(t.Exception.InnerException ?? t.Exception, 
                                    "处理IP {IpAddress} 的任务时发生未捕获的异常", ip);
                            }
                        }, TaskContinuationOptions.ExecuteSynchronously);
                    }
                    
                    // 等待下一个处理周期
                    await Task.Delay(ProcessingInterval, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 任务被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "任务处理循环发生错误");
                    await Task.Delay(ErrorRetryInterval, _cts.Token);
                }
            }
            
            _logger.LogInformation("PLC任务处理循环已停止");
        }
        
        /// <summary>
        /// 处理单个IP地址的所有任务
        /// </summary>
        private async Task ProcessIpTasksAsync(string ip, List<AutoPlcTaskWithIp> ipTasks, 
            IDatabaseService dbService, IPlcCommunicationService plcService)
        {
            try
            {
                // 按设备类型(PLCTypeDb)进一步分组
                var deviceGroups = ipTasks.GroupBy(t => new { t.PlcType, t.PLCTypeDb })
                    .Select(g => new { 
                        PlcType = g.Key.PlcType, 
                        PLCTypeDb = g.Key.PLCTypeDb, 
                        Tasks = g.Select(t => t as AutoPlcTask).ToList() 
                    })
                    .ToList();
                
                // 处理每个设备组
                foreach (var deviceGroup in deviceGroups)
                {
                    try
                    {
                        // 获取设备ID
                        int deviceId = await GetDeviceIdByPlcTypeAsync(dbService, deviceGroup.PlcType);
                        if (deviceId <= 0)
                        {
                            _logger.LogWarning("未找到匹配PlcType={PlcType}的设备，跳过相关任务", deviceGroup.PlcType);
                            continue;
                        }
                        
                        // 处理设备的所有任务
                        await ProcessDeviceTasksAsync(
                            dbService, 
                            plcService, 
                            deviceGroup.PlcType, 
                            deviceGroup.Tasks);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理IP {IpAddress}, 设备类型 {PLCTypeDb} 的任务组时发生错误", 
                            ip, deviceGroup.PLCTypeDb);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理IP {IpAddress} 的任务组时发生未处理的异常", ip);
                throw; // 重新抛出异常，让调用者知道处理失败
            }
        }

        /// <summary>
        /// 清理超过1天的已完成任务
        /// </summary>
        private async Task CleanupExpiredTasksAsync(IDatabaseService dbService)
        {
            try
            {
                using var conn = dbService.CreateConnection();
                
                // 清理一天前的已完成任务
                int deletedCount = await conn.ExecuteAsync(@"
                    DELETE FROM AutoPlcTasks
                    WHERE  CreatingTime < @ExpirationDate And IsSend =1",
                    new { ExpirationDate = DateTime.Now.AddDays(-1) });
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation("已清理 {DeletedCount} 条过期PLC任务", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期PLC任务失败");
            }
        }

        /// <summary>
        /// 获取待处理任务，包含IP地址信息
        /// </summary>
        private async Task<List<AutoPlcTaskWithIp>> GetPendingTasksWithIpAsync(IDatabaseService dbService)
        {
            using var conn = dbService.CreateConnection();

            // 查询未发送的任务，并关联设备IP地址
            var tasks = await conn.QueryAsync<AutoPlcTaskWithIp>(@"
                SELECT t.*, d.IpAddress 
                FROM AutoPlcTasks t
                LEFT JOIN RCS_PlcDevice d ON t.PlcType = d.IpAddress And t.PLCTypeDb = d.ModuleAddress
                WHERE t.IsSend = 0 
                ORDER BY t.CreatingTime ASC");

            return tasks.Take(MaxTasksPerBatch).ToList();
        }

        /// <summary>
        /// 包含IP地址的PLC任务
        /// </summary>
        private class AutoPlcTaskWithIp : AutoPlcTask
        {
            public string IpAddress { get; set; }
        }
        
        /// <summary>
        /// 获取信号ID - 使用IP地址、DB块和信号名称匹配
        /// </summary>
        private async Task<int> GetSignalIdAsync(IDatabaseService dbService, string ipAddress, string plcTypeDb, string signalName)
        {
            // 始终从数据库查询最新的信号ID，避免使用过时的缓存数据
            using var conn = dbService.CreateConnection();
            
            var signal = await conn.QueryFirstOrDefaultAsync<RCS_PlcSignal>(@"
                SELECT Id FROM RCS_PlcSignal 
                WHERE PlcDeviceId = @IpAddress 
                  AND PLCTypeDb = @PLCTypeDb 
                  AND Name = @SignalName",
                new { 
                    IpAddress = ipAddress, 
                    PLCTypeDb = plcTypeDb, 
                    SignalName = signalName 
                });
                
            if (signal != null)
            {
                return signal.Id;
            }
            
            return 0;
        }
        
        /// <summary>
        /// 处理单个设备的任务
        /// </summary>
        private async Task ProcessDeviceTasksAsync(IDatabaseService dbService, IPlcCommunicationService plcService, 
            string plcType, List<AutoPlcTask> tasks)
        {
            const int maxRetries = 2;
            
            for (int retryCount = 0; retryCount <= maxRetries; retryCount++)
            {
                try
                {
                    // 获取设备ID
                    int deviceId = await GetDeviceIdByPlcTypeAsync(dbService, plcType);
                    if (deviceId <= 0)
                    {
                        _logger.LogWarning("未找到匹配PlcType={PlcType}的设备，跳过相关任务", plcType);
                        return;
                    }
                    
                    // 按DB块分组处理任务
                    var taskGroups = tasks
                        .GroupBy(t => t.PLCTypeDb ?? string.Empty)
                        .ToDictionary(g => g.Key, g => g.OrderBy(t => t.CreatingTime).ToList());
                        
                    // 处理每个DB块的任务
                    foreach (var group in taskGroups)
                    {
                        string plcTypeDb = group.Key;
                        var orderedTasks = group.Value;
                        
                        // 记录日志（非心跳任务）
                        bool hasNormalTasks = !orderedTasks.All(t => t.Remark == "心跳信号");
                        if (hasNormalTasks)
                        {
                            _logger.LogInformation("开始处理PlcType={PlcType}, PLCTypeDb={PLCTypeDb}的任务组, 共{Count}条", 
                                plcType, plcTypeDb, orderedTasks.Count);
                        }
                        
                        // 按顺序处理任务
                        foreach (var task in orderedTasks)
                        {
                            await ProcessSingleTaskAsync(dbService, plcService, plcType, plcTypeDb, deviceId, task);
                        }
                    }
                    
                    // 成功处理完所有任务，退出重试循环
                    return;
                }
                catch (Exception ex) when (IsConnectionForciblyClosedException(ex))
                {
                    // 连接被强制关闭，尝试重试
                    if (retryCount < maxRetries)
                    {
                        int delayMs = 2000 * (retryCount + 1);
                        _logger.LogWarning(ex, "PLC连接被强制断开，等待{DelayMs}ms后第{RetryCount}次重试处理设备{PlcType}的任务", 
                            delayMs, retryCount + 1, plcType);
                            
                        // 尝试重置连接
                        await ResetDeviceConnectionAsync(dbService, plcService, plcType);
                        await Task.Delay(delayMs);
                    }
                    else
                    {
                        // 超过最大重试次数，记录错误并抛出异常
                        _logger.LogError(ex, "处理设备{PlcType}的任务失败，已达到最大重试次数", plcType);
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    // 其他未预期的错误
                    _logger.LogError(ex, "处理设备{PlcType}的任务时发生未预期的错误", plcType);
                    throw;
                }
            }
        }
        
        /// <summary>
        /// 判断异常是否为连接被强制关闭
        /// </summary>
        private bool IsConnectionForciblyClosedException(Exception ex)
        {
            return ex.Message.Contains("远程主机强迫关闭了一个现有的连接") ||
                   ex.Message.Contains("connection was forcibly closed") ||
                   (ex is System.IO.IOException && ex.InnerException?.Message.Contains("远程主机强迫关闭了一个现有的连接") == true) ||
                   (ex is S7.Net.PlcException && ex.InnerException?.Message.Contains("远程主机强迫关闭了一个现有的连接") == true);
        }
        
        /// <summary>
        /// 重置设备连接
        /// </summary>
        private async Task ResetDeviceConnectionAsync(IDatabaseService dbService, IPlcCommunicationService plcService, string plcType)
        {
            try
            {
                int deviceId = await GetDeviceIdByPlcTypeAsync(dbService, plcType);
                if (deviceId > 0)
                {
                    await plcService.ManualReadSignalsAsync(deviceId);
                    _logger.LogInformation("已触发设备{DeviceId}的PLC连接重置", deviceId);
                }
            }
            catch (Exception resetEx)
            {
                _logger.LogError(resetEx, "尝试重置设备{PlcType}的PLC连接失败", plcType);
            }
        }
        
        /// <summary>
        /// 处理单个任务
        /// </summary>
        private async Task ProcessSingleTaskAsync(IDatabaseService dbService, IPlcCommunicationService plcService,
            string plcType, string plcTypeDb, int deviceId, AutoPlcTask task)
        {
            var taskStart = DateTime.Now;
            _logger.LogInformation("开始处理任务: IP={PlcType}, DB={PLCTypeDb}, Signal={Signal}, Status={Status}, TaskId={Id}", 
                plcType, plcTypeDb, task.Signal, task.Status, task.Id);
                
            try
            {
                // 获取信号ID
                int signalId = await GetSignalIdAsync(dbService, plcType, plcTypeDb, task.Signal);
                
                // 执行写入操作
                await ExecuteTaskAsync(plcService, task, deviceId, signalId);
                
                
                var taskEnd = DateTime.Now;
                _logger.LogInformation("完成任务: IP={PlcType}, DB={PLCTypeDb}, Signal={Signal}, Status={Status}, TaskId={Id}, 耗时={Duration}ms", 
                    plcType, plcTypeDb, task.Signal, task.Status, task.Id, (taskEnd - taskStart).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行任务 ID={TaskId} 失败", task.Id);
                throw;
            }
        }
        
        /// <summary>
        /// 获取当前信号值
        /// </summary>
        private async Task<string> GetCurrentSignalValueAsync(IDatabaseService dbService, int signalId)
        {
            using var conn = dbService.CreateConnection();
            
            var signal = await conn.QueryFirstOrDefaultAsync<RCS_PlcSignal>(@"
                SELECT CurrentValue FROM RCS_PlcSignal 
                WHERE Id = @Id",
                new { Id = signalId });
                
            return signal?.CurrentValue ?? string.Empty;
        }
        
        /// <summary>
        /// 判断是否需要执行任务（比较当前值和目标值）
        /// </summary>
        private bool ShouldExecuteTask(AutoPlcTask task, string currentValue)
        {
            // 统一处理布尔型信号的字符串
            bool IsTrue(string val) => val != null && (val.Trim() == "1" || val.Trim().ToLower() == "true");
            bool IsFalse(string val) => val != null && (val.Trim() == "0" || val.Trim().ToLower() == "false");

            switch (task.Status)
            {
                case 1: // 写入布尔值 true
                    // 只有当前不是 true 时才需要写 true
                    return !IsTrue(currentValue);

                case 2: // 重置布尔值 false
                    // 只有当前不是 false 时才需要写 false
                    return !IsFalse(currentValue);

                case 5: // 写入字符串值
                    // 只有当前值和目标值不一致时才需要写
                    return currentValue != (task.Remark ?? string.Empty);

                case 6: // 重置字符串值（写入空字符串）
                    // 只有当前不是空字符串时才需要重置
                    return !string.IsNullOrEmpty(currentValue);

                default:
                    // 其他类型默认都需要执行
                    return true;
            }
        }
        
        /// <summary>
        /// 将任务值转换为字符串，方便后续比较
        /// </summary>
        private string ConvertTaskValueToString(AutoPlcTask task)
        {
            switch(task.Status)
            {
                case 1: // 写入布尔值 true
                    return "1";
                    
                case 2: // 重置布尔值 false
                    return "0";
                    
                case 3: // 写入整数值
                    return task.Signal;
                    
                case 4: // 重置整数值（写入0）
                    return "0";
                    
                case 5: // 写入字符串值
                    return task.Remark ?? string.Empty;
                    
                case 6: // 重置字符串值（写入空字符串）
                    return string.Empty;
                    
                default:
                    return string.Empty;
            }
        }
        
        /// <summary>
        /// 获取设备ID
        /// </summary>
        private async Task<int> GetDeviceIdByPlcTypeAsync(IDatabaseService dbService, string plcType)
        {
            using var conn = dbService.CreateConnection();
            
            var device = await conn.QueryFirstOrDefaultAsync<RCS_PlcDevice>(@"
                SELECT Id FROM RCS_PlcDevice 
                WHERE IpAddress = @IpAddress",
                new { IpAddress = plcType });
                
            return device?.Id ?? 0;
        }
        
        /// <summary>
        /// 获取待处理任务
        /// </summary>
        private async Task<List<AutoPlcTask>> GetPendingTasksAsync(IDatabaseService dbService)
        {
            using var conn = dbService.CreateConnection();
            
            // 查询未发送的任务
            // Status: 1=写入bool, 2=重置bool, 3=写入INT, 4=重置INT, 5=写入String, 6=重置String
            var tasks = await conn.QueryAsync<AutoPlcTask>(@"
                SELECT * FROM AutoPlcTasks 
                WHERE IsSend = 0 
                ORDER BY CreatingTime ASC");
                
            return tasks.Take(MaxTasksPerBatch).ToList();
        }
        
        /// <summary>
        /// 执行任务
        /// </summary>
        private async Task ExecuteTaskAsync(IPlcCommunicationService plcService, AutoPlcTask task, int deviceId, int signalId)
        {
            // 检查信号ID是否有效
            if (signalId <= 0)
            {
                _logger.LogWarning("未找到信号: 设备ID={DeviceId}, 信号名={SignalName}, 跳过任务", deviceId, task.Signal);
                return;
            }
            
            // 根据任务类型执行相应操作
            switch(task.Status)
            {
                case 1: // 写入布尔值 true
                    await plcService.WriteSignalValueAsync(deviceId, signalId, true);
                    break;
                    
                case 2: // 重置布尔值 false
                    await plcService.WriteSignalValueAsync(deviceId, signalId, false);
                    break;

                case 3: // 写入整数值
                    if (int.TryParse(task.Signal, out int intValue))
                    {
                        await plcService.WriteSignalValueAsync(deviceId, signalId, intValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"无法将信号值 '{task.Signal}' 解析为整数");
                    }
                    break;

                case 4: // 重置整数值（写入0）
                    await plcService.WriteSignalValueAsync(deviceId, signalId, 0);
                    break;

                case 5: // 写入字符串值
                    await plcService.WriteSignalValueAsync(deviceId, signalId, task.Remark ?? string.Empty);
                    break;
                    
                case 6: // 重置字符串值（写入空字符串）
                    await plcService.WriteSignalValueAsync(deviceId, signalId, string.Empty);
                    break;
                    
                default:
                    throw new NotSupportedException($"不支持的任务类型: {task.Status}");
            }
        }
        
        /// <summary>
        /// 标记任务为已处理
        /// </summary>
        private async Task MarkTaskAsProcessedAsync(IDatabaseService dbService, int taskId)
        {
            using var conn = dbService.CreateConnection();
            
            await conn.ExecuteAsync(@"
                UPDATE AutoPlcTasks 
                SET IsSend = 1, UpdateTime = @UpdateTime 
                WHERE ID = @ID",
                new { UpdateTime = DateTime.Now, ID = taskId });
        }
        
        /// <summary>
        /// 批量标记任务为已处理
        /// </summary>
        private async Task MarkTasksAsProcessedAsync(IDatabaseService dbService, List<string> taskIds)
        {
            if (taskIds == null || !taskIds.Any())
                return;
                
            using var conn = dbService.CreateConnection();
            
            // 构建IN查询参数
            string ids = string.Join(",", taskIds.Select((id, i) => $"@OrderCode{i}"));
            
            var parameters = new DynamicParameters();
            parameters.Add("UpdateTime", DateTime.Now);
            
            for (int i = 0; i < taskIds.Count; i++)
            {
                parameters.Add($"OrderCode{i}", taskIds[i]);
            }
            
            string sql = $@"
                UPDATE AutoPlcTasks
                SET IsSend = 1, UpdateTime = @UpdateTime 
                WHERE OrderCode IN ({ids})";
                
            await conn.ExecuteAsync(sql, parameters);
        }

        /// <summary>
        /// 比较任务的期望值和当前实际值
        /// </summary>
        private bool CompareTaskValueWithCurrentValue(AutoPlcTask task, string currentValue)
        {
            // 统一处理布尔型信号的字符串表示
            bool IsTrue(string val) => val != null && (val.Trim() == "1" || val.Trim().ToLower() == "true");
            bool IsFalse(string val) => val != null && (val.Trim() == "0" || val.Trim().ToLower() == "false");

            switch (task.Status)
            {
                case 1: // 写入布尔值 true
                    return IsTrue(currentValue);

                case 2: // 重置布尔值 false
                    return IsFalse(currentValue);

                case 5: // 写入字符串值
                    return currentValue == (task.Remark ?? string.Empty);

                case 6: // 重置字符串值（写入空字符串）
                    return string.IsNullOrEmpty(currentValue);

                case 3: // 写入整数值
                    if (int.TryParse(task.Signal, out int intTaskValue) && 
                        int.TryParse(currentValue, out int intCurrentValue))
                    {
                        return intTaskValue == intCurrentValue;
                    }
                    return false;

                case 4: // 重置整数值（写入0）
                    if (int.TryParse(currentValue, out int zeroValue))
                    {
                        return zeroValue == 0;
                    }
                    return false;

                default:
                    // 未知类型默认返回false
                    return false;
            }
        }
    }
} 