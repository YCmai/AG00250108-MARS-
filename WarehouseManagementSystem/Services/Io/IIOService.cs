using System.Net.Sockets;
using System.Net;
using NModbus;
using System.Data;
using WarehouseManagementSystem.Models.IO;
using WarehouseManagementSystem.Db;
using Dapper;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using static WarehouseManagementSystem.Service.Io.IOAGVTaskProcessor;

namespace WarehouseManagementSystem.Service.Io
{
    // Services/IIOService.cs
    public interface IIOService
    {
        Task<bool> Conn(string ip);
        Task<bool> ReadSignal(string ip, EIOAddress address);
        Task<bool> WriteSignal(string ip, EIOAddress address, bool value);
        List<Remote_IO_Info> GetConnectedClients();

        Task UpdateDeviceMonitoring(int deviceId, bool isEnabled);

        Task StartDeviceMonitoring();

        // 新增添加IO任务的接口
        Task<int> AddIOTask(string taskType, string deviceIP, string signalAddress, bool value, string taskId);
    }

    // Models/Remote_IO_Info.cs
    public class Remote_IO_Info
    {
        public string IP { get; set; }
        public ModbusFactory NModbus { get; set; }
        public TcpClient Master_TcpClient { get; set; }
        public IModbusMaster Master { get; set; }
    }

    // Services/IOService.cs
    public class IOService : IIOService
    {
        private readonly ILogger<IOService> _logger;
        private readonly List<Remote_IO_Info> io_List = new();
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private readonly IDatabaseService _db;
        private readonly IServiceProvider _serviceProvider;
        private readonly IIODeviceService _ioDeviceService;
     
        // 定义 _monitoringTasks
        private readonly ConcurrentDictionary<string, (Task, CancellationTokenSource)> _monitoringTasks = new();

        public IOService(
            ILogger<IOService> logger, IDatabaseService db, IIODeviceService ioDeviceService, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _db = db;
            _serviceProvider = serviceProvider;
            _monitoringTasks = new ConcurrentDictionary<string, (Task, CancellationTokenSource)>();
            _ioDeviceService = ioDeviceService;
        }

        public List<Remote_IO_Info> GetConnectedClients()
        {
            return io_List;
        }

     

