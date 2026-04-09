using System.Data;
using System.Net;
using System.Threading;
using System.Linq;
using System.Collections.Concurrent;

using Dapper;

using WarehouseManagementSystem.Db;
using WarehouseManagementSystem.Models.IO;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
namespace WarehouseManagementSystem.Service.Io
{
    public class IOAGVTaskProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatabaseService _db;
        private readonly ILogger<IOAGVTaskProcessor> _logger;
        private readonly IIOService _ioService;
        private static readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);  // 添加静态锁
        
        // 添加设备锁字典，每个IP地址一个锁
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new();

        public IOAGVTaskProcessor(IServiceProvider serviceProvider, IDatabaseService db, IIOService ioService, ILogger<IOAGVTaskProcessor> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _db = db;
            _ioService = ioService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 保留原有的后台服务逻辑，但实际处理将由各IP线程完成
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        /// <summary>
        /// 获取所有启用的IO设备IP地址
        /// </summary>
        public async Task<List<string>> GetAllDeviceIpsAsync()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var devices = await conn.QueryAsync<RCS_IODevices>("SELECT * FROM RCS_IODevices WHERE IsEnabled = 1");
                return devices.Select(d => d.IP).Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取IO设备IP地址失败");
                return new List<string>();
            }
        }

        /// <summary>
        /// 更新指定IP设备的所有IO信号
        /// </summary>
        public async Task UpdateIOSignalsForDevice(string deviceIp)
        {
            // 获取或创建设备锁
            var deviceLock = _deviceLocks.GetOrAdd(deviceIp, _ => new SemaphoreSlim(1, 1));
            
            // 使用设备锁确保同一设备的操作串行执行
            await deviceLock.WaitAsync();
            try
            {
                using var conn = _db.CreateConnection();
                
                // 只查询指定IP的设备
                var device = await conn.QueryFirstOrDefaultAsync<RCS_IODevices>(
                    "SELECT * FROM RCS_IODevices WHERE IP = @IP AND IsEnabled = 1", 
                    new { IP = deviceIp });
                
                if (device == null)
                {
                    return;
                }
                
               // _logger.LogDebug("设备 {DeviceIP}({DeviceName}) 开始更新信号", 
                    //deviceIp, device.Name);
                
                var signals = await conn.QueryAsync<RCS_IOSignals>(
                    "SELECT * FROM RCS_IOSignals WHERE DeviceId = @DeviceId",
                    new { DeviceId = device.Id });

                foreach (var signal in signals)
                {
                    try
                    {
                        if (!Enum.TryParse<EIOAddress>(signal.Address, out EIOAddress addressEnum))
                        {
                            continue;
                        }

                        // 读取信号值
                        var value = await _ioService.ReadSignal(device.IP, addressEnum);
                        signal.UpdatedTime = DateTime.Now;
                        
                        // 将bool值转换为int (true=1, false=0)
                        signal.Value = value ? 1 : 0;
                        
                        await conn.ExecuteAsync(
                            "UPDATE RCS_IOSignals SET Value = @Value, UpdatedTime = @UpdatedTime WHERE Id = @Id",
                            new { signal.Value, signal.UpdatedTime, signal.Id });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "读取设备{DeviceName}({DeviceIP})信号{Address}失败", 
                            device.Name, device.IP, signal.Address);
                        continue; // 继续读取下一个信号
                    }
                }
                
               // _logger.LogDebug("设备 {DeviceIP}({DeviceName}) 完成更新 {Count} 个信号", 
                   // deviceIp, device.Name, signals.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备{DeviceIP}的IO信号失败", deviceIp);
            }
            finally
            {
                deviceLock.Release();
            }
        }

        /// <summary>
        /// 处理指定IP设备的待执行任务
        /// </summary>
        public async Task ProcessTasksForDevice(string deviceIp)
        {
            // 获取或创建设备锁
            var deviceLock = _deviceLocks.GetOrAdd(deviceIp, _ => new SemaphoreSlim(1, 1));
            
            // 使用设备锁确保同一设备的操作串行执行
            await deviceLock.WaitAsync();
            try
            {
                using var conn = _db.CreateConnection();

                // 只查询指定IP的Pending状态任务
                var tasks = await conn.QueryAsync<RCS_IOAGV_Tasks>(@"
                    SELECT * 
                    FROM RCS_IOAGV_Tasks
                    WHERE Status = 'Pending' AND DeviceIP = @DeviceIP
                    ORDER BY CreatedTime ASC",
                    new { DeviceIP = deviceIp });

                var taskList = tasks.ToList();
                if (!taskList.Any())
                {
                    return;
                }

                _logger.LogInformation($"开始处理设备 {deviceIp} 的任务组，共{taskList.Count}条");

                foreach (var task in taskList)
                {
                    try
                    {
                        if (!Enum.TryParse<EIOAddress>(task.SignalAddress, out EIOAddress addressEnum))
                        {
                            _logger.LogWarning($"无效的信号地址: IP={task.DeviceIP}, Address={task.SignalAddress}");
                            continue;
                        }

                        bool success = false;
                        try
                        {
                            switch (task.TaskType)
                            {
                                case "ArrivalNotify":
                                case "PassComplete":
                                    // 直接从PLC读取信号
                                    var plcCurrentValue = await _ioService.ReadSignal(task.DeviceIP, addressEnum);
                                    if (plcCurrentValue == task.Value)
                                    {
                                        // 如果当前值已经是目标值，添加短暂延迟后再次确认
                                        await Task.Delay(300);
                                        var secondReadValue = await _ioService.ReadSignal(task.DeviceIP, addressEnum);
                                        if (secondReadValue == task.Value)
                                        {
                                            success = true;
                                            _logger.LogInformation($"二次确认信号已经是目标值 - TaskId: {task.Id}, Device: {task.DeviceIP}, Address: {task.SignalAddress}, Value: {task.Value}");
                                        }
                                        else
                                        {
                                            _logger.LogWarning($"二次读取信号值不一致 - TaskId: {task.Id}, 第一次: {plcCurrentValue}, 第二次: {secondReadValue}, 目标值: {task.Value}");
                                            // 尝试写入
                                            await _ioService.WriteSignal(task.DeviceIP, addressEnum, task.Value);
                                            // 添加延迟后验证
                                            await Task.Delay(300);
                                            var verifiedValue = await _ioService.ReadSignal(task.DeviceIP, addressEnum);
                                            success = (verifiedValue == task.Value);
                                        }
                                    }
                                    else
                                    {
                                        // 值不同时才写入
                                        await _ioService.WriteSignal(task.DeviceIP, addressEnum, task.Value);
                                        // 添加延迟后验证
                                        await Task.Delay(300);
                                        var verifiedValue = await _ioService.ReadSignal(task.DeviceIP, addressEnum);
                                        success = (verifiedValue == task.Value);
                                        // 如果第一次验证失败，再尝试一次
                                        if (!success)
                                        {
                                            _logger.LogWarning($"第一次验证失败，再次尝试 - TaskId: {task.Id}, Device: {task.DeviceIP}, Address: {task.SignalAddress}");
                                            await Task.Delay(300); // 延长等待时间
                                            verifiedValue = await _ioService.ReadSignal(task.DeviceIP, addressEnum);
                                            success = (verifiedValue == task.Value);
                                        }
                                    }
                                    break;
                                case "PassCheck":
                                    // 从数据库读取通行信号
                                    success = await GetSignalValueFromDatabase(conn, task.DeviceIP, task.SignalAddress);
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "处理任务失败 - TaskId: {TaskId}, Device: {DeviceIP}, Address: {Address}",
                                task.Id, task.DeviceIP, task.SignalAddress);
                            success = false;
                        }

                        if (success)
                        {
                            await UpdateTaskStatus(conn, task.Id, true);
                            _logger.LogInformation(
                                "任务处理成功 - TaskId: {TaskId}, Type: {TaskType}, Device: {DeviceIP}, Address: {Address}, 耗时: {Duration}ms",
                                task.Id, task.TaskType, task.DeviceIP, task.SignalAddress,
                                (DateTime.Now - task.CreatedTime).TotalMilliseconds);
                        }
                        else
                        {
                            // 如果任务失败，跳过该IP的后续任务
                            _logger.LogWarning(
                                "任务处理失败，跳过该设备的后续任务 - TaskId: {TaskId}, Type: {TaskType}, Device: {DeviceIP}, Address: {Address}",
                                task.Id, task.TaskType, task.DeviceIP, task.SignalAddress);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "处理任务失败，跳过该设备的后续任务 - TaskId: {TaskId}, Type: {TaskType}, Device: {DeviceIP}, Address: {Address}",
                            task.Id, task.TaskType, task.DeviceIP, task.SignalAddress);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理设备{DeviceIP}的IO任务失败", deviceIp);
            }
            finally
            {
                deviceLock.Release();
            }
        }

        // 保留原有的公共方法，但修改为使用新的按IP处理的方法
        public async Task ProcessTasks()
        {
            try
            {
                // 获取所有设备IP
                var deviceIps = await GetAllDeviceIpsAsync();
                
                // 并行处理所有设备的任务
                var tasks = deviceIps.Select(ip => ProcessTasksForDevice(ip));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理IO任务失败");
            }
        }

        public async Task UpdateIOSignals()
        {
            try
            {
                // 获取所有设备IP
                var deviceIps = await GetAllDeviceIpsAsync();
                
                // 并行处理所有设备的信号更新
                var tasks = deviceIps.Select(ip => UpdateIOSignalsForDevice(ip));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新IO信号失败");
            }
        }

        // 从数据库获取信号值
        private async Task<bool> GetSignalValueFromDatabase(IDbConnection conn, string deviceIP, string signalAddress)
        {
            try
            {
                // 查询设备ID
                var deviceId = await conn.QueryFirstOrDefaultAsync<int>(@"
                    SELECT Id FROM RCS_IODevices 
                    WHERE IP = @DeviceIP AND IsEnabled = 1", 
                    new { DeviceIP = deviceIP });

                if (deviceId == 0)
                {
                    _logger.LogWarning($"无法找到设备: IP={deviceIP}");
                    return false;
                }

                // 查询信号值
                var signalValue = await conn.QueryFirstOrDefaultAsync<bool?>(@"
                    SELECT Value FROM RCS_IOSignals 
                    WHERE DeviceId = @DeviceId AND Address = @Address",
                    new { DeviceId = deviceId, Address = signalAddress });

                if (signalValue == null)
                {
                    _logger.LogWarning($"无法找到信号: DeviceIP={deviceIP}, Address={signalAddress}");
                    return false;
                }

                return signalValue.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"从数据库获取信号值失败: DeviceIP={deviceIP}, Address={signalAddress}");
                return false;
            }
        }

        // VerifyIOSignal方法也应该修改为从数据库读取
        private async Task<bool> VerifyIOSignal(string deviceIP, EIOAddress address, bool expectedValue)
        {
            try
            {
                using var conn = _db.CreateConnection();
                var actualValue = await GetSignalValueFromDatabase(conn, deviceIP, address.ToString());
                if (actualValue == expectedValue)
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"验证IO信号失败 - Device: {deviceIP}, Address: {address}");
                return false;
            }
        }

        private async Task UpdateTaskStatus(IDbConnection conn, int taskId, bool isCompleted)
        {
            await conn.ExecuteAsync(
                @"UPDATE RCS_IOAGV_Tasks 
                SET Status = @Status, 
                    CompletedTime = @CompletedTime,
                    LastUpdatedTime = @LastUpdatedTime
                WHERE Id = @Id",
                new
                {
                    Id = taskId,
                    Status = isCompleted ? "Completed" : "Pending",
                    CompletedTime = isCompleted ? DateTime.Now : (DateTime?)null,
                    LastUpdatedTime = DateTime.Now
                });
        }

        // AGV任务实体类
        public class RCS_IOAGV_Tasks
        {
            public int Id { get; set; }
            /// <summary>
            /// 任务类型：ArrivalNotify(到达通知), PassCheck(通行检查), PassComplete(通行完成)
            /// </summary>
            public string TaskType { get; set; }
            /// <summary>
            /// 任务状态：Pending(待处理), Completed(已完成)
            /// </summary>
            public string Status { get; set; }
            /// <summary>
            /// IO设备IP地址
            /// </summary>
            public string DeviceIP { get; set; }
            /// <summary>
            ///  IO信号地址
            /// </summary>
            public string SignalAddress { get; set; }

            public DateTime CreatedTime { get; set; }
            /// <summary>
            /// 完成时间
            /// </summary>
            public DateTime? CompletedTime { get; set; }
            /// <summary>
            ///  最后更新时间
            /// </summary>
            public DateTime? LastUpdatedTime { get; set; }


            public string TaskId { get; set; }


            public bool Value { get; set; }

        }

        public enum TaskType
        {
            ArrivalNotify,
            PassCheck,
            PassComplete
        }

        public enum TaskStatus
        {
            Pending,
            Completed,
            Failed
        }

    }
}
