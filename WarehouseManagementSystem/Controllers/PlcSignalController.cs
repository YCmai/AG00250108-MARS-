using Microsoft.AspNetCore.Mvc;
using WarehouseManagementSystem.Models.PLC;
using WarehouseManagementSystem.Service.Plc;


    public class PlcSignalController : Controller
    {
        private readonly IPlcSignalService _plcSignalService;
        private readonly ILogger<PlcSignalController> _logger;

        public PlcSignalController(
            IPlcSignalService plcSignalService,
            ILogger<PlcSignalController> logger)
        {
            _plcSignalService = plcSignalService;
            _logger = logger;
        }

        // 页面入口 - 显示PLC设备列表
        public async Task<IActionResult> Index()
        {
            try
            {
                ViewBag.CurrentUser = "YCmai"; // 当前用户
                ViewBag.CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var devices = await _plcSignalService.GetAllPlcDevicesAsync();
                return View(devices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC设备列表失败");
                return View("Error");
            }
        }

        // 获取设备详情
        public async Task<IActionResult> DeviceDetails(int id)
        {
            try
            {
                var device = await _plcSignalService.GetPlcDeviceByIdAsync(id);
                if (device == null)
                {
                    return NotFound();
                }
                return View(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备详情失败，Id: {Id}", id);
                return View("Error");
            }
        }

        // 获取设备下的所有信号
        [HttpGet]
        public async Task<IActionResult> GetSignalsByDevice(string deviceId)
        {
            try
            {
                var signals = await _plcSignalService.GetPlcSignalsByDeviceIdAsync(deviceId);
                return Json(signals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备下的信号失败，DeviceId: {DeviceId}", deviceId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 根据ID获取信号详情
        [HttpGet]
        public async Task<IActionResult> GetSignalById(int id)
        {
            try
            {
                var signal = await _plcSignalService.GetPlcSignalByIdAsync(id);
                if (signal == null)
                {
                    return NotFound();
                }
                return Json(signal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取信号详情失败，Id: {Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 添加PLC设备
        [HttpPost]
        public async Task<IActionResult> AddDevice([FromBody] RCS_PlcDevice device)
        {
            try
            {
                // 基本验证
                if (device == null)
                {
                    return Json(new { success = false, message = "设备数据不能为空" });
                }

                if (string.IsNullOrWhiteSpace(device.IpAddress))
                {
                    return Json(new { success = false, message = "设备IP地址不能为空" });
                }

                if (string.IsNullOrWhiteSpace(device.Brand))
                {
                    return Json(new { success = false, message = "设备品牌不能为空" });
                }

                // 检查IP地址格式
                if (!IsValidIPAddress(device.IpAddress))
                {
                    return Json(new { success = false, message = "无效的IP地址格式" });
                }

                var id = await _plcSignalService.AddPlcDeviceAsync(device);
                _logger.LogInformation("成功添加PLC设备: {@Device}", device);

                return Json(new { success = true, id = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加PLC设备失败: {@Device}", device);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 添加PLC信号
        [HttpPost]
        public async Task<IActionResult> AddSignal([FromBody] RCS_PlcSignal signal)
        {
            try
            {
                // 基本验证
                if (signal == null)
                {
                    return Json(new { success = false, message = "信号数据不能为空" });
                }

                if (string.IsNullOrWhiteSpace(signal.Name))
                {
                    return Json(new { success = false, message = "信号名称不能为空" });
                }

                if (string.IsNullOrWhiteSpace(signal.Offset))
                {
                    return Json(new { success = false, message = "偏移量不能为空" });
                }

                if (string.IsNullOrWhiteSpace(signal.DataType))
                {
                    return Json(new { success = false, message = "数据类型不能为空" });
                }

                var id = await _plcSignalService.AddPlcSignalAsync(signal);
                _logger.LogInformation("成功添加PLC信号: {@Signal}", signal);

                return Json(new { success = true, id = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加PLC信号失败: {@Signal}", signal);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 更新PLC设备
        [HttpPost]
        public async Task<IActionResult> UpdateDevice([FromBody] RCS_PlcDevice device)
        {
            try
            {
                // 基本验证
                if (device == null)
                {
                    return Json(new { success = false, message = "设备数据不能为空" });
                }

                if (device.Id <= 0)
                {
                    return Json(new { success = false, message = "无效的设备ID" });
                }

                if (string.IsNullOrWhiteSpace(device.IpAddress))
                {
                    return Json(new { success = false, message = "设备IP地址不能为空" });
                }

                if (string.IsNullOrWhiteSpace(device.Brand))
                {
                    return Json(new { success = false, message = "设备品牌不能为空" });
                }

                // 检查IP地址格式
                if (!IsValidIPAddress(device.IpAddress))
                {
                    return Json(new { success = false, message = "无效的IP地址格式" });
                }

                // 确认设备存在
                var existingDevice = await _plcSignalService.GetPlcDeviceByIdAsync(device.Id);
                if (existingDevice == null)
                {
                    return Json(new { success = false, message = $"ID为{device.Id}的设备不存在" });
                }

                await _plcSignalService.UpdatePlcDeviceAsync(device);
                _logger.LogInformation("更新PLC设备成功: {@Device}", device);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PLC设备失败: {@Device}", device);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 更新PLC信号
        [HttpPost]
        public async Task<IActionResult> UpdateSignal([FromBody] RCS_PlcSignal signal)
        {
            try
            {
                // 基本验证
                if (signal == null)
                {
                    return Json(new { success = false, message = "信号数据不能为空" });
                }

                if (signal.Id <= 0)
                {
                    return Json(new { success = false, message = "无效的信号ID" });
                }

                if (string.IsNullOrWhiteSpace(signal.Name))
                {
                    return Json(new { success = false, message = "信号名称不能为空" });
                }

                if (string.IsNullOrWhiteSpace(signal.Offset))
                {
                    return Json(new { success = false, message = "偏移量不能为空" });
                }

                if (string.IsNullOrWhiteSpace(signal.DataType))
                {
                    return Json(new { success = false, message = "数据类型不能为空" });
                }

                // 确认信号存在
                var existingSignal = await _plcSignalService.GetPlcSignalByIdAsync(signal.Id);
                if (existingSignal == null)
                {
                    return Json(new { success = false, message = $"ID为{signal.Id}的信号不存在" });
                }

                await _plcSignalService.UpdatePlcSignalAsync(signal);
                _logger.LogInformation("更新PLC信号成功: {@Signal}", signal);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新PLC信号失败: {@Signal}", signal);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 删除PLC设备
        [HttpPost]
        public async Task<IActionResult> DeleteDevice([FromBody] int id)
        {
            try
            {
                await _plcSignalService.DeletePlcDeviceAsync(id);
                _logger.LogInformation("删除PLC设备成功: Id={Id}", id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除PLC设备失败: Id={Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 删除PLC信号
        [HttpPost]
        public async Task<IActionResult> DeleteSignal([FromBody] int id)
        {
            try
            {
                await _plcSignalService.DeletePlcSignalAsync(id);
                _logger.LogInformation("删除PLC信号成功: Id={Id}", id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除PLC信号失败: Id={Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 辅助方法：验证IP地址格式
        private bool IsValidIPAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // 尝试解析IP地址
            return System.Net.IPAddress.TryParse(ipAddress, out _);
        }
    }
