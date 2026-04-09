using WarehouseManagementSystem.Models.PLC;

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC通信服务接口
    /// </summary>
    public interface IPlcCommunicationService
    {
        /// <summary>
        /// 启动所有已启用PLC设备的通信服务
        /// </summary>
        Task StartServiceAsync();

        /// <summary>
        /// 停止所有PLC设备的通信服务
        /// </summary>
        Task StopServiceAsync();

        /// <summary>
        /// 重启所有已启用PLC设备的通信服务
        /// </summary>
        Task RestartServiceAsync();

        /// <summary>
        /// 向指定PLC设备写入信号值
        /// </summary>
        /// <param name="deviceId">PLC设备ID</param>
        /// <param name="signalId">信号ID</param>
        /// <param name="value">要写入的值</param>
        Task WriteSignalValueAsync(int deviceId, int signalId, object value);


        /// <summary>
        /// 心跳服务的写入
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="signalId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        Task WriteSignalHeatValueAsync(int deviceId, int signalId, object value);

        /// <summary>
        /// 手动触发读取指定PLC设备的所有信号
        /// </summary>
        /// <param name="deviceId">PLC设备ID</param>
        Task ManualReadSignalsAsync(int deviceId);

        /// <summary>
        /// 获取PLC通信服务的状态信息
        /// </summary>
        /// <returns>服务状态信息</returns>
        Task<Dictionary<int, bool>> GetServiceStatusAsync();
        
        /// <summary>
        /// 重置服务锁状态，解决可能的信号量问题
        /// </summary>
        Task ResetServiceLockAsync();
  
    }
} 