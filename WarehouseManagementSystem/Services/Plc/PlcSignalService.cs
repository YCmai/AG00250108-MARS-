using Client100.Entity;

using Dapper;

using WarehouseManagementSystem.Db;
using WarehouseManagementSystem.Models.PLC;

namespace WarehouseManagementSystem.Service.Plc
{
    public class PlcSignalService : IPlcSignalService
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<PlcSignalService> _logger;

        public PlcSignalService(IDatabaseService db, ILogger<PlcSignalService> logger)
        {
            _db = db;
            _logger = logger;
        }

        #region PLC设备相关操作
        public async Task<List<RCS_PlcDevice>> GetAllPlcDevicesAsync()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var devices = await conn.QueryAsync<RCS_PlcDevice>(@"
                    SELECT *
                    FROM RCS_PlcDevice 
                    ORDER BY Id");

                var deviceList = devices.ToList();

                // 获取每个设备下的信号
                foreach (var device in deviceList)
                {
                    var signals = await conn.QueryAsync<RCS_PlcSignal>(@"
                        SELECT *
                        FROM RCS_PlcSignal
                        WHERE PlcDeviceId = @DeviceId And PLCTypeDb =@PLCTypeDb
                        ORDER BY Id", new { DeviceId = device.IpAddress, PLCTypeDb = device.ModuleAddress });

                    device.Signals = signals.ToList();
                }

                return deviceList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有PLC设备失败");
                throw;
            }
        }

