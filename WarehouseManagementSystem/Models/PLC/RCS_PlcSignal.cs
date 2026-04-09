using System;

namespace WarehouseManagementSystem.Models.PLC
{
    /// <summary>
    /// PLC信号实体类
    /// </summary>
    public class RCS_PlcSignal
    {
        /// <summary>
        /// 信号ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 关联的PLC设备ID
        /// </summary>
        public string PlcDeviceId { get; set; }

        /// <summary>
        /// 数据类型(Bool/Int/String)
        /// </summary>
        public string DataType { get; set; }

        /// <summary>
        /// 偏移量
        /// </summary>
        public string Offset { get; set; }

        /// <summary>
        /// 信号名称/后缀序号描述
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 写入方(AGV/PLC)
        /// </summary>
        public string Writer { get; set; }

        /// <summary>
        /// 当前值
        /// </summary>
        public string CurrentValue { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }


        public string PLCTypeDb { get; set; }
    }
} 