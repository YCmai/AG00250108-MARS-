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

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// 心跳服务
    /// </summary>
    public class HeartbeatService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HeartbeatService> _logger;
        private readonly CancellationTokenSource _cts = new();
        private Task _heartbeatTask;
        private bool _currentHeartbeatState = true; // 当前心跳状态
        private readonly Dictionary<string, DateTime> _lastHeartbeatTime = new();
        private const int HeartbeatIntervalSeconds = 1; // 心跳间隔秒数
        private readonly IPlcCommunicationService _heartbeatPlcService;

        public HeartbeatService(
            IServiceProvider serviceProvider,
            ILogger<HeartbeatService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            // 创建专门用于心跳的PLC通信服务实例
            var plcSignalService = serviceProvider.GetRequiredService<IPlcSignalService>();
            var plcSignalUpdater = serviceProvider.GetRequiredService<PlcSignalUpdater>();
            var plcLogger = serviceProvider.GetRequiredService<ILogger<PlcCommunicationService>>();
            var dbService = serviceProvider.GetRequiredService<IDatabaseService>();
            
            _heartbeatPlcService = new PlcCommunicationService(
                plcSignalService,
                plcSignalUpdater,
                plcLogger,
                dbService);
        }

        /// <summary>
        /// 启动心跳服务
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("心跳服务正在启动...");
            _heartbeatTask = ProcessHeartbeatsAsync();
            _logger.LogInformation("心跳服务已启动");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止心跳服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("心跳服务正在停止...");
            _cts.Cancel();
            
            if (_heartbeatTask != null)
            {
                await Task.WhenAny(_heartbeatTask, Task.Delay(5000));
            }
            
            _logger.LogInformation("心跳服务已停止");
        }

        /// <summary>
        /// 心跳处理循环
        /// </summary>
        private async Task ProcessHeartbeatsAsync()
        {
            _logger.LogInformation("心跳处理循环已启动");

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

                    // 获取设备和对应的心跳信号
                    var deviceSignals = await GetHeartbeatDeviceSignalsAsync(dbService);
                    var now = DateTime.Now;

                    foreach (var (device, signal) in deviceSignals)
                    {
                        try
                        {
                            string deviceKey = $"{device.IpAddress}_{device.ModuleAddress}";
                            
                            // 检查是否需要发送心跳
                            if (_lastHeartbeatTime.TryGetValue(deviceKey, out var lastTime) && 
                                (now - lastTime).TotalSeconds < HeartbeatIntervalSeconds)
                            {
                                continue; // 跳过，等待下一个间隔
                            }

                            // 写入相反的心跳状态
                            await _heartbeatPlcService.WriteSignalHeatValueAsync(
                                device.Id,
                                signal.Id,
                                _currentHeartbeatState);

                            _lastHeartbeatTime[deviceKey] = now;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "处理设备 {IpAddress} 心跳信号时发生错误", device.IpAddress);
                        }
                    }

                    // 切换心跳状态用于下一轮
                    _currentHeartbeatState = !_currentHeartbeatState;
                    await Task.Delay(HeartbeatIntervalSeconds * 1000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "心跳处理循环发生错误");
                    await Task.Delay(1000, _cts.Token);
                }
            }
        }

        /// <summary>
        /// 获取心跳设备及其对应的心跳信号
        /// </summary>
        private async Task<List<(RCS_PlcDevice Device, RCS_PlcSignal Signal)>> GetHeartbeatDeviceSignalsAsync(IDatabaseService dbService)
        {
            using var conn = dbService.CreateConnection();
            var result = new List<(RCS_PlcDevice, RCS_PlcSignal)>();
            
            // 遍历配置的心跳设备
            foreach (var config in HeartbeatConfig.Devices)
            {
                // 查询启用的设备
                var device = await conn.QueryFirstOrDefaultAsync<RCS_PlcDevice>(@"
                    SELECT * FROM RCS_PlcDevice 
                    WHERE IsEnabled = 1 
                    AND IpAddress = @IpAddress 
                    AND ModuleAddress = @ModuleAddress",
                    new { 
                        config.IpAddress, 
                        config.ModuleAddress 
                    });
                    
                if (device == null) continue;
                
                // 直接查询对应的心跳信号
                var signal = await conn.QueryFirstOrDefaultAsync<RCS_PlcSignal>(@"
                    SELECT * FROM RCS_PlcSignal 
                    WHERE PlcDeviceId = @PlcDeviceId 
                    AND PLCTypeDb = @PLCTypeDb 
                    AND Remark = '进站心跳'",
                    new { 
                        PlcDeviceId = device.IpAddress, 
                        PLCTypeDb = config.ModuleAddress 
                    });
                
                if (signal != null)
                {
                    result.Add((device, signal));
                }
            }

            return result;
        }
    }
} 