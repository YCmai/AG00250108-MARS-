﻿using AciModule.Domain.Entitys;
using AciModule.Domain.Shared;
using AciModule.Domain.Worker;
using Spark.Domain.Entitys;
using Newtonsoft.Json;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spark.Application;
using WMS.LineCallInputModule.Domain;
using Volo.Abp.Uow;
using static AciModule.Domain.Entitys.RCS_UserTasks;
using Microsoft.CodeAnalysis;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Spark.Domain.Worker
{
    /// <summary>
    /// 中间表数据
    /// </summary>
    public class RCS_WmsTaskWorker : RepeatBackgroundWorkerBase, ITransientDependency
    {
        private readonly IRepository<NdcTask_Moves, Guid> _ndcTaskRepos;
        private readonly IRepository<RCS_WmsTask> _wmmTasks;
        private readonly IRepository<RCS_UserTasks> _userTasks;
        private readonly IRepository<RCS_Locations> _locations;
        private readonly IRepository<RCS_ApiTask> _apiTask;
        private readonly IRepository<RCS_IOAGV_Tasks> _rcs_IOAGV_Tasks;
        private static LoggerManager _loggerManager;
        private readonly IConfiguration _configuration;

        public RCS_WmsTaskWorker(IRepository<NdcTask_Moves, Guid> ndcTaskRepos, IRepository<RCS_IOAGV_Tasks> rcs_IOAGV_Tasks, LoggerManager loggerManager, IRepository<RCS_UserTasks> userTasks, IRepository<RCS_Locations> locations, IRepository<RCS_ApiTask> apiTask, IRepository<RCS_WmsTask> wmmTasks, IConfiguration configuration) : base(1)
        {
            _ndcTaskRepos = ndcTaskRepos ?? throw new ArgumentNullException(nameof(ndcTaskRepos));
            _wmmTasks = wmmTasks ?? throw new ArgumentNullException(nameof(wmmTasks));
            _apiTask = apiTask ?? throw new ArgumentNullException(nameof(apiTask));
            _loggerManager = loggerManager ?? throw new ArgumentNullException(nameof(loggerManager));
            _userTasks = userTasks;
            _locations = locations;
            _rcs_IOAGV_Tasks = rcs_IOAGV_Tasks;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [UnitOfWork]
        public override async Task Execute(IJobExecutionContext context)
        {
            
            await SafeExecute(CreateNewTasks, nameof(CreateNewTasks));
           
            await SafeExecute(UpdateWmsTasks, nameof(UpdateWmsTasks));

            await SafeExecute(CancleTaslk, nameof(CancleTaslk));
        }


        /// <summary>
        /// 封装任务执行方法，确保每个方法报错后不影响其他方法执行
        /// </summary>
        private async Task SafeExecute(Func<Task> taskFunc, string taskName)
        {
            try
            {
                await taskFunc();
            }
            catch (Exception ex)
            {
                await _loggerManager.LogAndLogError($"{taskName} 执行时发生异常: {ex.Message}");
            }
        }



        private async Task CancleTaslk()
        {
            var cancelTasks = await _userTasks.GetListAsync(x => x.taskStatus < TaskStatuEnum.TaskFinish && x.IsCancelled);

            foreach (var cancelTask in cancelTasks)
            {
                var task = await _ndcTaskRepos.FindAsync(x => x.SchedulTaskNo == cancelTask.requestCode);

                if (task != null)
                {
                    //要把储位的分配状态也去掉
                    if (task.TaskStatus == TaskStatuEnum.None || task.TaskStatus == TaskStatuEnum.CarWash)
                    {
                        task.SetStatus(TaskStatuEnum.Canceled);
                        await _ndcTaskRepos.UpdateAsync(task);
                        
                    }
                    else
                    {
                        if (!task.CancelTask)
                        {
                            task.CancelTask = true;
                            await _ndcTaskRepos.UpdateAsync(task);
                        }
                    }
                }
                else
                {
                    cancelTask.taskStatus = TaskStatuEnum.Canceled;
                    await _userTasks.UpdateAsync(cancelTask);
                }
            }

        }

        /// <summary>
        /// 更新任务状态
        /// </summary>
        /// <returns></returns>
        private async Task UpdateWmsTasks()
        {
            // 获取未完成的任务
            var taskStatusesToExclude = new List<TaskStatuEnum> { TaskStatuEnum.Canceled, TaskStatuEnum.TaskFinish };
            var tasksToUpdate = await _userTasks.GetListAsync(i => !taskStatusesToExclude.Contains(i.taskStatus));
            var taskCodesToUpdate = tasksToUpdate.Select(i => i.requestCode).Distinct();

            // 获取对应的 NDC 任务
            var ndcTasks = await _ndcTaskRepos.GetListAsync(x => taskCodesToUpdate.Contains(x.SchedulTaskNo));

            foreach (var userTask in tasksToUpdate)
            {
                var ndcTask = ndcTasks.FirstOrDefault(x => x.SchedulTaskNo == userTask.requestCode);
                if (ndcTask == null || ndcTask.TaskStatus == userTask.taskStatus) continue;

                // 更新任务状态
                userTask.taskStatus = ndcTask.TaskStatus;
                userTask.robotCode = ndcTask.AgvId.ToString();
                
                // 处理储位状态
                if (ndcTask.TaskStatus == TaskStatuEnum.TaskFinish)
                {
                    await HandleTaskFinish(userTask);
                }
                else if (ndcTask.TaskStatus == TaskStatuEnum.Canceled)
                {
                    await HandleTaskCancel(userTask);
                }

                // 更新用户任务
                await _userTasks.UpdateAsync(userTask);
                
                await _loggerManager.LogAndLogCritical(
                    $"更新任务 {userTask.requestCode} 状态为 {userTask.taskStatus}，" +
                    $"任务类型：{userTask.taskType}，" +
                    $"起点：{userTask.sourcePosition}，" +
                    $"终点：{userTask.targetPosition}");
            }
        }

        /// <summary>
        /// 处理任务完成状态
        /// </summary>
        private async Task HandleTaskFinish(RCS_UserTasks userTask)
        {
            // 解锁源储位和目标储位
            var sourceLocation = await _locations.FirstOrDefaultAsync(l => l.Name == userTask.sourcePosition);
            var targetLocation = await _locations.FirstOrDefaultAsync(l => l.Name == userTask.targetPosition);

            if (sourceLocation != null)
            {
                sourceLocation.Lock = false;
                // 清空源储位的物料信息
                sourceLocation.MaterialCode = null;
                sourceLocation.PalletID = null;
                sourceLocation.Weight = null;
                sourceLocation.Quanitity = null;
                await _locations.UpdateAsync(sourceLocation);
            }

            if (targetLocation != null)
            {
                targetLocation.Lock = false;
                // 把任务的物料编号放到目标储位
                targetLocation.MaterialCode = "full";
                targetLocation.PalletID = null;
                targetLocation.Weight = null;
                targetLocation.Quanitity = null;
                await _locations.UpdateAsync(targetLocation);
            }

            await _loggerManager.LogAndLogCritical(
                $"任务完成：{userTask.requestCode}，物料 {userTask.MaterialCode} 已移动到 {userTask.targetPosition}");
        }


        /// <summary>
        /// 处理任务取消状态
        /// </summary>
        private async Task HandleTaskCancel(RCS_UserTasks userTask)
        {
            // 解锁任务相关的储位
            await UnlockTaskLocations(userTask);
        }

        /// <summary>
        /// 解锁任务相关的储位
        /// </summary>
        private async Task UnlockTaskLocations(RCS_UserTasks task)
        {
            // 获取源储位和目标储位
            var sourceLocation = await _locations.FirstOrDefaultAsync(l => l.Name == task.sourcePosition);
            var targetLocation = await _locations.FirstOrDefaultAsync(l => l.Name == task.targetPosition);

            // 解锁源储位
            if (sourceLocation != null)
            {
                sourceLocation.Lock = false;
                await _locations.UpdateAsync(sourceLocation);
            }

            // 解锁目标储位
            if (targetLocation != null)
            {
                targetLocation.Lock = false;
                await _locations.UpdateAsync(targetLocation);
            }
            
            await _loggerManager.LogAndLogCritical(
                $"任务取消：{task.requestCode}，已解锁相关储位");
        }


        /// <summary>
        /// 创建新的NDC任务
        /// </summary>
        /// <returns></returns>
        private async Task CreateNewTasks()
        {
            try
            {
                // 获取状态为 None 的任务
                var carWashTasks = await _userTasks.GetListAsync(x => x.taskStatus == TaskStatuEnum.None);
                if (!carWashTasks.Any()) return;

                // 获取所有位置信息
                var locations = await _locations.GetListAsync();

                // 获取未完成的 NDC 任务
                var unfinishedNdcTasks = await _ndcTaskRepos.GetListAsync(
                    x => x.TaskStatus != TaskStatuEnum.TaskFinish &&
                         x.TaskStatus != TaskStatuEnum.Canceled);

                foreach (var task in carWashTasks)
                {
                    // 检查是否已存在相同的未完成任务
                    if (unfinishedNdcTasks.Any(e => e.SchedulTaskNo == task.requestCode))
                        continue;

                    // 获取起点和终点位置信息
                    var pickupLocation = locations.FirstOrDefault(l => l.Name == task.sourcePosition);
                    var unloadLocation = locations.FirstOrDefault(l => l.Name == task.targetPosition);

                    if (pickupLocation == null || unloadLocation == null)
                    {
                        await _loggerManager.LogAndLogError(
                            $"任务 {task.requestCode} 的起点或终点位置无效。起点：{task.sourcePosition}，终点：{task.targetPosition}");
                        task.taskStatus = TaskStatuEnum.Canceled;
                        await _userTasks.UpdateAsync(task);

                        continue;
                    }

                    var ndcModel = await _ndcTaskRepos.FirstOrDefaultAsync(x => x.SchedulTaskNo == task.requestCode);
                    if (ndcModel != null) { continue; }

                   
                    // 创建新的 NDC 任务，注意类型转换
                    var newTask = new NdcTask_Moves(
                        Guid.NewGuid(),                    // Id: Guid
                        Guid.NewGuid(),                    // TenantId: Guid
                        task.taskType.ToString(),          // TaskName: string
                        0,                                 // TaskNo: int
                        task.requestCode,                     // SchedulTaskNo: string
                        Convert.ToInt32(task.taskType),               // TaskType: int，从枚举转换为int
                        "K",                               // TaskMode: string
                        Convert.ToInt32(pickupLocation.Name),       // PickupPoint: string
                        pickupLocation.LiftingHeight,  // PickupHeight: int
                        Convert.ToInt32(unloadLocation.Name),       // UnloadPoint: string
                        unloadLocation.UnloadHeight,  // UnloadHeight: int
                        0     // Priority: int
                    );

                    await _ndcTaskRepos.InsertAsync(newTask);
                    await _loggerManager.LogAndLogCritical(
                        $"下发任务号 {task.requestCode}, 取料点: {task.sourcePosition}, 卸料点: {task.targetPosition} 任务成功！");


                    break;
                }
            }
            catch (Exception ex)
            {

                await _loggerManager.LogAndLogError($"创建任务失败{ex.Message}");
            }
        }

 
    }

}
