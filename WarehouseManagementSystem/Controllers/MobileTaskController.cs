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
            _logger.LogInformation("移动端创建单条任务: {Source} -> {Target}", request?.SourcePosition, request?.TargetPosition);
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

                _logger.LogInformation("单条任务创建成功: ID={TaskId}, {Source} -> {Target}", taskId, request.SourcePosition, request.TargetPosition);

                return Json(new
                {
                    success = true,
                    message = "Task created successfully",
                    data = new { TaskId = taskId }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建任务失败: {Source} -> {Target}", request?.SourcePosition, request?.TargetPosition);
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
            _logger.LogInformation("移动端请求批量创建任务。数量: {Count}, 目标组: {TargetGroup}", request?.SourcePositions?.Count, request?.TargetGroup);
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
                    _logger.LogWarning("目标组 {Group} 可用库位不足。需要: {Required}, 可用: {Available}", request.TargetGroup, request.SourcePositions.Count, availableLocationsList.Count);
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

                    _logger.LogInformation("批量任务-子任务创建成功: ID={TaskId}, {Source} -> {Target}", taskId, sourcePosition, targetLocation.Name);

                    createdTasks.Add(new
                    {
                        TaskId = taskId,
                        SourcePosition = sourcePosition,
                        TargetPosition = targetLocation.Name,
                        TargetRemark = targetLocation.NodeRemark
                    });
                }

                _logger.LogInformation("批量任务创建完成。总计: {Count}", createdTasks.Count);

                return Json(new
                {
                    success = true,
                    message = $"Successfully created {createdTasks.Count} tasks",
                    data = createdTasks
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建任务失败");
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
                    WHERE taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                    AND IsCancelled = 0";
                
                var dataSql = @"
                    SELECT 
                        t.ID, 
                        t.requestCode,
                        t.taskStatus, 
                        t.creatTime, 
                        t.sourcePosition, 
                        t.targetPosition,
                        t.IsCancelled,
                        src.NodeRemark AS SourceNodeRemark,
                        src.[Group] AS SourceGroup,
                        tgt.NodeRemark AS TargetNodeRemark,
                        tgt.[Group] AS TargetGroup
                    FROM RCS_UserTasks t
                    LEFT JOIN RCS_Locations src ON src.Name = t.sourcePosition
                    LEFT JOIN RCS_Locations tgt ON tgt.Name = t.targetPosition
                    WHERE t.taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                    AND t.IsCancelled = 0
                    ORDER BY t.creatTime DESC
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
                    t.TargetPosition,
                    SourceDisplayName = FormatLocationDisplayName(t.SourceGroup, t.SourceNodeRemark, t.SourcePosition),
                    TargetDisplayName = FormatLocationDisplayName(t.TargetGroup, t.TargetNodeRemark, t.TargetPosition),
                    t.IsCancelled
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
                TaskStatuEnum.PendingCondition => "等待条件满足",
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

        private static string FormatLocationDisplayName(string group, string nodeRemark, string position)
        {
            var displayCode = string.IsNullOrWhiteSpace(nodeRemark) ? position : nodeRemark;

            if (string.IsNullOrWhiteSpace(displayCode))
            {
                return "Waiting";
            }

            return string.IsNullOrWhiteSpace(group) ? displayCode : $"{group}_{displayCode}";
        }

        /// <summary>
        /// 批量创建带优先级的任务（用于库位页面）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateBatchTasksWithPriority([FromBody] CreateBatchTaskWithPriorityRequest request)
        {
            _logger.LogInformation("移动端批量创建任务(带优先级)。源库位数量: {Count}, 目标组: {TargetGroup}, 优先级: {Priority}", 
                request?.SourcePositions?.Count, request?.TargetGroup, request?.Priority);
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
                const int batchTaskCreateDelayMilliseconds = 300;

                using var connection = _db.CreateConnection();

                var createdTasks = new List<object>();
                var failedCount = 0;
                var skippedFgPmNonFgStationCount = 0;
                var failureMessages = new List<string>();
                var usedTargetLocations = new HashSet<string>(); // 跟踪已使用的目标库位，防止同一批次任务重复使用
                var createdSourcePositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sourcePositionInfos = await connection.QueryAsync<LocationDto>(
                    "SELECT Name, NodeRemark, [Group] FROM RCS_Locations WHERE Name IN @Names",
                    new { Names = request.SourcePositions }
                );
                var sourceLocationMap = sourcePositionInfos.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
                var orderedSourcePositions = request.SourcePositions
                    .OrderBy(x => GetPickupSideOrder(GetLocationSide(sourceLocationMap.TryGetValue(x, out var location) ? location.NodeRemark : x, x)))
                    .ThenBy(x => GetLocationNumber(sourceLocationMap.TryGetValue(x, out var location) ? location.NodeRemark : x, x))
                    .ThenBy(x => sourceLocationMap.TryGetValue(x, out var location) ? location.NodeRemark : x)
                    .ThenBy(x => x)
                    .ToList();
                
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
                var activePositionTaskSql = @"SELECT COUNT(*) FROM RCS_UserTasks 
                          WHERE (sourcePosition = @Position OR targetPosition = @Position) 
                          AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                          AND IsCancelled = 0";

                async Task<int> CreateTaskAsync(string sourcePosition, LocationDto? targetLocation, TaskStatuEnum taskStatus, int taskPriority)
                {
                    var taskId = await connection.ExecuteScalarAsync<int>(taskSql, new
                    {
                        TaskStatus = taskStatus,
                        Executed = false,
                        CreatTime = DateTime.Now,
                        RequestCode = Guid.NewGuid().ToString("N")[..8],
                        TaskType = RCS_UserTasks.TaskType.PlatingToBuffer,
                        Priority = taskPriority,
                        SourcePosition = sourcePosition,
                        TargetPosition = targetLocation?.Name ?? string.Empty,
                        IsCancelled = false
                    });

                    if (taskStatus == TaskStatuEnum.None && targetLocation != null)
                    {
                        await connection.ExecuteAsync(lockLocationSql, new { LocationName = sourcePosition });
                        await connection.ExecuteAsync(lockLocationSql, new { LocationName = targetLocation.Name });
                        usedTargetLocations.Add(targetLocation.Name);
                        createdSourcePositions.Add(sourcePosition);
                    }

                    return taskId;
                }

                // 为每个源库位创建任务
                for (int i = 0; i < orderedSourcePositions.Count; i++)
                {
                    var sourcePosition = orderedSourcePositions[i];
                    
                    // 1. 检查源库位是否存在
                    var sourceLocationInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT [Group], Lock, Enabled, Name, NodeRemark, MaterialCode FROM RCS_Locations WHERE Name = @Name",
                        new { Name = sourcePosition }
                    );
                    
                    if (sourceLocationInfo == null)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} does not exist");
                        failureMessages.Add($"Source location {sourcePosition} does not exist.");
                        failedCount++;
                        continue;
                    }

                    var sourceGroup = ((string)sourceLocationInfo.Group)?.Trim() ?? string.Empty;
                    
                    // 2. 根据源库位确定目标组
                    string actualTargetGroup = request.TargetGroup;

                    // 移动端任务分配固定路由：FG/PM -> FG，RPM -> FG/PM
                    if (sourceGroup == "FG/PM")
                    {
                        if (!IsFgPmFgStation((string)sourceLocationInfo.NodeRemark, (string)sourceLocationInfo.Name))
                        {
                            _logger.LogInformation("FG/PM source location {Source} is not an active FG station, task creation skipped", sourcePosition);
                            skippedFgPmNonFgStationCount++;
                            failureMessages.Add($"FG/PM_{sourceLocationInfo.NodeRemark} is not an allowed station for transfer to FG.");
                            failedCount++;
                            continue;
                        }

                        actualTargetGroup = "FG";
                    }
                    else if (sourceGroup == "RPM")
                    {
                        actualTargetGroup = "FG/PM";
                    }

                    // 3. 检查源库位是否与目标组相同（不允许同组移动）
                    if (sourceGroup == actualTargetGroup)
                    {
                        _logger.LogWarning($"Source location {sourcePosition} is in the same group as target group {actualTargetGroup}");
                        failureMessages.Add($"{FormatLocationDisplayName(sourceGroup, (string)sourceLocationInfo.NodeRemark, sourcePosition)} is already in the target area and cannot be assigned.");
                        failedCount++;
                        continue;
                    }
                    
                    // 4. 检查源库位是否已处于待执行或执行中的任务中
                    var activeTaskCount = await connection.ExecuteScalarAsync<int>(
                        activePositionTaskSql,
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
                        failureMessages.Add($"{FormatLocationDisplayName(sourceGroup, (string)sourceLocationInfo.NodeRemark, sourcePosition)} already has an active task and cannot be assigned again.");
                        failedCount++;
                        continue;
                    }

                    var hasCreatedPairedBInBatch = HasCreatedPairedBSourceInCurrentBatch(
                        sourceGroup,
                        (string)sourceLocationInfo.NodeRemark,
                        sourcePosition,
                        sourceLocationMap,
                        createdSourcePositions);

                    if (!hasCreatedPairedBInBatch)
                    {
                        var sourceBlockReason = await GetSourcePathBlockReasonAsync(
                            connection,
                            sourceGroup,
                            (string)sourceLocationInfo.NodeRemark,
                            sourcePosition,
                            activePositionTaskSql);

                        if (!string.IsNullOrEmpty(sourceBlockReason))
                        {
                            _logger.LogInformation(
                                "源库位 {Source} 无法拾取：{Reason}",
                                sourcePosition,
                                sourceBlockReason);
                            failureMessages.Add(sourceBlockReason);
                            failedCount++;
                            continue;
                        }
                    }

                    if (sourceLocationInfo.Lock == true || sourceLocationInfo.Enabled == false || IsEmptyMaterial((string)sourceLocationInfo.MaterialCode))
                    {
                        var reason = GetSourceNotReadyMessage(
                            (bool)sourceLocationInfo.Enabled,
                            (bool)sourceLocationInfo.Lock,
                            (string)sourceLocationInfo.MaterialCode,
                            FormatLocationDisplayName(sourceGroup, (string)sourceLocationInfo.NodeRemark, sourcePosition));
                        failureMessages.Add(reason);
                        failedCount++;
                        continue;
                    }
                    
                    // 5. 获取目标组中的库位。FG 入库先放 A 区，再放 B 区。
                    var isRpmToFgPm = sourceGroup == "RPM" && actualTargetGroup == "FG/PM";
                    var availableLocationsSql = @"
                        SELECT Name, NodeRemark, [Group], Enabled, Lock, MaterialCode
                        FROM RCS_Locations
                        WHERE LTRIM(RTRIM([Group])) = @GroupName
                        AND (
                            @RpmToFgPm = 0
                            OR UPPER(LTRIM(RTRIM(NodeRemark))) IN ('4A', '4B')
                        )";
                    
                    var availableLocations = await connection.QueryAsync<LocationDto>(availableLocationsSql, new
                    {
                        GroupName = actualTargetGroup,
                        RpmToFgPm = isRpmToFgPm ? 1 : 0
                    });
                    var availableLocationsList = OrderTargetLocations(availableLocations, actualTargetGroup).ToList();
                    if (isRpmToFgPm)
                    {
                        availableLocationsList = availableLocationsList
                            .Where(x => IsFgPmRpmStation(x.NodeRemark, x.Name))
                            .ToList();
                    }

                    var targetLocationNames = availableLocationsList.Select(x => x.Name).ToList();
                    var activeTargetPositions = targetLocationNames.Count == 0
                        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        : (await connection.QueryAsync<string>(
                            @"SELECT sourcePosition
                              FROM RCS_UserTasks
                              WHERE sourcePosition IN @Positions
                              AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                              AND IsCancelled = 0
                              UNION
                              SELECT targetPosition
                              FROM RCS_UserTasks
                              WHERE targetPosition IN @Positions
                              AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                              AND IsCancelled = 0",
                            new
                            {
                                Positions = targetLocationNames,
                                TaskFinish = TaskStatuEnum.TaskFinish,
                                Canceled = TaskStatuEnum.Canceled,
                                CanceledWashing = TaskStatuEnum.CanceledWashing,
                                CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                            }
                        )).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                    // 6. 查找尚未使用的目标库位；选择 A 时要求同数字 B 当前也空闲
                    var targetLocation = availableLocationsList.FirstOrDefault(loc =>
                        IsTargetLocationAvailable(loc, availableLocationsList, activeTargetPositions, usedTargetLocations));
                    
                    // 检查是否有可用的目标库位
                    if (targetLocation == null)
                    {
                        _logger.LogWarning("源库位 {Source} 无法在目标组 {Group} 中找到匹配的空闲库位", sourcePosition, actualTargetGroup);
                        failureMessages.Add("There are no idle locations available in this area.");
                        failedCount++;
                        continue;
                    }
                    
                    // 7. 验证目标库位是否处于活动任务中
                    var targetActiveTaskCount = await connection.ExecuteScalarAsync<int>(
                        activePositionTaskSql,
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
                        _logger.LogWarning("目标库位 {Target} 已经处于活动任务中，源库位 {Source} 无法创建任务", targetLocation.Name, sourcePosition);
                        failureMessages.Add($"{FormatLocationDisplayName(targetLocation.Group, targetLocation.NodeRemark, targetLocation.Name)} already has an active task and cannot be used as a target.");
                        failedCount++;
                        continue;
                    }

                    try
                    {
                        var taskPriority = GetBatchSourceTaskPriority(priority, (string)sourceLocationInfo.NodeRemark, sourcePosition);
                        var taskId = await CreateTaskAsync(sourcePosition, targetLocation, TaskStatuEnum.None, taskPriority);
                        _logger.LogInformation("任务创建成功: ID={TaskId}, {Source} -> {Target}, Priority={Priority}", taskId, sourcePosition, targetLocation.Name, taskPriority);

                        createdTasks.Add(new
                        {
                            TaskId = taskId,
                            SourcePosition = sourcePosition,
                            TargetPosition = targetLocation.Name,
                            TargetRemark = targetLocation.NodeRemark,
                            Priority = taskPriority,
                            TargetGroup = actualTargetGroup,
                            Status = "Ready"
                        });

                        if (i < orderedSourcePositions.Count - 1)
                        {
                            await Task.Delay(batchTaskCreateDelayMilliseconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "为源库位 {Source} 创建任务失败", sourcePosition);
                        failedCount++;
                    }
                }

                _logger.LogInformation("带优先级的批量任务创建完成。成功: {Created}, 失败: {Failed}", createdTasks.Count, failedCount);

                var detailMessage = string.Join(" ", failureMessages.Distinct());

                // 即使部分任务失败也返回成功（部分成功）
                return Json(new
                {
                    success = true,
                    message = string.IsNullOrWhiteSpace(detailMessage)
                        ? $"Successfully created {createdTasks.Count} task(s)."
                        : detailMessage,
                    data = createdTasks,
                    requestedCount = request.SourcePositions.Count,
                    createdCount = createdTasks.Count,
                    pendingCount = 0,
                    failedCount = failedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建带优先级任务失败");
                return Json(new
                {
                    success = false,
                    message = "Failed to create batch tasks: " + ex.Message
                });
            }
        }

        private static bool IsTargetLocationAvailable(
            LocationDto location,
            List<LocationDto> groupLocations,
            HashSet<string> activeTaskPositions,
            HashSet<string> usedTargetLocations)
        {
            if (!IsLocationIdle(location, activeTaskPositions, usedTargetLocations))
            {
                return false;
            }

            if (GetLocationSide(location.NodeRemark, location.Name) != 'A')
            {
                return true;
            }

            var pairedLocation = FindPairedLocation(location, groupLocations);
            return pairedLocation == null || IsLocationIdle(pairedLocation, activeTaskPositions, usedTargetLocations);
        }

        private static IOrderedEnumerable<LocationDto> OrderTargetLocations(IEnumerable<LocationDto> locations, string targetGroup)
        {
            if (string.Equals(targetGroup?.Trim(), "FG", StringComparison.OrdinalIgnoreCase))
            {
                return locations
                    .OrderBy(x => GetUnloadSideOrder(GetLocationSide(x.NodeRemark, x.Name)))
                    .ThenBy(x => GetLocationNumber(x.NodeRemark, x.Name))
                    .ThenBy(x => x.NodeRemark)
                    .ThenBy(x => x.Name);
            }

            return locations
                .OrderBy(x => GetLocationNumber(x.NodeRemark, x.Name))
                .ThenBy(x => GetUnloadSideOrder(GetLocationSide(x.NodeRemark, x.Name)))
                .ThenBy(x => x.NodeRemark)
                .ThenBy(x => x.Name);
        }

        private static int GetBatchSourceTaskPriority(int selectedPriority, string nodeRemark, string sourcePosition)
        {
            var sourceSide = GetLocationSide(nodeRemark, sourcePosition);
            return selectedPriority + (sourceSide == 'B' ? 1 : 0);
        }

        private static bool HasCreatedPairedBSourceInCurrentBatch(
            string sourceGroup,
            string sourceNodeRemark,
            string sourcePosition,
            Dictionary<string, LocationDto> selectedSourceLocations,
            HashSet<string> createdSourcePositions)
        {
            var sourceNumber = GetLocationNumber(sourceNodeRemark, sourcePosition);
            var sourceSide = GetLocationSide(sourceNodeRemark, sourcePosition);

            if (sourceNumber == int.MaxValue || sourceSide != 'A')
            {
                return false;
            }

            return selectedSourceLocations.Values.Any(x =>
                createdSourcePositions.Contains(x.Name)
                && string.Equals((x.Group ?? string.Empty).Trim(), sourceGroup, StringComparison.OrdinalIgnoreCase)
                && GetLocationNumber(x.NodeRemark, x.Name) == sourceNumber
                && GetLocationSide(x.NodeRemark, x.Name) == 'B');
        }

        private async Task<string> GetSourcePathBlockReasonAsync(
            System.Data.IDbConnection connection,
            string sourceGroup,
            string nodeRemark,
            string sourcePosition,
            string activePositionTaskSql)
        {
            var sourceNumber = GetLocationNumber(nodeRemark, sourcePosition);
            var sourceSide = GetLocationSide(nodeRemark, sourcePosition);

            if (sourceNumber == int.MaxValue || sourceSide != 'A')
            {
                return string.Empty;
            }

            var pairedBRemark = $"{sourceNumber}B";
            var pairedB = await connection.QueryFirstOrDefaultAsync<LocationDto>(@"
                SELECT Name, NodeRemark, [Group], Enabled, Lock, MaterialCode
                FROM RCS_Locations
                WHERE LTRIM(RTRIM([Group])) = @GroupName
                AND UPPER(LTRIM(RTRIM(NodeRemark))) = @NodeRemark",
                new
                {
                    GroupName = sourceGroup,
                    NodeRemark = pairedBRemark.ToUpperInvariant()
                });

            if (pairedB != null)
            {
                var pairedActiveTaskCount = await connection.ExecuteScalarAsync<int>(
                    activePositionTaskSql,
                    new
                    {
                        Position = pairedB.Name,
                        TaskFinish = TaskStatuEnum.TaskFinish,
                        Canceled = TaskStatuEnum.Canceled,
                        CanceledWashing = TaskStatuEnum.CanceledWashing,
                        CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                    });

                if (pairedActiveTaskCount > 0 || !IsLocationIdle(pairedB, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                {
                    return $"A{sourceNumber} cannot be picked because B{sourceNumber} is blocking the path.";
                }
            }

            var bLocations = (await connection.QueryAsync<LocationDto>(@"
                SELECT Name, NodeRemark, [Group], Enabled, Lock, MaterialCode
                FROM RCS_Locations
                WHERE LTRIM(RTRIM([Group])) = @GroupName",
                new { GroupName = sourceGroup }))
                .Where(x => GetLocationSide(x.NodeRemark, x.Name) == 'B')
                .ToList();

            if (bLocations.Count == 0)
            {
                return string.Empty;
            }

            var bNames = bLocations.Select(x => x.Name).ToList();
            var activeBPositions = (await connection.QueryAsync<string>(@"
                SELECT sourcePosition
                FROM RCS_UserTasks
                WHERE sourcePosition IN @Positions
                AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                AND IsCancelled = 0
                UNION
                SELECT targetPosition
                FROM RCS_UserTasks
                WHERE targetPosition IN @Positions
                AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                AND IsCancelled = 0",
                new
                {
                    Positions = bNames,
                    TaskFinish = TaskStatuEnum.TaskFinish,
                    Canceled = TaskStatuEnum.Canceled,
                    CanceledWashing = TaskStatuEnum.CanceledWashing,
                    CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                })).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasIdleB = bLocations.Any(x => IsLocationIdle(x, activeBPositions, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            return hasIdleB ? string.Empty : "A-side pickup is blocked because the B area is fully occupied.";
        }

        private static string GetSourceNotReadyMessage(bool enabled, bool locked, string materialCode, string sourceDisplayName)
        {
            if (!enabled)
            {
                return $"{sourceDisplayName} is disabled and cannot be assigned.";
            }

            if (locked)
            {
                return $"{sourceDisplayName} is locked and cannot be assigned.";
            }

            if (IsEmptyMaterial(materialCode))
            {
                return $"{sourceDisplayName} has no material and cannot be assigned.";
            }

            return $"{sourceDisplayName} does not meet the task conditions.";
        }

        private static bool IsLocationIdle(
            LocationDto location,
            HashSet<string> activeTaskPositions,
            HashSet<string> usedTargetLocations)
        {
            return location.Enabled
                && !location.Lock
                && IsEmptyMaterial(location.MaterialCode)
                && !activeTaskPositions.Contains(location.Name)
                && !usedTargetLocations.Contains(location.Name);
        }

        private static bool IsEmptyMaterial(string materialCode)
        {
            return string.IsNullOrWhiteSpace(materialCode)
                || string.Equals(materialCode, "empty", StringComparison.OrdinalIgnoreCase);
        }

        private static LocationDto? FindPairedLocation(LocationDto location, List<LocationDto> groupLocations)
        {
            var number = GetLocationNumber(location.NodeRemark, location.Name);
            var side = GetLocationSide(location.NodeRemark, location.Name);

            if (number == int.MaxValue || (side != 'A' && side != 'B'))
            {
                return null;
            }

            var pairedSide = side == 'A' ? 'B' : 'A';
            return groupLocations.FirstOrDefault(x =>
                GetLocationNumber(x.NodeRemark, x.Name) == number
                && GetLocationSide(x.NodeRemark, x.Name) == pairedSide);
        }

        private static int GetLocationNumber(string nodeRemark, string name)
        {
            var code = GetLocationCode(nodeRemark);
            if (code.Number != int.MaxValue)
            {
                return code.Number;
            }

            return GetLocationCode(name).Number;
        }

        private static char GetLocationSide(string nodeRemark, string name)
        {
            var code = GetLocationCode(nodeRemark);
            if (code.Side != '\0')
            {
                return code.Side;
            }

            return GetLocationCode(name).Side;
        }

        private static int GetPickupSideOrder(char side)
        {
            return side switch
            {
                'B' => 0,
                'A' => 1,
                _ => 2
            };
        }

        private static int GetUnloadSideOrder(char side)
        {
            return side switch
            {
                'A' => 0,
                'B' => 1,
                _ => 2
            };
        }

        private static bool IsFgPmFgStation(string nodeRemark, string name)
        {
            var number = GetLocationNumber(nodeRemark, name);
            var side = GetLocationSide(nodeRemark, name);

            return number >= 1
                && number <= 3
                && (side == 'A' || side == 'B');
        }

        private static bool IsFgPmRpmStation(string nodeRemark, string name)
        {
            var nodeRemarkCode = nodeRemark?.Trim().ToUpperInvariant();

            return nodeRemarkCode == "4A" || nodeRemarkCode == "4B";
        }

        private static (int Number, char Side) GetLocationCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (int.MaxValue, '\0');
            }

            value = value.Trim();

            if (char.IsLetter(value[0]))
            {
                var prefixSide = char.ToUpperInvariant(value[0]);
                var prefixNumberStart = 1;

                while (prefixNumberStart < value.Length && char.IsWhiteSpace(value[prefixNumberStart]))
                {
                    prefixNumberStart++;
                }

                var prefixNumberEnd = prefixNumberStart;
                while (prefixNumberEnd < value.Length && char.IsDigit(value[prefixNumberEnd]))
                {
                    prefixNumberEnd++;
                }

                if (prefixNumberEnd > prefixNumberStart && int.TryParse(value.Substring(prefixNumberStart, prefixNumberEnd - prefixNumberStart), out var prefixNumber))
                {
                    return (prefixNumber, prefixSide);
                }
            }

            var sideIndex = value.Length - 1;
            while (sideIndex >= 0 && char.IsWhiteSpace(value[sideIndex]))
            {
                sideIndex--;
            }

            if (sideIndex < 0 || !char.IsLetter(value[sideIndex]))
            {
                return (int.MaxValue, '\0');
            }

            var side = char.ToUpperInvariant(value[sideIndex]);
            var numberEnd = sideIndex - 1;
            var numberStart = numberEnd;
            while (numberStart >= 0 && char.IsDigit(value[numberStart]))
            {
                numberStart--;
            }

            if (numberStart == numberEnd || !int.TryParse(value.Substring(numberStart + 1, numberEnd - numberStart), out var number))
            {
                return (int.MaxValue, side);
            }

            return (number, side);
        }

        /// <summary>
        /// Location Information DTO
        /// </summary>
        public class LocationDto
        {
            public string Name { get; set; } = string.Empty;
            public string NodeRemark { get; set; } = string.Empty;
            public string Group { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public bool Lock { get; set; }
            public string MaterialCode { get; set; } = string.Empty;
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
            public string SourceNodeRemark { get; set; } = string.Empty;
            public string SourceGroup { get; set; } = string.Empty;
            public string TargetNodeRemark { get; set; } = string.Empty;
            public string TargetGroup { get; set; } = string.Empty;
            public string RequestCode {  get; set; }
            public bool IsCancelled { get; set; }
        }
    }
}
