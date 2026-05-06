using Microsoft.AspNetCore.Mvc;

using WarehouseManagementSystem.Models;

namespace WarehouseManagementSystem.Controllers
{
    public class SettingController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SettingController> _logger;
      
        public SettingController(IConfiguration configuration, ILogger<SettingController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }


        public IActionResult Index()
        {
            _logger.LogInformation("用户访问设置页面");
            try
            {
                var settings = _configuration.GetSection("ConnectionStrings").Get<ConnectionSettings>();
                return View(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取配置信息失败");
                return View(new ConnectionSettings());
            }
        }

        public IActionResult GetConnectionSettings()
        {
            try
            {
                var settings = _configuration.GetSection("ConnectionStrings").Get<ConnectionSettings>();
                return Json(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取连接设置失败");
                return Json(new { success = false, message = "获取连接设置失败" });
            }
        }


        [HttpPost]
        public IActionResult SaveSettings(ConnectionSettings settings)
        {
            _logger.LogInformation("开始保存连接设置。目标 IP: {IPAddress}, 端口: {Port}", settings.IPAddress, settings.Port);
            try
            {
                var json = System.IO.File.ReadAllText("appsettings.json");
                var jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);

                jsonObj.ConnectionStrings.IPAddress = settings.IPAddress;
                jsonObj.ConnectionStrings.Port = settings.Port;

                System.IO.File.WriteAllText("appsettings.json", Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented));

                _logger.LogInformation("连接设置已成功保存至 appsettings.json");
                TempData["Message"] = "设置保存成功";
                TempData["MessageType"] = "success";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存连接设置失败。尝试保存的值: IP={IPAddress}, Port={Port}", settings.IPAddress, settings.Port);
                TempData["Message"] = "保存设置失败，请检查文件写入权限或稍后再试";
                TempData["MessageType"] = "danger";
                return RedirectToAction("Index");
            }
        }
    }
}
