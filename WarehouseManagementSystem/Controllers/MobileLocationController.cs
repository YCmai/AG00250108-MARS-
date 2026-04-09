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
            public List<int> Ids { get; set; }
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
            public List<int> Ids { get; set; }
            public bool LockState { get; set; } // true=lock, false=unlock
        }

        /// <summary>
        /// Clear Location Material
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ClearMaterial([FromBody] ClearMaterialRequest request)
        {
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
                
                return Json(new
                {
                    success = success,
                    message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear location material");
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
                _logger.LogError(ex, "Batch clear material failed");
                return Json(new
                {
                    success = false,
                    message = "Batch clear failed: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Lock/Unlock Location
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ToggleLock([FromBody] ToggleLockRequest request)
        {
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
                
                return Json(new
                {
                    success = success,
                    message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lock/unlock location");
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
                
                return Json(new
                {
                    success = success,
                    message = message,
                    affectedCount = affectedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch lock/unlock failed");
                return Json(new
                {
                    success = false,
                    message = "Batch operation failed: " + ex.Message
                });
            }
        }
    }
}

