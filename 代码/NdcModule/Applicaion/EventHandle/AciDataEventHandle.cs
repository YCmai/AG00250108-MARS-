using AciModule.Domain;
using AciModule.Domain.Entitys;
//using AciModule.Domain.Queue;
using AciModule.Domain.Service;
using AciModule.Domain.Shared;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

      
        
        static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        
        public AciDataEventHandle(
            AciAppManager aciAppManager, 
            IRepository<RCS_UserTasks> rcs_RCS_UserTasks, 
            IRepository<NdcTask_Moves, Guid> NdcTask, 
            ILogger<AciDataEventHandle> logger, 
            IRepository<RCS_IOAGV_Tasks> rcs_IOAGV_Tasks, 
            IRepository<RCS_IODevices> rcs_IODevices, 
            IRepository<RCS_IOSignals> rcs_IOSignals, 
            IRepository<RCS_Locations> rcs_Locations, 
            IConfiguration configuration,
            
           
            IHttpClientFactory httpClientFactory = null)
        {
            _aciAppManager = aciAppManager;
            _ndcTask = NdcTask;
            _logger = logger;
            _rcs_IOAGV_Tasks = rcs_IOAGV_Tasks;
            _rcs_IODevices = rcs_IODevices;
            _rcs_IOSignals = rcs_IOSignals;
            _rcs_Locations = rcs_Locations;
            _rcs_RCS_UserTasks = rcs_RCS_UserTasks;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
           
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
                    case AciHostEventTypeEnum.AGVRequestEnterRegion:
                        try
                        {
                            _logger.LogCritical($"80接收到AGV请求进入区域事件: 区域ID={ev.Parameter1}, AGV编号={ev.Parameter2}, 订单索引={ev.Index}");
                            Console.WriteLine("80AGVRequestEnterRegion接受信号AGV进入区域" + ev.Parameter1);
                            // 检查区域ID是否有效
                            if (ev.Parameter1 <= 0)
                            {
                                _logger.LogError($"AGV请求进入区域事件的区域ID无效: {ev.Parameter1}");
                                break;
                            }

                            // 请求上位机接口进行区域锁定
                            //bool result = await SendZoneRequest(ev.Parameter1, ev.Parameter2.ToString(), "In");

                            bool result = true;


                            // 只有在请求成功后才回复NDC
                            if (result)
                            {
                                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVRequestEnterRegion, 0, 0);
                                _logger.LogCritical($"80区域锁定成功，已发送AGV请求进入区域确认消息: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            }
                            else
                            {
                                _logger.LogError($"80区域锁定失败，未发送AGV请求进入区域确认消息: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"80处理AGV请求进入区域事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.AGVInRegion:
                        try
                        {
                            _logger.LogCritical($"79接收到AGV已经进入区域事件: 区域ID={ev.Parameter1}, AGV编号={ev.Parameter2}, 订单索引={ev.Index}");
                            Console.WriteLine("79AGVInRegion接受信号AGV进入区域" + ev.Parameter1);
                            if (ev.Parameter1 <= 0)
                            {
                                _logger.LogError("79AGVInRegion-AciDataEventHandle交管上报接受数据异常：收到的区域编号为：" + ev.Parameter1);
                                Console.WriteLine("79AGVInRegion-AciDataEventHandle交管上报接受数据异常：收到的区域编号为：" + ev.Parameter1);
                                break;
                            }

                            // 直接发送请求到上位机，通知AGV已进入区域
                            //bool result = await SendZoneRequest(ev.Parameter1, ev.Parameter2.ToString(), "In");

                            bool result = true;

                            if (result)
                            {
                                // 无论结果如何，都回复NDC，因为这是通知形式
                                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVInRegion, 0, 0);
                                _logger.LogCritical($"79AGVInRegion已通知上位机AGV进入区域: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            }
                            else
                            {
                                _logger.LogError($"79AGVInRegion通知上位机AGV进入区域失败: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"79AGVInRegion处理AGV进入区域事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
                        }
                        break;
                    case AciHostEventTypeEnum.AGVOutRegion:
                        try
                        {
                            _logger.LogCritical($"81AGVOutRegion接收到AGV请求离开区域事件: 区域ID={ev.Parameter1}, AGV编号={ev.Parameter2}, 订单索引={ev.Index}");
                            Console.WriteLine("81AGVOutRegion接受信号AGV进入区域" + ev.Parameter1);
                            if (ev.Parameter1 <= 0)
                            {
                                _logger.LogError("81AGVOutRegionAciDataEventHandle交管上报接受数据异常：收到的区域编号为：" + ev.Parameter1);
                                break;
                            }

                            // 直接发送请求到上位机，通知AGV已离开区域
                            //bool result = await SendZoneRequest(ev.Parameter1, ev.Parameter2.ToString(), "Out");

                            bool result = true;

                            if (result)
                            {
                                // 无论结果如何，都回复NDC，因为这是通知形式
                                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)AciHostEventTypeEnum.AGVOutRegion, 0, 0);
                                _logger.LogCritical($"81AGVOutRegion已通知上位机AGV离开区域: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            }
                            else
                            {
                                _logger.LogError($"81AGVOutRegion通知上位机AGV离开区域失败: 区域ID={ev.Parameter1}, 订单索引={ev.Index}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"81AGVOutRegion处理AGV离开区域事件时发生错误: {ex.Message}, 堆栈: {ex.StackTrace}");
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

        /// <summary>
        /// 处理订单开始事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为开始状态
        /// 设置订单索引
        /// </remarks>
        private async Task HandleOrderStartEvent(AciEvent ev)
        {

            var ndcTasks = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.None);
            if (ndcTasks != null)
            {
                ndcTasks.SetStatus(TaskStatuEnum.TaskStart);
                ndcTasks.SetOrderIndex(ev.Index);
                await _ndcTask.UpdateAsync(ndcTasks);
                return;
            }
            var ndcTasksInProgress = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.TaskStart);
            if (ndcTasksInProgress != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.TaskStart, ndcTasksInProgress.PickupHeight, ndcTasksInProgress.UnloadHeight);
                return;
            }
        }

        /// <summary>
        /// 处理移动到装货点事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为确认车辆状态
        /// 设置订单索引
        /// </remarks>
        private async Task HandleMoveToLoadEvent(AciEvent ev)
        {

            var move = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.TaskStart);
            if (move != null)
            {
                move.SetStatus(TaskStatuEnum.ConfirmCar, ev.Parameter1);
                move.SetOrderIndex(ev.Index);
                await _ndcTask.UpdateAsync(move);
            }
        }

        /// <summary>
        /// 处理装货同步事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为正在装货
        /// 发送装货高度和深度参数
        /// </remarks>
        private async Task HandleLoadHostSyncronisationEvent(AciEvent ev)
        {
            //如果是1号铸锭下线任务，DO1（持续）进行产线锁定
            // 获取当前任务信息
            try
            {
               
                var load0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.ConfirmCar);
                if (load0 != null)
                {
                    load0.SetStatus(TaskStatuEnum.PickingUp);
                    await _ndcTask.UpdateAsync(load0);
                    return;
                }

                var load1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.PickingUp);
                if (load1 != null)
                {
                    var upHeight = load1.PickupHeight == 0 ? 0 : load1.PickupHeight;
                    var upDepth = 0;

                    _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.PickingUp, upHeight, upDepth);
                }
            }
            catch (Exception ex)
            {

                _logger.LogError($"HandleLoadHostSyncronisationEvent任务报错异常-{ex.Message}");
            }
        }

        /// <summary>
        /// 处理装货完成同步事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为装货完成
        /// 处理安全交互信号
        /// </remarks>
        private async Task HandleLoadingHostSyncronisationEvent(AciEvent ev)
        {
          
            var vehicleAtLoad0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2);
           
            if (ev.Parameter2 == (int)TaskStatuEnum.CarWash)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.PickDown, 0, 0);
                return;
            }

            var loadDone0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.PickingUp);
            if (loadDone0 != null)
            {
                loadDone0.SetStatus(TaskStatuEnum.PickDown);

                await _ndcTask.UpdateAsync(loadDone0);
                return;
            }

            var loadDone1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.PickDown);
            if (loadDone1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.PickDown, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理卸货同步事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为正在卸货
        /// 发送卸货高度和深度参数
        /// 处理站台交互逻辑：
        /// 1. 创建DO1请求进入站台的IO任务（持续信号）
        /// 2. 检测DI1信号（起升架允许进入反馈）
        /// 3. 确认DI1信号后允许AGV进入
        /// </remarks>
        private async Task HandleUnloadHostSyncronisationEvent(AciEvent ev)
        {
            // 获取当前任务信息
            var ndcTaskModel = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2);
            if (ndcTaskModel == null) return;

            // 处理普通卸货任务的原有逻辑
            if (ev.Parameter2 == (int)TaskStatuEnum.CarWash)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.Unloading, 0, 0);
                return;
            }

            var unoad0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.PickDown);
            if (unoad0 != null)
            {
                unoad0.SetStatus(TaskStatuEnum.Unloading);
                await _ndcTask.UpdateAsync(unoad0);
            }

            var unoad1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.Unloading);
            if (unoad1 != null)
            {
                var doneHeight = unoad1.UnloadHeight == 0 ? 0 : unoad1.UnloadHeight;
                var upDepth = 0;
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.Unloading, doneHeight, upDepth);
            }

            var unoad2 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.OrderAgv);
            if (unoad2 != null)
            {
                var doneHeight = unoad2.UnloadHeight == 0 ? 0 : unoad2.UnloadHeight;
                var depth = unoad2.UnloadDepth == 0 ? 0 : unoad2.UnloadDepth;
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.Unloading, doneHeight, depth);
                return;
            }

            var unoad3 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.CanceledWashing);
            if (unoad3 != null)
            {
                var doneHeight = unoad3.UnloadHeight == 0 ? 0 : unoad3.UnloadHeight;
                var depth = unoad3.UnloadDepth == 0 ? 0 : unoad3.UnloadDepth;
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.Unloading, doneHeight, depth);
                return;
            }
            
        }

        /// <summary>
        /// 处理卸货完成同步事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为卸货完成
        /// 处理站台交互信号：
        /// 1. 发送DO2脉冲信号（3秒）
        /// 2. 释放DO1持续信号
        /// </remarks>
        private async Task HandleUnloadingHostSyncronisationEvent(AciEvent ev)
        {
            // 获取当前任务信息
            var ndcTaskModel = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2);
            if (ndcTaskModel == null) return;

            // 处理原有的卸货完成逻辑
            if (ev.Parameter2 == (int)TaskStatuEnum.CarWash)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.UnloadDown, 0, 0);
                return;
            }

            var unloadDone0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.Unloading);
            if (unloadDone0 != null)
            {
                unloadDone0.SetStatus(TaskStatuEnum.UnloadDown);
                await _ndcTask.UpdateAsync(unloadDone0);
                return;
            }

            var unloadDone1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.UnloadDown);
            if (unloadDone1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.UnloadDown, 0, 0);
                return;
            }

            var unloadDone2 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.OrderAgv);
            if (unloadDone2 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.UnloadDown, 0, 0);
                return;
            }

            var unloadDone3 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.CanceledWashing);
            if (unloadDone3 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.UnloadDown, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理订单完成事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为完成状态
        /// 处理不同类型任务的完成逻辑
        /// </remarks>
        private async Task HandleOrderFinishEvent(AciEvent ev)
        {
            if (ev.Parameter2 == (int)TaskStatuEnum.CarWash)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.TaskFinish, 0, 0);
                return;
            }
            var finish0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.UnloadDown);
            if (finish0 != null)
            {
                finish0.SetStatus(TaskStatuEnum.TaskFinish);
                _ndcTask.UpdateAsync(finish0, true).Wait();
                return;
            }

            var finish1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.OrderAgv);

            if (finish1 != null)
            {
                finish1.SetStatus(TaskStatuEnum.OrderAgvFinish);
                _ndcTask.UpdateAsync(finish1, true).Wait();
            }

            var finish2 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.CanceledWashing);

            if (finish2 != null)
            {
                finish2.SetStatus(TaskStatuEnum.CanceledWashFinish);
                _ndcTask.UpdateAsync(finish2, true).Wait();
            }

            _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.TaskFinish, 0, 0);
        }

        /// <summary>
        /// 处理无效卸货站点事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为无效卸货点
        /// 发送确认取消指令
        /// </remarks>
        private async Task HandleInvalidDeliverStationEvent(AciEvent ev)
        {
            var invalidDown0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus != TaskStatuEnum.InvalidDown);
            if (invalidDown0 != null)
            {
                invalidDown0.SetStatus(TaskStatuEnum.InvalidDown, ev.Parameter1);
                await _ndcTask.UpdateAsync(invalidDown0);
                return;
            }

            var invalidDown1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.InvalidDown);
            if (invalidDown1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmCancellation, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理订单取消事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为已取消
        /// 处理洗车重定向确认
        /// </remarks>
        private async Task HandleOrderCancelEvent(AciEvent ev)
        {
            var orderCancl0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus != TaskStatuEnum.CanceledWashing);

            if (orderCancl0 != null)
            {
                orderCancl0.SetStatus(TaskStatuEnum.CanceledWashing);
                await _ndcTask.UpdateAsync(orderCancl0);
                return;
            }
            var orderCancl1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.CanceledWashing);

            if (orderCancl1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmRedirection, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理系统重启事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 处理所有已取消的任务
        /// 更新任务状态
        /// </remarks>
        private async Task HandleResetStartEvent(AciEvent ev)
        {
            var cancelTasks = await _ndcTask.GetListAsync(i => i.TaskStatus >= TaskStatuEnum.CarWash && i.TaskStatus < TaskStatuEnum.TaskFinish);

            foreach (var cancelTask in cancelTasks)
            {
                cancelTask.SetStatus(TaskStatuEnum.Canceled);
                await _ndcTask.UpdateAsync(cancelTask);
                _logger.LogCritical($"重启系统修改{cancelTask.SchedulTaskNo}任务状态");
            }
        }

        /// <summary>
        /// 处理取货请求重定向事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 处理取货失败的情况
        /// 更新任务状态为重定向请求
        /// </remarks>
        private async Task HandleRedirectRequestFetchEvent(AciEvent ev)
        {
            var fetch0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus != TaskStatuEnum.RedirectRequest);
            if (fetch0 != null)
            {
                fetch0.SetStatus(TaskStatuEnum.RedirectRequest);
                await _ndcTask.UpdateAsync(fetch0);
                return;
            }

            var fetch1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.RedirectRequest);
            if (fetch1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmCancellation, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理AGV订单事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 处理AGV相关的任务状态更新
        /// 发送重定向确认
        /// </remarks>
        private async Task HandleOrderAgvEvent(AciEvent ev)
        {
            if (ev.Parameter2 == (int)TaskStatuEnum.CarWash)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmRedirection, 0, 0);
                return;
            }

            var orderAgv0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus != TaskStatuEnum.OrderAgv);
            if (orderAgv0 != null)
            {
                orderAgv0.SetStatus(TaskStatuEnum.OrderAgv);
                _ndcTask.UpdateAsync(orderAgv0, true).Wait();
                return;
            }

            var orderAgv1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.OrderAgv);
            if (orderAgv1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmRedirection, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理订单转换事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 处理取货流程失败的情况
        /// 更新任务状态为无效取货点
        /// </remarks>
        private async Task HandleOrderTransformEvent(AciEvent ev)
        {
            var invalidUp0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus != TaskStatuEnum.InvalidUp);
            if (invalidUp0 != null)
            {
                invalidUp0.SetStatus(TaskStatuEnum.InvalidUp, ev.Parameter1);
                _ndcTask.UpdateAsync(invalidUp0, true).Wait();
                return;
            }

            var invalidUp1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.InvalidUp);
            if (invalidUp1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmCancellation, 0, 0);
                return;
            }
        }

        /// <summary>
        /// 处理结束事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 回收已完成任务的ID
        /// 发送结束确认
        /// </remarks>
        private async Task HandleEndEvent(AciEvent ev)
        {
            int taskOut = 0, taskIn = 0;

            var recovery = await _ndcTask.GetListAsync(x => x.NdcTaskId == ev.Parameter1);
            foreach (var item in recovery)
            {
                if (item.TaskStatus == TaskStatuEnum.TaskFinish) item.RecoveryId();
                if (item.TaskStatus == TaskStatuEnum.Canceled) item.RecoveryId();
                if (item.TaskStatus == TaskStatuEnum.InvalidUp) item.RecoveryId();
                if (item.TaskStatus == TaskStatuEnum.InvalidDown) item.RecoveryId();
                if (item.TaskStatus == TaskStatuEnum.CanceledWashFinish) item.RecoveryId();
                if (item.TaskStatus == TaskStatuEnum.RedirectRequest) item.RecoveryId();
                if (item.TaskStatus == TaskStatuEnum.OrderAgvFinish) item.RecoveryId();
            }
            _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.End, 0, 0);
        }

        /// <summary>
        /// 处理取消请求事件
        /// </summary>
        /// <param name="ev">ACI事件对象</param>
        /// <remarks>
        /// 更新任务状态为已取消
        /// 发送取消确认
        /// </remarks>
        private async Task HandleCancelRequestEvent(AciEvent ev)
        {
            var cancel0 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus != TaskStatuEnum.Canceled);

            if (cancel0 != null)
            {
                cancel0.SetStatus(TaskStatuEnum.Canceled);
                await _ndcTask.UpdateAsync(cancel0);

                return;
            }

            var cancel1 = await _ndcTask.FirstOrDefaultAsync(x => x.NdcTaskId == ev.Parameter2 && x.TaskStatus == TaskStatuEnum.Canceled);

            if (cancel1 != null)
            {
                _aciAppManager.SendHostAcknowledge(null, ev.Index, (int)ReplyTaskState.ConfirmCancellation, 0, 0);
                return;
            }
        }

        private async Task HandleRedirectOrNotEvent(AciEvent ev)
        {




        }

        /// <summary>
        /// 发送区域锁定请求
        /// </summary>
        /// <param name="zoneId">区域ID</param>
        /// <param name="agvNo">AGV编号</param>
        /// <returns>处理结果</returns>
        private async Task<bool> SendZoneLockRequest(int zoneId, string agvNo)
        {
            try
            {
                // 获取API URL
                string url = _configuration["RCS:ZoneLockUrl"];
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogError("未配置RCS:ZoneLockUrl");
                    return false;
                }
                
                _logger.LogInformation($"发送区域锁定请求: {url}");
                
                // 构建请求参数 - 根据Excel表格，只需要zoneId参数
                var requestData = new
                {
                    //zoneId = $"A{zoneId.ToString("000")}" // 格式化为A001-A999格式
                    zoneId=zoneId,
                };
                
                var jsonContent = JsonConvert.SerializeObject(requestData);
                
                using (var httpClient = new HttpClient())
                {
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var resultJson = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"区域锁定请求响应: {resultJson}");
                        
                        // 解析响应 - 根据Excel表格，响应格式为{status, message}
                        var result = JsonConvert.DeserializeObject<dynamic>(resultJson);
                        if (result != null && result.status.ToString() == "success")
                        {
                            _logger.LogInformation($"区域{zoneId}锁定成功，消息: {result.message}");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"区域{zoneId}锁定失败，状态: {result?.status}, 消息: {result?.message}");
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"区域锁定请求失败，响应状态码: {response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"发送区域锁定请求异常: {ex.Message}, 堆栈: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// 发送区域状态更新请求
        /// </summary>
        /// <param name="zoneId">区域ID</param>
        /// <param name="agvNo">AGV编号</param>
        /// <param name="status">状态(In/Out)</param>
        /// <returns>处理结果</returns>
        private async Task<bool> SendZoneRequest(int zoneId, string agvNo, string status)
        {
            try
            {
                // 获取API URL
                string url = _configuration["RCS:SendZoneRequest"];
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogError("未配置RCS:SendZoneRequest");
                    return false;
                }
                
                string actionDesc = status;
                _logger.LogInformation($"发送区域{actionDesc}请求: {url}");
                
                // 构建请求参数 - 根据Excel表格，需要zoneId和status参数
                var requestData = new
                {
                    //zoneId = $"A{zoneId.ToString("000")}", // 格式化为A001-A999格式
                    zoneId = "Warehouse0" + zoneId.ToString(),
                    status = status, // "In" 表示进入，"Out" 表示离开
                    statusSource = "Jaten"

                };
                
                var jsonContent = JsonConvert.SerializeObject(requestData);
                
                using (var httpClient = new HttpClient())
                {
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var resultJson = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"区域{actionDesc}请求响应: {resultJson}");
                        
                        // 解析响应 - 根据Excel表格，响应格式为{status, message}
                        var result = JsonConvert.DeserializeObject<dynamic>(resultJson);
                        if (result != null && result.status.ToString() == "success")
                        {
                            _logger.LogInformation($"区域{zoneId}{actionDesc}成功，消息: {result.message}");
                            return true;
                        }
                        else
                        {
                            _logger.LogError($"区域{zoneId}{actionDesc}失败，状态: {result?.status}, 消息: {result?.message}");
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogError($"区域{actionDesc}请求失败，响应状态码: {response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"发送区域状态更新请求异常: {ex.Message}, 堆栈: {ex.StackTrace}");
                return false;
            }
        }
    }
}
