using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Net.NetworkInformation;
using System.Net;
using HslCommunication.Profinet.Omron;
using HslCommunication;
using S7.Net;
using WarehouseManagementSystem.Models.PLC;
using S7NetPlc = S7.Net.Plc;
using System.Text;
using Dapper;
using WarehouseManagementSystem.Db;


namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC通信服务实现类
    /// </summary>
    public class PlcCommunicationService : IPlcCommunicationService, IDisposable
    {
        private readonly IPlcSignalService _plcSignalService;
        private readonly PlcSignalUpdater _signalUpdater;
        private readonly ILogger<PlcCommunicationService> _logger;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _deviceTokenSources = new();
        private readonly ConcurrentDictionary<int, Task> _deviceTasks = new();
        private readonly ConcurrentDictionary<int, object> _plcConnections = new();
        private readonly ConcurrentDictionary<int, PlcDeviceStatus> _deviceStatus = new();
        private readonly SemaphoreSlim _serviceActionLock = new(1, 1);
        private bool _isDisposed;
        // 用于配置刷新任务的取消令牌
        private CancellationTokenSource _configRefreshCts;
        // 配置刷新任务
        private Task _configRefreshTask;
        private readonly IDatabaseService _db;
        private readonly ConcurrentDictionary<int, object> _heartbeatConnections = new();

        public PlcCommunicationService(
            IPlcSignalService plcSignalService,
            PlcSignalUpdater signalUpdater,
            ILogger<PlcCommunicationService> logger,
            IDatabaseService db)
        {
            _plcSignalService = plcSignalService ?? throw new ArgumentNullException(nameof(plcSignalService));
            _signalUpdater = signalUpdater ?? throw new ArgumentNullException(nameof(signalUpdater));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 启动所有已启用PLC设备的通信服务
        /// </summary>
        public async Task StartServiceAsync()
        {
            int totalDevices = 0;
            int startedDevices = 0;
            Dictionary<string, List<RCS_PlcDevice>> deviceGroupsByIp = new Dictionary<string, List<RCS_PlcDevice>>();
            
            await _serviceActionLock.WaitAsync();
            try
            {
                _logger.LogInformation("正在启动PLC通信服务...");
                
                // 重置所有设备锁，防止之前的信号量问题
                _signalUpdater.ResetAllDeviceLocks();
                
                // 获取并分组设备
                var devices = await _plcSignalService.GetAllPlcDevicesAsync();
                var enabledDevices = devices.Where(d => d.IsEnabled).ToList();
                totalDevices = enabledDevices.Count;

                if (!enabledDevices.Any())
                {
                    _logger.LogWarning("没有启用的PLC设备，通信服务未启动");
                    // 即使没有设备，也启动配置刷新任务，以便后续添加的设备能够被检测到
                    StartConfigRefreshTask();
                    return;
                }

                // 按IP地址分组设备，处理IP地址相同但DB块不同的情况
                var devicesByIp = enabledDevices.GroupBy(d => d.IpAddress).ToList();
                _logger.LogInformation("PLC设备按IP地址分组，共 {Count} 个不同IP地址", devicesByIp.Count);

                // 预加载所有IP地址的信号数据，减少数据库查询次数
                var allIpAddresses = devicesByIp.Select(g => g.Key).ToList();
                Dictionary<string, List<RCS_PlcSignal>> allSignalsByIp = new Dictionary<string, List<RCS_PlcSignal>>();
                
                foreach (var ipAddress in allIpAddresses)
                {
                    try
                    {
                        var signals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(ipAddress);
                        allSignalsByIp[ipAddress] = signals;
                        _logger.LogInformation("已预加载IP地址 {IpAddress} 的 {Count} 个信号", ipAddress, signals.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "预加载IP地址 {IpAddress} 的信号失败", ipAddress);
                        allSignalsByIp[ipAddress] = new List<RCS_PlcSignal>();
                    }
                }

                // 并行处理每个IP地址的设备组
                List<Exception> startupExceptions = new List<Exception>();
                foreach (var deviceGroup in devicesByIp)
                {
                    string ipAddress = deviceGroup.Key;
                    var devicesInGroup = deviceGroup.ToList();
                    _logger.LogInformation("处理IP地址 {IpAddress} 的设备组，共 {Count} 个设备", ipAddress, devicesInGroup.Count);

                    // 获取该IP地址下的所有PLC信号
                    if (!allSignalsByIp.TryGetValue(ipAddress, out var allSignals))
                    {
                        _logger.LogWarning("IP地址 {IpAddress} 的信号数据不存在，跳过此组设备", ipAddress);
                        continue;
                    }
                    
                    // 按DB块（PLCTypeDb）分组信号
                    var signalsByDb = allSignals.GroupBy(s => s.PLCTypeDb ?? "default").ToDictionary(g => g.Key, g => g.ToList());
                    _logger.LogInformation("IP地址 {IpAddress} 下的信号按DB块分组，共 {Count} 个不同DB块", ipAddress, signalsByDb.Count);

                    // 选择组中的第一个设备作为主设备，用于创建连接
                    var primaryDevice = devicesInGroup.First();
                    _logger.LogInformation("为IP地址 {IpAddress} 选择主设备 ID={DeviceId} 创建连接", ipAddress, primaryDevice.Id);
                    
                    // 为每个设备分配对应DB块的信号
                    foreach (var device in devicesInGroup)
                    {
                        // 清空原有信号列表
                        device.Signals = new List<RCS_PlcSignal>();
                        
                        // 为设备分配对应DB块的信号
                        foreach (var dbKey in signalsByDb.Keys)
                        {
                            string dbBlock = dbKey;
                            // 对于每个设备，如果ModuleAddress匹配DB块，则分配信号
                            if (device.ModuleAddress == dbBlock || 
                                (string.IsNullOrEmpty(device.ModuleAddress) && dbBlock == "default"))
                            {
                                device.Signals.AddRange(signalsByDb[dbBlock]);
                                _logger.LogInformation("设备 {DeviceId} (IP: {IpAddress}) 分配了DB块 {DbBlock} 的 {Count} 个信号", 
                                    device.Id, device.IpAddress, dbBlock, signalsByDb[dbBlock].Count);
                            }
                        }
                    }
                    
                    // 为该IP地址创建一个共享连接
                    try
                    {
                        await StartIpCommunicationAsync(primaryDevice, devicesInGroup);
                        startedDevices += devicesInGroup.Count;
                        deviceGroupsByIp[ipAddress] = devicesInGroup;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "启动IP地址 {IpAddress} 的设备组通信失败", ipAddress);
                        startupExceptions.Add(ex);
                        
                        // 继续启动其他IP地址的设备，不因一个IP地址失败而终止整个启动过程
                    }
                }

                // 启动配置刷新任务，定期检查设备配置变化
                StartConfigRefreshTask();

                // 报告启动结果
                if (startupExceptions.Count > 0)
                {
                    _logger.LogWarning("PLC通信服务启动部分完成，成功启动 {SuccessCount}/{TotalCount} 个设备，有 {FailCount} 个IP地址启动失败", 
                        startedDevices, totalDevices, startupExceptions.Count);
                    
                    // 如果全部失败，则抛出聚合异常
                    if (startedDevices == 0)
                    {
                        throw new AggregateException("所有PLC设备启动失败", startupExceptions);
                    }
                }
                else
                {
                    _logger.LogInformation("PLC通信服务启动完成，共启动 {Count} 个设备，{IpCount} 个IP地址", 
                        startedDevices, deviceGroupsByIp.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动PLC通信服务失败");
                throw;
            }
            finally
            {
                _serviceActionLock.Release();
            }
        }

        /// <summary>
        /// 停止所有PLC设备的通信服务
        /// </summary>
        public async Task StopServiceAsync()
        {
            await _serviceActionLock.WaitAsync();
            try
            {
                _logger.LogInformation("正在停止PLC通信服务...");

                // 停止配置刷新任务
                if (_configRefreshCts != null)
                {
                    _configRefreshCts.Cancel();
                    
                    try 
                    {
                        if (_configRefreshTask != null)
                        {
                            await Task.WhenAny(_configRefreshTask, Task.Delay(5000));
                        }
                    }
                    catch { }
                    
                    _configRefreshCts.Dispose();
                    _configRefreshCts = null;
                    _configRefreshTask = null;
                }

                foreach (var deviceId in _deviceTokenSources.Keys.ToList())
                {
                    await StopDeviceCommunicationAsync(deviceId);
                }

                _logger.LogInformation("PLC通信服务已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止PLC通信服务失败");
                throw;
            }
            finally
            {
                _serviceActionLock.Release();
            }
        }

        /// <summary>
        /// 重启所有已启用PLC设备的通信服务
        /// </summary>
        public async Task RestartServiceAsync()
        {
            await _serviceActionLock.WaitAsync();
            try
            {
                _logger.LogInformation("正在重启PLC通信服务...");
                
                // 重置所有设备锁，防止之前的信号量问题
                _signalUpdater.ResetAllDeviceLocks();
                
                // 先停止所有设备通信
                foreach (var deviceId in _deviceTokenSources.Keys.ToList())
                {
                    await StopDeviceCommunicationAsync(deviceId);
                }
                
                // 停止配置刷新任务
                if (_configRefreshCts != null)
                {
                    _configRefreshCts.Cancel();
                    
                    try 
                    {
                        if (_configRefreshTask != null)
                        {
                            await Task.WhenAny(_configRefreshTask, Task.Delay(5000));
                        }
                    }
                    catch { }
                    
                    _configRefreshCts.Dispose();
                    _configRefreshCts = null;
                    _configRefreshTask = null;
                }
                
                // 再重新启动已启用的设备
                var devices = await _plcSignalService.GetAllPlcDevicesAsync();
                var enabledDevices = devices.Where(d => d.IsEnabled).ToList();
                
                foreach (var device in enabledDevices)
                {
                    // 获取该设备的信号
                    var signals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(device.IpAddress);
                    device.Signals = signals.Where(s => s.PLCTypeDb == device.ModuleAddress || 
                                              (string.IsNullOrEmpty(s.PLCTypeDb) && 
                                               string.IsNullOrEmpty(device.ModuleAddress))).ToList();
                    
                    await StartDeviceCommunicationAsync(device);
                }
                
                // 启动配置刷新任务
                StartConfigRefreshTask();
                
                _logger.LogInformation("PLC通信服务重启完成，共启动 {Count} 个设备", enabledDevices.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重启PLC通信服务失败");
                throw;
            }
            finally
            {
                _serviceActionLock.Release();
            }
        }

        /// <summary>
        /// 启动单个PLC设备的通信
        /// </summary>
        private async Task StartDeviceCommunicationAsync(RCS_PlcDevice device)
        {
            if (device == null || !device.IsEnabled)
            {
                return;
            }

            // 如果设备已经在运行，先停止
            if (_deviceTokenSources.TryGetValue(device.Id, out _))
            {
                await StopDeviceCommunicationAsync(device.Id);
            }

            try
            {
                _logger.LogInformation("正在启动设备 {DeviceId} ({Brand}) 的通信...", device.Id, device.Brand);

                // 创建取消令牌
                var cts = new CancellationTokenSource();

                _deviceTokenSources[device.Id] = cts;

                // 创建设备状态
                _deviceStatus[device.Id] = new PlcDeviceStatus
                {
                    Device = device,
                    IsOnline = false,
                    LastCommunicationTime = DateTime.Now,
                    RetryCount = 0
                };

                // 启动设备通信任务
                var deviceTask = Task.Run(async () => 
                {
                    await DeviceCommunicationLoopAsync(device, cts.Token);
                }, cts.Token);

                _deviceTasks[device.Id] = deviceTask;

                _logger.LogInformation("设备 {DeviceId} ({Brand}) 通信已启动", device.Id, device.Brand);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动设备 {DeviceId} 通信失败", device.Id);
                
                // 确保资源被清理
                if (_deviceTokenSources.TryRemove(device.Id, out var cts))
                {
                    try { cts.Dispose(); } catch { }
                }
                
                _deviceTasks.TryRemove(device.Id, out _);

                _plcConnections.TryRemove(device.Id, out _);
                
                // 更新设备状态
                if (_deviceStatus.TryGetValue(device.Id, out var statusObj))
                {
                    statusObj.IsOnline = false;
                    statusObj.Error = ex.Message;
                }
            }
        }

        /// <summary>
        /// 停止单个PLC设备的通信
        /// </summary>
        private async Task StopDeviceCommunicationAsync(int deviceId)
        {
            try
            {
                _logger.LogInformation("正在停止设备 {DeviceId} 的通信...", deviceId);

                // 取消通信任务
                if (_deviceTokenSources.TryRemove(deviceId, out var cts))
                {
                    try
                    {
                        // 安全地取消令牌源，防止已释放的对象异常
                    cts.Cancel();
                    cts.Dispose();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        // 已被释放，记录后忽略
                        _logger.LogWarning(ex, "设备 {DeviceId} 的取消令牌源已被释放", deviceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "取消设备 {DeviceId} 的通信任务时出错", deviceId);
                    }
                }

                // 等待任务结束
                if (_deviceTasks.TryRemove(deviceId, out var task))
                {
                    try
                    {
                        await Task.WhenAny(task, Task.Delay(5000)); // 最多等待5秒
                    }
                    catch (Exception ex) 
                    {
                        _logger.LogWarning(ex, "等待设备 {DeviceId} 的通信任务结束时出错", deviceId);
                    }
                }

                // 关闭并释放PLC连接
                if (_plcConnections.TryRemove(deviceId, out var connection))
                {
                    try
                    {
                        if (connection is OmronFinsUdp omron)
                        {
                            // UDP连接不需要显式关闭
                        }
                        else if (connection is S7NetPlc siemens)
                        {
                            try { siemens.Close(); } catch { }
                        }

                        if (connection is IDisposable disposable)
                        {
                            try { disposable.Dispose(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "关闭设备 {DeviceId} 的PLC连接时发生错误", deviceId);
                    }
                }

                // 更新设备状态
                if (_deviceStatus.TryGetValue(deviceId, out var statusObj))
                {
                    statusObj.IsOnline = false;
                }

                _logger.LogInformation("设备 {DeviceId} 的通信已停止", deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止设备 {DeviceId} 通信失败", deviceId);
            }
        }

        /// <summary>
        /// 设备通信循环
        /// </summary>
        private async Task DeviceCommunicationLoopAsync(RCS_PlcDevice device, CancellationToken cancellationToken)
        {
            int reconnectCount = 0;
            int failedPingCount = 0;
            int socketErrorCount = 0;
            bool connectionLost = false;
            
            const int maxFailedPingBeforeNetworkDown = 3;
            const int maxSocketErrorsBeforePause = 3;
            const int pauseDurationMinutes = 5;

            _logger.LogInformation("设备 {DeviceId} ({IpAddress}) 通信循环已启动", device.Id, device.IpAddress);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 1. 网络连接检查
                    var networkResult = await CheckDeviceNetworkAsync(device, failedPingCount, connectionLost);
                    if (!networkResult.IsSuccess)
                    {
                        failedPingCount = networkResult.FailedPingCount;
                        connectionLost = networkResult.ConnectionLost;
                        await Task.Delay(PlcCommunicationConfig.ReconnectWaitTime, cancellationToken);
                        continue;
                    }
                    
                    // 网络连接正常，重置Ping计数
                    failedPingCount = 0;
                    
                    // 2. PLC连接检查与建立
                    var connectionResult = await EnsurePlcConnectionAsync(device, socketErrorCount, connectionLost);
                    if (!connectionResult.IsSuccess)
                    {
                        socketErrorCount = connectionResult.SocketErrorCount;
                        connectionLost = connectionResult.ConnectionLost;
                        
                        if (socketErrorCount >= maxSocketErrorsBeforePause)
                        {
                            await HandlePersistentSocketErrorAsync(device, pauseDurationMinutes, cancellationToken);
                            socketErrorCount = 0;
                        }
                        
                        await Task.Delay(PlcCommunicationConfig.ReconnectWaitTime, cancellationToken);
                        continue;
                    }

                    // 3. 信号检查
                    if (!HasValidSignals(device))
                    {
                        await Task.Delay(PlcCommunicationConfig.CommunicationCycle, cancellationToken);
                        continue;
                    }

                    // 4. 读取PLC信号
                    var readResult = await ReadDeviceSignalsAsync(device, connectionLost, socketErrorCount);
                    connectionLost = readResult.ConnectionLost;
                    socketErrorCount = readResult.SocketErrorCount;
                    
                    if (!readResult.IsSuccess && socketErrorCount >= maxSocketErrorsBeforePause)
                    {
                        await HandlePersistentSocketErrorAsync(device, pauseDurationMinutes, cancellationToken);
                        socketErrorCount = 0;
                    }

                    // 5. 等待下一个通信周期
                    await Task.Delay(PlcCommunicationConfig.CommunicationCycle, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 任务被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    var exceptionResult = await HandleUnexpectedExceptionAsync(device, ex, reconnectCount, connectionLost, socketErrorCount);
                    reconnectCount = exceptionResult.ReconnectCount;
                    connectionLost = exceptionResult.ConnectionLost;
                    socketErrorCount = exceptionResult.SocketErrorCount;
                    
                    if (socketErrorCount >= maxSocketErrorsBeforePause)
                    {
                        await HandlePersistentSocketErrorAsync(device, pauseDurationMinutes, cancellationToken);
                        socketErrorCount = 0;
                    }
                    
                    // 等待后重试
                    await Task.Delay(PlcCommunicationConfig.ReconnectWaitTime, cancellationToken);
                }
            }

            _logger.LogInformation("设备 {DeviceId} 通信循环结束", device.Id);
        }

        /// <summary>
        /// 检查设备网络连接
        /// </summary>
        private async Task<(bool IsSuccess, int FailedPingCount, bool ConnectionLost)> CheckDeviceNetworkAsync(
            RCS_PlcDevice device, int failedPingCount, bool connectionLost)
        {
            bool isPingSuccess = await CheckNetworkConnectionAsync(device.IpAddress);
            if (!isPingSuccess)
            {
                failedPingCount++;
                _logger.LogWarning("设备 {DeviceId} ({IpAddress}) 网络连接失败，第 {Count} 次", 
                    device.Id, device.IpAddress, failedPingCount);
                
                if (failedPingCount >= 3 && !connectionLost)
                {
                    connectionLost = true;
                    _logger.LogError("设备 {DeviceId} ({IpAddress}) 网络连接中断", device.Id, device.IpAddress);
                    
                    // 清理连接资源
                    CleanupDeviceConnection(device.Id);
                    
                    // 更新设备状态
                    UpdateDeviceStatusToOffline(device.Id, "网络连接失败");
                    
                    // 更新数据库
                    await UpdateDeviceStatusInDatabase(device.Id, false, "网络连接失败");
                }
                
                return (false, failedPingCount, connectionLost);
            }
            
            // Ping成功，重置计数
            return (true, 0, connectionLost);
        }

        /// <summary>
        /// 确保PLC连接已建立
        /// </summary>
        private async Task<(bool IsSuccess, int SocketErrorCount, bool ConnectionLost)> EnsurePlcConnectionAsync(
            RCS_PlcDevice device, int socketErrorCount, bool connectionLost)
        {
            if (!_plcConnections.TryGetValue(device.Id, out var connection) || connection == null)
            {
                try
                {
                    await ConnectPlcAsync(device);
                    
                    if (connectionLost)
                    {
                        _logger.LogInformation("设备 {DeviceId} ({IpAddress}) 连接已恢复", device.Id, device.IpAddress);
                        connectionLost = false;
                        socketErrorCount = 0; // 重置Socket错误计数
                    }
                    
                    return (true, socketErrorCount, connectionLost);
                }
                catch (Exception ex)
                {
                    // 检查是否是连接被远程关闭的问题
                    bool isSocketError = IsSocketError(ex);
                    
                    if (isSocketError)
                    {
                        socketErrorCount++;
                        _logger.LogWarning("设备 {DeviceId} ({IpAddress}) 发生Socket错误，第 {Count} 次", 
                            device.Id, device.IpAddress, socketErrorCount);
                    }
                    
                    if (!connectionLost)
                    {
                        _logger.LogError(ex, "设备 {DeviceId} ({IpAddress}) PLC连接失败", device.Id, device.IpAddress);
                        connectionLost = true;
                        await UpdateDeviceStatusInDatabase(device.Id, false, ex.Message);
                    }
                    
                    return (false, socketErrorCount, connectionLost);
                }
            }
            
            return (true, socketErrorCount, connectionLost);
        }

        /// <summary>
        /// 检查设备是否有有效信号
        /// </summary>
        private bool HasValidSignals(RCS_PlcDevice device)
        {
            if (device.Signals == null || device.Signals.Count == 0)
            {
                _logger.LogWarning("设备 {DeviceId} ({IpAddress}) 未分配信号，跳过读取", device.Id, device.IpAddress);
                
                // 更新设备状态（连接正常但无信号可读）
                if (_deviceStatus.TryGetValue(device.Id, out var emptySignalStatus))
                {
                    emptySignalStatus.IsOnline = true;
                    emptySignalStatus.LastCommunicationTime = DateTime.Now;
                    emptySignalStatus.Error = "无信号可读";
                }
                
                return false;
            }
            
            return true;
        }

        /// <summary>
        /// 读取设备信号
        /// </summary>
        private async Task<(bool IsSuccess, bool ConnectionLost, int SocketErrorCount)> ReadDeviceSignalsAsync(
            RCS_PlcDevice device, bool connectionLost, int socketErrorCount)
        {
            try
            {
                await ReadPlcSignalsAsync(device);
                
                // 成功读取，更新状态
                if (_deviceStatus.TryGetValue(device.Id, out var successStatus))
                {
                    successStatus.IsOnline = true;
                    successStatus.LastCommunicationTime = DateTime.Now;
                    successStatus.RetryCount = 0;
                    successStatus.Error = null;
                }
                
                // 恢复连接状态
                if (connectionLost)
                {
                    connectionLost = false;
                    socketErrorCount = 0; // 重置Socket错误计数
                    await UpdateDeviceStatusInDatabase(device.Id, true);
                    _logger.LogInformation("设备 {DeviceId} ({IpAddress}) 通信已恢复正常", device.Id, device.IpAddress);
                }
                
                return (true, connectionLost, socketErrorCount);
            }
            catch (Exception readEx)
            {
                // 处理读取异常
                if (IsDisposedError(readEx))
                {
                    _logger.LogWarning("设备 {DeviceId} 连接对象已被释放，将重新连接", device.Id);
                    _plcConnections.TryRemove(device.Id, out _);
                    return (false, connectionLost, socketErrorCount);
                }
                
                if (IsSocketError(readEx))
                {
                    socketErrorCount++;
                    _logger.LogWarning("设备 {DeviceId} ({IpAddress}) 读取信号时发生Socket错误，第 {Count} 次", 
                        device.Id, device.IpAddress, socketErrorCount);
                }
                else
                {
                    _logger.LogError(readEx, "设备 {DeviceId} 读取信号失败", device.Id);
                }
                
                // 更新状态
                UpdateDeviceStatusToOffline(device.Id, readEx.Message);
                
                // 如果不是连接丢失状态，更新数据库
                if (!connectionLost)
                {
                    connectionLost = true;
                    await UpdateDeviceStatusInDatabase(device.Id, false, readEx.Message);
                }
                
                return (false, connectionLost, socketErrorCount);
            }
        }

        /// <summary>
        /// 处理持续的Socket错误
        /// </summary>
        private async Task HandlePersistentSocketErrorAsync(RCS_PlcDevice device, int pauseMinutes, CancellationToken cancellationToken)
        {
            _logger.LogError("设备 {DeviceId} ({IpAddress}) 连续发生Socket错误，将暂停该设备的通信{Minutes}分钟", 
                device.Id, device.IpAddress, pauseMinutes);
                
            // 更新状态为严重错误
            UpdateDeviceStatusToOffline(device.Id, "连续Socket错误，暂停通信");
            
            // 更新数据库
            await UpdateDeviceStatusInDatabase(device.Id, false, "连续Socket错误，暂停通信");
            
            // 清理连接
            CleanupDeviceConnection(device.Id);
            
            // 暂停较长时间后再尝试
            await Task.Delay(TimeSpan.FromMinutes(pauseMinutes), cancellationToken);
        }

        /// <summary>
        /// 处理意外异常
        /// </summary>
        private async Task<(int ReconnectCount, bool ConnectionLost, int SocketErrorCount)> HandleUnexpectedExceptionAsync(
            RCS_PlcDevice device, Exception ex, int reconnectCount, bool connectionLost, int socketErrorCount)
        {
            // 检查是否是连接被释放或Socket错误
            bool isConnectionDisposed = IsDisposedError(ex);
            bool isSocketError = IsSocketError(ex);
            
            if (isConnectionDisposed)
            {
                _logger.LogWarning("设备 {DeviceId} 连接已释放，将清理连接", device.Id);
                _plcConnections.TryRemove(device.Id, out _);
            }
            else if (isSocketError)
            {
                socketErrorCount++;
                _logger.LogWarning("设备 {DeviceId} 通信循环中发生Socket错误，第 {Count} 次", 
                    device.Id, socketErrorCount);
            }
            else
            {
                _logger.LogError(ex, "设备 {DeviceId} 通信循环异常", device.Id);
            }
            
            // 更新状态
            UpdateDeviceStatusToOffline(device.Id, ex.Message);
            
            // 标记连接丢失
            if (!connectionLost)
            {
                connectionLost = true;
                await UpdateDeviceStatusInDatabase(device.Id, false, ex.Message);
            }
            
            return (reconnectCount, connectionLost, socketErrorCount);
        }

        /// <summary>
        /// 清理设备连接资源
        /// </summary>
        private void CleanupDeviceConnection(int deviceId)
        {
            if (_plcConnections.TryRemove(deviceId, out var oldConnection))
            {
                try
                {
                    if (oldConnection is S7NetPlc siemens)
                    {
                        try { siemens.Close(); } catch { }
                    }
                    
                    if (oldConnection is IDisposable disposable)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 更新设备状态为离线
        /// </summary>
        private void UpdateDeviceStatusToOffline(int deviceId, string errorMessage)
        {
            if (_deviceStatus.TryGetValue(deviceId, out var status))
            {
                status.IsOnline = false;
                status.Error = errorMessage;
                status.RetryCount++;
            }
        }

        /// <summary>
        /// 检查是否是Socket错误
        /// </summary>
        private bool IsSocketError(Exception ex)
        {
            return ex.Message.Contains("远程主机强迫关闭") || 
                (ex.InnerException?.Message?.Contains("远程主机强迫关闭") == true) ||
                ex.ToString().Contains("SocketException");
        }

        /// <summary>
        /// 检查是否是对象已释放错误
        /// </summary>
        private bool IsDisposedError(Exception ex)
        {
            return ex is ObjectDisposedException || 
                ex.Message.Contains("Cannot access a disposed object") ||
                (ex.GetType().Name == "PlcException" && ex.InnerException is ObjectDisposedException);
        }

        /// <summary>
        /// 手动触发读取指定PLC设备的所有信号
        /// </summary>
        public async Task ManualReadSignalsAsync(int deviceId)
        {
            try
            {
                await SafeExecuteWithLockAsync(async () => 
            {
                var device = await _plcSignalService.GetPlcDeviceByIdAsync(deviceId);
                if (device == null)
                {
                    throw new ArgumentException($"设备ID {deviceId} 不存在");
                }

                if (!device.IsEnabled)
                {
                    throw new InvalidOperationException($"设备ID {deviceId} 未启用");
                }

                    // 添加：检查设备是否存在于当前设备状态中，并且是否在线
                    if (_deviceStatus.TryGetValue(deviceId, out var status) && !status.IsOnline)
                    {
                        _logger.LogWarning("设备 {DeviceId} 当前状态为离线，跳过手动读取信号", deviceId);
                        throw new InvalidOperationException($"设备ID {deviceId} 网络连接失败");
                }

                string ipAddress = device.IpAddress;
                
                // 检查网络连接
                bool isPingSuccess = await CheckNetworkConnectionAsync(ipAddress);
                if (!isPingSuccess)
                {
                    throw new InvalidOperationException($"设备ID {deviceId} 网络连接失败");
                }

                    // 在使用连接前添加额外检查
                    if (_plcConnections.TryGetValue(deviceId, out var connection) && connection != null)
                    {
                        // 检查连接是否有效
                        if (connection is S7NetPlc siemens && !siemens.IsConnected)
                        {
                            _logger.LogWarning("设备 {DeviceId} 的西门子PLC连接已断开，需要重新连接", deviceId);
                            
                            // 移除无效连接
                            _plcConnections.TryRemove(deviceId, out _);
                            connection = null;
                        }
                }

                // 确保PLC已连接，获取该设备对应的连接对象
                    if (!_plcConnections.TryGetValue(deviceId, out connection) || connection == null)
                {
                    // 不存在直接连接，检查是否是同IP地址下的其他设备已建立了连接
                    var connectedDeviceIds = _plcConnections.Keys.ToList();
                    var sameIpDeviceIds = _deviceStatus.Keys
                            .Where(id => _deviceStatus.TryGetValue(id, out var devStatus) && 
                                      devStatus.Device?.IpAddress == ipAddress)
                        .Intersect(connectedDeviceIds)
                        .ToList();
                    
                    if (sameIpDeviceIds.Any())
                    {
                        // 使用同IP地址的第一个已连接设备的连接
                        connection = _plcConnections[sameIpDeviceIds.First()];
                        _plcConnections[deviceId] = connection; // 保存以便后续使用
                        _logger.LogInformation("设备 {DeviceId} 使用已存在的同IP连接", deviceId);
                    }
                    else
                    {
                        // 没有同IP的连接，创建新连接
                        await ConnectPlcAsync(device);
                        connection = _plcConnections[deviceId];
                    }
                }

                // 获取设备对应的信号
                if (device.Signals == null || !device.Signals.Any())
                {
                    var allSignals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(ipAddress);
                    device.Signals = allSignals.Where(s => s.PLCTypeDb == device.ModuleAddress || 
                                                   (string.IsNullOrEmpty(s.PLCTypeDb) && 
                                                    string.IsNullOrEmpty(device.ModuleAddress))).ToList();
                }

                // 读取PLC信号值
                    try
                    {
                await ReadPlcSignalsAsync(device);
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogError(ex, "手动读取设备 {DeviceId} 信号时发生信号量已满异常，正在重置设备锁", deviceId);
                        // 重置设备锁
                        _signalUpdater.ResetDeviceLock(device.IpAddress);
                        // 重新尝试读取
                        await ReadPlcSignalsAsync(device);
                    }

                _logger.LogInformation("已手动触发读取设备 {DeviceId} 的信号", deviceId);
                }, "手动读取信号");
            }
            catch (SemaphoreFullException ex)
            {
                _logger.LogError(ex, "手动读取设备 {DeviceId} 的信号时发生信号量已满异常", deviceId);
                
                try
                {
                    // 获取设备信息以获取IP地址
                    var device = await _plcSignalService.GetPlcDeviceByIdAsync(deviceId);
                    if (device != null)
                    {
                        // 重置此设备的信号量锁
                        _signalUpdater.ResetDeviceLock(device.IpAddress);
                        _logger.LogInformation("已重置设备 {DeviceId} ({IpAddress}) 的锁", deviceId, device.IpAddress);
                    }
                }
                catch (Exception resetEx)
                {
                    _logger.LogError(resetEx, "尝试重置设备 {DeviceId} 的锁时发生错误", deviceId);
                }
                
                // 尝试重置服务锁并通知用户
                await ResetServiceLockAsync();
                throw new InvalidOperationException($"读取设备 {deviceId} 信号时出现信号量错误，已尝试修复，请重试", ex);
            }
            catch (Exception ex)
            {
                // 处理连接被释放的特殊情况
                bool isDisposed = ex is ObjectDisposedException || 
                    ex.Message.Contains("Cannot access a disposed object") ||
                    (ex.GetType().Name == "PlcException" && ex.InnerException is ObjectDisposedException);
                    
                if (isDisposed)
                {
                    _logger.LogWarning("发现已释放的PLC连接对象，正在清理资源");
                    
                    // 强制移除连接对象
                    _plcConnections.TryRemove(deviceId, out _);
                }
                
                _logger.LogError(ex, "手动读取设备 {DeviceId} 的信号失败", deviceId);
                throw;
            }
        }

        /// <summary>
        /// 获取PLC通信服务的状态信息
        /// </summary>
        public async Task<Dictionary<int, bool>> GetServiceStatusAsync()
        {
            await _serviceActionLock.WaitAsync();
            try
            {
                var result = new Dictionary<int, bool>();
                
                foreach (var status in _deviceStatus)
                {
                    result[status.Key] = status.Value.IsOnline;
                }
                
                return result;
            }
            finally
            {
                _serviceActionLock.Release();
            }
        }

        /// <summary>
        /// 向指定PLC设备写入信号值
        /// </summary>
        public async Task WriteSignalValueAsync(int deviceId, int signalId, object value)
        {

            var startTime = DateTime.Now;
            bool lockTaken = false;
            
            try {
                // 先获取写入锁，防止多线程同时写入造成冲突
                lockTaken = await _serviceActionLock.WaitAsync(TimeSpan.FromSeconds(5));
                if (!lockTaken)
                {
                    _logger.LogWarning("获取设备 {DeviceId} 信号写入锁超时", deviceId);
                    throw new TimeoutException($"获取设备 {deviceId} 信号写入锁超时");
                }
                
                // 获取设备和信号信息
                var device = await _plcSignalService.GetPlcDeviceByIdAsync(deviceId);
                if (device == null)
                {
                    throw new ArgumentException($"设备ID {deviceId} 不存在");
                }

                var signal = await _plcSignalService.GetPlcSignalByIdAsync(signalId);
                if (signal == null)
                {
                    throw new ArgumentException($"信号ID {signalId} 不存在");
                }


                string ipAddress = device.IpAddress;

                // 检查网络连接
                bool isPingSuccess = await CheckNetworkConnectionAsync(ipAddress);
                if (!isPingSuccess)
                {
                    throw new InvalidOperationException($"设备ID {deviceId} 网络连接失败");
                }

                // 在使用连接前检查设备状态
                if (_deviceStatus.TryGetValue(deviceId, out var status) && !status.IsOnline)
                {
                    _logger.LogWarning("设备 {DeviceId} 当前状态为离线，跳过写入信号", deviceId);
                    throw new InvalidOperationException($"设备ID {deviceId} 当前状态为离线");
                }

                // 检查连接对象是否存在且是否有效
                bool connectionDisposed = false;
                if (_plcConnections.TryGetValue(deviceId, out var connection) && connection != null)
                {
                    // 检查连接是否已被释放
                    if (connection is S7NetPlc siemens)
                    {
                        try
                        {
                            // 尝试检查连接状态，如果已释放会抛出异常
                            if (!siemens.IsConnected)
                            {
                                _logger.LogWarning("设备 {DeviceId} 的PLC连接已断开，需要重新连接", deviceId);
                                _plcConnections.TryRemove(deviceId, out _);
                                connection = null;
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogWarning("设备 {DeviceId} 的PLC连接对象已被释放，需要重新连接", deviceId);
                            _plcConnections.TryRemove(deviceId, out _);
                            connection = null;
                            connectionDisposed = true;
                        }
                    }
                }

                // 确保PLC已连接，获取该设备对应的连接对象
                if (!_plcConnections.TryGetValue(deviceId, out connection) || connection == null)
                {
                    // 如果之前发现连接已被释放，直接抛出异常而不尝试新建连接
                    if (connectionDisposed)
                    {
                        throw new ObjectDisposedException($"设备 {deviceId} 的连接对象已被释放，请重新启动通信服务");
                    }
                    
                    // 不存在直接连接，检查是否是同IP地址下的其他设备已建立了连接
                    var connectedDeviceIds = _plcConnections.Keys.ToList();
                    var sameIpDeviceIds = _deviceStatus.Keys
                        .Where(id => _deviceStatus.TryGetValue(id, out var devStatus) &&
                               devStatus.Device?.IpAddress == ipAddress)
                        .Intersect(connectedDeviceIds)
                        .ToList();

                    if (sameIpDeviceIds.Any())
                    {
                        // 使用同IP地址的第一个已连接设备的连接
                        var firstDeviceId = sameIpDeviceIds.First();
                        
                        // 检查要使用的连接是否已被释放
                        if (_plcConnections.TryGetValue(firstDeviceId, out var sharedConnection) && 
                            sharedConnection != null)
                        {
                            bool isDisposed = false;
                            
                            if (sharedConnection is S7NetPlc sharedSiemens)
                            {
                                try
                                {
                                    isDisposed = !sharedSiemens.IsConnected;
                                }
                                catch (ObjectDisposedException)
                                {
                                    isDisposed = true;
                                }
                            }
                            
                            if (isDisposed)
                            {
                                _logger.LogWarning("共享连接设备 {DeviceId} 的PLC连接已被释放", firstDeviceId);
                                
                                // 清理所有使用这个连接的设备
                                foreach (var id in sameIpDeviceIds)
                                {
                                    _plcConnections.TryRemove(id, out _);
                                }
                                
                                throw new ObjectDisposedException($"共享连接 {ipAddress} 已被释放，请重新启动通信服务");
                            }
                            
                            // 连接正常，可以共享
                            connection = sharedConnection;
                        _plcConnections[deviceId] = connection; // 保存以便后续使用
                        _logger.LogInformation("设备 {DeviceId} 使用已存在的同IP连接", deviceId);
                        }
                        else
                        {
                            // 应该不会到达这里，但以防万一
                            await ConnectPlcAsync(device);
                            connection = _plcConnections[deviceId];
                        }
                    }
                    else
                    {
                        // 没有同IP的连接，创建新连接
                        await ConnectPlcAsync(device);
                        connection = _plcConnections[deviceId];
                    }
                }

                // 写入PLC信号值
                await WritePlcSignalValueAsync(connection, signal, value, device.Brand);

            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError(ex, "写入信号 {SignalId} 时发现连接对象已被释放，请稍后重试", signalId);
                
                // 尝试清理设备连接
                _plcConnections.TryRemove(deviceId, out _);
                
            }
            catch (Exception ex) {
                // 检查异常中是否包含已释放对象的信息
                bool isDisposed = ex is ObjectDisposedException || 
                    ex.Message.Contains("Cannot access a disposed object") ||
                    (ex.GetType().Name == "PlcException" && ex.InnerException is ObjectDisposedException);
                
                if (isDisposed)
                {
                    _logger.LogError(ex, "写入信号 {SignalId} 时发现网络流已被释放，请稍后重试", signalId);
                    
                    // 尝试清理设备连接
                    _plcConnections.TryRemove(deviceId, out _);
                    
                    // 标记设备离线
                    if (_deviceStatus.TryGetValue(deviceId, out var devStatus))
                    {
                        devStatus.IsOnline = false;
                        devStatus.Error = "连接对象已被释放";
                    }
                    
                    throw new InvalidOperationException($"设备 {deviceId} 连接已被释放，请稍后重试", ex);
                }
                
                _logger.LogError(ex, "PLC信号写入失败 DeviceID={DeviceId}, SignalID={SignalId}, Value={Value}", 
                    deviceId, signalId, value);
                throw;
            }
            finally
            {
                // 确保释放锁
                if (lockTaken)
                {
                    _serviceActionLock.Release();
                }
            }
        }

        /// <summary>
        /// 写入PLC信号值
        /// </summary>
        private async Task WritePlcSignalValueAsync(object connection, RCS_PlcSignal signal, object value, string brand)
        {
            try
            {

                // 根据品牌选择写入方式
                if (brand?.ToLower().Contains("欧姆龙") == true && connection is OmronFinsUdp omron)
                {
                    try
                    {
                        // 检查连接并尝试重连
                        if (await EnsureOmronConnectionAsync(omron))
                        {
                            await WriteOmronSignalValueAsync(omron, signal, value);
                        }
                        else
                        {
                            throw new Exception("欧姆龙PLC连接失败，无法写入信号值");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 捕获所有异常，包括"远程主机强迫关闭"异常
                        if (ex.Message.Contains("远程主机强迫关闭") || ex.Message.Contains("connection was forcibly closed"))
                        {
                            _logger.LogWarning($"写入信号时连接断开，尝试获取设备ID重新连接: {ex.Message}");
                            
                            // 尝试找到当前设备ID
                            int? deviceId = null;
                            foreach (var pair in _plcConnections)
                            {
                                if (pair.Value == connection)
                                {
                                    deviceId = pair.Key;
                                    break;
                                }
                            }
                            
                            if (deviceId.HasValue)
                            {
                                // 重新创建连接对象
                                var newOmron = new OmronFinsUdp();
                                newOmron.IpAddress = omron.IpAddress;
                                newOmron.Port = 9600; // 固定使用9600端口
                                newOmron.SA1 = 192;
                                newOmron.DA1 = 0;
                                newOmron.DA2 = 0;
                                newOmron.ReceiveTimeout = 2000;
                                
                                // 更新全局连接字典
                                _plcConnections[deviceId.Value] = newOmron;
                                
                                _logger.LogInformation($"已更新设备ID={deviceId.Value}的欧姆龙PLC连接对象");
                                
                                // 使用新连接重试写入
                                await WriteOmronSignalValueAsync(newOmron, signal, value);
                                return;
                            }
                        }
                        
                        // 其他异常或未找到设备ID，重新抛出
                        throw;
                    }
                }
                else if (brand?.ToLower().Contains("西门子") == true && connection is S7NetPlc siemens)
                {
                    await WriteSiemensSignalValueAsync(siemens, signal, value);
                }
                else
                {
                    throw new NotSupportedException($"不支持的PLC品牌: {brand}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入信号 {SignalId} ({Offset}) 的值失败", signal.Id, signal.Offset);
                throw;
            }
        }

        /// <summary>
        /// 写入欧姆龙PLC信号值
        /// </summary>
        private async Task WriteOmronSignalValueAsync(OmronFinsUdp omron, RCS_PlcSignal signal, object value)
        {
            // 解析地址
            string address = ParseOmronAddress(signal.Offset);

            //_logger.LogInformation($"写入欧姆龙{omron.IpAddress}信号地址{address}，值：{value}");

            // 根据数据类型执行不同的写入方法
            OperateResult result = null;
            int maxRetries = 2;
            int retryCount = 0;
            bool encounteredConnectionClosed = false;
            
            
            // 保存连接信息，以便需要时重新创建连接
            string ipAddress = omron.IpAddress;
            int port = omron.Port;
            
            while (retryCount <= maxRetries)
            {
                try
                {
                    // 如果之前遇到连接关闭错误，尝试重新创建连接对象
                    if (encounteredConnectionClosed)
                    {
                        _logger.LogInformation($"正在重新创建欧姆龙PLC连接对象用于写入: IP={ipAddress}, Port={port}");
                        
                        // 重新创建连接对象
                        omron = new OmronFinsUdp();
                        omron.IpAddress = ipAddress;
                        omron.Port = 9600; // 固定使用9600端口
                        omron.SA1 = 192;
                        omron.DA1 = 0;
                        omron.DA2 = 0;
                        omron.ReceiveTimeout = 2000;
                        
                        // 尝试更新全局连接字典
                        try
                        {
                            // 查找使用此IP的设备
                            foreach (var entry in _plcConnections.ToList())
                            {
                                var connection = entry.Value as OmronFinsUdp;
                                if (connection != null && connection.IpAddress == ipAddress)
                                {
                                    _plcConnections[entry.Key] = omron;
                                    _logger.LogInformation($"已更新设备ID={entry.Key}的欧姆龙PLC连接对象");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "更新全局连接字典失败");
                        }
                        
                        encounteredConnectionClosed = false;
                    }
                    
                    switch (signal.DataType?.ToLower())
                    {
                        case "bool":
                            bool boolValue = Convert.ToBoolean(value);
                            result = await Task.Run(() => omron.Write(address, boolValue));
                            if (result != null && result.IsSuccess)
                            {
                                // 写入后立即读取验证
                                try
                                {
                                    await Task.Delay(200); // 短暂延时，确保PLC有时间处理
                                    var readResult = await Task.Run(() => omron.ReadBool(address));
                                    if (readResult.IsSuccess && readResult.Content == boolValue)
                                    {
                                        // 第一次验证成功
                                       // _logger.LogDebug($"写入欧姆龙布尔值 {address}={boolValue} 第一次验证成功");

                                        // 添加二次确认：再次延时后读取验证
                                        await Task.Delay(500); // 等待更长时间确保PLC状态稳定
                                        var secondReadResult = await Task.Run(() => omron.ReadBool(address));
                                        if (secondReadResult.IsSuccess && secondReadResult.Content == boolValue)
                                        {
                                            // 二次验证也成功
                                           // _logger.LogDebug($"写入欧姆龙布尔值 {address}={boolValue} 二次验证成功");
                                            int status = boolValue ? 1 : 2;
                                            await UpdateMatchingTasksAsync(signal, status);
                                        }
                                        else
                                        {
                                            // 二次验证失败
                                            _logger.LogWarning($"写入欧姆龙布尔值 {address}={boolValue} 二次验证失败：读取值={secondReadResult.Content}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, $"验证欧姆龙布尔值写入时发生异常: {address}");
                                }
                            }
                            break;
                        
                        case "int":
                            int intValue = Convert.ToInt32(value);
                            result = await Task.Run(() => omron.Write(address, intValue));
                            break;
                        
                        case "string":
                            string stringValue = value?.ToString() ?? string.Empty;
                            result = await Task.Run(() => omron.Write(address, stringValue));
                            break;
                        
                        default:
                            throw new NotSupportedException($"不支持的数据类型: {signal.DataType}");
                    }
                    
                    if (result != null && result.IsSuccess)
                    {
                        // 写入成功，跳出循环
                       // _logger.LogDebug($"成功写入欧姆龙{omron.IpAddress}信号地址{address}");
                        return;
                    }
                    
                    // 写入失败，记录错误并重试
                    string errorMsg = result != null ? result.Message : "未知错误";
                    _logger.LogWarning($"写入欧姆龙PLC失败，第{retryCount + 1}次尝试：{errorMsg}，地址：{address}");
                    
                    // 重置连接参数并重试
                    omron.SA1 = 192;
                    omron.DA1 = 0;
                    omron.DA2 = 0;
                    omron.ReceiveTimeout = 2000;
                    
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        await Task.Delay(500 * retryCount); // 根据重试次数增加延迟
                    }
                }
                catch (Exception ex)
                {
                    // 检查是否是连接被远程主机关闭的错误
                    bool isConnectionClosed = ex.Message.Contains("远程主机强迫关闭") || 
                                             ex.Message.Contains("connection was forcibly closed");
                    
                    _logger.LogError(ex, $"写入欧姆龙PLC异常，地址：{address}，值：{value}，第{retryCount + 1}次尝试");
                    
                    // 如果是连接被关闭，标记需要重新创建连接
                    if (isConnectionClosed)
                    {
                        encounteredConnectionClosed = true;
                        _logger.LogWarning($"写入时检测到连接被远程主机关闭: {ex.Message}，将在下次尝试时重新创建连接");
                    }
                    
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        await Task.Delay(1000 * retryCount); // 连接错误时增加更长的延迟
                    }
                    else
                    {
                        // 记录最终失败原因
                        _logger.LogError($"最终写入失败，所有重试均失败: {ex.Message}");
                        throw new Exception($"写入PLC失败: {ex.Message}，已重试{maxRetries}次", ex);
                    }
                }
            }
            
            // 所有重试都失败
            string finalErrorMsg = result != null ? result.Message : "未知错误";
            throw new Exception($"写入值失败: {finalErrorMsg}，已重试{maxRetries}次");
        }

        /// <summary>
        /// 写入西门子PLC信号值
        /// </summary>
        private async Task WriteSiemensSignalValueAsync(S7NetPlc siemens, RCS_PlcSignal signal, object value)
        {
            string address = ParseSiemensAddress(signal.Offset, signal.DataType, signal.PLCTypeDb);
            
            // 如果是进站心跳信号，不输出日志
            bool isHeartbeat = signal.Remark == "进站心跳";

            switch (signal.DataType?.ToLower())
            {
                case "bool":
                    bool boolValue = Convert.ToBoolean(value);
                    
                    if (!isHeartbeat)
                    {
                        _logger.LogDebug($"开始写入布尔值: {boolValue}, 地址: {address}, 信号名称: {signal.Name}");
                    }

                    try
                    {
                        // 使用超时控制写入操作
                        using var cts = new CancellationTokenSource(4000); // 5秒超时

                        await Task.Run(() => siemens.Write(address, boolValue), cts.Token);
                        
                        // 写入后立即读取验证
                        await Task.Delay(100); // 第一次延时，确保PLC有时间处理
                        bool? firstReadValue = await Task.Run(() => siemens.Read(address) as bool?);

                        if (!isHeartbeat)
                        {
                            _logger.LogDebug($"第一次读取结果: {firstReadValue}, 期望值: {boolValue}");
                        }

                        // 第二次读取验证
                        await Task.Delay(200); // 第二次延时
                        bool? secondReadValue = await Task.Run(() => siemens.Read(address) as bool?);

                        if (!isHeartbeat)
                        {
                            _logger.LogDebug($"第二次读取结果: {secondReadValue}, 期望值: {boolValue}");
                        }

                        int status = 1;

                        if (!boolValue)
                        {
                            status = 2;
                        }

                        // 只有当两次读取的值都与写入值相同时才执行更新
                        if (firstReadValue.HasValue && secondReadValue.HasValue && 
                            firstReadValue.Value == boolValue && secondReadValue.Value == boolValue)
                        {
                            if (!isHeartbeat)
                            {
                                _logger.LogDebug($"两次读取验证通过，开始更新任务状态");
                            }
                            await UpdateMatchingTasksAsync(signal, status);
                        }
                        else
                        {
                            if (!isHeartbeat)
                            {
                                _logger.LogWarning($"两次读取验证失败：第一次读取值={firstReadValue}, 第二次读取值={secondReadValue}, 期望值={boolValue}");
                            }
                            throw new Exception("写入验证失败");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning($"PLC写入操作超时，准备重新连接并重试，地址: {address}");
                        // 关闭当前连接
                        await Task.Run(() => siemens.Close());
                        await Task.Delay(400);
                        // 重新打开连接
                        await Task.Run(() => siemens.Open());
                        await Task.Delay(400);
                        // 重新写入
                        await Task.Run(() => siemens.Write(address, boolValue));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"PLC写入操作失败，准备重新连接并重试，地址: {address}, 错误: {ex.Message}");
                        // 关闭当前连接
                        await Task.Run(() => siemens.Close());
                        await Task.Delay(400);
                        // 重新打开连接
                        await Task.Run(() => siemens.Open());
                        await Task.Delay(400);
                        // 重新写入
                        await Task.Run(() => siemens.Write(address, boolValue));
                    }
                    break;

                case "int":
                    int intValue = Convert.ToInt32(value);
                    try
                    {
                        using var cts = new CancellationTokenSource(5000); // 5秒超时
                        await Task.Run(() => siemens.Write(address, intValue), cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning($"PLC写入操作超时，准备重新连接并重试，地址: {address}");
                        await Task.Run(() => siemens.Close());
                        await Task.Delay(400);
                        await Task.Run(() => siemens.Open());
                        await Task.Delay(400);
                        await Task.Run(() => siemens.Write(address, intValue));
                    }
                    break;

                case "string":
                    string stringValue = value?.ToString() ?? "";
                    try
                    {
                        using var cts = new CancellationTokenSource(5000); // 5秒超时
                        await Task.Run(() =>
                        {
                            if (string.IsNullOrEmpty(stringValue))
                            {
                                string[] addressParts = address.Split('.');
                                int dbNumber = int.Parse(addressParts[0].Substring(2));
                                int offset = int.Parse(addressParts[1].Substring(3));
                                siemens.WriteBytes(DataType.DataBlock, dbNumber, offset + 1, new byte[] { 0 });
                            }
                            else
                            {
                                siemens.Write(address, stringValue);
                            }
                        }, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning($"PLC写入操作超时，准备重新连接并重试，地址: {address}");
                        await Task.Run(() => siemens.Close());
                        await Task.Delay(400);
                        await Task.Run(() => siemens.Open());
                        await Task.Delay(400);
                        await Task.Run(() =>
                        {
                            if (string.IsNullOrEmpty(stringValue))
                            {
                                string[] addressParts = address.Split('.');
                                int dbNumber = int.Parse(addressParts[0].Substring(2));
                                int offset = int.Parse(addressParts[1].Substring(3));
                                siemens.WriteBytes(DataType.DataBlock, dbNumber, offset + 1, new byte[] { 0 });
                            }
                            else
                            {
                                siemens.Write(address, stringValue);
                            }
                        });
                    }
                    break;

                default:
                    throw new NotSupportedException($"不支持的数据类型: {signal.DataType}");
            }
        }

        /// <summary>
        /// 将值转换为字符串
        /// </summary>
        private string ConvertValueToString(object value, string dataType)
        {
            if (value == null)
            {
                return string.Empty;
            }
            
            switch (dataType?.ToLower())
            {
                case "bool":
                    return Convert.ToBoolean(value) ? "1" : "0";
                
                case "int":
                    return Convert.ToInt32(value).ToString();
                
                case "string":
                    return value.ToString();
                
                default:
                    return value.ToString();
            }
        }

        /// <summary>
        /// 检查网络连接
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
        /// 连接PLC设备
        /// </summary>
        private async Task ConnectPlcAsync(RCS_PlcDevice device)
        {
            _logger.LogInformation("开始连接PLC设备 ID={DeviceId}, 品牌={Brand}, IP={IpAddress}:{Port}", 
                device.Id, device.Brand, device.IpAddress, device.Port);
            
            int retryCount = 0;
            const int maxRetryCount = 2;
            TimeSpan retryDelay = TimeSpan.FromSeconds(1);
            
            while (retryCount <= maxRetryCount)
            {
                try 
                {
                    // 先检查网络连接
                    if (retryCount > 0)
                    {
                        _logger.LogInformation("尝试第 {RetryCount} 次连接PLC设备 ID={DeviceId}, IP={IpAddress}",
                            retryCount, device.Id, device.IpAddress);
                    }
                    
                    bool isPingSuccess = await CheckNetworkConnectionAsync(device.IpAddress);
                    if (!isPingSuccess)
                    {
                        throw new InvalidOperationException($"设备 {device.Id} ({device.IpAddress}) 网络连接失败");
                    }
                
                // 根据设备品牌选择连接方式
                if (device.Brand?.ToLower().Contains("欧姆龙") == true)
                {
                    // 创建欧姆龙PLC连接 - 使用UDP连接方式
                    var omron = new OmronFinsUdp();
                    omron.IpAddress = device.IpAddress;
                    omron.Port = 9600; // 固定使用9600端口
                    
                    // 设置连接参数
                    omron.SA1 = 192; // 参考用户成功连接的配置
                    omron.DA1 = 0;   // 默认DA1
                    omron.DA2 = 0;
                    omron.ReceiveTimeout = 2000;
                    
                    // 存储连接对象
                    _plcConnections[device.Id] = omron;
                }
                else if (device.Brand?.ToLower().Contains("西门子") == true)
                {
                    // 创建西门子PLC连接
                    CpuType cpuType = CpuType.S71200; // 默认为S71200
                    
                        // 创建PLC对象，默认机架0和槽位1
                    var siemens = new S7NetPlc(cpuType, device.IpAddress, 0, 1);
                    
                        // 设置连接超时时间
                        typeof(S7NetPlc).GetProperty("ConnTimeout", 
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?
                            .SetValue(siemens, 5000); // 5秒连接超时
                        
                        try
                        {
                            // 连接PLC，添加取消令牌以便可以中断长时间的连接尝试
                            var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            
                            await Task.Run(() => 
                            {
                                try 
                                {
                                    siemens.Open();
                                }
                                catch (Exception ex)
                                {
                                    // 捕获连接过程中的异常
                                    if (ex is S7.Net.PlcException plcEx)
                                    {
                                        if (plcEx.InnerException is System.IO.IOException ioEx)
                                        {
                                            if (ioEx.InnerException is System.Net.Sockets.SocketException sockEx && 
                                                sockEx.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
                                            {
                                                throw new Exception($"PLC连接被拒绝，可能PLC未开启或网络问题：{sockEx.Message}", sockEx);
                                            }
                                            else if (ioEx.Message.Contains("远程主机强迫关闭了一个现有的连接"))
                                            {
                                                throw new Exception($"PLC连接被远程设备关闭：{ioEx.Message}", ioEx);
                                            }
                                        }
                                    }
                                    throw; // 重新抛出原始异常
                                }
                            }, connectCts.Token);
                    
                    // 验证连接状态
                    if (!siemens.IsConnected)
                    {
                                throw new Exception("西门子PLC连接失败，设备返回未连接状态");
                    }
                    
                    // 存储连接对象
                    _plcConnections[device.Id] = siemens;
                        }
                        catch (TaskCanceledException)
                        {
                            throw new TimeoutException($"连接西门子PLC {device.IpAddress} 超时");
                        }
                }
                
                // 更新内存中的设备状态
                if (_deviceStatus.TryGetValue(device.Id, out var statusObj))
                {
                    statusObj.IsOnline = true;
                    statusObj.LastCommunicationTime = DateTime.Now;
                    statusObj.Error = null;
                }
                
                // 更新设备状态信息在数据库中
                await UpdateDeviceStatusInDatabase(device.Id, true);
                
                _logger.LogInformation("PLC设备连接成功 ID={DeviceId}, 品牌={Brand}", device.Id, device.Brand);
                    return; // 连接成功，退出重试循环
                }
                catch (Exception ex) 
                {
                    // 网络连接问题的具体日志
                    if (ex.Message.Contains("网络连接失败"))
                    {
                        _logger.LogError("无法连接到PLC设备 ID={DeviceId}, IP={IpAddress}：网络不通", 
                            device.Id, device.IpAddress);
                    }
                    // 处理连接被远程关闭的情况
                    else if (ex.Message.Contains("远程主机强迫关闭") || 
                             (ex.InnerException?.Message?.Contains("远程主机强迫关闭") == true))
                    {
                        _logger.LogError("PLC设备 ID={DeviceId}, IP={IpAddress} 连接被远程设备拒绝或关闭", 
                            device.Id, device.IpAddress);
                    }
                    else
                    {
                _logger.LogError(ex, "PLC设备连接失败 ID={DeviceId}, 品牌={Brand}, IP={IpAddress}:{Port}", 
                    device.Id, device.Brand, device.IpAddress, device.Port);
                    }
                    
                    // 清理可能创建的连接资源
                    _plcConnections.TryRemove(device.Id, out _);
                
                // 更新内存中的设备状态
                if (_deviceStatus.TryGetValue(device.Id, out var statusObj))
                {
                    statusObj.IsOnline = false;
                    statusObj.Error = ex.Message;
                        statusObj.RetryCount++;
                }
                
                    // 只在最后一次尝试失败时更新数据库
                    if (retryCount == maxRetryCount)
                    {
                await UpdateDeviceStatusInDatabase(device.Id, false, ex.Message);
                        throw; // 重新抛出异常，通知调用者连接失败
                    }
                    
                    // 重试
                    retryCount++;
                    if (retryCount <= maxRetryCount)
                    {
                        // 等待一段时间再重试，时间随重试次数增加
                        await Task.Delay(retryDelay * retryCount);
                    }
                }
            }
            
            // 所有重试都失败的情况下到达这里，但实际上上面已经抛出异常
            throw new Exception($"连接PLC设备 ID={device.Id} 失败，已尝试 {maxRetryCount + 1} 次");
        }

        /// <summary>
        /// 重连PLC设备
        /// </summary>
        private async Task ReconnectPlcAsync(RCS_PlcDevice device)
        {
            _logger.LogWarning("开始重新连接PLC设备 ID={DeviceId}, IP={IpAddress}, 当前状态={Status}", 
                device.Id, device.IpAddress, _deviceStatus.TryGetValue(device.Id, out var currentStatus) ? (currentStatus.IsOnline ? "在线" : "离线") : "未知");
            
            int attemptCount = 0;
            const int maxAttempts = 3;
            
            // 更新状态为正在重连
            if (_deviceStatus.TryGetValue(device.Id, out var reconnectStatus))
            {
                reconnectStatus.IsOnline = false;
                reconnectStatus.Error = "正在重新连接";
            }
            
            // 先关闭旧连接
            if (_plcConnections.TryRemove(device.Id, out var oldConnection))
            {
                try
                {
                    if (oldConnection is S7NetPlc siemens)
                    {
                        try { siemens.Close(); } catch { }
                    }
                    
                    if (oldConnection is IDisposable disposable)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭设备 {DeviceId} 的PLC连接时出错", device.Id);
                }
            }
            
            // 尝试重新连接
            while (attemptCount < maxAttempts)
            {
                attemptCount++;
                
                try
                {
                    _logger.LogInformation("正在尝试重连设备 {DeviceId}，第 {Count}/{MaxCount} 次", 
                        device.Id, attemptCount, maxAttempts);
                    
                    await ConnectPlcAsync(device);
                    
                    _logger.LogInformation("设备 {DeviceId} 重连成功", device.Id);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "设备 {DeviceId} 第 {Count} 次重连失败", device.Id, attemptCount);
                    
                    if (attemptCount < maxAttempts)
                    {
                        // 短暂等待后重试
                        await Task.Delay(1000 * attemptCount);
                    }
                }
            }
            
            // 重连失败
            _logger.LogError("设备 {DeviceId} 重连失败，已尝试 {Count} 次", device.Id, maxAttempts);
            
            // 更新状态为连接失败
            if (_deviceStatus.TryGetValue(device.Id, out var failedStatus))
            {
                failedStatus.IsOnline = false;
                failedStatus.Error = $"重连失败，已尝试{maxAttempts}次";
            }
            
            throw new Exception($"设备 {device.Id} 重连失败，已尝试 {maxAttempts} 次");
        }
        
        /// <summary>
        /// 更新设备状态到数据库
        /// </summary>
        private async Task UpdateDeviceStatusInDatabase(int deviceId, bool isConnected, string errorMessage = null)
        {
            try
            {
                // 每次都重新从数据库获取最新的设备及其所有信号
                var device = await _plcSignalService.GetPlcDeviceByIdAsync(deviceId);
                if (device != null)
                {
                    // 更新设备表的UpdateTime
                    device.UpdateTime = DateTime.Now;
                    await _plcSignalService.UpdatePlcDeviceAsync(device);
                    
                    // 获取设备的所有信号(强制从数据库重新获取)
                    var allSignals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(device.IpAddress);
                    if (allSignals != null)
                    {
                        // 筛选出与当前设备DB块匹配的信号
                        var deviceSignals = allSignals.Where(s =>
                            s.PLCTypeDb == device.ModuleAddress ||
                            (string.IsNullOrEmpty(s.PLCTypeDb) && string.IsNullOrEmpty(device.ModuleAddress))).ToList();
                        
                        // 在断开连接时，更新所有PLC信号的当前值为连接状态信息
                        if (!isConnected && deviceSignals != null && deviceSignals.Any())
                        {
                            // 添加时间戳到错误消息，确保数据库发现是新值并更新
                            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            string statusValue = errorMessage;

                           // _logger.LogDebug("更新设备 {DeviceId} 的信号状态为: {StatusValue}", deviceId, statusValue);
                            
                            // 批处理更新所有信号的当前值
                            foreach (var signal in deviceSignals)
                            {
                                // 设置信号当前值为连接失败状态
                                signal.CurrentValue = statusValue;
                                signal.UpdateTime = DateTime.Now;
                                
                                // 更新到数据库
                                await _plcSignalService.UpdatePlcSignalAsync(signal);
                            }
                            
                            _logger.LogDebug("已更新设备 {DeviceId} 的所有信号值为连接失败状态", deviceId);
                        }
                    }
                    
                    _logger.LogDebug("已更新设备 {DeviceId} 的连接状态到数据库: {Status}", 
                        deviceId, isConnected ? "在线" : "离线");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备 {DeviceId} 的连接状态到数据库失败", deviceId);
            }
        }

        /// <summary>
        /// 读取PLC设备信号
        /// </summary>
        private async Task ReadPlcSignalsAsync(RCS_PlcDevice device)
        {
            //_logger.LogDebug("开始读取PLC设备信号 ID={DeviceId}, IP={IpAddress}, 共{Count}个信号", 
            //    device.Id, device.IpAddress, device.Signals.Count);
            
            // 记录读取开始时间用于性能统计
            var startTime = DateTime.Now;
            int readCount = 0;
            int plcSignalCount = 0;
            
            try {
                if (device == null || !_plcConnections.TryGetValue(device.Id, out var connection))
                {
                    throw new InvalidOperationException($"设备 {device.Id} 未连接");
                }

                // 写入心跳信号
                //await WriteHeartbeatSignalsAsync(device);


                var signalUpdates = new Dictionary<int, string>();
                
                // 读取PLC设备的所有信号
                foreach (var signal in device.Signals)
                {
                    // 确保信号的DB块与设备匹配
                    if (!string.IsNullOrEmpty(signal.PLCTypeDb) && 
                        !string.IsNullOrEmpty(device.ModuleAddress) && 
                        signal.PLCTypeDb != device.ModuleAddress)
                    {
                        _logger.LogWarning("跳过信号 {SignalId}，因为DB块不匹配。信号DB块: {SignalDb}, 设备DB块: {DeviceDb}", 
                            signal.Id, signal.PLCTypeDb, device.ModuleAddress);
                        continue;
                    }

                    // 读取所有信号
                    string value = await ReadPlcSignalValueAsync(connection, signal, device.Brand);
                    if (value != null)
                    {
                        signalUpdates[signal.Id] = value;
                        readCount++;
                    }
                    plcSignalCount++;
                }
                
                // 批量更新信号值
                if (signalUpdates.Any())
                {
                    try 
                {
                    await _signalUpdater.BatchUpdateSignalValues(device.IpAddress, signalUpdates);
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogError(ex, "设备 {DeviceId} 批量更新信号值时发生信号量已满异常，正在重置锁", device.Id);
                        // 重置此设备的信号量锁
                        _signalUpdater.ResetDeviceLock(device.IpAddress);
                        // 再次尝试更新
                        await _signalUpdater.BatchUpdateSignalValues(device.IpAddress, signalUpdates);
                    }
                }

               // _logger.LogDebug("PLC设备信号读取完成 ID={DeviceId}, 耗时={ElapsedMs}ms, 共读取{ReadCount}个信号, 更新{UpdateCount}个值",
                    //device.Id, (DateTime.Now - startTime).TotalMilliseconds, readCount, signalUpdates.Count);
            }
            catch (SemaphoreFullException ex)
            {
                _logger.LogError(ex, "设备 {DeviceId} 读取信号过程中发生信号量已满异常，正在重置锁", device.Id);
                // 重置此设备的信号量锁
                _signalUpdater.ResetDeviceLock(device.IpAddress);
                throw;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "PLC设备信号读取失败 ID={DeviceId}, IP={IpAddress}", device.Id, device.IpAddress);
                throw;
            }
        }

        /// <summary>
        /// 读取单个PLC信号值
        /// </summary>
        private async Task<string> ReadPlcSignalValueAsync(object connection, RCS_PlcSignal signal, string brand)
        {
            try
            {
                if (string.IsNullOrEmpty(signal.Offset))
                {
                    return null;
                }
                
                // 根据品牌选择读取方式
                if (brand?.ToLower().Contains("欧姆龙") == true && connection is OmronFinsUdp omron)
                {
                    try
                    {
                        // 检查连接并尝试重连
                        if (await EnsureOmronConnectionAsync(omron))
                        {
                            return await ReadOmronSignalValueAsync(omron, signal);
                        }
                        return null;
                    }
                    catch (Exception ex)
                    {
                        // 捕获所有异常，包括"远程主机强迫关闭"异常
                        if (ex.Message.Contains("远程主机强迫关闭") || ex.Message.Contains("connection was forcibly closed"))
                        {
                            //_logger.LogWarning($"读取信号时连接断开，尝试获取设备ID重新连接: {ex.Message}");
                            
                            // 尝试找到当前设备ID
                            int? deviceId = null;
                            foreach (var pair in _plcConnections)
                            {
                                if (pair.Value == connection)
                                {
                                    deviceId = pair.Key;
                                    break;
                                }
                            }
                            
                            if (deviceId.HasValue)
                            {
                                // 重新创建连接对象
                                var newOmron = new OmronFinsUdp();
                                newOmron.IpAddress = omron.IpAddress;
                                newOmron.Port = 9600; // 固定使用9600端口
                                newOmron.SA1 = 192;
                                newOmron.DA1 = 0;
                                newOmron.DA2 = 0;
                                newOmron.ReceiveTimeout = 2000;
                                
                                // 更新全局连接字典
                                _plcConnections[deviceId.Value] = newOmron;
                                
                                _logger.LogInformation($"已更新设备ID={deviceId.Value}的欧姆龙PLC连接对象");
                                
                                // 使用新连接重试读取
                                return await ReadOmronSignalValueAsync(newOmron, signal);
                            }
                        }
                        
                        _logger.LogError(ex, "读取欧姆龙PLC信号失败 {SignalId}", signal.Id);
                        return null;
                    }
                }
                else if (brand?.ToLower().Contains("西门子") == true && connection is S7NetPlc siemens)
                {
                    return await ReadSiemensSignalValueAsync(siemens, signal);
                }
                
                throw new NotSupportedException($"不支持的PLC品牌: {brand}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取信号 {SignalId} ({Offset}) 的值失败", signal.Id, signal.Offset);
                return null;
            }
        }

        /// <summary>
        /// 读取欧姆龙PLC信号值
        /// </summary>
        private async Task<string> ReadOmronSignalValueAsync(OmronFinsUdp omron, RCS_PlcSignal signal)
        {
            // 解析地址
            string address = ParseOmronAddress(signal.Offset);
            
            // 重试次数和当前尝试次数
            int maxRetries = 2;
            int retryCount = 0;
            bool encounteredConnectionClosed = false;
            
            // 保存连接信息，以便需要时重新创建连接
            string ipAddress = omron.IpAddress;
            int port = omron.Port;
            
            while (retryCount <= maxRetries)
            {
                try
                {
                    // 如果之前遇到连接关闭错误，尝试重新创建连接对象
                    if (encounteredConnectionClosed)
                    {
                        _logger.LogInformation($"正在重新创建欧姆龙PLC连接对象: IP={ipAddress}, Port={port}");
                        
                        // 重新创建连接对象
                        omron = new OmronFinsUdp();
                        omron.IpAddress = ipAddress;
                        omron.Port = 9600; // 固定使用9600端口
                        omron.SA1 = 192;
                        omron.DA1 = 0;
                        omron.DA2 = 0;
                        omron.ReceiveTimeout = 2000;
                        
                        // 尝试更新全局连接字典
                        try
                        {
                            // 查找使用此IP的设备
                            foreach (var entry in _plcConnections.ToList())
                            {
                                var connection = entry.Value as OmronFinsUdp;
                                if (connection != null && connection.IpAddress == ipAddress)
                                {
                                    _plcConnections[entry.Key] = omron;
                                    _logger.LogInformation($"已更新设备ID={entry.Key}的欧姆龙PLC连接对象");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "更新全局连接字典失败");
                        }
                        
                        encounteredConnectionClosed = false;
                    }
                    
                    // 根据数据类型执行不同的读取方法
                    switch (signal.DataType?.ToLower())
                    {
                        case "bool":
                            var boolResult = await Task.Run(() => omron.ReadBool(address));
                            if (boolResult.IsSuccess)
                            {
                                // AGV写入的信号不需要二次验证
                                return boolResult.Content ? "1" : "0";
                            }
                            else
                            {
                                // 读取失败，记录并重试
                                _logger.LogWarning($"欧姆龙读取Bool值失败: {boolResult.Message}，地址: {address}，第{retryCount + 1}次尝试");
                            }
                            break;
                        
                        case "int":
                            var intResult = await Task.Run(() => omron.ReadInt32(address));
                            if (intResult.IsSuccess)
                            {
                                return intResult.Content.ToString();
                            }
                            else
                            {
                                _logger.LogWarning($"欧姆龙读取Int值失败: {intResult.Message}，地址: {address}，第{retryCount + 1}次尝试");
                            }
                            break;
                        
                        case "string":
                            // 欧姆龙字符串需要指定长度，默认为50
                            //var stringResult = await Task.Run(() => omron.ReadString(address, 50));
                            //if (stringResult.IsSuccess)
                            //{
                            //    return stringResult.Content;
                            //}
                            //else
                            //{
                            //    _logger.LogWarning($"读取String值失败: {stringResult.Message}，地址: {address}，第{retryCount + 1}次尝试");
                            //}
                            break;
                        
                        default:
                            throw new NotSupportedException($"不支持的数据类型: {signal.DataType}");
                    }
                    
                    // 如果执行到这里，说明读取失败，重置连接参数后重试
                    omron.SA1 = 192;
                    omron.DA1 = 0;
                    omron.DA2 = 0;
                    omron.ReceiveTimeout = 2000;
                    
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        await Task.Delay(500 * retryCount); // 根据重试次数增加延迟
                    }
                }
                catch (Exception ex)
                {
                    // 检查是否是连接被远程主机关闭的错误
                    bool isConnectionClosed = ex.Message.Contains("远程主机强迫关闭") || 
                                             ex.Message.Contains("connection was forcibly closed") ||
                                             ex.Message.Contains("10054");
                    
                    _logger.LogError(ex, $"读取欧姆龙PLC数据异常，地址: {address}，类型: {signal.DataType}，第{retryCount + 1}次尝试");
                    
                    // 如果是连接被关闭，标记需要重新创建连接
                    if (isConnectionClosed)
                    {
                        encounteredConnectionClosed = true;
                        _logger.LogWarning($"检测到连接被远程主机关闭: {ex.Message}，将在下次尝试时重新创建连接");
                    }
                    
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        await Task.Delay(1000 * retryCount); // 连接错误时增加更长的延迟
                    }
                    else
                    {
                        // 最后一次尝试还失败，记录详细错误
                        _logger.LogError($"最终读取失败，所有重试均失败: {ex.Message}");
                    }
                }
            }
            
            // 所有重试都失败
           // _logger.LogError($"读取PLC数据失败，地址: {address}，已重试{maxRetries}次");
            return null;
        }

        /// <summary>
        /// 读取西门子PLC信号值
        /// </summary>
        private async Task<string> ReadSiemensSignalValueAsync(S7NetPlc siemens, RCS_PlcSignal signal)
        {
            try 
            {
                // 解析地址
                string address = ParseSiemensAddress(signal.Offset, signal.DataType, signal.PLCTypeDb);
                
                try
                {
                    // 根据数据类型执行不同的读取方法
                    switch (signal.DataType?.ToLower())
                    {
                        case "bool":
                            {
                                using var cts = new CancellationTokenSource(4000); // 4秒超时
                                bool? boolValue = await Task.Run(() => siemens.Read(address) as bool?, cts.Token);
                                if (boolValue.HasValue)
                                {
                                    // 二次验证：读取两次确认
                                    if (signal.Writer?.Equals("PLC", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        // 只有PLC写入的信号才需要二次验证
                                        await Task.Delay(50, cts.Token); // 短暂延时
                                        bool? secondRead = await Task.Run(() => siemens.Read(address) as bool?, cts.Token);
                                        if (secondRead.HasValue && secondRead.Value == boolValue.Value)
                                        {
                                            return boolValue.Value ? "1" : "0";
                                        }
                                        // 如果两次读取不一致，再读一次
                                        await Task.Delay(50, cts.Token);
                                        bool? thirdRead = await Task.Run(() => siemens.Read(address) as bool?, cts.Token);
                                        return thirdRead.HasValue ? (thirdRead.Value ? "1" : "0") : null;
                                    }
                                    else
                                    {
                                        // AGV写入的信号不需要二次验证
                                        return boolValue.Value ? "1" : "0";
                                    }
                                }
                                return null;
                            }
                        
                        case "int":
                            {
                                using var intCts = new CancellationTokenSource(4000); // 4秒超时
                                var intValue = await Task.Run(() => {
                                    try {
                                        // 获取原始值，再进行类型转换
                                        var rawValue = siemens.Read(address);
                                        return rawValue != null ? (short?)Convert.ToInt16(rawValue) : null;
                                    } 
                                    catch (Exception ex) {
                                        _logger.LogError(ex, "读取int值失败: {Address}", address);
                                        return null;
                                    }
                                }, intCts.Token);
                                
                                if (intValue.HasValue)
                                {
                                    return intValue.Value.ToString();
                                }
                                return null;
                            }

                        case "string":
                            {
                                using var strCts = new CancellationTokenSource(4000); // 4秒超时
                                return await Task.Run(() =>
                                {
                                    try
                                    {
                                        // 解析地址 DB504.DBB258
                                        string[] addressParts = address.Split('.');
                                        int dbNumber = int.Parse(addressParts[0].Substring(2)); // 504
                                        int offset = int.Parse(addressParts[1].Substring(3));  // 258

                                        // 首先读取字符串长度字节
                                        byte[] header = siemens.ReadBytes(DataType.DataBlock, dbNumber, offset, 2);
                                        byte actLen = header[1];  // 实际长度

                                        // 如果实际长度为0，说明是空字符串
                                        if (actLen == 0)
                                        {
                                            return string.Empty;
                                        }

                                        // 使用原有的Read方法读取数据
                                        var m = siemens.Read(DataType.DataBlock, dbNumber, offset, VarType.Byte, 50);
                                        byte[] a = (byte[])m;
                                        string str = Encoding.ASCII.GetString(a).Trim();
                                        return str;
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, $"读取西门子PLC字符串失败，地址: {address}");
                                        return string.Empty;
                                    }
                                }, strCts.Token);
                            }

                        default:
                            throw new NotSupportedException($"不支持的数据类型: {signal.DataType}");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"西门子PLC读取超时，地址: {address}，将重新建立连接");
                    // 关闭当前连接
                    await Task.Run(() => siemens.Close());
                    await Task.Delay(400);
                    // 重新打开连接
                    await Task.Run(() => siemens.Open());
                    await Task.Delay(400);
                    return null;
                }
                catch (Exception ex)
                {
                    // 检查是否是连接被远程主机关闭的错误
                    bool isConnectionClosed = ex.Message.Contains("远程主机强迫关闭") || 
                                            ex.Message.Contains("connection was forcibly closed") ||
                                            ex.Message.Contains("10054") ||
                                            ex.Message.ToString().Contains("SocketException");
                    
                    if (isConnectionClosed)
                    {
                        _logger.LogWarning($"西门子PLC读取检测到连接被远程主机关闭: {ex.Message}，将重新建立连接");
                        // 关闭当前连接
                        await Task.Run(() => siemens.Close());
                        await Task.Delay(400);
                        // 重新打开连接
                        await Task.Run(() => siemens.Open());
                        await Task.Delay(400);
                        return null;
                    }
                    
                    _logger.LogError(ex, $"西门子PLC读取数据异常，IP ：{signal.PlcDeviceId}地址: {address}，类型: {signal.DataType}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"西门子信号读取失败{signal.PlcDeviceId}-{signal.PLCTypeDb}-{signal.Offset}-{signal.Remark}");
                return null;
            }
        }

        /// <summary>
        /// 解析欧姆龙PLC地址
        /// </summary>
        private string ParseOmronAddress(string offset)
        {
            if (string.IsNullOrEmpty(offset))
                return "D0"; // 使用默认地址

            // 首先检查格式是否已经正确
            if (offset.StartsWith("D") || offset.StartsWith("CIO") || 
                offset.StartsWith("W") || offset.StartsWith("H") || 
                offset.StartsWith("A") || offset.StartsWith("E"))
            {
                return offset; // 已经是正确格式，直接返回
            }
            
            // 如果是纯数字，默认加D前缀
            if (int.TryParse(offset, out _))
            {
                return $"D{offset}";
            }
            
            // 无法识别的格式，使用安全的默认值
            _logger.LogWarning("无法解析欧姆龙地址: {Offset}，将使用默认地址D0", offset);
            return "D0";
        }

        /// <summary>
        /// 解析西门子PLC地址
        /// </summary>
        private string ParseSiemensAddress(string offset, string dataType, string plcTypeDb)
        {

            // 获取DB块号，去掉可能的"DB"前缀
            string dbNumber = plcTypeDb;
            if (dbNumber.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
            {
                dbNumber = dbNumber.Substring(2);
            }
            
            // 根据数据类型构建不同格式的地址
            switch (dataType?.ToLower())
            {
                case "bool":
                    // 检查偏移量是否包含小数点（位地址）
                    if (offset.Contains('.'))
                    {
                        return $"DB{dbNumber}.DBX{offset}";
                    }
                    else
                    {
                        // 如果没有小数点，默认使用0位
                        return $"DB{dbNumber}.DBX{offset}.0";
                    }
                
                case "int":
                    return $"DB{dbNumber}.DBW{offset}";
                
                case "dint":
                case "real":
                    return $"DB{dbNumber}.DBD{offset}";
                
                case "string":
                    // 字符串使用DBXX格式，但不加.String后缀，因为这可能导致读取问题
                    return $"DB{dbNumber}.DBB{offset}";
                
                default:
                    // 默认使用字节访问
                    return $"DB{dbNumber}.DBB{offset}";
            }
        }

        /// <summary>
        /// 清理PLC字符串，去除不必要的控制字符、前后空格和特殊符号
        /// </summary>
        private string CleanPlcString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // 去掉前后空格
            string trimmed = input.Trim();
            
            // 移除控制字符和非打印字符
            var cleaned = new System.Text.StringBuilder();
            bool seenNonZero = false; // 跟踪是否已经遇到非零字符
            
            foreach (char c in trimmed)
            {
                // 只保留可打印ASCII字符，并且跳过结尾的空字符或无用字符
                if (c >= 32 && c <= 126)
                {
                    cleaned.Append(c);
                    seenNonZero = true;
                }
                else if (seenNonZero && c == 0)
                {
                    // 如果已经有有效字符，并且遇到结束符，则停止解析
                    break;
                }
            }
            
            // 用于诊断日志
            string result = cleaned.ToString();
            //if (result.Contains("V_") && result.Length > 10)
            //{
            //    _logger.LogWarning("检测到异常的字符串格式：'{Input}' -> '{Result}'", input, result);
            //    // 尝试自动修复可能的格式问题，例如"V_9dab9730"应该是"AGV_9dab9730"
            //    if (result.StartsWith("V_") && !result.StartsWith("AGV_"))
            //    {
            //        result = "AG" + result;
            //        _logger.LogInformation("尝试修复为：'{Result}'", result);
            //    }
            //}
            
            return result;
        }

        /// <summary>
        /// 启动配置刷新任务，定期检查设备配置变化
        /// </summary>
        private void StartConfigRefreshTask()
        {
            // 如果已经有任务在运行，先停止
            if (_configRefreshTask != null && _configRefreshCts != null)
            {
                _configRefreshCts.Cancel();
                _configRefreshCts.Dispose();
            }

            _configRefreshCts = new CancellationTokenSource();
            var token = _configRefreshCts.Token;

            _configRefreshTask = Task.Run(async () =>
            {
                _logger.LogInformation("PLC配置刷新任务已启动");

                // 每15秒检查一次设备配置变化，使检测新设备更及时
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), token);
                        
                        // 如果任务被取消，不继续执行
                        if (token.IsCancellationRequested)
                            break;

                        await RefreshDeviceConfigurationsAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        // 任务被取消
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PLC配置刷新任务发生错误");
                        // 出错后等待短暂时间再继续
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                    }
                }

                _logger.LogInformation("PLC配置刷新任务已停止");
            }, token);
        }

        /// <summary>
        /// 刷新设备配置，检查是否有设备需要停止或启动
        /// </summary>
        private async Task RefreshDeviceConfigurationsAsync()
        {
            await _serviceActionLock.WaitAsync();
            try
            {
                _logger.LogInformation("正在刷新PLC设备配置...");
                
                // 获取最新的设备配置
                var latestDevices = await _plcSignalService.GetAllPlcDevicesAsync();
               // _logger.LogInformation("从数据库获取到 {Count} 个PLC设备", latestDevices.Count);
                
                // 获取当前正在运行的设备ID列表
                var runningDeviceIds = _deviceTokenSources.Keys.ToList();
               // _logger.LogInformation("当前系统中正在运行 {Count} 个PLC设备", runningDeviceIds.Count);
                
                // 按IP地址分组当前运行的设备
                var runningDevices = runningDeviceIds
                    .Select(id => _deviceStatus.TryGetValue(id, out var status) ? status.Device : null)
                    .Where(d => d != null)
                    .ToList();
                
                var runningIpGroups = runningDevices
                    .GroupBy(d => d.IpAddress)
                    .ToDictionary(g => g.Key, g => g.ToList());
                
                // 按IP地址分组最新的设备
                var latestEnabledDevices = latestDevices
                    .Where(d => d.IsEnabled)
                    .ToList();
                
                var latestIpGroups = latestEnabledDevices
                    .GroupBy(d => d.IpAddress)
                    .ToDictionary(g => g.Key, g => g.ToList());
                
                // 找出需要停止的IP地址（没有启用的设备或已被删除）
                var ipsToStop = runningIpGroups.Keys
                    .Where(ip => !latestIpGroups.ContainsKey(ip))
                    .ToList();
                
                // 找出需要启动的IP地址（新增或原本所有设备都禁用现在有启用的）
                var ipsToStart = latestIpGroups.Keys
                    .Where(ip => !runningIpGroups.ContainsKey(ip))
                    .ToList();
                
                //_logger.LogInformation("配置检查结果：需停止 {StopCount} 个IP地址，需启动 {StartCount} 个IP地址", 
                //    ipsToStop.Count, ipsToStart.Count);
                
                if (ipsToStart.Any())
                {
                    _logger.LogInformation("新增/启用的IP地址：{IpAddresses}", string.Join(", ", ipsToStart));
                }
                
                // 停止已删除或没有启用设备的IP地址
                foreach (var ip in ipsToStop)
                {
                    var devicesToStop = runningIpGroups[ip];
                    _logger.LogInformation("IP地址 {IpAddress} 已删除或没有启用的设备，停止 {Count} 个设备", 
                        ip, devicesToStop.Count);
                    
                    foreach (var device in devicesToStop)
                    {
                        await StopDeviceCommunicationAsync(device.Id);
                    }
                }
                
                // 启动新增或有新启用设备的IP地址
                foreach (var ip in ipsToStart)
                {
                    var devicesToStart = latestIpGroups[ip];
                    _logger.LogInformation("发现新增或启用设备的IP地址 {IpAddress}，含 {Count} 个设备", 
                        ip, devicesToStart.Count);
                    
                    // 获取该IP的所有信号
                    var allSignals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(ip);
                    
                    // 按DB块分组信号
                    var signalsByDb = allSignals.GroupBy(s => s.PLCTypeDb ?? "default").ToList();
                    
                    // 为每个设备分配对应DB块的信号
                    foreach (var device in devicesToStart)
                    {
                        device.Signals = new List<RCS_PlcSignal>();
                        
                        foreach (var signalGroup in signalsByDb)
                        {
                            string dbBlock = signalGroup.Key;
                            if (device.ModuleAddress == dbBlock || 
                                (string.IsNullOrEmpty(device.ModuleAddress) && dbBlock == "default"))
                            {
                                device.Signals.AddRange(signalGroup.ToList());
                            }
                        }
                    }
                    
                    // 启动IP地址的通信
                    var primaryDevice = devicesToStart.First();
                    await StartIpCommunicationAsync(primaryDevice, devicesToStart);
                }
                
                // 检查信号更新
                foreach (var ip in latestIpGroups.Keys.Intersect(runningIpGroups.Keys))
                {
                    // 比较运行中的设备和最新的设备，检查是否有变化
                    var runningDevicesInIp = runningIpGroups[ip];
                    var latestDevicesInIp = latestIpGroups[ip];
                    
                    // 检查是否有设备变化（启用/禁用）
                    var runningIds = runningDevicesInIp.Select(d => d.Id).ToHashSet();
                    var latestIds = latestDevicesInIp.Select(d => d.Id).ToHashSet();
                    
                    bool deviceChanges = !runningIds.SetEquals(latestIds);
                    
                    if (deviceChanges)
                    {
                        _logger.LogInformation("IP地址 {IpAddress} 的设备配置已变化，重启通信", ip);
                        
                        // 停止当前所有设备
                        foreach (var device in runningDevicesInIp)
                        {
                            await StopDeviceCommunicationAsync(device.Id);
                        }
                        
                        // 重新启动
                        if (latestDevicesInIp.Any())
                        {
                            // 获取该IP的所有信号
                            var allSignals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(ip);
                            
                            // 按DB块分组信号
                            var signalsByDb = allSignals.GroupBy(s => s.PLCTypeDb ?? "default")
                                .ToDictionary(g => g.Key, g => g.ToList());
                            
                            // 为每个设备分配对应DB块的信号
                            foreach (var device in latestDevicesInIp)
                            {
                                device.Signals = new List<RCS_PlcSignal>();
                                
                                string dbBlock = device.ModuleAddress ?? "default";
                                if (signalsByDb.TryGetValue(dbBlock, out var signals))
                                {
                                    device.Signals.AddRange(signals);
                                    //_logger.LogDebug("已更新设备 {DeviceId} 的信号列表，当前有 {Count} 个信号", 
                                    //    device.Id, signals.Count);
                                }
                            }
                            
                            // 启动IP地址的通信
                            var primaryDevice = latestDevicesInIp.First();
                            await StartIpCommunicationAsync(primaryDevice, latestDevicesInIp);
                        }
                    }
                    else
                    {
                        // 只更新信号列表
                        var allSignals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(ip);
                        
                        // 按DB块分组信号
                        var signalsByDb = allSignals.GroupBy(s => s.PLCTypeDb ?? "default")
                            .ToDictionary(g => g.Key, g => g.ToList());
                        
                        // 更新每个设备的信号列表
                        foreach (var device in runningDevicesInIp)
                        {
                            var statusDevice = _deviceStatus.TryGetValue(device.Id, out var status) ? status.Device : null;
                            if (statusDevice != null)
                            {
                                statusDevice.Signals = new List<RCS_PlcSignal>();
                                
                                string dbBlock = device.ModuleAddress ?? "default";
                                if (signalsByDb.TryGetValue(dbBlock, out var signals))
                                {
                                    statusDevice.Signals.AddRange(signals);
                                    //_logger.LogDebug("已更新设备 {DeviceId} 的信号列表，当前有 {Count} 个信号", 
                                    //    device.Id, signals.Count);
                                }
                            }
                        }
                    }
                }
                
               // _logger.LogInformation("PLC设备配置刷新完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新PLC设备配置失败");
            }
            finally
            {
                _serviceActionLock.Release();
            }
        }

        /// <summary>
        /// 启动一个IP地址下的所有设备通信（共享同一连接）
        /// </summary>
        private async Task StartIpCommunicationAsync(RCS_PlcDevice primaryDevice, List<RCS_PlcDevice> allDevices)
        {
            if (primaryDevice == null || !primaryDevice.IsEnabled || allDevices == null || !allDevices.Any())
            {
                _logger.LogWarning("无法启动设备通信：主设备为空或未启用，或设备列表为空");
                return;
            }

            string ipAddress = primaryDevice.IpAddress;
            _logger.LogInformation("开始为IP地址 {IpAddress} 创建设备连接，共有 {Count} 个设备", ipAddress, allDevices.Count);

            // 1. 停止该IP下所有已运行的设备
            await StopExistingDevicesAsync(allDevices);

            try
            {
                // 2. 为每个设备启动通信任务
                await StartDeviceCommunicationTasksAsync(allDevices);
                _logger.LogInformation("IP地址 {IpAddress} 下的 {Count} 个设备通信任务已启动", ipAddress, allDevices.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动IP地址 {IpAddress} 的设备通信失败", ipAddress);
                await CleanupDeviceResourcesAsync(allDevices, ex.Message);
            }
        }

        /// <summary>
        /// 停止已存在的设备通信
        /// </summary>
        private async Task StopExistingDevicesAsync(List<RCS_PlcDevice> devices)
        {
            foreach (var device in devices)
            {
                if (_deviceTokenSources.TryGetValue(device.Id, out _))
                {
                    await StopDeviceCommunicationAsync(device.Id);
                }
            }
        }

        /// <summary>
        /// 启动设备通信任务
        /// </summary>
        private async Task StartDeviceCommunicationTasksAsync(List<RCS_PlcDevice> devices)
        {
            foreach (var device in devices)
            {
                // 创建取消令牌
                var cts = new CancellationTokenSource();
                _deviceTokenSources[device.Id] = cts;
                
                // 创建设备状态
                _deviceStatus[device.Id] = new PlcDeviceStatus
                {
                    Device = device,
                    IsOnline = false,
                    LastCommunicationTime = DateTime.Now,
                    RetryCount = 0
                };

                // 启动设备通信任务
                var deviceTask = Task.Run(async () => 
                {
                    await DeviceCommunicationLoopAsync(device, cts.Token);
                }, cts.Token);

                _deviceTasks[device.Id] = deviceTask;
                
                _logger.LogInformation("设备 {DeviceId} ({Brand}) 通信任务已启动", device.Id, device.Brand);
            }
        }

        /// <summary>
        /// 清理设备资源
        /// </summary>
        private async Task CleanupDeviceResourcesAsync(List<RCS_PlcDevice> devices, string errorMessage)
        {
            foreach (var device in devices)
            {
                // 清理取消令牌
                if (_deviceTokenSources.TryRemove(device.Id, out var cts))
                {
                    try { cts.Dispose(); } catch { }
                }
                
                // 清理任务
                _deviceTasks.TryRemove(device.Id, out _);
                
                // 清理连接
                _plcConnections.TryRemove(device.Id, out _);
                
                // 更新设备状态
                if (_deviceStatus.TryGetValue(device.Id, out var statusObj))
                {
                    statusObj.IsOnline = false;
                    statusObj.Error = errorMessage;
                }
                
                // 更新数据库状态
                try
                {
                    await UpdateDeviceStatusInDatabase(device.Id, false, errorMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "更新设备 {DeviceId} 状态到数据库失败", device.Id);
                }
            }
        }

        /// <summary>
        /// IP通信循环，处理同一IP下多个设备的通信
        /// </summary>
        private async Task IpCommunicationLoopAsync(RCS_PlcDevice primaryDevice, List<RCS_PlcDevice> allDevices, CancellationToken cancellationToken)
        {
            int reconnectCount = 0;
            int failedPingCount = 0;
            const int maxFailedPingBeforeNetworkDown = 3;
            bool connectionLost = false;
            string ipAddress = primaryDevice.IpAddress;

            _logger.LogInformation("启动IP地址 {IpAddress} 的通信循环，包含 {Count} 个设备", ipAddress, allDevices.Count);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 先检查网络连接，网络不通就不用尝试PLC通信了
                    bool isPingSuccess = await CheckNetworkConnectionAsync(ipAddress);
                    if (!isPingSuccess)
                    {
                        failedPingCount++;
                        _logger.LogWarning("IP地址 {IpAddress} 网络连接失败，第 {Count} 次", 
                            ipAddress, failedPingCount);
                        
                        // 不管connectionLost标志，每次ping失败都更新数据库
                        _logger.LogError("IP地址 {IpAddress} 网络连接中断", ipAddress);
                        
                        // 重要修改: 在网络连接失败的情况下，主动释放并移除连接
                        foreach (var device in allDevices)
                        {
                            if (_plcConnections.TryRemove(device.Id, out var oldConnection))
                            {
                                try
                                {
                                    if (oldConnection is S7NetPlc siemens)
                                    {
                                        try { siemens.Close(); } catch { }
                                    }
                                    
                                    if (oldConnection is IDisposable disposable)
                                    {
                                        try { disposable.Dispose(); } catch { }
                                    }
                                }
                                catch { }
                            }
                            
                            // 更新所有设备状态
                            if (_deviceStatus.TryGetValue(device.Id, out var status))
                            {
                                status.IsOnline = false;
                                status.Error = "网络连接失败";
                            }
                            
                            // 不管connectionLost状态，强制更新数据库
                            await UpdateDeviceStatusInDatabase(device.Id, false, "网络连接失败");
                        }
                        
                        // 设置connectionLost标志
                        connectionLost = true;
                        
                        // 等待一段时间后重试
                        await Task.Delay(PlcCommunicationConfig.ReconnectWaitTime, cancellationToken);
                        continue;
                    }
                    else
                    {
                        // Ping成功，重置计数
                        failedPingCount = 0;
                    }
                    
                    // 确保PLC已连接
                    bool needReconnect = false;
                    if (!_plcConnections.TryGetValue(primaryDevice.Id, out var connection) || connection == null)
                    {
                        needReconnect = true;
                    }
                    else if (connection is S7NetPlc siemens && !siemens.IsConnected)
                    {
                        _logger.LogWarning("IP地址 {IpAddress} 的西门子PLC连接已断开，需要重新连接", ipAddress);
                        needReconnect = true;
                    }
                    
                    if (needReconnect)
                    {
                        // 网络正常但PLC未连接，尝试建立连接
                        try 
                        {
                            // 主动清理之前的连接
                            foreach (var device in allDevices)
                            {
                                if (_plcConnections.TryRemove(device.Id, out var oldConnection))
                                {
                                    try
                                    {
                                        if (oldConnection is S7NetPlc s)
                                        {
                                            try { s.Close(); } catch { }
                                        }
                                        
                                        if (oldConnection is IDisposable disposable)
                                        {
                                            try { disposable.Dispose(); } catch { }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            
                            await ConnectPlcAsync(primaryDevice);
                            connection = _plcConnections[primaryDevice.Id];
                            
                            // 为所有设备共享连接
                            foreach (var device in allDevices)
                            {
                                if (device.Id != primaryDevice.Id)
                                {
                                    _plcConnections[device.Id] = connection;
                                }
                            }
                            
                            // 连接成功但connectionLost标记未重置（可能之前是网络断开）
                            if (connectionLost)
                            {
                                _logger.LogInformation("IP地址 {IpAddress} 连接已恢复", ipAddress);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 网络正常但PLC连接失败，可能是PLC设备问题
                            _logger.LogError(ex, "IP地址 {IpAddress} PLC连接失败", ipAddress);
                            
                            // 不管connectionLost标志，每次连接失败都更新数据库
                            foreach (var device in allDevices)
                            {
                                // 更新内存中的状态
                                if (_deviceStatus.TryGetValue(device.Id, out var status))
                                {
                                    status.IsOnline = false;
                                    status.Error = $"PLC连接失败: {ex.Message}";
                                }
                                
                                // 强制更新数据库
                                await UpdateDeviceStatusInDatabase(device.Id, false, $"PLC连接失败: {ex.Message}");
                            }
                            
                            // 设置connectionLost标志
                            connectionLost = true;
                            
                            await Task.Delay(PlcCommunicationConfig.ReconnectWaitTime, cancellationToken);
                            continue;
                        }
                    }

                    // 遍历所有设备，读取各自的信号
                    foreach (var device in allDevices)
                    {
                        // 检查设备是否有分配的信号
                        if (device.Signals == null || device.Signals.Count == 0)
                        {
                            _logger.LogWarning("设备 {DeviceId} ({IpAddress}) 未分配信号，跳过读取", device.Id, ipAddress);
                            
                            // 更新设备状态（连接正常但无信号可读）
                            if (_deviceStatus.TryGetValue(device.Id, out var emptySignalStatus))
                            {
                                emptySignalStatus.IsOnline = true;
                                emptySignalStatus.LastCommunicationTime = DateTime.Now;
                                emptySignalStatus.Error = "无信号可读";
                            }
                            
                            continue;
                        }

                        try
                        {
                            // PLC连接正常且有信号，读取信号值
                            await ReadPlcSignalsAsync(device);
                            
                            // 通信成功，更新设备状态
                            if (_deviceStatus.TryGetValue(device.Id, out var successStatus))
                            {
                                successStatus.IsOnline = true;
                                successStatus.LastCommunicationTime = DateTime.Now;
                                successStatus.RetryCount = 0;
                                successStatus.Error = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 检查是否是连接被释放的问题
                            bool isDisposed = ex is ObjectDisposedException || 
                                ex.Message.Contains("Cannot access a disposed object") ||
                                (ex.GetType().Name == "PlcException" && ex.InnerException is ObjectDisposedException);
                                
                            if (isDisposed)
                            {
                                _logger.LogWarning("设备 {DeviceId} 的连接对象已被释放，将清理资源", device.Id);
                                
                                // 清理所有设备的连接
                                foreach (var dev in allDevices)
                                {
                                    _plcConnections.TryRemove(dev.Id, out _);
                                }
                                
                                // 在下一个循环中会自动重新连接
                                break;
                            }
                            
                            _logger.LogError(ex, "读取设备 {DeviceId} 的信号失败", device.Id);
                            
                            // 更新设备状态
                            if (_deviceStatus.TryGetValue(device.Id, out var errorStatus))
                            {
                                errorStatus.IsOnline = false;
                                errorStatus.Error = ex.Message;
                                errorStatus.RetryCount++;
                            }
                            
                            // 每次读取失败也更新数据库
                            await UpdateDeviceStatusInDatabase(device.Id, false, $"读取信号失败");
                        }
                    }
                    
                    // 读取成功，重置连接丢失标记
                    if (connectionLost)
                    {
                        connectionLost = false;
                        _logger.LogInformation("IP地址 {IpAddress} 通信已恢复正常", ipAddress);
                        
                        // 通信恢复时，也更新所有设备的状态为在线
                        foreach (var device in allDevices)
                        {
                            await UpdateDeviceStatusInDatabase(device.Id, true, null);
                        }
                    }

                    // 等待通信周期
                    await Task.Delay(PlcCommunicationConfig.CommunicationCycle, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 任务被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    // 检测连接被释放的情况并重置连接
                    bool isConnectionDisposed = ex is ObjectDisposedException || 
                        (ex.GetType().Name == "PlcException" && ex.InnerException is ObjectDisposedException) ||
                        ex.Message.Contains("Cannot access a disposed object");
                    
                    if (isConnectionDisposed)
                    {
                        _logger.LogWarning("检测到PLC连接已被释放，正在清理连接资源");
                        
                        // 清理所有设备的连接
                        foreach (var device in allDevices)
                        {
                            _plcConnections.TryRemove(device.Id, out _);
                        }
                    }

                    // 更新所有设备的状态
                    foreach (var device in allDevices)
                    {
                        if (_deviceStatus.TryGetValue(device.Id, out var errorStatus))
                        {
                            errorStatus.IsOnline = false;
                            errorStatus.Error = ex.Message;
                            errorStatus.RetryCount++;
                        }
                        
                        // 不管connectionLost标志，每次发生异常都更新数据库
                        await UpdateDeviceStatusInDatabase(device.Id, false, ex.Message);
                    }

                    _logger.LogError(ex, "IP地址 {IpAddress} 通信出错", ipAddress);
                    
                    // 设置connectionLost标志
                    connectionLost = true;
                    
                    // 如果是连接问题，尝试重连
                    if (ex is InvalidOperationException || ex is System.IO.IOException)
                    {
                        reconnectCount++;
                        _logger.LogWarning("IP地址 {IpAddress} 将尝试重新连接，第 {Count} 次重连", 
                            ipAddress, reconnectCount);
                            
                        try
                        {
                            // 尝试断开并重新连接
                            await ReconnectPlcAsync(primaryDevice);
                            var connection = _plcConnections[primaryDevice.Id];
                            
                            // 为所有设备共享连接
                            foreach (var device in allDevices)
                            {
                                if (device.Id != primaryDevice.Id)
                                {
                                    _plcConnections[device.Id] = connection;
                                }
                            }
                        }
                        catch (Exception reconnectEx)
                        {
                            _logger.LogError(reconnectEx, "IP地址 {IpAddress} 重连失败", ipAddress);
                            
                            // 重连失败也更新数据库
                            foreach (var device in allDevices)
                            {
                                await UpdateDeviceStatusInDatabase(device.Id, false, $"重连失败: {reconnectEx.Message}");
                            }
                        }
                    }
                    
                    // 等待重试间隔
                    await Task.Delay(PlcCommunicationConfig.ReconnectWaitTime, cancellationToken);
                }
            }

            _logger.LogInformation("IP地址 {IpAddress} 通信循环结束，共重连 {ReconnectCount} 次", 
                ipAddress, reconnectCount);
        }

        /// <summary>
        /// 写入PLC心跳信号
        /// </summary>
        /// <summary>
        /// 写入PLC心跳信号
        /// </summary>
        private async Task WriteHeartbeatSignalsAsync(RCS_PlcDevice device)
        {
            try
            {
                if (device == null)
                {
                    _logger.LogWarning("无法写入心跳信号：设备为空");
                    return;
                }

                // 从数据库获取设备的所有信号
                var allSignals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(device.IpAddress);
                if (allSignals == null || !allSignals.Any())
                {
                    return; // 没有找到信号，直接返回
                }

                // 筛选出与当前设备DB块匹配的信号
                var deviceSignals = allSignals.Where(s =>
                    s.PLCTypeDb == device.ModuleAddress ||
                    (string.IsNullOrEmpty(s.PLCTypeDb) && string.IsNullOrEmpty(device.ModuleAddress))).ToList();

                // 从中找出心跳信号
                var heartbeatSignals = deviceSignals
                    .Where(s => s.Remark == "进站心跳")
                    .ToList();

                if (!heartbeatSignals.Any())
                {
                    return; // 没有心跳信号，直接返回
                }

                // 遍历所有心跳信号并创建任务
                foreach (var heartbeatSignal in heartbeatSignals)
                {
                    try
                    {
                        // 检查数据库中的当前值，如果已经是1则跳过
                        if (heartbeatSignal.CurrentValue == "1")
                        {
                            continue;
                        }

                        // 检查是否已经存在未处理的心跳任务
                        using (var conn = _db.CreateConnection())
                        {
                            // 检查是否存在未处理的心跳任务
                            var existingTask = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                @"SELECT TOP 1 * FROM AutoPlcTasks 
                                  WHERE Signal = @Signal 
                                  AND PlcType = @PlcType 
                                  AND PLCTypeDb = @PLCTypeDb 
                                  AND IsSend = 0 
                                  AND Status = 1 
                                  AND Remark = '心跳信号'
                                  ORDER BY CreatingTime DESC",
                                new
                                {
                                    Signal = heartbeatSignal.Name,
                                    PlcType = device.IpAddress,
                                    PLCTypeDb = device.ModuleAddress
                                });

                            // 如果已经存在未处理的任务，则跳过创建新任务
                            if (existingTask != null)
                            {
                                continue;
                            }

                            // 创建新的心跳信号任务
                            string sql = @"INSERT INTO AutoPlcTasks (OrderCode, Status, IsSend, Signal, CreatingTime, Remark, PlcType, PLCTypeDb)
                                           VALUES (@OrderCode, @Status, @IsSend, @Signal, @CreatingTime, @Remark, @PlcType, @PLCTypeDb)";
                            await conn.ExecuteAsync(sql, new
                            {
                                OrderCode = Guid.NewGuid().ToString(),
                                Status = 1, // 1表示设置为true
                                IsSend = 0,
                                Signal = heartbeatSignal.Name,
                                CreatingTime = DateTime.Now,
                                Remark = "心跳信号",
                                PlcType = device.IpAddress,
                                PLCTypeDb = device.ModuleAddress
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "创建心跳信号 {SignalRemark} 任务失败",
                            heartbeatSignal.Remark);
                        // 继续处理下一个信号，不抛出异常
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获所有异常，不影响主流程
                _logger.LogError(ex, "处理设备 {DeviceId} 心跳信号过程中发生错误", device.Id);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _logger.LogInformation("正在释放PLC通信服务资源...");
                    
                    // 停止配置刷新任务
                    if (_configRefreshCts != null)
                    {
                        try 
                        {
                            _configRefreshCts.Cancel();
                            _configRefreshCts.Dispose();
                        }
                        catch (Exception ex) 
                        { 
                            _logger.LogWarning(ex, "释放配置刷新任务时出错"); 
                        }
                        _configRefreshCts = null;
                    }
                    
                    // 释放所有的取消令牌源
                    foreach (var deviceId in _deviceTokenSources.Keys.ToList())
                    {
                        if (_deviceTokenSources.TryRemove(deviceId, out var cts))
                    {
                        try
                        {
                            cts.Cancel();
                            cts.Dispose();
                        }
                            catch (ObjectDisposedException)
                            {
                                // 已经释放，忽略
                            }
                            catch (Exception ex) 
                            { 
                                _logger.LogWarning(ex, "释放设备 {DeviceId} 的取消令牌时出错", deviceId); 
                            }
                        }
                    }
                    
                    _deviceTokenSources.Clear();
                    _deviceTasks.Clear();
                    
                    // 释放所有PLC连接
                    foreach (var deviceId in _plcConnections.Keys.ToList())
                    {
                        if (_plcConnections.TryRemove(deviceId, out var connection))
                    {
                        try
                        {
                                if (connection is S7NetPlc siemens)
                                {
                                    try { siemens.Close(); } catch { }
                                }
                                
                            if (connection is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                        }
                            catch (Exception ex) 
                            { 
                                _logger.LogWarning(ex, "释放设备 {DeviceId} 的PLC连接时出错", deviceId); 
                            }
                        }
                    }
                    
                    _plcConnections.Clear();
                    _deviceStatus.Clear();
                    
                    try
                    {
                    _serviceActionLock.Dispose();
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogWarning(ex, "释放服务锁时出错"); 
                    }
                    
                    _logger.LogInformation("PLC通信服务资源已释放");
                }

                _isDisposed = true;
            }
        }

        ~PlcCommunicationService()
        {
            Dispose(false);
        }

        /// <summary>
        /// 确保欧姆龙PLC连接正常，如果不正常则重连
        /// </summary>
        private async Task<bool> EnsureOmronConnectionAsync(OmronFinsUdp omron)
        {
            // 始终假设连接正常，不进行测试（用户已注释掉测试代码）
            try
            {
                // 先保存当前连接信息
                string ipAddress = omron.IpAddress;
                int port = omron.Port;
                
                // 重新设置连接参数
                omron.IpAddress = ipAddress; // 确保IP地址被重新赋值
                omron.Port = 9600; // 固定使用9600端口
                omron.SA1 = 192;
                omron.DA1 = 0;
                omron.DA2 = 0;
                omron.ReceiveTimeout = 2000;
                
               // _logger.LogDebug($"重置欧姆龙PLC连接参数: IP={ipAddress}, Port=9600, SA1=192, DA1=0");
                
                // 直接返回成功，不进行连接测试
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置欧姆龙PLC连接参数时发生错误");
                return true; // 即使出错也返回成功，避免阻止后续操作
            }
        }

        /// <summary>
        /// 重置服务锁状态，解决可能的信号量问题
        /// </summary>
        public async Task ResetServiceLockAsync()
        {
            try
            {
                _logger.LogInformation("正在重置PLC通信服务锁状态...");
                
                // 尝试获取锁
                bool lockTaken = false;
                try
                {
                    lockTaken = await _serviceActionLock.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogWarning("信号量已被释放，将重新创建");
                    await RecreateServiceLockAsync();
                    return;
                }
                catch (SemaphoreFullException) 
                {
                    _logger.LogWarning("信号量已满，将重新创建");
                    await RecreateServiceLockAsync();
                    return;
                }
                
                if (lockTaken)
                {
                    _logger.LogInformation("成功获取服务锁，立即释放");
                    try 
                    {
                        _serviceActionLock.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        _logger.LogWarning("尝试释放信号量时发现信号量已满，将重新创建");
                        await RecreateServiceLockAsync();
                    }
                }
                else
                {
                    _logger.LogWarning("无法获取服务锁，可能已被其他线程占用");
                    
                    // 日志记录无法获取锁的情况
                    _logger.LogInformation("信号量状态: CurrentCount={CurrentCount}, MaximumCount={MaximumCount}",
                        GetSemaphoreCurrentCount(), GetSemaphoreMaxCount());
                    
                    // 等待较长时间后尝试获取锁，如果还是失败则重新创建
                    try
                    {
                        lockTaken = await _serviceActionLock.WaitAsync(TimeSpan.FromSeconds(30));
                        if (lockTaken)
                        {
                            _serviceActionLock.Release();
                        }
                        else
                        {
                            _logger.LogWarning("长时间无法获取服务锁，将重新创建");
                            await RecreateServiceLockAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "尝试获取服务锁时出错，将重新创建");
                        await RecreateServiceLockAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置服务锁状态失败");
                throw;
            }
        }
        
        /// <summary>
        /// 重新创建服务锁（信号量）
        /// </summary>
        private async Task RecreateServiceLockAsync()
        {
            _logger.LogWarning("正在重新创建服务锁...");
            
            // 获取全局锁以确保安全访问
            using var globalLock = new Mutex(false, "Global\\PlcCommunicationServiceLock");
            try
            {
                // 尝试获取全局锁，最多等待10秒
                bool lockAcquired = globalLock.WaitOne(10000);
                if (!lockAcquired)
                {
                    _logger.LogError("无法获取全局锁，重新创建服务锁失败");
                    return;
                }
                
                // 安全地释放当前信号量
                try
                {
                    // 由于_serviceActionLock是只读字段，不能使用Interlocked.Exchange
                    // 直接尝试释放原有信号量
                    if (_serviceActionLock != null)
                    {
                        try 
                        { 
                            // 先将计数恢复到1，防止释放时出错
                            while (GetSemaphoreCurrentCount() < 1)
                            {
                                try { _serviceActionLock.Release(); }
                                catch { break; }
                            }
                            
                            _serviceActionLock.Dispose(); 
                        }
                        catch (Exception ex) 
                        { 
                            _logger.LogWarning(ex, "释放旧服务锁时出错"); 
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "释放旧服务锁时出错");
                }
                
                // 创建新的信号量
                // 注意：由于_serviceActionLock是只读字段，我们不能直接赋值
                // 但在实际使用时通常会在构造函数中初始化这个字段
                // 这里我们无法直接修改字段，需要通过反射来修改
                try
                {
                    var newLock = new SemaphoreSlim(1, 1);
                    
                    // 使用反射设置只读字段
                    var field = typeof(PlcCommunicationService).GetField("_serviceActionLock", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        
                    if (field != null)
                    {
                        // 使反射字段可写入
                        System.Reflection.FieldInfo fieldToHack = field;
                        fieldToHack.SetValue(this, newLock);
                        _logger.LogInformation("服务锁已成功重新创建");
                    }
                    else
                    {
                        _logger.LogError("无法通过反射找到服务锁字段，重新创建失败");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "通过反射创建新服务锁失败");
                    return;
                }
                
                // 停止所有设备通信，然后重新启动
                try
                {
                    // 停止所有设备通信
                    foreach (var deviceId in _deviceTokenSources.Keys.ToList())
                    {
                        try
                        {
                            await StopDeviceCommunicationAsync(deviceId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "停止设备 {DeviceId} 通信时出错", deviceId);
                        }
                    }
                    
                    // 延迟一段时间，让所有资源有时间释放
                    await Task.Delay(3000);
                    
                    // 重新启动服务
                    _logger.LogInformation("正在重新启动PLC通信服务...");
                    
                    // 获取启用的设备
                    var devices = await _plcSignalService.GetAllPlcDevicesAsync();
                    var enabledDevices = devices.Where(d => d.IsEnabled).ToList();
                    
                    if (enabledDevices.Any())
                    {
                        // 按IP地址分组
                        var devicesByIp = enabledDevices.GroupBy(d => d.IpAddress).ToList();
                        
                        foreach (var deviceGroup in devicesByIp)
                        {
                            var primaryDevice = deviceGroup.First();
                            
                            // 获取信号
                            foreach (var device in deviceGroup)
                            {
                                var signals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(device.IpAddress);
                                device.Signals = signals.Where(s => s.PLCTypeDb == device.ModuleAddress || 
                                                          (string.IsNullOrEmpty(s.PLCTypeDb) && 
                                                           string.IsNullOrEmpty(device.ModuleAddress))).ToList();
                            }
                            
                            // 启动通信
                            foreach (var device in deviceGroup)
                            {
                                try
                                {
                                    await StartDeviceCommunicationAsync(device);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "重新启动设备 {DeviceId} 通信时出错", device.Id);
                                }
                            }
                        }
                    }
                    
                    _logger.LogInformation("PLC通信服务已重新启动");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重启PLC通信服务失败");
                }
            }
            finally
            {
                // 释放全局锁
                try { globalLock.ReleaseMutex(); }
                catch { }
            }
        }
        
        /// <summary>
        /// 安全执行需要锁的操作
        /// </summary>
        private async Task SafeExecuteWithLockAsync(Func<Task> action, string operationName)
        {
            bool lockTaken = false;
            try
            {
                // 尝试获取锁，设置超时避免无限等待
                lockTaken = await _serviceActionLock.WaitAsync(TimeSpan.FromSeconds(10));
                if (!lockTaken)
                {
                    _logger.LogWarning("获取 {OperationName} 操作锁超时", operationName);
                    
                    // 检查是否需要重置锁
                    if (GetSemaphoreCurrentCount() <= 0)
                    {
                        _logger.LogWarning("信号量当前计数 <= 0，可能存在问题，尝试重置");
                        await ResetServiceLockAsync();
                        
                        // 再次尝试获取锁
                        lockTaken = await _serviceActionLock.WaitAsync(TimeSpan.FromSeconds(5));
                        if (!lockTaken)
                        {
                            _logger.LogError("重置锁后仍无法获取 {OperationName} 操作锁", operationName);
                            throw new TimeoutException($"获取 {operationName} 操作锁超时");
                        }
                    }
                    else
                    {
                        throw new TimeoutException($"获取 {operationName} 操作锁超时");
                    }
                }
                
                // 执行操作
                await action();
            }
            catch (SemaphoreFullException ex)
            {
                _logger.LogError(ex, "{OperationName} 操作时发生信号量已满异常", operationName);
                
                // 尝试重置锁
                await ResetServiceLockAsync();
                throw;
            }
            finally
            {
                // 确保释放锁
                if (lockTaken)
                {
                    try
                    {
                        _serviceActionLock.Release();
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogError(ex, "释放 {OperationName} 操作锁时发生信号量已满异常", operationName);
                        await ResetServiceLockAsync();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        _logger.LogWarning(ex, "释放 {OperationName} 操作锁时信号量已被释放", operationName);
                    }
                }
            }
        }

        /// <summary>
        /// 获取信号量当前计数（反射方法，仅用于诊断）
        /// </summary>
        private int GetSemaphoreCurrentCount()
        {
            try
            {
                var field = typeof(SemaphoreSlim).GetField("m_currentCount", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    return (int)field.GetValue(_serviceActionLock);
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// 获取信号量最大计数（反射方法，仅用于诊断）
        /// </summary>
        private int GetSemaphoreMaxCount()
        {
            try
            {
                var field = typeof(SemaphoreSlim).GetField("m_maxCount", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    return (int)field.GetValue(_serviceActionLock);
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// 根据信号信息和任务状态更新匹配的任务为已处理
        /// </summary>
        private async Task UpdateMatchingTasksAsync(RCS_PlcSignal signal,int status)
        {
            // 由于数据库连接问题，这里我们只记录日志，表明验证成功
            // 实际任务状态更新将在下一个PlcTaskProcessor循环中进行
            //_logger.LogInformation("布尔值写入验证成功: Signal={SignalName}, PLCTypeDb={PLCTypeDb},Status={Status}", signal.Name, signal.PLCTypeDb, status);
                
            
            // 标记任务处理：将写入验证成功的事实记录下来
            //_logger.LogInformation("信号 {SignalName} 已成功验证并更新，相关任务将在下次循环中标记为已完成", signal.Name);

            //找出任务
            var taskModel =await _plcSignalService.GetAutoTask(signal.PlcDeviceId, signal.PLCTypeDb, signal.Name, status);

            if (taskModel!=null)
            {
                //_logger.LogInformation($"查找到任务ID为{taskModel.Id}");
                await _plcSignalService.UpdateAutoTask(taskModel.Id);
            }

            return;
        }

        /// <summary>
        /// 心跳服务的写入
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="signalId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public async Task WriteSignalHeatValueAsync(int deviceId, int signalId, object value)
        {
            try
            {
                // 获取设备和信号信息
                var device = await _plcSignalService.GetPlcDeviceByIdAsync(deviceId);
                if (device == null)
                {
                    throw new ArgumentException($"设备ID {deviceId} 不存在");
                }

                var signal = await _plcSignalService.GetPlcSignalByIdAsync(signalId);
                if (signal == null)
                {
                    throw new ArgumentException($"信号ID {signalId} 不存在");
                }

                // 尝试获取心跳专用的连接，如果不存在或无效则创建
                object connection = null;
                bool needNewConnection = true;

                // 检查是否已有连接并且是否有效
                if (_heartbeatConnections.TryGetValue(deviceId, out connection) && connection != null)
                {
                    if (device.Brand?.ToLower().Contains("西门子") == true && connection is S7NetPlc siemens)
                    {
                        needNewConnection = !siemens.IsConnected;
                    }
                    else
                    {
                        // 欧姆龙连接不需要特别检查，暂时认为有效
                        needNewConnection = false;
                    }
                }

                // 如果需要新连接，根据PLC品牌创建对应连接
                if (needNewConnection)
                {
                    connection = await CreateHeartbeatConnectionAsync(device);
                    _heartbeatConnections[deviceId] = connection;
                }

                // 写入心跳信号
                await WriteHeartbeatValueWithRetryAsync(device, signal, connection, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "心跳信号写入失败 DeviceID={DeviceId}, SignalID={SignalId}, Value={Value}", 
                    deviceId, signalId, value);
                throw;
            }
        }

        /// <summary>
        /// 创建用于心跳的PLC连接
        /// </summary>
        private async Task<object> CreateHeartbeatConnectionAsync(RCS_PlcDevice device)
        {
            try
            {
                if (device.Brand?.ToLower().Contains("西门子") == true)
                {
                    var plc = new S7NetPlc(
                        S7.Net.CpuType.S71200,
                        device.IpAddress,
                        0, 1);
                    await Task.Run(() => plc.Open());
                    return plc;
                }
                else if (device.Brand?.ToLower().Contains("欧姆龙") == true)
                {
                    var plc = new OmronFinsUdp
                    {
                        IpAddress = device.IpAddress,
                        Port = 9600, // 固定使用9600端口
                        SA1 = 192,
                        DA1 = 0,
                        DA2 = 0,
                        ReceiveTimeout = 2000
                    };
                    return plc;
                }
                else
                {
                    throw new NotSupportedException($"不支持的PLC品牌: {device.Brand}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备ID={DeviceId}创建心跳连接失败", device.Id);
                throw;
            }
        }

        /// <summary>
        /// 使用重试机制写入心跳值
        /// </summary>
        private async Task WriteHeartbeatValueWithRetryAsync(RCS_PlcDevice device, RCS_PlcSignal signal, object connection, object value)
        {
            int retryCount = 0;
            const int maxRetries = 2;
            bool success = false;
            Exception lastException = null;

            while (!success && retryCount <= maxRetries)
            {
                try
                {
                    using var cts = new CancellationTokenSource(4000); // 4秒超时

                    if (device.Brand?.ToLower().Contains("西门子") == true && connection is S7NetPlc siemens)
                    {
                        string address = ParseSiemensAddress(signal.Offset, signal.DataType, signal.PLCTypeDb);
                        bool boolValue = Convert.ToBoolean(value);
                        await Task.Run(() => siemens.Write(address, boolValue), cts.Token);
                    }
                    else if (device.Brand?.ToLower().Contains("欧姆龙") == true && connection is OmronFinsUdp omron)
                    {
                        string address = ParseOmronAddress(signal.Offset);
                        bool boolValue = Convert.ToBoolean(value);
                        await Task.Run(() => omron.Write(address, boolValue), cts.Token);
                    }
                    else
                    {
                        throw new NotSupportedException($"不支持的PLC品牌或连接类型: {device.Brand}");
                    }

                    success = true;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("心跳信号写入超时，设备ID: {DeviceId}, 尝试次数: {RetryCount}/{MaxRetries}", 
                        device.Id, retryCount + 1, maxRetries);
                    lastException = new TimeoutException("心跳信号写入操作超时");
                    
                    // 超时后重新创建连接
                    if (retryCount < maxRetries)
                    {
                        try
                        {
                            // 清理心跳专用连接
                            _heartbeatConnections.TryRemove(device.Id, out _);
                            // 重新创建连接
                            connection = await CreateHeartbeatConnectionAsync(device);
                            _heartbeatConnections[device.Id] = connection;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "重新创建心跳连接失败，设备ID: {DeviceId}", device.Id);
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 检查是否是连接被远程主机关闭的错误
                    bool isConnectionClosed = ex.Message.Contains("远程主机强迫关闭") || 
                                            ex.Message.Contains("connection was forcibly closed") ||
                                            ex.ToString().Contains("SocketException");
                    
                    _logger.LogWarning(ex, "心跳信号写入失败，设备ID: {DeviceId}, 尝试次数: {RetryCount}/{MaxRetries}", 
                        device.Id, retryCount + 1, maxRetries);
                    lastException = ex;
                    
                    // 连接错误后重新创建连接
                    if (isConnectionClosed && retryCount < maxRetries)
                    {
                        try
                        {
                            // 清理心跳专用连接
                            _heartbeatConnections.TryRemove(device.Id, out _);
                            // 重新创建连接
                            connection = await CreateHeartbeatConnectionAsync(device);
                            _heartbeatConnections[device.Id] = connection;
                        }
                        catch (Exception connEx)
                        {
                            _logger.LogError(connEx, "重新创建心跳连接失败，设备ID: {DeviceId}", device.Id);
                            throw;
                        }
                    }
                    else if (retryCount >= maxRetries)
                    {
                        // 达到最大重试次数，抛出异常
                        throw;
                    }
                }

                retryCount++;
                if (!success && retryCount <= maxRetries)
                {
                    // 延迟后重试，延迟时间随重试次数增加
                    await Task.Delay(500 * retryCount);
                }
            }

            // 所有重试都失败
            if (!success)
            {
                throw lastException ?? new Exception("心跳信号写入失败，原因未知");
            }
        }

        /// <summary>
        /// 读取指定PLC设备的信号值
        /// </summary>
        public async Task<string> ReadSignalValueAsync(int deviceId, int signalId)
        {
            // 获取设备和信号信息
            var device = await _plcSignalService.GetPlcDeviceByIdAsync(deviceId);
            if (device == null)
            {
                throw new ArgumentException($"设备ID {deviceId} 不存在");
            }

            var signal = await _plcSignalService.GetPlcSignalByIdAsync(signalId);
            if (signal == null)
            {
                throw new ArgumentException($"信号ID {signalId} 不存在");
            }

            // 获取PLC连接
            if (!_plcConnections.TryGetValue(deviceId, out var connection))
            {
                throw new InvalidOperationException($"设备ID {deviceId} 未建立连接");
            }

            try
            {
                // 根据PLC品牌读取信号值
                if (device.Brand?.ToLower().Contains("西门子") == true && connection is S7NetPlc siemens)
                {
                    return await ReadSiemensSignalValueAsync(siemens, signal);
                }
                else if (device.Brand?.ToLower().Contains("欧姆龙") == true && connection is OmronFinsUdp omron)
                {
                    return await ReadOmronSignalValueAsync(omron, signal);
                }
                else
                {
                    throw new NotSupportedException($"不支持的PLC品牌: {device.Brand}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取设备 {DeviceId} 信号 {SignalId} 失败", deviceId, signalId);
                throw;
            }
        }
    }
} 