        public async Task<RCS_PlcDevice> GetPlcDeviceByIdAsync(int id)
        {
            try
            {
                using var conn = _db.CreateConnection();
                var device = await conn.QueryFirstOrDefaultAsync<RCS_PlcDevice>(@"
                    SELECT Id, IpAddress, Port, IsEnabled, Brand, StationPoint, 
                    SignalRequestPoint, LeaveResetPoint, Remark, ModuleAddress, CreateTime, UpdateTime
                    FROM RCS_PlcDevice
                    WHERE Id = @Id", new { Id = id });

                if (device != null)
                {
                    var signals = await conn.QueryAsync<RCS_PlcSignal>(@"
                        SELECT Id, PlcDeviceId, DataType, Offset, Name, Writer, CurrentValue, Remark, PLCTypeDb, CreateTime, UpdateTime
                        FROM RCS_PlcSignal
                        WHERE PlcDeviceId = @DeviceId And PLCTypeDb =@PLCTypeDb
                        ORDER BY Id", new { DeviceId = device.IpAddress, PLCTypeDb = device.ModuleAddress });

                    device.Signals = signals.ToList();
                }

                return device;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC设备失败，Id: {Id}", id);
                throw;
            }
        }

        public async Task<int> AddPlcDeviceAsync(RCS_PlcDevice device)
        {
            try
            {
                using var conn = _db.CreateConnection();

                device.CreateTime = DateTime.Now;

                int deviceId = await conn.QuerySingleAsync<int>(@"
                    INSERT INTO RCS_PlcDevice (IpAddress, Port, IsEnabled, Brand, StationPoint, 
                    SignalRequestPoint, LeaveResetPoint, Remark, ModuleAddress, CreateTime)
                    VALUES (@IpAddress, @Port, @IsEnabled, @Brand, @StationPoint, 
                    @SignalRequestPoint, @LeaveResetPoint, @Remark, @ModuleAddress, @CreateTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)", device);

                // 添加设备后自动添加默认的PLC信号
                await AddDefaultPlcSignalsAsync(device.IpAddress, device.ModuleAddress, device.Brand);

                return deviceId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加PLC设备失败");
                throw;
            }
        }

        // 添加默认的PLC信号
        private async Task AddDefaultPlcSignalsAsync(string ipAddress, string dbAddress, string brand)
        {
            try
            {
                using var conn = _db.CreateConnection();


                // 获取设备的ModuleAddress作为PLCTypeDb和设备ID
                string moduleAddress = dbAddress;
                string deviceIp = ipAddress;

                // 根据提供的规范定义默认信号列表
                var defaultSignals = new List<RCS_PlcSignal>
                {
                    // 进站信号
                     new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "0.0", Name = "进站心跳", Writer = "AGV", Remark = "进站心跳", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                     new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "4.4", Name = "进站AGV请求进入", Writer = "AGV", Remark = "进站AGV请求进入", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "4.6", Name = "进站AGV已到达", Writer = "AGV", Remark = "进站AGV已到达", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                      new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "122.7", Name = "出站离开中", Writer = "AGV", Remark = "出站离开中", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "123", Name = "出站已离开", Writer = "AGV", Remark = "出站已离开", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "248.5", Name = "允许进入", Writer = "PLC", Remark = "允许进入", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "248.7", Name = "允许离开", Writer = "PLC", Remark = "允许离开", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                      new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "249.2", Name = "工位状态", Writer = "PLC", Remark = "工位状态", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "249.1", Name = "已收到离开", Writer = "PLC", Remark = "已收到离开", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                          new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "240.0", Name = "反馈已清除信号", Writer = "AGV", Remark = "反馈已清除信号", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                            new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Int", Offset = "250", Name = "指定AGV路线1容量测试2进六面检", Writer = "PLC", Remark = "指定AGV路线1容量测试2进六面检", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },

                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "4", Name = "进站AGV离线模式", Writer = "AGV", Remark = "进站AGV离线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "4.1", Name = "进站AGV在线模式", Writer = "AGV", Remark = "进站AGV在线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "4.2", Name = "进站AGV故障", Writer = "AGV", Remark = "进站AGV故障", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "4.3", Name = "进站AGV急停", Writer = "AGV", Remark = "进站AGV急停", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },

                    // 出站信号
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "122", Name = "出站AGV离线模式", Writer = "AGV", Remark = "出站AGV离线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "122.1", Name = "出站AGV在线模式", Writer = "AGV", Remark = "出站AGV在线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "122.2", Name = "出站AGV故障", Writer = "AGV", Remark = "出站AGV故障", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "122.3", Name = "出站AGV急停", Writer = "AGV", Remark = "出站AGV急停", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },

                    // PLC侧信号
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "248.2", Name = "PLC报警", Writer = "PLC", Remark = "PLC报警", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "248.3", Name = "PLC急停", Writer = "PLC", Remark = "PLC急停", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "String", Offset = "258", Name = "写入PACKID", Writer = "PLC", Remark = "写入PACKID", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                  
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "7.1", Name = "进站PACKID写入应答", Writer = "AGV", Remark = "进站PACKID写入应答", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                    new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "String", Offset = "14", Name = "进站AGV_PackID", Writer = "AGV", Remark = "进站AGV_PackID", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                      new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "118.0", Name = "出站心跳", Writer = "AGV", Remark = "出站心跳", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },

                     
                };

                if (brand == "欧姆龙")
                {
                    defaultSignals = new List<RCS_PlcSignal>
                    {

                          new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.0", Name = "进站心跳", Writer = "AGV", Remark = "进站心跳", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                            new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.5", Name = "进站AGV请求进入", Writer = "AGV", Remark = "进站AGV请求进入", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.7", Name = "进站AGV已到达", Writer = "AGV", Remark = "进站AGV已到达", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                         new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.8", Name = "出站离开中", Writer = "AGV", Remark = "出站离开中", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.9", Name = "出站已离开", Writer = "AGV", Remark = "出站已离开", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                          new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D300.6", Name = "允许进入", Writer = "PLC", Remark = "允许进入", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D300.8", Name = "允许离开", Writer = "PLC", Remark = "允许离开", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                          new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D300.10", Name = "已收到离开", Writer = "PLC", Remark = "已收到离开", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                          new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D257.0", Name = "反馈已清除信号", Writer = "AGV", Remark = "反馈已清除信号", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                            new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Int", Offset = "D302", Name = "指定AGV路线1容量测试2进六面检", Writer = "PLC", Remark = "指定AGV路线1容量测试2进六面检", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },


                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.1", Name = "进站AGV离线模式", Writer = "AGV", Remark = "进站AGV离线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.2", Name = "进站AGV在线模式", Writer = "AGV", Remark = "进站AGV在线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.3", Name = "进站AGV故障", Writer = "AGV", Remark = "进站AGV故障", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D100.4", Name = "进站AGV急停", Writer = "AGV", Remark = "进站AGV急停", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                      

                        // 出站信号
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.1", Name = "出站AGV离线模式", Writer = "AGV", Remark = "出站AGV离线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.2", Name = "出站AGV在线模式", Writer = "AGV", Remark = "出站AGV在线模式", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.3", Name = "出站AGV故障", Writer = "AGV", Remark = "出站AGV故障", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.4", Name = "出站AGV急停", Writer = "AGV", Remark = "出站AGV急停", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },

                        // PLC侧信号
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D300.3", Name = "PLC报警", Writer = "PLC", Remark = "PLC报警", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D300.4", Name = "PLC急停", Writer = "PLC", Remark = "PLC急停", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                      
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "String", Offset = "D306", Name = "写入PACKID", Writer = "PLC", Remark = "写入PACKID", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                       // new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "249.2", Name = "工位状态", Writer = "PLC", Remark = "工位状态", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now }
                         // 进站信号
                         new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D102.1", Name = "进站PACKID写入应答", Writer = "AGV", Remark = "进站PACKID写入应答", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                        new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "String", Offset = "D106", Name = "进站AGV_PackID", Writer = "AGV", Remark = "进站AGV_PackID", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },
                           new RCS_PlcSignal { PlcDeviceId = ipAddress, DataType = "Bool", Offset = "D200.0", Name = "出站心跳", Writer = "AGV", Remark = "出站心跳", PLCTypeDb = moduleAddress, CreateTime = DateTime.Now },

                          
                    };
                }


                // 批量插入默认信号
                var sql = @"
                    INSERT INTO RCS_PlcSignal (PlcDeviceId, DataType, Offset, Name, Writer, CurrentValue, Remark, PLCTypeDb, CreateTime)
                    VALUES (@PlcDeviceId, @DataType, @Offset, @Name, @Writer, @CurrentValue, @Remark, @PLCTypeDb, @CreateTime);";

                await conn.ExecuteAsync(sql, defaultSignals);
                _logger.LogInformation("成功为设备 IP={IpAddress} (ID={DeviceId}) 添加 {Count} 个默认PLC信号", ipAddress, deviceIp, defaultSignals.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备 IP={IpAddress} 添加默认PLC信号失败", ipAddress);
                // 不抛出异常，确保即使添加默认信号失败，也不影响设备的创建
            }
        }

