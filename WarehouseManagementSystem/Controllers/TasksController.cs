using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using OfficeOpenXml;

using WarehouseManagementSystem.Models;
using Microsoft.Extensions.Logging;
using Dapper;
using WarehouseManagementSystem.Data;

namespace WarehouseManagementSystem.Controllers
{
    // 任务管理控制器
    public class TasksController : Controller
    {
        private readonly ApplicationDbContext _context;

      

        private readonly HttpClient _httpClient;

        private readonly IConfiguration _configuration;

        private readonly ITaskService _taskService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(ApplicationDbContext context,IConfiguration configuration, ITaskService taskService, ILogger<TasksController> logger)
        {
            _context = context;
          
            _httpClient = new HttpClient();
            _configuration = configuration;
            _taskService = taskService;
            _logger = logger;
            
            // 设置EPPlus许可证上下文 - Set EPPlus license context
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
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

        // 任务列表页面，支持分页和筛选
        public async Task<IActionResult> Index(int page = 1, string dropLocation = "",
            DateTime? filterDate = null, DateTime? endDate = null, string palletId = "", int pageSize = 10)
        {
            try
            {
                var (items, totalItems) = await _taskService.GetUserTasks(page, pageSize, filterDate, endDate);

                // 保存筛选条件到 ViewData - Save filter conditions to ViewData
                ViewData["dropLocation"] = dropLocation;
                ViewData["filterDate"] = filterDate?.ToString("yyyy-MM-dd");
                ViewData["endDate"] = endDate?.ToString("yyyy-MM-dd");
                ViewData["palletId"] = palletId;

                return View(new PagedResult<RCS_UserTasks>
                {
                    Items = items,
                    TotalItems = totalItems,
                    PageNumber = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get task list - 获取任务列表失败");
                return View(new PagedResult<RCS_UserTasks>
                {
                    Items = new List<RCS_UserTasks>(),
                    TotalItems = 0,
                    PageNumber = page,
                    PageSize = pageSize,
                    TotalPages = 0
                });
            }
        }

        /// <summary>
        /// 获取最近任务列表，用于浮动任务面板 - Get recent task list for floating task panel
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRecentTasks(string status = "all", int count = 10)
        {
            try
            {
                // 使用任务服务获取数据，而不是直接访问上下文 - Use task service to get data instead of direct context access
                DateTime? filterDate = null;
                DateTime? endDate = null;
                
                // 获取最近的任务，不使用分页 - Get recent tasks without pagination
                var (tasks, _) = await _taskService.GetUserTasks(1, count, filterDate, endDate);
                
                // 根据状态筛选 - Filter by status
                var filteredTasks = tasks.AsQueryable();
                switch (status)
                {
                    case "running":
                        // 执行中的任务：状态小于TaskFinish(11)且未被取消 - Running tasks: status less than TaskFinish(11) and not cancelled
                        filteredTasks = filteredTasks.Where(t => t.taskStatus < TaskStatuEnum.TaskFinish && !t.IsCancelled);
                        break;
                    case "finished":
                        // 已完成的任务：状态为TaskFinish(11) - Completed tasks: status is TaskFinish(11)
                        filteredTasks = filteredTasks.Where(t => t.taskStatus == TaskStatuEnum.TaskFinish);
                        break;
                    case "canceled":
                        // 已取消的任务：已标记为取消或状态为Canceled(30) - Cancelled tasks: marked as cancelled or status is Canceled(30)
                        filteredTasks = filteredTasks.Where(t => t.IsCancelled || t.taskStatus == TaskStatuEnum.Canceled);
                        break;
                }
                
                // 获取最近的任务
                var result = filteredTasks
                    .OrderByDescending(t => t.creatTime)
                    .Take(count)
                    .ToList();
                
                return Json(new { success = true, tasks = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get recent task list - 获取最近任务列表失败");
                return Json(new { success = false, message = "Failed to get task data" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelTask(int id)
        {
            var (success, message) = await _taskService.CancelTask(id);
            return Json(new { success, message });
        }

        /// <summary>
        /// Export task data to Excel
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExportTasks(DateTime? filterDate = null, DateTime? endDate = null)
        {
            try
            {
                // Get all data that meets the conditions (no pagination) - use reasonable page size
                var tasks = await GetAllTasksForExport(filterDate, endDate);
                
                // Create Excel file
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Task Data");
                
                // Set table headers
                var headers = new[]
                {
                    "Request Code", "Task Type", "Source Position", "Target Position", "Task Status", "AGV Code", 
                    "Run Task ID", "Created Time", "Completed Time"
                };
                
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cells[1, i + 1].Value = headers[i];
                    worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                    worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                
                // Fill data
                for (int i = 0; i < tasks.Count; i++)
                {
                    var task = tasks[i];
                    var row = i + 2;
                    
                    worksheet.Cells[row, 1].Value = task.requestCode;
                    worksheet.Cells[row, 2].Value = task.taskType;
                    worksheet.Cells[row, 3].Value = task.sourcePosition;
                    worksheet.Cells[row, 4].Value = task.targetPosition;
                    worksheet.Cells[row, 5].Value = GetStatusText(task.taskStatus);
                    worksheet.Cells[row, 6].Value = task.robotCode;
                    worksheet.Cells[row, 7].Value = task.runTaskId;
                    worksheet.Cells[row, 8].Value = task.creatTime?.ToString("yyyy-MM-dd HH:mm:ss");
                    worksheet.Cells[row, 9].Value = task.endTime?.ToString("yyyy-MM-dd HH:mm:ss");
                }
                
                // Auto adjust column width
                worksheet.Cells.AutoFitColumns();
                
                // Generate file name
                string fileName;
                if (filterDate.HasValue || endDate.HasValue)
                {
                    var startStr = filterDate?.ToString("yyyyMMdd") ?? "All";
                    var endStr = endDate?.ToString("yyyyMMdd") ?? "All";
                    fileName = $"TaskData_{startStr}_to_{endStr}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                }
                else
                {
                    fileName = $"TaskData_All_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                }
                
                // Return file
                var fileBytes = package.GetAsByteArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export task data - 导出任务数据失败");
                return Json(new { success = false, message = "Export failed, please try again" });
            }
        }

        /// <summary>
        /// Get task statistics data
        /// </summary>
        /// <param name="filterDate">Start date filter (optional)</param>
        /// <param name="endDate">End date filter (optional)</param>
        /// <returns>JSON response containing various statistical data</returns>
        [HttpGet]
        public async Task<IActionResult> GetTaskStatistics(DateTime? filterDate = null, DateTime? endDate = null)
        {
            try
            {
                // Get all data that meets the conditions - use reasonable page size
                // Note: This uses TaskService.GetUserTasks, which only returns non-cancelled tasks (taskStatus < Canceled)
                // If you need to include cancelled tasks, modify TaskService or use GetAllTasksForExport method
                var (tasks, _) = await _taskService.GetUserTasks(1, 10000, filterDate, endDate);
                
                var statistics = new
                {
                    // ========== Basic Statistical Indicators ==========
                    // Total tasks: All task count under current filter conditions
                    TotalTasks = tasks.Count,
                    
                    // Completed tasks: Number of tasks with TaskFinish status
                    CompletedTasks = tasks.Count(t => t.taskStatus == TaskStatuEnum.TaskFinish),
                    
                    // Running tasks: Number of tasks with status less than TaskFinish (including various running states)
                    // Note: Cancelled tasks are not excluded here because TaskService has already filtered them
                    RunningTasks = tasks.Count(t => t.taskStatus < TaskStatuEnum.TaskFinish),
                    
                    // Cancelled tasks: Number of tasks marked as cancelled or with Canceled status
                    // Note: This value may be 0 because TaskService has filtered cancelled tasks
                    CanceledTasks = tasks.Count(t => t.IsCancelled || t.taskStatus == TaskStatuEnum.Canceled),
                    
                    // ========== Calculated Indicators ==========
                    // Completion rate: Completed tasks / Total tasks * 100
                    // Use Math.Round to keep 2 decimal places, avoid division by zero error
                    CompletionRate = tasks.Count > 0 ? Math.Round((double)tasks.Count(t => t.taskStatus == TaskStatuEnum.TaskFinish) / tasks.Count * 100, 2) : 0,
                    
                    // ========== Status Distribution Statistics ==========
                    // Group by task status, count tasks for each status
                    // Use GetStatusText method to convert enum to text description
                    // Sort by count in descending order for chart display
                    StatusDistribution = tasks.GroupBy(t => t.taskStatus)
                        .Select(g => new { 
                            Status = GetStatusText(g.Key),  // Status text description
                            Count = g.Count()               // Task count for this status
                        })
                        .OrderByDescending(x => x.Count)    // Sort by count in descending order
                        .ToList(),
                    
                    // ========== AGV Efficiency Statistics ==========
                    // Group by AGV code, count task status for each AGV
                    // Only count tasks with AGV code (robotCode not empty)
                    // Take top 10 AGVs with most tasks to avoid chart overcrowding
                    AgvStatistics = tasks.Where(t => !string.IsNullOrEmpty(t.robotCode))  // Filter empty AGV codes
                        .GroupBy(t => t.robotCode)                                          // Group by AGV code
                        .Select(g => new { 
                            AgvCode = g.Key,                                               // AGV code
                            TaskCount = g.Count(),                                         // Total task count
                            CompletedCount = g.Count(t => t.taskStatus == TaskStatuEnum.TaskFinish)  // Completed task count
                        })
                        .OrderByDescending(x => x.TaskCount)                              // Sort by total task count in descending order
                        .Take(10)                                                          // Take only top 10
                        .ToList(),
                    
                    // ========== Time Statistics ==========
                    // Average execution time: Average execution time of completed tasks (unit: minutes)
                    // Only count completed tasks with complete time information (both creatTime and endTime are not null)
                    // Use DefaultIfEmpty(0) to avoid exceptions caused by empty collections
                    AverageExecutionTime = tasks.Where(t => t.taskStatus == TaskStatuEnum.TaskFinish && 
                                                           t.creatTime.HasValue && 
                                                           t.endTime.HasValue)
                        .Select(t => (t.endTime.Value - t.creatTime.Value).TotalMinutes)  // Calculate execution time (minutes)
                        .DefaultIfEmpty(0)                                                 // Default to 0 if no data
                        .Average()                                                         // Calculate average
                };
                
                return Json(new { success = true, data = statistics });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get task statistics data");
                return Json(new { success = false, message = "Failed to get statistics data: " + ex.Message });
            }
        }

        /// <summary>
        /// Get all tasks for export (including cancelled tasks)
        /// </summary>
        /// <param name="filterDate">Start date filter (optional)</param>
        /// <param name="endDate">End date filter (optional)</param>
        /// <returns>List of tasks that meet the conditions</returns>
        /// <remarks>
        /// This method queries the database directly, not through TaskService, so it can get all tasks (including cancelled ones)
        /// Unlike GetTaskStatistics method, this does not filter cancelled tasks
        /// Use Dapper for database queries to improve performance
        /// </remarks>
        private async Task<List<RCS_UserTasks>> GetAllTasksForExport(DateTime? filterDate = null, DateTime? endDate = null)
        {
            try
            {
                // Get database connection
                using var conn = _context.GetConnection();
                await conn.OpenAsync();

                // Basic query: get all tasks
                var query = "SELECT * FROM RCS_UserTasks WHERE 1=1";
                var parameters = new DynamicParameters();

                // Add start date filter condition
                if (filterDate.HasValue)
                {
                    query += " AND creatTime >= @FilterDate";
                    parameters.Add("@FilterDate", filterDate.Value);
                }

                // Add end date filter condition
                // Note: AddDays(1) is used here to include the entire day data of the end date
                if (endDate.HasValue)
                {
                    query += " AND creatTime <= @EndDate";
                    parameters.Add("@EndDate", endDate.Value.AddDays(1));
                }

                // Sort by creation time in descending order, newest tasks first
                query += " ORDER BY creatTime DESC";

                // Execute query and return results
                var tasks = await conn.QueryAsync<RCS_UserTasks>(query, parameters);
                return tasks.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get export task data - 获取导出任务数据失败");
                throw;
            }
        }

        /// <summary>
        /// Test data retrieval
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TestData()
        {
            try
            {
                var (tasks, totalItems) = await _taskService.GetUserTasks(1, 10, null, null);
                return Json(new { 
                    success = true, 
                    taskCount = tasks.Count, 
                    totalItems = totalItems,
                    sampleTask = tasks.FirstOrDefault() 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试数据获取失败");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetStatusText(TaskStatuEnum status)
        {
            return status switch
            {
                TaskStatuEnum.PendingCondition => "等待条件满足",
                TaskStatuEnum.None => "未执行",
                TaskStatuEnum.CarWash => "洗车中",
                TaskStatuEnum.TaskStart => "任务开始",
                TaskStatuEnum.Confirm => "参数确认中",
                TaskStatuEnum.ConfirmCar => "确认执行AGV",
                TaskStatuEnum.PickingUp => "取货中",
                TaskStatuEnum.PickDown => "取货完成",
                TaskStatuEnum.Unloading => "卸货中",
                TaskStatuEnum.UnloadDown => "卸货完成",
                TaskStatuEnum.TaskFinish => "任务完成",
                TaskStatuEnum.Canceled => "已取消",
                TaskStatuEnum.CanceledWashing => "取消后洗车中",
                TaskStatuEnum.CanceledWashFinish => "取消洗车完成",
                TaskStatuEnum.RedirectRequest => "取货路线异常",
                TaskStatuEnum.InvalidUp => "无效取货点",
                TaskStatuEnum.InvalidDown => "无效卸货点",
                TaskStatuEnum.OrderAgv => "卸货路线异常",
                TaskStatuEnum.OrderAgvFinish => "异常任务完成",
                _ => status.ToString()
            };
        }

        // GET: TasksController/Details/5
        public ActionResult Details(int id)
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务详情失败: {Id}", id);
                return View("Error");
            }
        }

        // GET: TasksController/Create
        public ActionResult Create()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建任务页面加载失败");
                return View("Error");
            }
        }

        // POST: TasksController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: TasksController/Edit/5
        public ActionResult Edit(int id)
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "编辑任务页面加载失败: {Id}", id);
                return View("Error");
            }
        }

        // POST: TasksController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: TasksController/Delete/5
        public ActionResult Delete(int id)
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除任务页面加载失败: {Id}", id);
                return View("Error");
            }
        }

        // POST: TasksController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
