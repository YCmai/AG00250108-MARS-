using Client100.Entity;

using WarehouseManagementSystem.Models.PLC;

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC信号服务接口
    /// </summary>
    public interface IPlcSignalService
    {
        Task<List<RCS_PlcDevice>> GetAllPlcDevicesAsync();
        Task<RCS_PlcDevice> GetPlcDeviceByIdAsync(int id);
        Task<int> AddPlcDeviceAsync(RCS_PlcDevice device);
        Task UpdatePlcDeviceAsync(RCS_PlcDevice device);
        Task DeletePlcDeviceAsync(int id);
        
        /// <summary>
        /// 获取所有PLC信号
        /// </summary>
        Task<List<RCS_PlcSignal>> GetAllPlcSignalsAsync();

        /// <summary>
        /// 根据设备ID获取PLC信号
        /// </summary>
        Task<List<RCS_PlcSignal>> GetPlcSignalsByDeviceIdAsync(string deviceId);

        Task<RCS_PlcSignal> GetPlcSignalByIdAsync(int id);
        Task<int> AddPlcSignalAsync(RCS_PlcSignal signal);
        Task UpdatePlcSignalAsync(RCS_PlcSignal signal);
        Task DeletePlcSignalAsync(int id);

        /// <summary>
        /// 重置PLC信号
        /// </summary>
        Task ResetPlcSignalAsync(int signalId);

        /// <summary>
        /// 手动触发PLC信号
        /// </summary>
        Task ManualTriggerSignalAsync(int signalId, bool value);


        Task<AutoPlcTask>GetAutoTask(string PlcType,string PLCTypeDb,string Signal,int Status);


        Task UpdateAutoTask(int Id);

    }
} 