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
            try
            {
                var json = System.IO.File.ReadAllText("appsettings.json");
                var jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);

                jsonObj.ConnectionStrings.IPAddress = settings.IPAddress;
                jsonObj.ConnectionStrings.Port = settings.Port;

                System.IO.File.WriteAllText("appsettings.json", Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented));

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存连接设置失败");
                TempData["Message"] = "保存设置失败，请稍后再试";
                TempData["MessageType"] = "danger";
                return RedirectToAction("Index");
            }
        }
    }
}
