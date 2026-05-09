using Dapper;
using WarehouseManagementSystem.Db;

namespace WarehouseManagementSystem.Services
{
    public class PendingConditionTaskService : BackgroundService
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<PendingConditionTaskService> _logger;

        public PendingConditionTaskService(IDatabaseService db, ILogger<PendingConditionTaskService> logger)
        {
            _db = db;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PromoteReadyTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process pending condition tasks");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        private async Task PromoteReadyTasksAsync()
        {
            using var connection = _db.CreateConnection();
            connection.Open();

            if (await HasRunningOrReadyTaskAsync(connection))
            {
               // _logger.LogDebug("存在待执行或执行中的任务，本轮不释放等待任务");
                return;
            }

            var pendingTasks = (await connection.QueryAsync<PendingTaskDto>(@"
                SELECT TOP 20 ID, sourcePosition, priority, creatTime
                FROM RCS_UserTasks
                WHERE taskStatus = @PendingCondition
                AND IsCancelled = 0
                ORDER BY priority DESC, creatTime, ID",
                new { PendingCondition = TaskStatuEnum.PendingCondition })).ToList();

            if (pendingTasks.Count > 0)
            {
                _logger.LogInformation("开始检查 {PendingCount} 条等待条件满足的任务", pendingTasks.Count);
            }

            foreach (var task in pendingTasks)
            {
                var sourceLocation = await connection.QueryFirstOrDefaultAsync<LocationStateDto>(@"
                    SELECT Name, NodeRemark, [Group], Enabled, Lock, MaterialCode
                    FROM RCS_Locations
                    WHERE Name = @Name",
                    new { Name = task.SourcePosition });

                if (sourceLocation == null)
                {
                    _logger.LogWarning(
                        "待处理任务 {TaskId} 继续等待：起点储位 {SourcePosition} 不存在",
                        task.ID,
                        task.SourcePosition);
                    continue;
                }

                var sourceNotReadyReason = GetSourceNotReadyReason(sourceLocation);
                if (!string.IsNullOrEmpty(sourceNotReadyReason))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 继续等待：起点 {SourcePosition} 条件不满足。原因：{Reason}。Enabled={Enabled}, Lock={Lock}, MaterialCode={MaterialCode}",
                        task.ID,
                        sourceLocation.Name,
                        sourceNotReadyReason,
                        sourceLocation.Enabled,
                        sourceLocation.Lock,
                        sourceLocation.MaterialCode);
                    continue;
                }

                if (await HasActivePositionTaskAsync(connection, sourceLocation.Name, task.ID))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 继续等待：起点 {SourcePosition} 已存在其它待处理或执行中的任务",
                        task.ID,
                        sourceLocation.Name);
                    continue;
                }

                var sourceGroup = sourceLocation.Group?.Trim() ?? string.Empty;
                var sourcePathBlockReason = await GetSourcePathBlockReasonAsync(connection, task.ID, sourceLocation);
                if (!string.IsNullOrEmpty(sourcePathBlockReason))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 继续等待：起点 {SourcePosition} 取货路径未满足。原因：{Reason}",
                        task.ID,
                        sourceLocation.Name,
                        sourcePathBlockReason);
                    continue;
                }

                if (GetLocationSide(sourceLocation.NodeRemark, sourceLocation.Name) == 'A')
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId}：起点 {SourcePosition} 为 A 位，路径检查已通过，允许继续寻找终点",
                        task.ID,
                        sourceLocation.Name);
                }

                if (sourceGroup == "FG/PM" && !IsFgPmFgStation(sourceLocation.NodeRemark))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 继续等待：FG/PM 起点 {SourcePosition}（{SourceRemark}）不是允许发往 FG 的工位",
                        task.ID,
                        sourceLocation.Name,
                        sourceLocation.NodeRemark);
                    continue;
                }

                var targetGroup = GetTargetGroup(sourceGroup);
                if (string.IsNullOrWhiteSpace(targetGroup) || sourceGroup == targetGroup)
                {
                    _logger.LogWarning(
                        "待处理任务 {TaskId} 继续等待：起点 {SourcePosition} 没有合法目标分组，源分组={SourceGroup}, 目标分组={TargetGroup}",
                        task.ID,
                        sourceLocation.Name,
                        sourceGroup,
                        targetGroup);
                    continue;
                }

                var targetLocation = await FindAvailableTargetAsync(connection, task.ID, sourceLocation, targetGroup);
                if (targetLocation == null)
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 继续等待：目标分组 {TargetGroup} 中没有可用终点，起点={SourcePosition}",
                        task.ID,
                        targetGroup,
                        sourceLocation.Name);
                    continue;
                }

                if (await HasActivePositionTaskAsync(connection, targetLocation.Name, task.ID))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 继续等待：已选终点 {TargetPosition} 已存在其它待处理或执行中的任务",
                        task.ID,
                        targetLocation.Name);
                    continue;
                }

                using var transaction = connection.BeginTransaction();
                var affectedRows = await connection.ExecuteAsync(@"
                    UPDATE RCS_UserTasks
                    SET taskStatus = @None,
                        targetPosition = @TargetPosition
                    WHERE ID = @TaskId
                    AND taskStatus = @PendingCondition
                    AND IsCancelled = 0
                    AND NOT EXISTS (
                        SELECT 1
                        FROM RCS_UserTasks
                        WHERE taskStatus > @PendingCondition
                        AND taskStatus < @TaskFinish
                        AND IsCancelled = 0
                    );",
                    new
                    {
                        None = TaskStatuEnum.None,
                        PendingCondition = TaskStatuEnum.PendingCondition,
                        TaskFinish = TaskStatuEnum.TaskFinish,
                        TargetPosition = targetLocation.Name,
                        TaskId = task.ID
                    },
                    transaction);

                if (affectedRows == 0)
                {
                    transaction.Rollback();
                    _logger.LogInformation(
                        "待处理任务 {TaskId} 未转为未执行：更新前任务状态已变化或已被取消",
                        task.ID);
                    continue;
                }

                await connection.ExecuteAsync(
                    "UPDATE RCS_Locations SET Lock = 1 WHERE Name IN @LocationNames",
                    new { LocationNames = new[] { sourceLocation.Name, targetLocation.Name } },
                    transaction);

                transaction.Commit();

                _logger.LogInformation(
                    "待处理任务 {TaskId} 已转为未执行：{SourcePosition} -> {TargetPosition}",
                    task.ID,
                    sourceLocation.Name,
                    targetLocation.Name);

                return;
            }
        }

        private async Task<bool> HasRunningOrReadyTaskAsync(System.Data.IDbConnection connection)
        {
            var count = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*)
                FROM RCS_UserTasks
                WHERE taskStatus > @PendingCondition
                AND taskStatus < @TaskFinish
                AND IsCancelled = 0",
                new
                {
                    PendingCondition = TaskStatuEnum.PendingCondition,
                    TaskFinish = TaskStatuEnum.TaskFinish
                });

            return count > 0;
        }

        private async Task<LocationStateDto?> FindAvailableTargetAsync(
            System.Data.IDbConnection connection,
            int taskId,
            LocationStateDto sourceLocation,
            string targetGroup)
        {
            var isRpmToFgPm = (sourceLocation.Group?.Trim() ?? string.Empty) == "RPM" && targetGroup == "FG/PM";
            var targetLocations = (await connection.QueryAsync<LocationStateDto>(@"
                SELECT Name, NodeRemark, [Group], Enabled, Lock, MaterialCode
                FROM RCS_Locations
                WHERE LTRIM(RTRIM([Group])) = @TargetGroup
                AND (
                    @RpmToFgPm = 0
                    OR UPPER(LTRIM(RTRIM(NodeRemark))) IN ('4A', '4B')
                )",
                new
                {
                    TargetGroup = targetGroup,
                    RpmToFgPm = isRpmToFgPm ? 1 : 0
                }))
                .OrderBy(x => GetTargetSideSortOrder(x, targetGroup))
                .ThenBy(x => GetLocationNumber(x.NodeRemark, x.Name))
                .ThenBy(x => GetUnloadSideOrder(GetLocationSide(x.NodeRemark, x.Name)))
                .ThenBy(x => x.NodeRemark)
                .ThenBy(x => x.Name)
                .ToList();

            if (isRpmToFgPm)
            {
                targetLocations = targetLocations
                    .Where(x => IsFgPmRpmStation(x.NodeRemark))
                    .ToList();
                _logger.LogInformation(
                    "待处理任务 {TaskId}：RPM 起点只检查 FG/PM 的 4A/4B 终点：{TargetCandidates}",
                    taskId,
                    string.Join(", ", targetLocations.Select(x => $"{x.Name}({x.NodeRemark})")));
            }

            if (targetLocations.Count == 0)
            {
                _logger.LogInformation(
                    "待处理任务 {TaskId}：目标分组 {TargetGroup} 没有候选储位",
                    taskId,
                    targetGroup);
                return null;
            }

            var targetNames = targetLocations.Select(x => x.Name).ToList();
            var activeTargetPositions = (await connection.QueryAsync<string>(@"
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
                    Positions = targetNames,
                    TaskFinish = TaskStatuEnum.TaskFinish,
                    Canceled = TaskStatuEnum.Canceled,
                    CanceledWashing = TaskStatuEnum.CanceledWashing,
                    CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                })).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var location in targetLocations)
            {
                var reason = GetTargetNotReadyReason(location, targetLocations, activeTargetPositions);
                if (string.IsNullOrEmpty(reason))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId}：选中终点 {TargetPosition}（{TargetRemark}）",
                        taskId,
                        location.Name,
                        location.NodeRemark);
                    return location;
                }

                _logger.LogInformation(
                    "待处理任务 {TaskId}：跳过终点 {TargetPosition}（{TargetRemark}）。原因：{Reason}。Enabled={Enabled}, Lock={Lock}, MaterialCode={MaterialCode}, InActiveTask={InActiveTask}",
                    taskId,
                    location.Name,
                    location.NodeRemark,
                    reason,
                    location.Enabled,
                    location.Lock,
                    location.MaterialCode,
                    activeTargetPositions.Contains(location.Name));
            }

            return null;
        }

        private async Task<string> GetSourcePathBlockReasonAsync(
            System.Data.IDbConnection connection,
            int currentTaskId,
            LocationStateDto sourceLocation)
        {
            var sourceNumber = GetLocationNumber(sourceLocation.NodeRemark, sourceLocation.Name);
            var sourceSide = GetLocationSide(sourceLocation.NodeRemark, sourceLocation.Name);

            if (sourceNumber == int.MaxValue || sourceSide != 'A')
            {
                return string.Empty;
            }

            var sourceGroup = sourceLocation.Group?.Trim() ?? string.Empty;
            var pairedB = await connection.QueryFirstOrDefaultAsync<LocationStateDto>(@"
                SELECT Name, NodeRemark, [Group], Enabled, Lock, MaterialCode
                FROM RCS_Locations
                WHERE LTRIM(RTRIM([Group])) = @GroupName
                AND UPPER(LTRIM(RTRIM(NodeRemark))) = @NodeRemark",
                new
                {
                    GroupName = sourceGroup,
                    NodeRemark = $"{sourceNumber}B"
                });

            if (pairedB != null)
            {
                var pairedBHasActiveTask = await HasActivePositionTaskAsync(connection, pairedB.Name, currentTaskId);
                if (pairedBHasActiveTask || !IsPathLocationIdle(pairedB, pairedBHasActiveTask))
                {
                    _logger.LogInformation(
                        "待处理任务 {TaskId}：A 位 {SourcePosition} 继续等待，配对 B 位 {PairedBPosition} 仍未让路。BHasActiveTask={BHasActiveTask}, Enabled={Enabled}, Lock={Lock}, MaterialCode={MaterialCode}",
                        currentTaskId,
                        sourceLocation.Name,
                        pairedB.Name,
                        pairedBHasActiveTask,
                        pairedB.Enabled,
                        pairedB.Lock,
                        pairedB.MaterialCode);
                    return $"B{sourceNumber} is blocking A{sourceNumber}";
                }

                _logger.LogInformation(
                    "待处理任务 {TaskId}：A 位 {SourcePosition} 的配对 B 位 {PairedBPosition} 已让路",
                    currentTaskId,
                    sourceLocation.Name,
                    pairedB.Name);
            }

            var bLocations = (await connection.QueryAsync<LocationStateDto>(@"
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
                WHERE ID <> @CurrentTaskId
                AND sourcePosition IN @Positions
                AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                AND IsCancelled = 0
                UNION
                SELECT targetPosition
                FROM RCS_UserTasks
                WHERE ID <> @CurrentTaskId
                AND targetPosition IN @Positions
                AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                AND IsCancelled = 0",
                new
                {
                    CurrentTaskId = currentTaskId,
                    Positions = bNames,
                    TaskFinish = TaskStatuEnum.TaskFinish,
                    Canceled = TaskStatuEnum.Canceled,
                    CanceledWashing = TaskStatuEnum.CanceledWashing,
                    CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                })).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasIdleB = bLocations.Any(x => IsPathLocationIdle(x, activeBPositions.Contains(x.Name)));
            if (!hasIdleB)
            {
                _logger.LogInformation(
                    "待处理任务 {TaskId}：A 位 {SourcePosition} 继续等待，B 区当前没有任何空闲位",
                    currentTaskId,
                    sourceLocation.Name);
            }
            return hasIdleB ? string.Empty : "the B area is fully occupied";
        }

        private async Task<bool> HasActivePositionTaskAsync(System.Data.IDbConnection connection, string position, int currentTaskId)
        {
            var count = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*)
                FROM RCS_UserTasks
                WHERE ID <> @CurrentTaskId
                AND (sourcePosition = @Position OR targetPosition = @Position)
                AND taskStatus NOT IN (@TaskFinish, @Canceled, @CanceledWashing, @CanceledWashFinish)
                AND IsCancelled = 0",
                new
                {
                    CurrentTaskId = currentTaskId,
                    Position = position,
                    TaskFinish = TaskStatuEnum.TaskFinish,
                    Canceled = TaskStatuEnum.Canceled,
                    CanceledWashing = TaskStatuEnum.CanceledWashing,
                    CanceledWashFinish = TaskStatuEnum.CanceledWashFinish
                });

            return count > 0;
        }

        private static string GetTargetGroup(string sourceGroup)
        {
            return (sourceGroup?.Trim() ?? string.Empty) switch
            {
                "FG/PM" => "FG",
                "RPM" => "FG/PM",
                _ => string.Empty
            };
        }

        private static bool IsFgPmFgStation(string? nodeRemark)
        {
            var code = GetLocationCode(nodeRemark);

            return code.Number >= 1
                && code.Number <= 3
                && (code.Side == 'A' || code.Side == 'B');
        }

        private static bool IsFgPmRpmStation(string? nodeRemark)
        {
            var nodeRemarkCode = nodeRemark?.Trim().ToUpperInvariant();
            return nodeRemarkCode == "4A" || nodeRemarkCode == "4B";
        }

        private static bool IsSourceReady(LocationStateDto location)
        {
            return location.Enabled
                && !location.Lock
                && !IsEmptyMaterial(location.MaterialCode);
        }

        private static string GetSourceNotReadyReason(LocationStateDto location)
        {
            if (!location.Enabled)
            {
                return "起点未启用";
            }

            if (location.Lock)
            {
                return "起点已锁定";
            }

            if (IsEmptyMaterial(location.MaterialCode))
            {
                return "起点没有物料";
            }

            return string.Empty;
        }

        private static bool IsTargetLocationAvailable(
            LocationStateDto location,
            List<LocationStateDto> groupLocations,
            HashSet<string> activeTaskPositions)
        {
            if (!IsTargetIdle(location, activeTaskPositions))
            {
                return false;
            }

            if (GetLocationSide(location.NodeRemark, location.Name) != 'A')
            {
                return true;
            }

            var pairedLocation = FindPairedLocation(location, groupLocations);
            return pairedLocation == null || IsTargetIdle(pairedLocation, activeTaskPositions);
        }

        private static int GetTargetSideSortOrder(LocationStateDto location, string targetGroup)
        {
            if (string.Equals(targetGroup?.Trim(), "FG", StringComparison.OrdinalIgnoreCase))
            {
                return GetUnloadSideOrder(GetLocationSide(location.NodeRemark, location.Name));
            }

            return 0;
        }

        private static string GetTargetNotReadyReason(
            LocationStateDto location,
            List<LocationStateDto> groupLocations,
            HashSet<string> activeTaskPositions)
        {
            if (!location.Enabled)
            {
                return "终点未启用";
            }

            if (location.Lock)
            {
                return "终点已锁定";
            }

            if (!IsEmptyMaterial(location.MaterialCode))
            {
                return "终点已有物料";
            }

            if (activeTaskPositions.Contains(location.Name))
            {
                return "终点已存在待处理或执行中的任务";
            }

            if (GetLocationSide(location.NodeRemark, location.Name) == 'A')
            {
                var pairedLocation = FindPairedLocation(location, groupLocations);
                if (pairedLocation != null && !IsTargetIdle(pairedLocation, activeTaskPositions))
                {
                    return $"配对 B 位 {pairedLocation.Name} 不空闲";
                }
            }

            return string.Empty;
        }

        private static bool IsTargetIdle(LocationStateDto location, HashSet<string> activeTaskPositions)
        {
            return location.Enabled
                && !location.Lock
                && IsEmptyMaterial(location.MaterialCode)
                && !activeTaskPositions.Contains(location.Name);
        }

        private static bool IsPathLocationIdle(LocationStateDto location, bool hasActiveTask)
        {
            return location.Enabled
                && !location.Lock
                && IsEmptyMaterial(location.MaterialCode)
                && !hasActiveTask;
        }

        private static bool IsEmptyMaterial(string? materialCode)
        {
            return string.IsNullOrWhiteSpace(materialCode)
                || string.Equals(materialCode, "empty", StringComparison.OrdinalIgnoreCase);
        }

        private static LocationStateDto? FindPairedLocation(LocationStateDto location, List<LocationStateDto> groupLocations)
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

        private static int GetLocationNumber(string? nodeRemark, string? name)
        {
            var code = GetLocationCode(nodeRemark);
            if (code.Number != int.MaxValue)
            {
                return code.Number;
            }

            return GetLocationCode(name).Number;
        }

        private static char GetLocationSide(string? nodeRemark, string? name)
        {
            var code = GetLocationCode(nodeRemark);
            if (code.Side != '\0')
            {
                return code.Side;
            }

            return GetLocationCode(name).Side;
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

        private static (int Number, char Side) GetLocationCode(string? value)
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

        private sealed class PendingTaskDto
        {
            public int ID { get; set; }
            public string SourcePosition { get; set; } = string.Empty;
            public int Priority { get; set; }
            public DateTime? CreatTime { get; set; }
        }

        private sealed class LocationStateDto
        {
            public string Name { get; set; } = string.Empty;
            public string NodeRemark { get; set; } = string.Empty;
            public string Group { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public bool Lock { get; set; }
            public string MaterialCode { get; set; } = string.Empty;
        }
    }
}
