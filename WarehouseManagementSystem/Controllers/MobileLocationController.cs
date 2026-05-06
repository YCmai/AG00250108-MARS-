using Microsoft.AspNetCore.Mvc;
using Dapper;
using WarehouseManagementSystem.Db;

namespace WarehouseManagementSystem.Controllers
{
    /// <summary>
    /// Mobile Location Status Management Controller
    /// </summary>
    public class MobileLocationController : Controller
    {
        private readonly ILocationService _locationService;
        private readonly ILogger<MobileLocationController> _logger;

        public MobileLocationController(ILocationService locationService, ILogger<MobileLocationController> logger)
        {
            _locationService = locationService;
            _logger = logger;
        }

        /// <summary>
        /// Mobile Location Status Management Page
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Get All Locations List (for Mobile Display)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLocations(string searchString = "", int page = 1, int pageSize = 50)
        {
            try
            {
                var (items, totalItems) = await _locationService.GetSearchLocations(searchString, page, pageSize);
                
                var locationList = items.Select(l => new
                {
                    l.Id,
                    l.Name,
                    l.NodeRemark,
                    l.Group,
                    l.MaterialCode,
                    l.PalletID,
                    l.Weight,
                    l.Quanitity,
                    l.EntryDate,
                    l.Lock,
                    IsEmpty = string.IsNullOrEmpty(l.MaterialCode) || l.MaterialCode == "empty"
                }).ToList();

                return Json(new
                {
                    success = true,
                    data = locationList,
                    total = totalItems,
                    page = page,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling((double)totalItems / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get location list");
                return Json(new
                {
                    success = false,
                    message = "Failed to get location list: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Clear Material Request Model
        /// </summary>
        public class ClearMaterialRequest
        {
            public int Id { get; set; }
        }

        /// <summary>
        /// Batch Clear Material Request Model
        /// </summary>
        public class BatchClearMaterialRequest
        {
            public List<int> Ids { get; set; } = new();
        }

        /// <summary>
        /// Batch Set Occupied Request Model
        /// </summary>
        public class BatchSetOccupiedRequest
        {
            public List<int> Ids { get; set; } = new();
        }

        /// <summary>
        /// Toggle Lock Request Model
        /// </summary>
        public class ToggleLockRequest
        {
            public int Id { get; set; }
            public bool LockState { get; set; } // true=lock, false=unlock
        }

        /// <summary>
        /// Batch Toggle Lock Request Model
        /// </summary>
        public class BatchToggleLockRequest
        {
            public List<int> Ids { get; set; } = new();
            public bool LockState { get; set; } // true=lock, false=unlock
        }

        /// <summary>
        /// Clear Location Material
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ClearMaterial([FromBody] ClearMaterialRequest request)
        {
            _logger.LogInformation("移动端请求清除库位物料。库位ID: {Id}", request?.Id);
            try
            {
                if (request == null || request.Id <= 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid location ID"
                    });
                }

                // type = 3 means clear material
                var (success, message) = await _locationService.HandleLocationOperation(request.Id, 3);
                
                if (success)
                    _logger.LogInformation("库位 {Id} 物料清除成功", request.Id);
                else
                    _logger.LogWarning("库位 {Id} 物料清除失败: {Message}", request.Id, message);

                return Json(new
                {
                    success = success,
                    message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除库位物料失败。ID: {Id}", request?.Id);
                return Json(new
                {
                    success = false,
                    message = "Failed to clear material: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Batch Clear Location Materials
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BatchClearMaterial([FromBody] BatchClearMaterialRequest request)
        {
            _logger.LogInformation("移动端请求批量清除库位物料。数量: {Count}, IDs: {Ids}", request?.Ids?.Count, request?.Ids);
            try
            {
                if (request == null || request.Ids == null || request.Ids.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid location IDs"
                    });
                }

                int successCount = 0;
                int failCount = 0;
                List<string> errors = new List<string>();

                foreach (var id in request.Ids)
                {
                    try
                    {
                        // type = 3 means clear material
                        var (success, message) = await _locationService.HandleLocationOperation(id, 3);
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                            errors.Add($"ID {id}: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        errors.Add($"ID {id}: {ex.Message}");
                    }
                }

                string resultMessage = $"Batch clear completed: {successCount} succeeded, {failCount} failed";
                _logger.LogInformation("批量清除库位完成。成功: {Success}, 失败: {Fail}", successCount, failCount);
                if (errors.Count > 0 && errors.Count <= 5)
                {
                    resultMessage += ". Errors: " + string.Join("; ", errors);
                }

                return Json(new
                {
                    success = successCount > 0,
                    message = resultMessage,
                    successCount = successCount,
                    failCount = failCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量清除物料失败。IDs: {Ids}", request?.Ids);
                return Json(new
                {
                    success = false,
                    message = "Batch clear failed: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Batch Set Locations Occupied
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BatchSetOccupied([FromBody] BatchSetOccupiedRequest request)
        {
            _logger.LogInformation("移动端批量设置库位占用。IDs: {Ids}", request?.Ids);
            try
            {
                if (request == null || request.Ids == null || request.Ids.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid location IDs"
                    });
                }

                var (success, message, affectedCount) = await _locationService.BatchSetOccupiedByIds(request.Ids);
                
                if (success)
                    _logger.LogInformation("批量设置占用成功。受影响数量: {Count}", affectedCount);

                return Json(new
                {
                    success = success,
                    message = message,
                    affectedCount = affectedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量设置占用失败。IDs: {Ids}", request?.Ids);
                return Json(new
                {
                    success = false,
                    message = "Batch set occupied failed: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Lock/Unlock Location
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ToggleLock([FromBody] ToggleLockRequest request)
        {
            _logger.LogInformation("移动端请求锁定/解锁库位。ID: {Id}, 目标状态: {State}", request?.Id, request?.LockState);
            try
            {
                if (request == null || request.Id <= 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid location ID"
                    });
                }

                // Use batch lock/unlock method with explicit lock state
                var (success, message, affectedCount) = await _locationService.BatchToggleLockByIds(
                    new List<int> { request.Id }, 
                    request.LockState
                );
                
                if (success)
                    _logger.LogInformation("库位 {Id} 状态已更新为 {State}", request.Id, request.LockState ? "锁定" : "解锁");

                return Json(new
                {
                    success = success,
                    message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新库位锁定状态失败。ID: {Id}, 目标状态: {State}", request?.Id, request?.LockState);
                return Json(new
                {
                    success = false,
                    message = "Operation failed: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Batch Lock/Unlock Locations
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BatchToggleLock([FromBody] BatchToggleLockRequest request)
        {
            _logger.LogInformation("移动端批量锁定/解锁库位。数量: {Count}, 目标状态: {State}", request?.Ids?.Count, request?.LockState);
            try
            {
                if (request == null || request.Ids == null || request.Ids.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid location IDs"
                    });
                }

                // Use batch lock/unlock method
                var (success, message, affectedCount) = await _locationService.BatchToggleLockByIds(
                    request.Ids, 
                    request.LockState
                );
                
                if (success)
                    _logger.LogInformation("批量状态更新成功。受影响数量: {Count}", affectedCount);

                return Json(new
                {
                    success = success,
                    message = message,
                    affectedCount = affectedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新锁定状态失败。数量: {Count}, 目标状态: {State}", request?.Ids?.Count, request?.LockState);
                return Json(new
                {
                    success = false,
                    message = "Batch operation failed: " + ex.Message
                });
            }
        }
    }
}
