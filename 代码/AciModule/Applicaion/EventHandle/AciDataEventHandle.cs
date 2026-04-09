using AciModule.Domain;
using AciModule.Domain.Entitys;
//using AciModule.Domain.Queue;
using AciModule.Domain.Service;
using AciModule.Domain.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

using WMS.StorageModule.Domain;

namespace AciModule.Applicaion.EventHandle
{
    /// <summary>
    /// ACI数据事件处理器
    /// </summary>
    /// <remarks>
    /// 负责处理所有ACI相关的事件，包括订单开始、参数确认、装卸货、任务完成等状态的处理
    /// 实现 ILocalEventHandler<AciDataEventArgs> 接口以处理本地事件
    /// </remarks>
    public class AciDataEventHandle : ILocalEventHandler<AciDataEventArgs>, ITransientDependency
    {
        private readonly AciAppManager _aciAppManager;
        private readonly IRepository<NdcTask_Moves, Guid> _ndcTask;
        private readonly ILogger<AciDataEventHandle> _logger;
        private readonly IRepository<RCS_IOAGV_Tasks> _rcs_IOAGV_Tasks;
        private readonly IRepository<RCS_UserTasks> _rcs_RCS_UserTasks;
        private readonly IRepository<RCS_IODevices> _rcs_IODevices;
        private readonly IRepository<RCS_IOSignals> _rcs_IOSignals;
        private readonly IRepository<RCS_Locations> _rcs_Locations;
        private readonly TrafficControlService _trafficControlService;
        static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        public AciDataEventHandle(AciAppManager aciAppManager, IRepository<RCS_UserTasks> rcs_RCS_UserTasks, IRepository<NdcTask_Moves, Guid> NdcTask, ILogger<AciDataEventHandle> logger, IRepository<RCS_IOAGV_Tasks> rcs_IOAGV_Tasks, IRepository<RCS_IODevices> rcs_IODevices, IRepository<RCS_IOSignals> rcs_IOSignals, IRepository<RCS_Locations> rcs_Locations, TrafficControlService trafficControlService = null)
        {
            _aciAppManager = aciAppManager;
            _ndcTask = NdcTask;
            _logger = logger;
            _rcs_IOAGV_Tasks=rcs_IOAGV_Tasks;
            _rcs_IODevices = rcs_IODevices;
            _rcs_IOSignals= rcs_IOSignals;
            _rcs_Locations=rcs_Locations;
            _rcs_RCS_UserTasks=rcs_RCS_UserTasks;
            _trafficControlService = trafficControlService;
        }