        public async Task<bool> Conn(string ip)
        {
            try
            {
                _logger.LogInformation($"IO_【{ip}】尝试连接");
                var remoteInfo = io_List.FirstOrDefault(m => m.IP == ip);

                // 先清理旧的连接资源
                if (remoteInfo != null)
                {
                    try
                    {
                        if (remoteInfo.Master_TcpClient != null)
                        {
                            remoteInfo.Master_TcpClient.Close();
                            remoteInfo.Master_TcpClient.Dispose();
                        }
                        if (remoteInfo.Master != null)
                        {
                            remoteInfo.Master.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"IO_【{ip}】清理旧连接资源时发生错误：{ex.Message}");
                    }
                    io_List.Remove(remoteInfo);
                }

                // 创建新的连接
                remoteInfo = new Remote_IO_Info { IP = ip };
                io_List.Add(remoteInfo);

                try
                {
                    remoteInfo.NModbus = new ModbusFactory();
                    remoteInfo.Master_TcpClient = new TcpClient();

                    // 设置连接超时
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await remoteInfo.Master_TcpClient.ConnectAsync(ip, 502).WaitAsync(cts.Token);

                    if (remoteInfo.Master_TcpClient.Connected)
                    {
                        remoteInfo.Master = remoteInfo.NModbus.CreateMaster(remoteInfo.Master_TcpClient);
                        remoteInfo.Master.Transport.ReadTimeout = 2000;
                        remoteInfo.Master.Transport.Retries = 2000;
                        _logger.LogInformation($"IO_【{ip}】_连接成功");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"IO_【{ip}】连接尝试失败，错误：{ex.Message}");

                    // 清理失败连接的资源
                    try
                    {
                        if (remoteInfo.Master_TcpClient != null)
                        {
                            remoteInfo.Master_TcpClient.Close();
                            remoteInfo.Master_TcpClient.Dispose();
                        }
                        if (remoteInfo.Master != null)
                        {
                            remoteInfo.Master.Dispose();
                        }
                        io_List.Remove(remoteInfo);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning($"IO_【{ip}】清理失败连接资源时发生错误：{cleanupEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"IO_【{ip}】连接过程发生异常：{ex.Message}");
            }
            return false;
        }

        public async Task<bool> ReadSignal(string ip, EIOAddress address)
        {
            //await _writeSemaphore.WaitAsync();
            //try
            //{
                return await ReadSignalImpl(ip, address);
            //}
            //finally
            //{
            //    _writeSemaphore.Release();
            //}
        }

        private async Task<bool> ReadSignalImpl(string ip, EIOAddress address)
        {
            try
            {
                var remoteInfo = io_List.FirstOrDefault(m => m.IP == ip);

                // 使用 Task.WhenAny 和超时控制来处理连接
                if (remoteInfo?.Master_TcpClient?.Client?.Connected != true ||
                    !await CheckConnection(remoteInfo.Master_TcpClient))
                {
                    bool isConnected = await Conn(ip);
                    if (!isConnected)
                    {
                        _logger.LogWarning($"IO_【{ip}】连接失败");
                        return false;
                    }
                    remoteInfo = io_List.FirstOrDefault(m => m.IP == ip);
                }

                // 增加重试机制
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        var result = await remoteInfo.Master.ReadCoilsAsync(1, (ushort)address, 1);
                        return result[0];
                    }
                    catch (Exception ex)
                    {
                        if (retry == 2)
                        {
                            _logger.LogError($"IO_【{ip}】读取失败（最后一次尝试）：{ex.Message}");
                            // 最后一次尝试失败时重新连接
                            await Conn(ip);
                            return false;
                        }

                        _logger.LogWarning($"IO_【{ip}】读取失败（第{retry + 1}次尝试）：{ex.Message}");
                        await Task.Delay(200 * (retry + 1)); // 递增延迟
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"IO_【{ip}】_ReadSignal读取异常：{ex.Message}");
            }
            return false;
        }

        private async Task<bool> WriteSignalImpl(string ip, EIOAddress address, bool value)
        {
            try
            {
                var remoteInfo = io_List.FirstOrDefault(m => m.IP == ip);

                if (remoteInfo?.Master_TcpClient?.Client?.Connected != true ||
                    !await CheckConnection(remoteInfo.Master_TcpClient))
                {
                    bool isConnected = await Conn(ip);
                    if (!isConnected)
                    {
                        _logger.LogWarning($"IO_【{ip}】连接失败");
                        return false;
                    }
                    remoteInfo = io_List.FirstOrDefault(m => m.IP == ip);
                }

                // 增加重试机制
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        await remoteInfo.Master.WriteSingleCoilAsync(1, (ushort)address, value);
                        _logger.LogInformation($"IO_【{ip}】地址{address}写入{value}成功");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        if (retry == 2)
                        {
                            _logger.LogError($"IO_【{ip}】写入失败（最后一次尝试）：{ex.Message}");
                            // 最后一次尝试失败时重新连接
                            await Conn(ip);
                            return false;
                        }

                        _logger.LogWarning($"IO_【{ip}】写入失败（第{retry + 1}次尝试）：{ex.Message}");
                        await Task.Delay(200 * (retry + 1)); // 递增延迟

                        try
                        {
                            remoteInfo.Master_TcpClient?.Close();
                            remoteInfo.Master_TcpClient?.Dispose();
                        }
                        catch { }

                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"IO_【{ip}】_WriteSignal写入异常：{ex.Message}");
            }
            return false;
        }

        public async Task<bool> WriteSignal(string ip, EIOAddress address, bool value)
        {
           // await _writeSemaphore.WaitAsync();
            //try
            //{
                return await WriteSignalImpl(ip, address, value);
            //}
            //finally
            //{
            //    _writeSemaphore.Release();
            //}
        }

        private async Task<bool> CheckConnection(TcpClient client)
        {
            try
            {
                if (client?.Client == null) return false;
                if (!client.Connected) return false;

                using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                {
                    try
                    {
                        return !(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task StartDeviceMonitoring()
        {
            _logger.LogInformation("开始监控IO设备");

            // 创建一个后台任务
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        // 获取所有设备和信号
                        using var conn = _db.CreateConnection();
                        var devices = await conn.QueryAsync<RCS_IODevices>("SELECT * FROM RCS_IODevices WHERE IsEnabled = 1");

                        foreach (var device in devices)
                        {
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

                                    // 读取信号并更新数据库
                                    var value = Convert.ToInt32(await ReadSignal(device.IP, addressEnum));
                                    if (signal.Value!=value)
                                    {
                                        signal.UpdatedTime = DateTime.Now;
                                        signal.Value = value;
                                        await conn.ExecuteAsync(
                                            "UPDATE RCS_IOSignals SET Value = @Value, UpdatedTime = @UpdatedTime WHERE Id = @Id",
                                            new { signal.Value, signal.UpdatedTime, signal.Id });
                                    }

                                   // _logger.LogInformation($"设备{device.Name}({device.IP})信号{signal.Address}更新成功：{value}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"读取设备{device.Name}({device.IP})信号{signal.Address}失败：{ex.Message}");
                                    continue; // 继续读取下一个信号
                                }
                            }
                        }

                        await Task.Delay(1000); // 每500ms刷新一次
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"监控IO设备出错：{ex.Message}");
                        await Task.Delay(1000); // 出错后等待1秒再重试
                    }
                }
            });
        }

