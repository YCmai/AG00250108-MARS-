using System;
using System.Collections.Generic;

namespace WarehouseManagementSystem.Models.PLC
{
    /// <summary>
    /// PLC设备实体类
    /// </summary>
    public class RCS_PlcDevice
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// 端口
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 品牌（欧姆龙、西门子）
        /// </summary>
        public string Brand { get; set; }

        /// <summary>
        /// 对应的工位点
        /// </summary>
        public string StationPoint { get; set; }

        /// <summary>
        /// 信号请求点
        /// </summary>
        public string SignalRequestPoint { get; set; }

        /// <summary>
        /// 离开复位点
        /// </summary>
        public string LeaveResetPoint { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// 模块地址
        /// </summary>
        public string ModuleAddress { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }
        
        /// <summary>
        /// 相关联的PLC信号列表
        /// </summary>
        public List<RCS_PlcSignal> Signals { get; set; } = new List<RCS_PlcSignal>();
    }
} 