        /// <summary>
        /// 处理ACI事件的主要方法
        /// </summary>
        /// <param name="e">ACI数据事件参数</param>
        /// <remarks>
        /// 使用信号量确保事件处理的线程安全
        /// 根据事件类型分发到不同的处理方法
        /// </remarks>
        [UnitOfWork]
        public async Task HandleEventAsync(AciDataEventArgs e)
        {
            await _semaphore.WaitAsync();

            try
            {
                switch (e.AciData.DataType)
                {
                    case MessageType.OrderEvent:
                        Task.WaitAll(HandleOrderEvent(e));
                        break;
                }
            }
            finally
            {
                _semaphore.Release();
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理订单事件
        /// </summary>
        /// <param name="e">ACI数据事件参数</param>
        /// <remarks>
        /// 解析订单事件数据并根据不同的事件类型进行相应处理
        /// 维护事件历史记录
        /// </remarks>
        private async Task HandleOrderEvent(AciDataEventArgs e)
        {
            try
            {
                OrderEventAciData? data = e.AciData as OrderEventAciData;
                AciEvent ev = new AciEvent()
                {
                    Type = (AciHostEventTypeEnum)data.MagicCode1,
                    Parameter1 = data.MagicCode2,
                    Parameter2 = data.MagicCode3,
                    Index = data.OrderIndex
                };
                var ndcTask = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2);
                switch (ev.Type)
                {
                    case AciHostEventTypeEnum.OrderStart:
                        
                        try
                        {
                            await HandleOrderStartEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理订单开始事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.ParameterCheck:
                        try
                        {
                            _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.Confirm, 0, 0);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理参数检查事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.MoveToLoad:
                        try
                        {
                            await HandleMoveToLoadEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理移动到装货点事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.LoadHostSyncronisation:
                        try
                        {
                            await HandleLoadHostSyncronisationEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理装货同步事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.LoadingHostSyncronisation:
                        try
                        {
                            await HandleLoadingHostSyncronisationEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理装货完成同步事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.UnloadHostSyncronisation:
                        try
                        {
                            await HandleUnloadHostSyncronisationEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理卸货同步事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.UnloadingHostSyncronisation:
                        try
                        {
                            await HandleUnloadingHostSyncronisationEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理卸货完成同步事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.OrderFinish:
                        try
                        {
                            await HandleOrderFinishEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理订单完成事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.End:
                        try
                        {
                            await HandleEndEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理结束事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.CancelRequest:
                        try
                        {
                            await HandleCancelRequestEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理取消请求事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.Cancel:
                        try
                        {
                            if (ev.Parameter2 == (int)TaskStatuEnum.CarWash)
                            {
                                _aciAppManager.SendHostAcknowledge(null, ev.Index, 255, 0, 0);
                                break;
                            }
                            _aciAppManager.SendHostAcknowledge(null, ev.Index, 255, 0, 0);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理取消事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.CarrierConnected:
                        try
                        {
                            // 当前没有实现的方法
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理载具连接事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.Redirect:
                        try
                        {
                            _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmWashing, 400, 0);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理重定向事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.OrderTransform:
                        try
                        {
                            await HandleOrderTransformEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理订单转换事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.InvalidDeliverStation:
                        try
                        {
                            await HandleInvalidDeliverStationEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理无效卸货站点事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.OrderCancel:
                        try
                        {
                            await HandleOrderCancelEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理订单取消事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.OrderAgv:
                        try
                        {
                            await HandleOrderAgvEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理订单AGV事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.RedirectRequestFetch:
                        try
                        {
                            await HandleRedirectRequestFetchEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理重定向请求取货事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.RedirectOrNot:
                        try
                        {
                            await HandleRedirectOrNotEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理是否重定向事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.CarWashRequest:
                        try
                        {
                            _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmUnknown, 1160, 0);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理洗车请求事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.ResetStart:
                        try
                        {
                            _logger.LogCritical($"ResetStart任务编号{ev.Index}系统重启。");
                            await HandleResetStartEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理重启开始事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.ResetStart2:
                        try
                        {
                            _logger.LogCritical($"ResetStart2任务编号{ev.Index}系统重启。");
                            await HandleResetStartEvent(ev);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理重启开始2事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    // 添加交管相关事件处理
                    case AciHostEventTypeEnum.AGVInRegion:
                        try
                        {
                            _logger.LogInformation($"接收到AGV进入区域事件: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            
                            // 检查区域ID是否有效
                            if (ev.Parameter1 <= 0)
                            {
                                _logger.LogWarning($"AGV进入区域事件的区域ID无效: {ev.Parameter1}");
                                break;
                            }
                            
                            // 如果交管服务不可用，则直接使用HTTP请求
                            if (_trafficControlService == null)
                            {
                                _logger.LogWarning("交管服务不可用，将直接发送HTTP请求");
                                bool result = await SendInOrOutRegion(ev.Parameter1, true);
                                if (result)
                                {
                                    _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVInRegion, 0, 0);
                                    _logger.LogInformation($"已发送AGV进入区域确认消息: 订单索引={ev.Index}");
                                }
                                break;
                            }
                            
                            // 创建交管任务
                            Guid taskId = await _trafficControlService.CreateTrafficControlTask(ev.Parameter1, true, ev.Index);
                            _logger.LogInformation($"创建AGV进入区域交管任务成功: 任务ID={taskId}");
                            
                            // 等待任务处理结果
                            int maxRetries = 10;
                            int retryCount = 0;
                            bool taskCompleted = false;
                            
                            while (retryCount < maxRetries && !taskCompleted)
                            {
                                // 查询任务状态
                                int status = await _trafficControlService.GetTrafficControlTaskStatus(taskId);
                                
                                if (status == 2) // 任务已完成
                                {
                                    taskCompleted = true;
                                    _logger.LogInformation($"AGV进入区域交管任务已完成: 任务ID={taskId}");
                                    
                                    // 发送确认消息给NDC
                                    _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVInRegion, 0, 0);
                                    _logger.LogInformation($"已发送AGV进入区域确认消息: 订单索引={ev.Index}");
                                }
                                else if (status == 3) // 任务失败
                                {
                                    _logger.LogWarning($"AGV进入区域交管任务失败: 任务ID={taskId}");
                                    break;
                                }
                                
                                // 如果任务未完成，等待一段时间后重试
                                if (!taskCompleted)
                                {
                                    retryCount++;
                                    await Task.Delay(500); // 等待500毫秒
                                }
                            }
                            
                            if (!taskCompleted)
                            {
                                _logger.LogWarning($"AGV进入区域交管任务未能在规定时间内完成: 任务ID={taskId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理AGV进入区域事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.AGVOutRegion:
                        try
                        {
                            _logger.LogInformation($"接收到AGV离开区域事件: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            
                            // 检查区域ID是否有效
                            if (ev.Parameter1 <= 0)
                            {
                                _logger.LogWarning($"AGV离开区域事件的区域ID无效: {ev.Parameter1}");
                                break;
                            }
                            
                            // 如果交管服务不可用，则直接使用HTTP请求
                            if (_trafficControlService == null)
                            {
                                _logger.LogWarning("交管服务不可用，将直接发送HTTP请求");
                                bool result = await SendInOrOutRegion(ev.Parameter1, false);
                                if (result)
                                {
                                    _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVOutRegion, 0, 0);
                                    _logger.LogInformation($"已发送AGV离开区域确认消息: 订单索引={ev.Index}");
                                }
                                break;
                            }
                            
                            // 创建交管任务
                            Guid taskId = await _trafficControlService.CreateTrafficControlTask(ev.Parameter1, false, ev.Index);
                            _logger.LogInformation($"创建AGV离开区域交管任务成功: 任务ID={taskId}");
                            
                            // 等待任务处理结果
                            int maxRetries = 10;
                            int retryCount = 0;
                            bool taskCompleted = false;
                            
                            while (retryCount < maxRetries && !taskCompleted)
                            {
                                // 查询任务状态
                                int status = await _trafficControlService.GetTrafficControlTaskStatus(taskId);
                                
                                if (status == 2) // 任务已完成
                                {
                                    taskCompleted = true;
                                    _logger.LogInformation($"AGV离开区域交管任务已完成: 任务ID={taskId}");
                                    
                                    // 发送确认消息给NDC
                                    _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVOutRegion, 0, 0);
                                    _logger.LogInformation($"已发送AGV离开区域确认消息: 订单索引={ev.Index}");
                                }
                                else if (status == 3) // 任务失败
                                {
                                    _logger.LogWarning($"AGV离开区域交管任务失败: 任务ID={taskId}");
                                    break;
                                }
                                
                                // 如果任务未完成，等待一段时间后重试
                                if (!taskCompleted)
                                {
                                    retryCount++;
                                    await Task.Delay(500); // 等待500毫秒
                                }
                            }
                            
                            if (!taskCompleted)
                            {
                                _logger.LogWarning($"AGV离开区域交管任务未能在规定时间内完成: 任务ID={taskId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理AGV离开区域事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.AGVRegionStatusUpdate:
                        try
                        {
                            _logger.LogInformation($"接收到AGV区域状态更新事件: 区域ID={ev.Parameter1}, 状态={ev.Parameter2}, 订单索引={ev.Index}");
                            
                            // 这里可以添加更新区域状态的逻辑
                            
                            // 发送确认消息给NDC
                            _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVRegionStatusUpdate, 0, 0);
                            _logger.LogInformation($"已发送AGV区域状态更新确认消息: 订单索引={ev.Index}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"处理AGV区域状态更新事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                }

                if (ev.Type != AciHostEventTypeEnum.HostSync)
                {
                    try
                    {
                        _aciAppManager.AciEventAdd(ev);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"添加ACI事件历史记录时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"HandleOrderEvent整体处理出现异常: {ex.Message}, 堆栈: {ex.StackTrace}");
            }
        }

        // ... 现有的处理方法 ...

        /// <summary>
        /// 发送进入或离开区域的请求
        /// </summary>
        /// <param name="regionId">区域ID</param>
        /// <param name="IsIn">是否为进入区域</param>
        /// <returns>处理结果</returns>
        private async Task<bool> SendInOrOutRegion(int regionId, bool IsIn = true)
        {
            bool result = false;
            string testUrl = "";
            try
            {
                if (IsIn)
                {
                    var url = $"{RCS.RCSModule.ApiRespondUrl}apply_area_token?agv_no=\"0\"&crossing_no=\"{regionId}\"";
                    testUrl = url;
                    _logger.LogInformation($"发送AGV进入区域请求: {url}");
                    var respond = await HttpRequest.GetAsync(url);
                    testUrl = url+":Test";
                    if (string.IsNullOrEmpty(respond) || !respond.Equals("1"))
                    {
                        _logger.LogDebug($"上报进入区域{regionId}失败,返回结果为:{respond}");
                        Console.WriteLine($"上报进入区域{regionId}失败,返回结果为:{respond}");
                        return false;
                    }
                    else if (respond.Equals("1"))
                    {
                        _logger.LogInformation($"上报进入区域{regionId}成功");
                        return true;
                    }
                }
                else
                {
                    var url = $"{RCS.RCSModule.ApiRespondUrl}free_area_token?agv_no=\"0\"&crossing_no=\"{regionId}\"";
                    testUrl = url;
                    _logger.LogInformation($"发送AGV离开区域请求: {url}");
                    var respond = await HttpRequest.GetAsync(url);
                    testUrl = url+":Test2";

                    if (string.IsNullOrEmpty(respond) || !respond.Equals("1"))
                    {
                        _logger.LogDebug($"上报离开区域{regionId}失败,返回结果为:{respond}");
                        Console.WriteLine($"上报离开区域{regionId}失败,返回结果为:{respond}");
                        return false;
                    }
                    else if (respond.Equals("1"))
                    {
                        _logger.LogInformation($"上报离开区域{regionId}成功");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"SendInOrOutRegion异常: URL={testUrl}, 错误={ex.Message}, 堆栈={ex.StackTrace}");
                Console.WriteLine($"SendInOrOutRegion异常: {ex.Message}");
            }
            return false;
        }
    }
} 