        private void StartMonitoringDevice(RCS_IODevices device)
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var monitoringTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var conn = _db.CreateConnection();
                        var signals = await conn.QueryAsync<RCS_IOSignals>(
                            "SELECT * FROM RCS_IOSignals WHERE DeviceId = @DeviceId",
                            new { DeviceId = device.Id });

                        // 使用 SemaphoreSlim 限制并发数
                        using var semaphore = new SemaphoreSlim(5); // 同时最多处理5个信号

                        var tasks = signals.Select(async signal =>
                        {
                            try
                            {
                                await semaphore.WaitAsync(token);
                                try
                                {
                                    if (!Enum.TryParse<EIOAddress>(signal.Address, out EIOAddress addressEnum))
                                    {
                                        _logger.LogWarning($"无效的信号地址: {signal.Address}");
                                        return;
                                    }

                                    _logger.LogInformation($"设备{device.Id}信号{signal.Address}");

                                    var value = Convert.ToInt32(await ReadSignal(device.IP, addressEnum));
                                    signal.UpdatedTime = DateTime.Now;
                                    signal.Value = value;
                                    await _ioDeviceService.UpdateSignalAsync(signal);

                                    _logger.LogInformation($"设备{device.Id}信号{signal.Address}更新成功，当前值：{value}");
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _logger.LogError(ex, $"读取设备{device.Id}信号{signal.Address}失败");
                            }
                        });

                        try
                        {
                            // 等待所有任务完成，设置更长的超时时间
                            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), token);
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning($"设备{device.Id}的部分信号读取超时");
                        }

                        // 固定间隔
                        await Task.Delay(500, token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, $"监控设备{device.Id}出错");
                        await Task.Delay(1000, token);
                    }
                }

                _logger.LogInformation($"设备{device.Id}监控已停止");
            }, token);

            _monitoringTasks.TryAdd(device.IP, (monitoringTask, cts));
        }

        // 更新设备状态时重启监控
        public async Task UpdateDeviceMonitoring(int deviceId, bool isEnabled)
        {
            using var conn = _db.CreateConnection();

            var device = await conn.QueryFirstOrDefaultAsync<RCS_IODevices>(
                "SELECT * FROM RCS_IODevices WHERE Id = @Id",
                new { Id = deviceId });

            if (device == null) return;

            if (isEnabled)
            {
                // 启动设备监控
                StartMonitoringDevice(device);
                _logger.LogInformation(
                    "设备监控已启动: ID={ID}, IP={IP}, Time={Time}, User={User}",
                    device.Id, device.IP, DateTime.UtcNow, "YCmai");
            }
            else
            {
                // 停止设备监控
                if (_monitoringTasks.TryRemove(device.IP, out var taskInfo))
                {
                    var (task, cts) = taskInfo;
                    cts.Cancel(); // 取消任务
                    try
                    {
                        await task; // 等待任务完成
                        cts.Dispose(); // 释放资源
                    }
                    catch (OperationCanceledException)
                    {
                        // 任务被取消
                    }

                    _logger.LogInformation(
                        "设备监控已停止: ID={ID}, IP={IP}, Time={Time}, User={User}",
                        device.Id, device.IP, DateTime.UtcNow, "YCmai");
                }
            }
        }

        // 新增添加IO任务的接口
        public async Task<int> AddIOTask(string taskType, string deviceIP, string signalAddress, bool value, string taskId)
        {
            try
            {
                using var conn = _db.CreateConnection();
                var task = new RCS_IOAGV_Tasks
                {
                    TaskType = taskType,
                    Status = "Pending",
                    DeviceIP = deviceIP,
                    SignalAddress = signalAddress,
                    Value = value,
                    TaskId = taskId,
                    CreatedTime = DateTime.Now,
                    LastUpdatedTime = DateTime.Now
                };

                var sql = @"INSERT INTO RCS_IOAGV_Tasks 
                    (TaskType, Status, DeviceIP, SignalAddress, Value, TaskId, CreatedTime, LastUpdatedTime) 
                    VALUES 
                    (@TaskType, @Status, @DeviceIP, @SignalAddress, @Value, @TaskId, @CreatedTime, @LastUpdatedTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)";

                var newTaskId = await conn.ExecuteScalarAsync<int>(sql, task);
                _logger.LogInformation($"创建IO任务成功: ID={newTaskId}, Type={taskType}, Device={deviceIP}");
                return newTaskId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建IO任务失败");
                throw;
            }
        }
    }
}
