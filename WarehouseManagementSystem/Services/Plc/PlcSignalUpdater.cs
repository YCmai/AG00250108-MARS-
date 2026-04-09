using WarehouseManagementSystem.Models.PLC;
using System.Collections.Concurrent;

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC信号值更新器，用于线程安全地更新信号值
    /// </summary>
    public class PlcSignalUpdater
    {
        private readonly IPlcSignalService _plcSignalService;
        private readonly ILogger<PlcSignalUpdater> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastUpdateTimes = new();
        private readonly TimeSpan _updateThreshold = TimeSpan.FromMilliseconds(100); // 更新信号值的最小时间间隔

        public PlcSignalUpdater(IPlcSignalService plcSignalService, ILogger<PlcSignalUpdater> logger)
        {
            _plcSignalService = plcSignalService ?? throw new ArgumentNullException(nameof(plcSignalService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 批量更新信号值
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="signalUpdates">信号ID和值字典</param>
        public async Task BatchUpdateSignalValues(string deviceId, Dictionary<int, string> signalUpdates)
        {
            if (signalUpdates == null || !signalUpdates.Any())
            {
                return;
            }

            var deviceLock = _deviceLocks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
            bool lockTaken = false;
            
            try
            {
                // 检查上次更新时间，避免过于频繁的更新
                var now = DateTimeOffset.Now;
                if (_lastUpdateTimes.TryGetValue(deviceId, out var lastUpdate) && 
                    (now - lastUpdate) < _updateThreshold)
                {
                    return;
                }
                
                // 添加超时，避免无限等待
                lockTaken = await deviceLock.WaitAsync(TimeSpan.FromSeconds(5));
                if (!lockTaken)
                {
                    _logger.LogWarning("获取设备 {DeviceId} 的锁超时，跳过本次更新", deviceId);
                    return;
                }
                
                // 记录本次更新时间
                _lastUpdateTimes[deviceId] = now;
                
                try
                {
                    var signals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(deviceId);
                    var signalMap = signals.ToDictionary(s => s.Id, s => s);
                    var updateBatch = new List<RCS_PlcSignal>();
                    
                    foreach (var update in signalUpdates)
                    {
                        if (signalMap.TryGetValue(update.Key, out var signal))
                        {
                            // 只有当值变化时才更新
                            if (signal.CurrentValue != update.Value)
                            {
                                signal.CurrentValue = update.Value;
                                signal.UpdateTime = DateTime.Now;
                                updateBatch.Add(signal);
                            }
                        }
                    }
                    
                    if (updateBatch.Any())
                    {
                        foreach (var signal in updateBatch)
                        {
                            await _plcSignalService.UpdatePlcSignalAsync(signal);
                        }
                        
                        //_logger.LogDebug("已批量更新设备 {DeviceId} 的 {Count} 个信号值", 
                        //    deviceId, updateBatch.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量更新设备 {DeviceId} 的信号值失败", deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理设备 {DeviceId} 的信号更新过程中发生异常", deviceId);
            }
            finally
            {
                // 只有在成功获取锁的情况下才释放
                if (lockTaken)
                {
                    try
                    {
                        deviceLock.Release();
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogError(ex, "释放设备 {DeviceId} 锁时发生信号量已满异常", deviceId);
                        // 移除问题锁，下次将创建新的
                        _deviceLocks.TryRemove(deviceId, out _);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        _logger.LogWarning(ex, "释放设备 {DeviceId} 锁时发现信号量已被释放", deviceId);
                        // 移除问题锁，下次将创建新的
                        _deviceLocks.TryRemove(deviceId, out _);
                    }
                }
            }
        }

        /// <summary>
        /// 更新单个信号值
        /// </summary>
        /// <param name="signalId">信号ID</param>
        /// <param name="value">新值</param>
        public async Task UpdateSignalValue(int signalId, string value)
        {
            try
            {
                var signal = await _plcSignalService.GetPlcSignalByIdAsync(signalId);
                if (signal == null)
                {
                    _logger.LogWarning("更新信号值失败：信号 {SignalId} 不存在", signalId);
                    return;
                }
                
                // 获取设备锁
                var deviceLock = _deviceLocks.GetOrAdd(signal.PlcDeviceId, _ => new SemaphoreSlim(1, 1));
                bool lockTaken = false;
                
                try
                {
                    // 添加超时，避免无限等待
                    lockTaken = await deviceLock.WaitAsync(TimeSpan.FromSeconds(5));
                    if (!lockTaken)
                    {
                        _logger.LogWarning("获取设备 {DeviceId} 的锁超时，跳过更新信号 {SignalId}", signal.PlcDeviceId, signalId);
                        return;
                    }
                    
                    // 只有当值变化时才更新
                    if (signal.CurrentValue != value)
                    {
                        signal.CurrentValue = value;
                        signal.UpdateTime = DateTime.Now;
                        await _plcSignalService.UpdatePlcSignalAsync(signal);
                        
                        _logger.LogDebug("已更新信号 {SignalId} 的值为 {Value}", signalId, value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理信号 {SignalId} 的更新过程中发生异常", signalId);
                }
                finally
                {
                    // 只有在成功获取锁的情况下才释放
                    if (lockTaken)
                    {
                        try
                        {
                            deviceLock.Release();
                        }
                        catch (SemaphoreFullException ex)
                        {
                            _logger.LogError(ex, "释放设备 {DeviceId} 锁时发生信号量已满异常", signal.PlcDeviceId);
                            // 移除问题锁，下次将创建新的
                            _deviceLocks.TryRemove(signal.PlcDeviceId, out _);
                        }
                        catch (ObjectDisposedException ex)
                        {
                            _logger.LogWarning(ex, "释放设备 {DeviceId} 锁时发现信号量已被释放", signal.PlcDeviceId);
                            // 移除问题锁，下次将创建新的
                            _deviceLocks.TryRemove(signal.PlcDeviceId, out _);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新信号 {SignalId} 的值失败", signalId);
            }
        }
        
        /// <summary>
        /// 重置设备锁，用于处理信号量异常情况
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        public void ResetDeviceLock(string deviceId)
        {
            try
            {
                if (_deviceLocks.TryRemove(deviceId, out var oldLock))
                {
                    try
                    {
                        oldLock.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "释放设备 {DeviceId} 的旧锁时发生异常", deviceId);
                    }
                }
                
                // 创建新锁
                _deviceLocks[deviceId] = new SemaphoreSlim(1, 1);
                _logger.LogInformation("设备 {DeviceId} 的锁已重置", deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置设备 {DeviceId} 的锁时发生异常", deviceId);
            }
        }
        
        /// <summary>
        /// 重置所有设备锁
        /// </summary>
        public void ResetAllDeviceLocks()
        {
            try
            {
                _logger.LogInformation("开始重置所有设备锁...");
                
                foreach (var deviceId in _deviceLocks.Keys.ToList())
                {
                    ResetDeviceLock(deviceId);
                }
                
                _logger.LogInformation("所有设备锁已重置");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置所有设备锁时发生异常");
            }
        }
    }
} 