        public async Task UpdatePlcDeviceAsync(RCS_PlcDevice device)
        {
            try
            {
                using var conn = _db.CreateConnection();

                // 设置更新时间
                device.UpdateTime = DateTime.Now;

                // 查询设备是否存在
                var existingDevice = await conn.QueryFirstOrDefaultAsync<RCS_PlcDevice>(
                    "SELECT Id FROM RCS_PlcDevice WHERE Id = @Id", new { Id = device.Id });

                if (existingDevice == null)
                {
                    throw new Exception($"设备ID {device.Id} 不存在");
                }

                // 执行更新
                int rowsAffected = await conn.ExecuteAsync(@"
                    UPDATE RCS_PlcDevice
                    SET IpAddress = @IpAddress,
                        Port = @Port,
                        IsEnabled = @IsEnabled,
                        Brand = @Brand,
                        StationPoint = @StationPoint,
                        SignalRequestPoint = @SignalRequestPoint,
                        LeaveResetPoint = @LeaveResetPoint,
                        Remark = @Remark,
                        ModuleAddress = @ModuleAddress,
                        UpdateTime = @UpdateTime
                    WHERE Id = @Id", device);

                if (rowsAffected == 0)
                {
                    throw new Exception($"更新设备失败，设备ID {device.Id} 可能已被删除");
                }

                _logger.LogInformation("成功更新PLC设备: ID={Id}", device.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PLC设备失败，Id: {Id}", device.Id);
                throw;
            }
        }

        public async Task DeletePlcDeviceAsync(int id)
        {
            try
            {
                using var conn = _db.CreateConnection();

                // 先获取设备的IpAddress
                var device = await conn.QueryFirstOrDefaultAsync<RCS_PlcDevice>(
                    "SELECT * FROM RCS_PlcDevice WHERE Id = @Id", new { Id = id });

                if (device != null)
                {
                    // 通过IpAddress删除设备下所有信号
                    await conn.ExecuteAsync("DELETE FROM RCS_PlcSignal WHERE PlcDeviceId = @IpAddress And PLCTypeDb = @PLCTypeDb",
                        new { IpAddress = device.IpAddress, PLCTypeDb = device.ModuleAddress });

                    // 再删除设备
                    await conn.ExecuteAsync("DELETE FROM RCS_PlcDevice WHERE Id = @Id", new { Id = id });
                }
                else
                {
                    _logger.LogWarning("尝试删除不存在的PLC设备，Id: {Id}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除PLC设备失败，Id: {Id}", id);
                throw;
            }
        }
        #endregion

        #region PLC信号相关操作
        public async Task<List<RCS_PlcSignal>> GetAllPlcSignalsAsync()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var signals = await conn.QueryAsync<RCS_PlcSignal>(@"
                    SELECT Id, PlcDeviceId, DataType, Offset, Name, Writer, CurrentValue, Remark, CreateTime, UpdateTime
                    FROM RCS_PlcSignal
                    ORDER BY PlcDeviceId, Id");

                return signals.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有PLC信号失败");
                throw;
            }
        }

        public async Task<List<RCS_PlcSignal>> GetPlcSignalsByDeviceIdAsync(string deviceId)
        {
            try
            {
                using var conn = _db.CreateConnection();
                var signals = await conn.QueryAsync<RCS_PlcSignal>(@"
                    SELECT Id, PlcDeviceId, DataType, Offset, Name, Writer, CurrentValue, Remark, PLCTypeDb, CreateTime, UpdateTime
                    FROM RCS_PlcSignal
                    WHERE PlcDeviceId = @DeviceId
                    ORDER BY Id", new { DeviceId = deviceId });

                return signals.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC设备下的信号失败，设备Id: {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<RCS_PlcSignal> GetPlcSignalByIdAsync(int id)
        {
            try
            {
                using var conn = _db.CreateConnection();
                return await conn.QueryFirstOrDefaultAsync<RCS_PlcSignal>(@"
                    SELECT *
                    FROM RCS_PlcSignal
                    WHERE Id = @Id", new { Id = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC信号失败，Id: {Id}", id);
                throw;
            }
        }

        public async Task<int> AddPlcSignalAsync(RCS_PlcSignal signal)
        {
            try
            {
                using var conn = _db.CreateConnection();

                signal.CreateTime = DateTime.Now;

                return await conn.QuerySingleAsync<int>(@"
                    INSERT INTO RCS_PlcSignal (PlcDeviceId, DataType, Offset, Name, Writer, CurrentValue, Remark, PLCTypeDb, CreateTime)
                    VALUES (@PlcDeviceId, @DataType, @Offset, @Name, @Writer, @CurrentValue, @Remark, @PLCTypeDb, @CreateTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)", signal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加PLC信号失败");
                throw;
            }
        }

        public async Task UpdatePlcSignalAsync(RCS_PlcSignal signal)
        {
            try
            {
                using var conn = _db.CreateConnection();

                signal.UpdateTime = DateTime.Now;

                await conn.ExecuteAsync(@"
                    UPDATE RCS_PlcSignal
                    SET 
                        PlcDeviceId = @PlcDeviceId,
                        DataType = @DataType,
                        Offset = @Offset,
                        Name = @Name,
                        Writer = @Writer,
                        CurrentValue = @CurrentValue,
                        Remark = @Remark,
                        PLCTypeDb = @PLCTypeDb,
                        UpdateTime = @UpdateTime
                    WHERE Id = @Id", signal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PLC信号失败，Id: {Id}", signal.Id);
                throw;
            }
        }

        public async Task DeletePlcSignalAsync(int id)
        {
            try
            {
                using var conn = _db.CreateConnection();
                await conn.ExecuteAsync("DELETE FROM RCS_PlcSignal WHERE Id = @Id", new { Id = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除PLC信号失败，Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// 重置PLC信号
        /// </summary>
        public async Task ResetPlcSignalAsync(int signalId)
        {
            try
            {
                using var conn = _db.CreateConnection();

                // 获取信号信息
                var signal = await conn.QueryFirstOrDefaultAsync<RCS_PlcSignal>(
                    "SELECT * FROM RCS_PlcSignal WHERE Id = @Id",
                    new { Id = signalId });

                if (signal == null)
                {
                    throw new Exception($"信号ID {signalId} 不存在");
                }

                // 重置信号值为默认值 (false/0)
                await conn.ExecuteAsync(@"
                    UPDATE RCS_PlcSignal
                    SET CurrentValue = 0, 
                        UpdateTime = @UpdateTime
                    WHERE Id = @Id",
                    new
                    {
                        Id = signalId,
                        UpdateTime = DateTime.Now
                    });

                _logger.LogInformation("成功重置PLC信号: Id={Id}", signalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置PLC信号失败，Id: {Id}", signalId);
                throw;
            }
        }

        /// <summary>
        /// 手动触发PLC信号
        /// </summary>
        public async Task ManualTriggerSignalAsync(int signalId, bool value)
        {
            try
            {
                using var conn = _db.CreateConnection();

                // 获取信号信息
                var signal = await conn.QueryFirstOrDefaultAsync<RCS_PlcSignal>(
                    "SELECT * FROM RCS_PlcSignal WHERE Id = @Id",
                    new { Id = signalId });

                if (signal == null)
                {
                    throw new Exception($"信号ID {signalId} 不存在");
                }

                // 更新信号值
                await conn.ExecuteAsync(@"
                    UPDATE RCS_PlcSignal
                    SET CurrentValue = @CurrentValue, 
                        UpdateTime = @UpdateTime
                    WHERE Id = @Id",
                    new
                    {
                        Id = signalId,
                        CurrentValue = value ? 1 : 0,
                        UpdateTime = DateTime.Now
                    });

                _logger.LogInformation("成功手动触发PLC信号: Id={Id}, Value={Value}", signalId, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "手动触发PLC信号失败，Id: {Id}, Value: {Value}", signalId, value);
                throw;
            }
        }

        public async Task<AutoPlcTask> GetAutoTask(string PlcType, string PLCTypeDb, string Signal, int Status)
        {
            try
            {
               // _logger.LogInformation($"查找交互任务PlcType-{PlcType}-PLCTypeDb-{PLCTypeDb}-Signal{Signal}-Status-{Status}");

                using var conn = _db.CreateConnection();

                // 获取信号信息
                var task = await conn.QueryFirstOrDefaultAsync<AutoPlcTask>(
                    "SELECT * FROM AutoPlcTasks WHERE Signal = @Signal And PlcType = @PlcType And PLCTypeDb = @PLCTypeDb And Status=@Status And IsSend = 0",
                    new { Signal = Signal, PlcType = PlcType, PLCTypeDb = PLCTypeDb, Status= Status });

                return task; // 返回查询结果，可能为null
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询PLC交互任务失败");
                throw;
            }
        }

        public async Task UpdateAutoTask(int Id)
        {
            try
            {
                using var conn = _db.CreateConnection();

                // 查询设备是否存在
                var existingDevice = await conn.QueryFirstOrDefaultAsync<AutoPlcTask>(
                    "SELECT * FROM AutoPlcTasks WHERE Id = @Id", new { Id = Id });

                if (existingDevice == null)
                {
                    throw new Exception($"PLC交互任务ID {Id} 不存在");
                }

                await conn.ExecuteAsync(@"
                    UPDATE AutoPlcTasks
                    SET IsSend = 1, 
                        UpdateTime = @UpdateTime
                    WHERE Id = @Id",
                   new
                   {
                       Id = Id,
                       UpdateTime = DateTime.Now
                   });

               // _logger.LogInformation("成功更新PLC任务成功: ID={Id}", Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PLC任务成功，Id: {Id}", Id);
                throw;
            }
        }
        #endregion
    }
}