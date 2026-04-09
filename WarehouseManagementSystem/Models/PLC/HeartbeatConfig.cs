using System.Collections.Generic;

namespace WarehouseManagementSystem.Models.PLC
{
    /// <summary>
    /// 心跳配置类
    /// </summary>
    public class HeartbeatConfig
    {
        /// <summary>
        /// 心跳设备配置列表
        /// </summary>
        public static readonly List<HeartbeatDeviceConfig> Devices = new List<HeartbeatDeviceConfig>
        {
            new HeartbeatDeviceConfig { IpAddress = "192.168.40.1", ModuleAddress = "DB126" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.40.2", ModuleAddress = "DB126" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.60.180", ModuleAddress = "" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.51.210", ModuleAddress = "" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.51.101", ModuleAddress = "DB60" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.51.15", ModuleAddress = "DB10000" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.51.51", ModuleAddress = "DB10000" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.51.151", ModuleAddress = "DB10000" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.60.51", ModuleAddress = "DB210" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.60.100", ModuleAddress = "DB513" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.60.21", ModuleAddress = "DB230" },
            new HeartbeatDeviceConfig { IpAddress = "192.168.60.191", ModuleAddress = "DB100" }
        };
    }

    /// <summary>
    /// 心跳设备配置
    /// </summary>
    public class HeartbeatDeviceConfig
    {
        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// 模块地址
        /// </summary>
        public string ModuleAddress { get; set; }
    }
} 