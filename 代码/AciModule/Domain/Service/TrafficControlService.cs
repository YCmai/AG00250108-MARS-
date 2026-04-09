using System;
using System.Threading.Tasks;
using AciModule.Domain.Entitys;
using AciModule.Domain.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace AciModule.Domain.Service
{
    /// <summary>
    /// 交管服务
    /// </summary>
    public class TrafficControlService : ITransientDependency
    {
        private readonly IRepository<TrafficControlTask, Guid> _trafficControlTaskRepository;
        private readonly ILogger<TrafficControlService> _logger;
        private readonly HttpRequestHelper _httpRequestHelper;
        private readonly IConfiguration _configuration;

        public TrafficControlService(
            IRepository<TrafficControlTask, Guid> trafficControlTaskRepository,
            ILogger<TrafficControlService> logger,
            HttpRequestHelper httpRequestHelper,
            IConfiguration configuration)
        {
            _trafficControlTaskRepository = trafficControlTaskRepository;
            _logger = logger;
            _httpRequestHelper = httpRequestHelper;
            _configuration = configuration;
        }

        /// <summary>
        /// 创建交管任务
        /// </summary>
        /// <param name="regionId">区域ID</param>
        /// <param name="isEntering">是否进入区域</param>
        /// <param name="orderIndex">订单索引</param>
        /// <returns>任务ID</returns>
        public async Task<Guid> CreateTrafficControlTask(int regionId, bool isEntering, int orderIndex)
        {
            try
            {
                _logger.LogInformation($"创建交管任务: 区域ID={regionId}, 类型={(isEntering ? "进入" : "离开")}, 订单索引={orderIndex}");
                
                var taskId = Guid.NewGuid();
                var task = new TrafficControlTask(
                    taskId,
                    regionId,
                    "0", // 默认AGV编号
                    isEntering ? 1 : 2, // 1-进入，2-离开
                    orderIndex
                );
                
                await _trafficControlTaskRepository.InsertAsync(task);
                _logger.LogInformation($"交管任务创建成功: ID={taskId}");
                
                return taskId;
            }
            catch (Exception ex)
            {
                _logger.LogError($"创建交管任务异常: {ex.Message}, 堆栈: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 处理交管任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>处理结果</returns>
        public async Task<bool> ProcessTrafficControlTask(Guid taskId)
        {
            try
            {
                var task = await _trafficControlTaskRepository.GetAsync(taskId);
                if (task == null)
                {
                    _logger.LogWarning($"交管任务不存在: ID={taskId}");
                    return false;
                }

                if (task.Status == 2) // 已完成
                {
                    _logger.LogInformation($"交管任务已完成: ID={taskId}");
                    return true;
                }

                // 获取API基础URL
                string apiBaseUrl = _configuration["RCS:ApiRespondUrl"];
                if (string.IsNullOrEmpty(apiBaseUrl))
                {
                    _logger.LogError("未配置RCS:ApiRespondUrl");
                    task.SetFailed("未配置RCS:ApiRespondUrl");
                    await _trafficControlTaskRepository.UpdateAsync(task);
                    return false;
                }

                // 构建请求URL
                string url;
                if (task.TaskType == 1) // 进入区域
                {
                    url = $"{apiBaseUrl}apply_area_token?agv_no=\"{task.AgvNo}\"&crossing_no=\"{task.RegionId}\"";
                }
                else // 离开区域
                {
                    url = $"{apiBaseUrl}free_area_token?agv_no=\"{task.AgvNo}\"&crossing_no=\"{task.RegionId}\"";
                }

                // 设置任务为处理中
                task.SetProcessing(url);
                await _trafficControlTaskRepository.UpdateAsync(task);

                // 发送请求
                string response = await _httpRequestHelper.GetAsync(url);
                
                // 处理响应
                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning($"交管任务响应为空: ID={taskId}, URL={url}");
                    task.SetFailed("响应为空");
                    await _trafficControlTaskRepository.UpdateAsync(task);
                    return false;
                }

                if (response.Equals("1"))
                {
                    _logger.LogInformation($"交管任务处理成功: ID={taskId}, 响应={response}");
                    task.SetCompleted(response);
                    await _trafficControlTaskRepository.UpdateAsync(task);
                    return true;
                }
                else
                {
                    _logger.LogWarning($"交管任务处理失败: ID={taskId}, 响应={response}");
                    task.SetFailed($"响应不为1: {response}");
                    await _trafficControlTaskRepository.UpdateAsync(task);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"处理交管任务异常: ID={taskId}, 错误={ex.Message}, 堆栈={ex.StackTrace}");
                var task = await _trafficControlTaskRepository.GetAsync(taskId);
                if (task != null)
                {
                    task.SetFailed(ex.Message);
                    await _trafficControlTaskRepository.UpdateAsync(task);
                }
                return false;
            }
        }

        /// <summary>
        /// 查询交管任务状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>任务状态</returns>
        public async Task<int> GetTrafficControlTaskStatus(Guid taskId)
        {
            try
            {
                var task = await _trafficControlTaskRepository.GetAsync(taskId);
                if (task == null)
                {
                    _logger.LogWarning($"交管任务不存在: ID={taskId}");
                    return -1;
                }

                return task.Status;
            }
            catch (Exception ex)
            {
                _logger.LogError($"查询交管任务状态异常: ID={taskId}, 错误={ex.Message}");
                return -1;
            }
        }
    }
} 