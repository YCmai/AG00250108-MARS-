using Microsoft.AspNetCore.Mvc;
using WarehouseManagementSystem.Models.PLC;
using WarehouseManagementSystem.Service.Plc;
using System;
using System.Data;
using System.Data.SqlClient;
using Dapper;


    /// <summary>
    /// PLC信号状态控制器
    /// </summary>
    public class PlcSignalStatusController : Controller
    {
        private readonly IPlcSignalService _plcSignalService;
        private readonly ILogger<PlcSignalStatusController> _logger;
        private readonly WarehouseManagementSystem.Db.IDatabaseService _db;

        public PlcSignalStatusController(
            IPlcSignalService plcSignalService,
            ILogger<PlcSignalStatusController> logger,
            WarehouseManagementSystem.Db.IDatabaseService db
        )
        {
            _plcSignalService = plcSignalService;
            _logger = logger;
            _db = db;
        }

        /// <summary>
        /// PLC信号状态页面入口
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        // 2. 获取所有设备及其信号
        [HttpGet]
        public async Task<IActionResult> GetAllDevices()
        {
            try
            {
                var devices = await _plcSignalService.GetAllPlcDevicesAsync();
                return Json(new { success = true, data = devices });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC设备及信号失败");
                return Json(new { success = false, message = ex.Message });
            }
        }



        /// <summary>
        /// 获取所有PLC信号数据
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllPlcSignals()
        {
            try
            {
                var signals = await _plcSignalService.GetAllPlcSignalsAsync();
                return Json(new { success = true, data = signals });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC信号数据失败");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 获取特定设备的所有信号
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSignalsByDevice(string deviceId)
        {
            try
            {
                var signals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(deviceId);
                return Json(new { success = true, data = signals });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备信号数据失败，设备ID: {DeviceId}", deviceId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 重置PLC信号
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResetSignal(int signalId)
        {
            try
            {
                await _plcSignalService.ResetPlcSignalAsync(signalId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置PLC信号失败，信号ID: {SignalId}", signalId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 手动触发PLC信号
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TriggerSignal(int signalId, bool value)
        {
            try
            {
                // 获取信号信息
                var signal = await _plcSignalService.GetPlcSignalByIdAsync(signalId);
                if (signal == null)
                {
                    return Json(new { success = false, message = $"信号ID {signalId} 不存在" });
                }

                // 处理值
                int status = value ? 1 : 2;
                string signalValue = "人工重置";
                
                // 对于字符串类型的信号特殊处理
                if (signal.DataType != null && (signal.DataType.Equals("String", StringComparison.OrdinalIgnoreCase) 
                    || signal.DataType.Equals("string", StringComparison.OrdinalIgnoreCase)))
                {
                    status = value ? 5 : 6;
                    
                    if (status == 5) // 随机字符串
                    {
                        signalValue = Guid.NewGuid().ToString().Substring(0, 8);
                    }
                    else // 空值
                    {
                        signalValue = string.Empty;
                    }
                }

                // 插入AutoPlcTasks任务
                using (var conn = _db.CreateConnection())
                {
                    string sql = @"INSERT INTO AutoPlcTasks (OrderCode, Status, IsSend, Signal, CreatingTime, Remark, PlcType, PLCTypeDb)
                                   VALUES (@OrderCode, @Status, @IsSend, @Signal, @CreatingTime, @Remark, @PlcType, @PLCTypeDb)";
                    await conn.ExecuteAsync(sql, new
                    {
                        OrderCode = Guid.NewGuid().ToString(),
                        Status = status,
                        IsSend = 0,
                        Signal = signal.Name,
                        CreatingTime = DateTime.Now,
                        Remark = signalValue,
                        PlcType = signal.PlcDeviceId,
                        PLCTypeDb = signal.PLCTypeDb
                    });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "手动触发PLC信号失败，信号ID: {SignalId}, 值: {Value}", signalId, value);
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
