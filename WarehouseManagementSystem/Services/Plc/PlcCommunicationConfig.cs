using WarehouseManagementSystem.Models.PLC;

namespace WarehouseManagementSystem.Service.Plc
{
    /// <summary>
    /// PLC通信配置类
    /// </summary>
    public class PlcCommunicationConfig
    {
        /// <summary>
        /// PLC通信重试次数
        /// </summary>
        public const int MaxRetryCount = 3;

        /// <summary>
        /// PLC通信超时时间（毫秒）
        /// </summary>
        public const int ConnectionTimeout = 5000;

        /// <summary>
        /// PLC通信周期（毫秒）
        /// </summary>
        public const int CommunicationCycle = 300;

        /// <summary>
        /// PLC重连等待时间（毫秒）
        /// </summary>
        public const int ReconnectWaitTime = 1000;

        /// <summary>
        /// 西门子S7协议名称
        /// </summary>
        public const string SiemensBrand = "Siemens";

        /// <summary>
        /// 欧姆龙FINS协议名称
        /// </summary>
        public const string OmronBrand = "Omron";
    }

    /// <summary>
    /// PLC设备通信状态信息
    /// </summary>
    public class PlcDeviceStatus
    {
        /// <summary>
        /// PLC设备信息
        /// </summary>
        public RCS_PlcDevice Device { get; set; }

        /// <summary>
        /// 设备是否在线
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// 最后通信时间
        /// </summary>
        public DateTime LastCommunicationTime { get; set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 通信错误
        /// </summary>
        public string Error { get; set; }
    }
} 