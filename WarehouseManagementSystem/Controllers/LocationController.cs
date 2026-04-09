using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static System.Net.WebRequestMethods;

// 储位管理控制器
public class LocationController : Controller
{
    private readonly HttpClient _httpClient;

    private readonly ILocationService _locationService;

    private readonly IConfiguration _configuration;

    private readonly ILogger<LocationController> _logger;

    public LocationController(ILogger<LocationController> logger, ILocationService locationService, IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _configuration = configuration;
        _locationService = locationService;
        _logger = logger;
    }

    // 获取连接参数
    private (string baseUrl, string port, string http) GetConnectionParameters()
    {
        try
        {
            var baseUrl = _configuration["ConnectionStrings:IPAddress"];
            var port = _configuration["ConnectionStrings:Port"];
            var http = _configuration["ConnectionStrings:Http"];
            return (baseUrl, port, http);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get connection parameters - 获取连接参数失败");
            return (string.Empty, string.Empty, string.Empty);
        }
    }




    // 储位列表页面，支持搜索和分页
    public async Task<IActionResult> Index(string searchString, int page = 1)
    {
        try
        {
            int pageSize = 20; // 或者其他适合的页面大小 - Or other suitable page size
            
            // 保存搜索条件到ViewData - Save search criteria to ViewData
            ViewData["searchString"] = searchString;

            var (items, totalItems) = await _locationService.GetSearchLocations(searchString, page, pageSize);

            var model = new PagedResult<RCS_Locations>
            {
                Items = items.ToList(),
                TotalItems = totalItems,
                PageNumber = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
            };

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get location list - 获取库位列表失败");
            return View(new PagedResult<RCS_Locations>());
        }
    }

    // 创建或编辑储位页面
    public async Task<IActionResult> CreateEdit(int? id)
    {
        try
        {
            if (id == null)
            {
                return View(new RCS_Locations());
            }

            var location = await _locationService.GetLocationById(id.Value);
            if (location == null)
            {
                return NotFound();
            }

            return View(location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get location info - 获取库位信息失败");
            TempData["Message"] = "Failed to get location info! Please try again later.";
            TempData["MessageType"] = "danger";
            return View(new RCS_Locations());
        }
    }


    // 保存储位信息（创建或更新）
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEdit(RCS_Locations location)
    {
        try
        {
            var (success, message) = await _locationService.CreateOrUpdateLocation(location);

            TempData["Message"] = message;
            TempData["MessageType"] = success ? "success" : "danger";
            if (success)
            {
                TempData["RedirectAfterDelay"] = true;
            }

            return View(location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save location info - 保存库位信息失败");
            TempData["Message"] = "Save failed, please try again later.";
            TempData["MessageType"] = "danger";
            return View(location);
        }
    }


    // 删除确认操作
    [HttpPost]
    public async Task<IActionResult> DeleteConfirmed(int id, int type)
    {
        try
        {
            var (success, message) = await _locationService.HandleLocationOperation(id, type);
            return Json(new { success, message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed - 操作失败");
            return Json(new { success = false, message = "Operation failed, please try again later." });
        }
    }

    // 处理可选字段
    private void HandleOptionalFields(RCS_Locations location)
    {
        // 手动移除可选字段的错误 - Manually remove errors for optional fields
        if (string.IsNullOrEmpty(location.MaterialCode))
        {
            ModelState.Remove(nameof(location.MaterialCode));
        }
        if (string.IsNullOrEmpty(location.PalletID))
        {
            ModelState.Remove(nameof(location.PalletID));
        }
        if (string.IsNullOrEmpty(location.Weight))
        {
            ModelState.Remove(nameof(location.Weight));
        }
        if (string.IsNullOrEmpty(location.Quanitity))
        {
            ModelState.Remove(nameof(location.Quanitity));
        }
        if (string.IsNullOrEmpty(location.EntryDate))
        {
            ModelState.Remove(nameof(location.EntryDate));
        }
       

    }

    /// <summary>
    /// 储位统计页面
    /// </summary>
    public async Task<IActionResult> Statistics()
    {
        try
        {
            // 获取储位容量统计信息
            var (available, used) = await _locationService.GetStorageCapacityStats();
            
            // 获取分组数据
            var locations = await _locationService.GetLocations("", 1, int.MaxValue);
            var groupStats = locations.Items
                .GroupBy(l => l.Group)
                .Select(g => new GroupStatistics
                {
                    GroupName = g.Key,
                    TotalCount = g.Count(),
                    UsedCount = g.Count(l => !string.IsNullOrEmpty(l.MaterialCode) && l.MaterialCode != "empty"),
                    LockedCount = g.Count(l => l.Lock)
                })
                .OrderBy(g => g.GroupName)
                .ToList();
            
            var viewModel = new LocationStatisticsViewModel
            {
                TotalLocations = available + used,
                UsedLocations = used,
                AvailableLocations = available,
                UsagePercentage = available + used > 0 ? (int)(used * 100 / (available + used)) : 0,
                GroupStatistics = groupStats
            };
            
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取储位统计信息失败");
            return View(new LocationStatisticsViewModel());
        }
    }
    
    /// <summary>
    /// 历史记录页面
    /// </summary>
    public async Task<IActionResult> History(string searchString, string group, DateTime? startDate, DateTime? endDate, int page = 1)
    {
        try
        {
            int pageSize = 20;
            
            // 这里需要实现获取操作历史记录的逻辑
            // 假设有一个获取历史记录的服务方法
            // var (items, totalItems) = await _locationService.GetLocationHistoryAsync(searchString, group, startDate, endDate, page, pageSize);
            
            // 由于没有具体的历史记录服务，我们这里可以展示一个空列表
            var items = new List<LocationHistory>();
            var totalItems = 0;
            
            ViewData["SearchString"] = searchString;
            ViewData["Group"] = group;
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
            
            var model = new PagedResult<LocationHistory>
            {
                Items = items,
                TotalItems = totalItems,
                PageNumber = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
            };
            
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取储位历史记录失败");
            return View(new PagedResult<LocationHistory>());
        }
    }
}

public class GroupStatistics
{
    public string GroupName { get; set; }
    public int TotalCount { get; set; }
    public int UsedCount { get; set; }
    public int LockedCount { get; set; }
    public int AvailableCount => TotalCount - UsedCount;
    public double UsagePercentage => TotalCount > 0 ? Math.Round((double)UsedCount / TotalCount * 100, 2) : 0;
}

public class LocationStatisticsViewModel
{
    public int TotalLocations { get; set; }
    public int UsedLocations { get; set; }
    public int AvailableLocations { get; set; }
    public int UsagePercentage { get; set; }
    public List<GroupStatistics> GroupStatistics { get; set; } = new List<GroupStatistics>();
}

public class LocationHistory
{
    public int Id { get; set; }
    public string LocationCode { get; set; }
    public string LocationName { get; set; }
    public string Group { get; set; }
    public string OperationType { get; set; }
    public string MaterialCode { get; set; }
    public string MaterialName { get; set; }
    public string OperatorName { get; set; }
    public DateTime OperationTime { get; set; }
    public string Remarks { get; set; }
}
