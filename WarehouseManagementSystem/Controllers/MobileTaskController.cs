using Microsoft.AspNetCore.Mvc;
using Dapper;
using WarehouseManagementSystem.Db;
using System.ComponentModel.DataAnnotations;

namespace WarehouseManagementSystem.Controllers
{
    public class MobileTaskController : Controller
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<MobileTaskController> _logger;

        public MobileTaskController(IDatabaseService db, ILogger<MobileTaskController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Task Assignment Page
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Task List Page
        /// </summary>
        public IActionResult TaskList()
        {
            return View();
        }

        /// <summary>
        /// Check if duplicate unfinished tasks exist
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckDuplicateTask(string sourcePosition, string targetPosition)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePosition) || string.IsNullOrEmpty(targetPosition))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Source and target positions cannot be empty"
                    });
                }

                using var connection = _db.CreateConnection();
                
                var sql = @"
                    SELECT COUNT(*) 
                    FROM RCS_UserTasks 
                    WHERE sourcePosition = @SourcePosition 
                    AND targetPosition = @TargetPosition 
                    AND taskStatus NOT IN (@TaskFinish, @Canceled)";

                var count = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    SourcePosition = sourcePosition,
                    TargetPosition = targetPosition,
                    TaskFinish = TaskStatuEnum.TaskFinish,
                    Canceled = TaskStatuEnum.Canceled,
                
                });

                return Json(new
                {
                    success = true,
                    data = new { isDuplicate = count > 0 }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check duplicate tasks");
                return Json(new
                {
                    success = false,
                    message = "Failed to check duplicate tasks: " + ex.Message
                });
            }
        }
        
        /// <summary>
        /// Get all available location information (only return enabled and unlocked locations)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLocations()
        {
            try
            {
                using var connection = _db.CreateConnection();
                // Only return enabled and unlocked locations (Enabled = 1 and Lock = 0)
                var sql = "SELECT Name, NodeRemark FROM RCS_Locations WHERE Enabled = 1 AND Lock = 0 ORDER BY NodeRemark";
                var locations = await connection.QueryAsync<LocationDto>(sql);

                return Json(new
                {
                    success = true,
                    data = locations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get location information");
                return Json(new
                {
                    success = false,
                    message = "Failed to get location information: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Get all location groups
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLocationGroups()
        {
            try
            {
                using var connection = _db.CreateConnection();
                var sql = "SELECT DISTINCT [Group] FROM RCS_Locations WHERE [Group] IS NOT NULL AND [Group] != '' ORDER BY [Group]";
                var groups = await connection.QueryAsync<string>(sql);

                return Json(new
                {
                    success = true,
                    data = groups
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get location groups");
                return Json(new
                {
                    success = false,
                    message = "Failed to get location groups: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Get available locations in a specific group (empty and unlocked)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAvailableLocationsByGroup(string groupName)
        {
            try
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Group name cannot be empty"
                    });
                }

                using var connection = _db.CreateConnection();
                // Find locations that are enabled, unlocked, and have no material (empty)
                var sql = @"SELECT Name, NodeRemark, [Group] 
                           FROM RCS_Locations 
                           WHERE [Group] = @GroupName 
                           AND Enabled = 1 
                           AND Lock = 0 
                           AND (MaterialCode IS NULL OR MaterialCode = '' OR MaterialCode = 'empty')
                           ORDER BY Name";
                
                var locations = await connection.QueryAsync<LocationDto>(sql, new { GroupName = groupName });

                return Json(new
                {
                    success = true,
                    data = locations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available locations by group");
                return Json(new
                {
                    success = false,
                    message = "Failed to get available locations by group: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Create new user task
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.SourcePosition) || string.IsNullOrEmpty(request.TargetPosition))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Source and target positions cannot be empty"
                    });
                }

                if (request.SourcePosition == request.TargetPosition)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Source and target positions cannot be the same"
                    });
                }

                using var connection = _db.CreateConnection();
                
                var sql = @"
                    INSERT INTO RCS_UserTasks (
                        taskStatus, 
                        executed, 
                        creatTime, 
                        requestCode, 
                        taskType, 
                        priority, 
                        sourcePosition, 
                        targetPosition, 
                        IsCancelled
                    )
                    VALUES (
                        @TaskStatus, 
                        @Executed, 
                        @CreatTime, 
                        @RequestCode, 
                        @TaskType, 
                        @Priority, 
                        @SourcePosition, 
                        @TargetPosition, 
                        @IsCancelled
                    );
                    SELECT CAST(SCOPE_IDENTITY() as int)";

                var taskId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    TaskStatus = TaskStatuEnum.None,
                    Executed = false,
                    CreatTime = DateTime.Now,
                    RequestCode = Guid.NewGuid().ToString("N")[..8],
                    TaskType = RCS_UserTasks.TaskType.PlatingToBuffer, // Default task type
                    Priority = 1,
                    SourcePosition = request.SourcePosition,
                    TargetPosition = request.TargetPosition,
                    IsCancelled = false
                });

                return Json(new
                {
                    success = true,
                    message = "Task created successfully",
                    data = new { TaskId = taskId }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create task");
                return Json(new
                {
                    success = false,
                    message = "Failed to create task: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Create multiple tasks (batch creation)
        /// </summary>
        /// <param name="request">Batch task creation request</param>
        /// <returns>Batch task creation result</returns>
        [HttpPost]
        public async Task<IActionResult> CreateBatchTasks([FromBody] CreateBatchTaskRequest request)
        {
            try
            {
                if (request == null || request.SourcePositions == null || request.SourcePositions.Count == 0 || string.IsNullOrEmpty(request.TargetGroup))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Source positions and target group cannot be empty"
                    });
                }

                using var connection = _db.CreateConnection();
                
                // Get available locations in the target group
                var availableLocationsSql = @"SELECT Name, NodeRemark 
                                             FROM RCS_Locations 
                                             WHERE [Group] = @GroupName 
                                             AND Enabled = 1 
                                             AND Lock = 0 
                                             AND (MaterialCode IS NULL OR MaterialCode = '' OR MaterialCode = 'empty')
                                             ORDER BY Name";
                
                var availableLocations = await connection.QueryAsync<LocationDto>(availableLocationsSql, new { GroupName = request.TargetGroup });
                var availableLocationsList = availableLocations.ToList();

                if (availableLocationsList.Count < request.SourcePositions.Count)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Insufficient available locations in group '{request.TargetGroup}'. Available: {availableLocationsList.Count}, Required: {request.SourcePositions.Count}"
                    });
                }

                var createdTasks = new List<object>();
                var taskSql = @"
                    INSERT INTO RCS_UserTasks (
                        taskStatus, 
                        executed, 
                        creatTime, 
                        requestCode, 
                        taskType, 
                        priority, 
                        sourcePosition, 
                        targetPosition, 
                        IsCancelled
                    )
                    VALUES (
                        @TaskStatus, 
                        @Executed, 
                        @CreatTime, 
                        @RequestCode, 
                        @TaskType, 
                        @Priority, 
                        @SourcePosition, 
                        @TargetPosition, 
                        @IsCancelled
                    );
                    SELECT CAST(SCOPE_IDENTITY() as int)";

                // SQL to lock locations
                var lockLocationSql = @"UPDATE RCS_Locations SET Lock = 1 WHERE Name = @LocationName";

                // Create tasks for each source position
                for (int i = 0; i < request.SourcePositions.Count; i++)
                {
                    var sourcePosition = request.SourcePositions[i];
                    var targetLocation = availableLocationsList[i];

                    var taskId = await connection.ExecuteScalarAsync<int>(taskSql, new
                    {
                        TaskStatus = TaskStatuEnum.None,
                        Executed = false,
                        CreatTime = DateTime.Now,
                        RequestCode = Guid.NewGuid().ToString("N")[..8],
                        TaskType = RCS_UserTasks.TaskType.PlatingToBuffer,
                        Priority = 1,
                        SourcePosition = sourcePosition,
                        TargetPosition = targetLocation.Name,
                        IsCancelled = false
                    });

                    // Lock the source position
                    await connection.ExecuteAsync(lockLocationSql, new { LocationName = sourcePosition });

                    // Lock the target position
                    await connection.ExecuteAsync(lockLocationSql, new { LocationName = targetLocation.Name });

                    createdTasks.Add(new
                    {
                        TaskId = taskId,
                        SourcePosition = sourcePosition,
                        TargetPosition = targetLocation.Name,
                        TargetRemark = targetLocation.NodeRemark
                    });
                }

                return Json(new
                {
                    success = true,
                    message = $"Successfully created {createdTasks.Count} tasks",
                    data = createdTasks
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create batch tasks");
                return Json(new
                {
                    success = false,
                    message = "Failed to create batch tasks: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 获取任务列表（分页）
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTasks(int pageIndex = 1, int pageSize = 5)
        {
            try
            {
                if (pageIndex < 1) pageIndex = 1;
                if (pageSize < 1 || pageSize > 50) pageSize = 5;

                using var connection = _db.CreateConnection();
                
                // 获取未完成和未取消的任务
                var parameters = new DynamicParameters();
                parameters.Add("Offset", (pageIndex - 1) * pageSize);
                parameters.Add("PageSize", pageSize);
                
                var countSql = @"
                    SELECT COUNT(*) 
                    FROM RCS_UserTasks 
                    WHERE taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)";
                
                var dataSql = @"
                    SELECT 
                        ID, 
                        requestCode,
                        taskStatus, 
                        creatTime, 
                        sourcePosition, 
                        targetPosition
                    FROM RCS_UserTasks
                    WHERE taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                    ORDER BY creatTime DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                parameters.Add("TaskFinish", TaskStatuEnum.TaskFinish);
                parameters.Add("Canceled", TaskStatuEnum.Canceled);
                parameters.Add("CanceledWashing", TaskStatuEnum.CanceledWashing);
                parameters.Add("CanceledWashFinish", TaskStatuEnum.CanceledWashFinish);

                var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);
                var tasks = await connection.QueryAsync<TaskListDto>(dataSql, parameters);

                // 转换任务状态显示名称
                var taskList = tasks.Select(t => new
                {
                    t.ID,
                    t.RequestCode,
                    t.TaskStatus,
                    TaskStatusName = GetTaskStatusDisplayName(t.TaskStatus),
                    t.CreatTime,
                    t.SourcePosition,
                    t.TargetPosition
                }).ToList();

                return Json(new
                {
                    success = true,
                    data = taskList,
                    total = totalCount,
                    pageIndex = pageIndex,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务列表失败");
                return Json(new
                {
                    success = false,
                    message = "获取任务列表失败: " + ex.Message
                });
            }
        }

        private string GetTaskStatusDisplayName(TaskStatuEnum status)
        {
            return status switch
            {
                TaskStatuEnum.None => "未执行",
                TaskStatuEnum.TaskStart => "任务开始",
                TaskStatuEnum.Confirm => "参数确认中",
                TaskStatuEnum.ConfirmCar => "确认执行AGV",
                TaskStatuEnum.PickingUp => "取货中",
                TaskStatuEnum.PickDown => "取货完成",
                TaskStatuEnum.Unloading => "卸货中",
                TaskStatuEnum.UnloadDown => "卸货完成",
                TaskStatuEnum.TaskFinish => "任务完成",
                TaskStatuEnum.Canceled => "已取消",
                _ => status.ToString()
            };
        }

        /// <summary>
        /// 批量创建带优先级的任务（用于库位页面）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateBatchTasksWithPriority([FromBody] CreateBatchTaskWithPriorityRequest request)
        {
            try
            {
                if (request == null || request.SourcePositions == null || request.SourcePositions.Count == 0 || string.IsNullOrEmpty(request.TargetGroup))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Source positions and target group cannot be empty"
                    });
                }

                // 如果未指定优先级，则设置默认优先级（1 = 低）
                int priority = request.Priority > 0 ? request.Priority : 1;

                using var connection = _db.CreateConnection();

                var createdTasks = new List<object>();
                var failedCount = 0;
                var usedTargetLocations = new HashSet<string>(); // 跟踪已使用的目标库位，防止同一批次任务重复使用
                
                var taskSql = @"
                    INSERT INTO RCS_UserTasks (
                        taskStatus, 
                        executed, 
                        creatTime, 
                        requestCode, 
                        taskType, 
                        priority, 
                        sourcePosition, 
                        targetPosition, 
                        IsCancelled
                    )
                    VALUES (
                        @TaskStatus, 
                        @Executed, 
                        @CreatTime, 
                        @RequestCode, 
                        @TaskType, 
                        @Priority, 
                        @SourcePosition, 
                        @TargetPosition, 
                        @IsCancelled
                    );
                    SELECT CAST(SCOPE_IDENTITY() as int)";

                // 锁定库位的 SQL
                var lockLocationSql = @"UPDATE RCS_Locations SET Lock = 1 WHERE Name = @LocationName";

                // 为每个源库位创建任务
                for (int i = 0; i < request.SourcePositions.Count; i++)
                {
                    var sourcePosition = request.SourcePositions[i];
                    
                    // 1. 检查源库位是否存在
                    var sourceLocationInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT [Group], Lock, Enabled, Name FROM RCS_Locations WHERE Name = @Name",
                        new { Name = sourcePosition }
                    );
                    
                    if (sourceLocationInfo == null)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} does not exist");
                        failedCount++;
                        continue;
                    }
                    
                    // 2. 检查源库位是否被锁定
                    if (sourceLocationInfo.Lock == true)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} is already locked");
                        failedCount++;
                        continue;
                    }
                    
                    // 3. 检查源库位是否启用
                    if (sourceLocationInfo.Enabled == false)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} is disabled");
                        failedCount++;
                        continue;
                    }
                    
                    // 4. 根据源库位确定目标组
                    string actualTargetGroup = request.TargetGroup;
                    
                    // 如果源组是 FG/PM，则根据具体位置确定目标
                    if (sourceLocationInfo.Group == "FG/PM")
                    {
                        // 如果源是 4A 或 4B，目标是 RPM；否则目标是 FG
                        if (sourcePosition == "4A" || sourcePosition == "4B")
                        {
                            actualTargetGroup = "RPM";
                        }
                        else
                        {
                            actualTargetGroup = "FG";
                        }
                    }
                    
                    // 5. 检查源库位是否与目标组相同（不允许同组移动）
                    if (sourceLocationInfo.Group == actualTargetGroup)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} is in the same group as target group {actualTargetGroup}");
                        failedCount++;
                        continue;
                    }
                    
                    // 6. 检查源库位是否已处于活动任务中
                    var activeTaskCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM RCS_UserTasks 
                          WHERE (sourcePosition = @Position OR targetPosition = @Position) 
                          AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                          AND IsCancelled = 0",
                        new 
                        { 
                            Position = sourcePosition,
                            TaskFinish = TaskStatuEnum.TaskFinish,
                            Canceled = TaskStatuEnum.Canceled,
                            CanceledWashing = TaskStatuEnum.CanceledWashing,
                            CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                        }
                    );
                    
                    if (activeTaskCount > 0)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} is already in an active task");
                        failedCount++;
                        continue;
                    }
                    
                    // 7. 获取目标组中可用的库位
                    var availableLocationsSql = @"SELECT Name, NodeRemark 
                                                 FROM RCS_Locations 
                                                 WHERE [Group] = @GroupName 
                                                 AND Enabled = 1 
                                                 AND Lock = 0 
                                                 AND (MaterialCode IS NULL OR MaterialCode = '' OR MaterialCode = 'empty')
                                                 ORDER BY NodeRemark";
                    
                    var availableLocations = await connection.QueryAsync<LocationDto>(availableLocationsSql, new { GroupName = actualTargetGroup });
                    var availableLocationsList = availableLocations.ToList();
                    
                    // 8. 查找尚未使用的可用目标库位
                    var targetLocation = availableLocationsList.FirstOrDefault(loc => !usedTargetLocations.Contains(loc.Name));
                    
                    // 检查是否有可用的目标库位
                    if (targetLocation == null)
                    {
                        _logger.LogWarning($"No available target location in group {actualTargetGroup} for source {sourcePosition}");
                        failedCount++;
                        continue;
                    }
                    
                    // 9. 验证目标库位是否处于活动任务中
                    var targetActiveTaskCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM RCS_UserTasks 
                          WHERE (sourcePosition = @Position OR targetPosition = @Position) 
                          AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                          AND IsCancelled = 0",
                        new 
                        { 
                            Position = targetLocation.Name,
                            TaskFinish = TaskStatuEnum.TaskFinish,
                            Canceled = TaskStatuEnum.Canceled,
                            CanceledWashing = TaskStatuEnum.CanceledWashing,
                            CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                        }
                    );
                    
                    if (targetActiveTaskCount > 0)
                    {
                        _logger.LogWarning($"Target location {targetLocation.Name} is already in an active task");
                        failedCount++;
                        continue;
                    }

                    try
                    {
                        var taskId = await connection.ExecuteScalarAsync<int>(taskSql, new
                        {
                            TaskStatus = TaskStatuEnum.None,
                            Executed = false,
                            CreatTime = DateTime.Now,
                            RequestCode = Guid.NewGuid().ToString("N")[..8],
                            TaskType = RCS_UserTasks.TaskType.PlatingToBuffer,
                            Priority = priority,
                            SourcePosition = sourcePosition,
                            TargetPosition = targetLocation.Name,
                            IsCancelled = false
                        });

                        // 锁定源库位
                        await connection.ExecuteAsync(lockLocationSql, new { LocationName = sourcePosition });

                        // 锁定目标库位
                        await connection.ExecuteAsync(lockLocationSql, new { LocationName = targetLocation.Name });

                        // 将此目标库位标记为已使用
                        usedTargetLocations.Add(targetLocation.Name);

                        createdTasks.Add(new
                        {
                            TaskId = taskId,
                            SourcePosition = sourcePosition,
                            TargetPosition = targetLocation.Name,
                            TargetRemark = targetLocation.NodeRemark,
                            Priority = priority,
                            TargetGroup = actualTargetGroup
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to create task for source position {sourcePosition}");
                        failedCount++;
                    }
                }

                // 即使部分任务失败也返回成功（部分成功）
                return Json(new
                {
                    success = true,
                    message = $"Successfully created {createdTasks.Count} task(s). {(failedCount > 0 ? $"{failedCount} task(s) failed due to validation errors (locked, disabled, in active task, or insufficient target locations)." : "")}",
                    data = createdTasks,
                    requestedCount = request.SourcePositions.Count,
                    createdCount = createdTasks.Count,
                    failedCount = failedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create batch tasks with priority");
                return Json(new
                {
                    success = false,
                    message = "Failed to create batch tasks: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Location Information DTO
        /// </summary>
        public class LocationDto
        {
            public string Name { get; set; }
            public string NodeRemark { get; set; }
            public string Group { get; set; }
        }

        /// <summary>
        /// Create Task Request
        /// </summary>
        public class CreateTaskRequest
        {
            [Required(ErrorMessage = "Source position cannot be empty")]
            public string SourcePosition { get; set; }

            [Required(ErrorMessage = "Target position cannot be empty")]
            public string TargetPosition { get; set; }
        }

        /// <summary>
        /// Create Batch Tasks Request
        /// </summary>
        public class CreateBatchTaskRequest
        {
            [Required(ErrorMessage = "Source positions cannot be empty")]
            public List<string> SourcePositions { get; set; }

            [Required(ErrorMessage = "Target group cannot be empty")]
            public string TargetGroup { get; set; }
        }

        /// <summary>
        /// Create Batch Tasks with Priority Request
        /// </summary>
        public class CreateBatchTaskWithPriorityRequest
        {
            [Required(ErrorMessage = "Source positions cannot be empty")]
            public List<string> SourcePositions { get; set; }

            [Required(ErrorMessage = "Target group cannot be empty")]
            public string TargetGroup { get; set; }

            public int Priority { get; set; } = 1; // Default to low priority (1=Low, 2=Medium, 3=High)
        }

        /// <summary>
        /// Task List DTO
        /// </summary>
        public class TaskListDto
        {
            public int ID { get; set; }
            public TaskStatuEnum TaskStatus { get; set; }
            public DateTime? CreatTime { get; set; }
            public string SourcePosition { get; set; }
            public string TargetPosition { get; set; }
            public string RequestCode {  get; set; }
        }
    }
}