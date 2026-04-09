using System;
using System.Linq;
using System.Threading.Tasks;
using AciModule.Domain.Entitys;
using AciModule.Domain.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;

namespace AciModule.Domain.BackgroundWorkers
{
    /// <summary>
    /// 交管任务后台处理服务
    /// </summary>
    public class TrafficControlTaskWorker : AsyncPeriodicBackgroundWorkerBase
    {
        private readonly IRepository<TrafficControlTask, Guid> _trafficControlTaskRepository;
        private readonly ILogger<TrafficControlTaskWorker> _logger;

        public TrafficControlTaskWorker(
            AbpAsyncTimer timer,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<TrafficControlTaskWorker> logger)
            : base(timer, serviceScopeFactory)
        {
            _logger = logger;
            timer.Period = 2000; // 2秒执行一次
        }

        protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
        {
            _logger.LogDebug("交管任务后台处理服务开始执行...");

            try
            {
                // 获取待处理的任务
                _trafficControlTaskRepository = workerContext.ServiceProvider.GetRequiredService<IRepository<TrafficControlTask, Guid>>();
                var trafficControlService = workerContext.ServiceProvider.GetRequiredService<TrafficControlService>();

                // 获取所有待处理或处理失败但重试次数小于3的任务
                var pendingTasks = await _trafficControlTaskRepository.GetListAsync(
                    t => (t.Status == 0 || (t.Status == 3 && t.RetryCount < 3)));

                if (pendingTasks.Any())
                {
                    _logger.LogInformation($"发现{pendingTasks.Count}个待处理的交管任务");

                    foreach (var task in pendingTasks)
                    {
                        _logger.LogInformation($"开始处理交管任务: ID={task.Id}, 区域ID={task.RegionId}, 类型={(task.TaskType == 1 ? "进入" : "离开")}");
                        
                        // 处理任务
                        bool result = await trafficControlService.ProcessTrafficControlTask(task.Id);
                        
                        _logger.LogInformation($"交管任务处理结果: ID={task.Id}, 结果={result}");
                    }
                }
                else
                {
                    _logger.LogDebug("没有待处理的交管任务");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"交管任务后台处理服务异常: {ex.Message}, 堆栈: {ex.StackTrace}");
            }

            _logger.LogDebug("交管任务后台处理服务执行完成");
        }
    }
} 