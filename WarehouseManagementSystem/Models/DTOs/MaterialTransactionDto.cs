using System;
using System.ComponentModel.DataAnnotations;
using WarehouseManagementSystem.Models.Enums;

namespace WarehouseManagementSystem.Models.DTOs
{
    /// <summary>
    /// 物料交易数据传输对象，用于处理出入库请求
    /// </summary>
    public class MaterialTransactionDto
    {
        /// <summary>
        /// 物料编码
        /// </summary>
        [Required(ErrorMessage = "物料编码不能为空")]
        public string MaterialCode { get; set; }
        
        /// <summary>
        /// 交易类型
        /// </summary>
        [Required(ErrorMessage = "交易类型不能为空")]
        public TransactionType Type { get; set; }
        
        /// <summary>
        /// 交易数量
        /// </summary>
        [Required(ErrorMessage = "数量不能为空")]
        [Range(0.01, double.MaxValue, ErrorMessage = "数量必须大于0")]
        public decimal Quantity { get; set; }
        
        /// <summary>
        /// 储位编码
        /// </summary>
        [Required(ErrorMessage = "储位编码不能为空")]
        public string LocationCode { get; set; }
        
        /// <summary>
        /// 目标储位编码（用于库内移位）
        /// </summary>
        public string TargetLocationCode { get; set; }
        
        /// <summary>
        /// 批次号
        /// </summary>
        public string BatchNumber { get; set; }
        
        /// <summary>
        /// 操作人ID
        /// </summary>
        public string OperatorId { get; set; }
        
        /// <summary>
        /// 操作人姓名
        /// </summary>
        public string OperatorName { get; set; }
        
        /// <summary>
        /// 相关任务ID
        /// </summary>
        public int? TaskId { get; set; }
        
        /// <summary>
        /// 相关任务编号
        /// </summary>
        public string TaskCode { get; set; }
        
        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }
        
        /// <summary>
        /// 出库原因
        /// </summary>
        public string OutReason { get; set; }
    }
} 