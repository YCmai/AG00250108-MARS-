using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WarehouseManagementSystem.Models
{
    /// <summary>
    ///WebApi任务表
    /// </summary>
    public class RCS_ApiTask
    {
        public int ID { get; set; }

        /// <summary>
        /// 任务编号
        /// </summary>
        public string TaskCode { get; set; }

        /// <summary>
        /// 是否执行
        /// </summary>
        public bool Excute { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 上位机回复
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 任务类型
        /// </summary>
        public int TaskType { get; set; }
    }
} 