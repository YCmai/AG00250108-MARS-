using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WarehouseManagementSystem.Models.Enums;

namespace WarehouseManagementSystem.Models
{
    /// <summary>
    /// 物料出入库交易记录模型
    /// </summary>
    public class RCS_MaterialTransactions
    {
        /// <summary>
        /// 交易记录ID，主键
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 交易单号，系统自动生成
        /// </summary>
        [Required]
        [MaxLength(50)]
        [DisplayName("交易单号")]
        public string TransactionCode { get; set; }

        /// <summary>
        /// 关联物料ID
        /// </summary>
        [Required]
        [DisplayName("物料ID")]
        public int MaterialId { get; set; }

        /// <summary>
        /// 物料编码，冗余字段，方便查询
        /// </summary>
        [Required]
        [MaxLength(50)]
        [DisplayName("物料编码")]
        public string MaterialCode { get; set; }

        /// <summary>
        /// 交易类型（入库/出库/调整等）
        /// </summary>
        [Required]
        [DisplayName("交易类型")]
        public TransactionType Type { get; set; }

        /// <summary>
        /// 交易数量
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [DisplayName("交易数量")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// 交易前库存
        /// </summary>
        [Column(TypeName = "decimal(18, 2)")]
        [DisplayName("交易前库存")]
        public decimal BeforeQuantity { get; set; }

        /// <summary>
        /// 交易后库存
        /// </summary>
        [Column(TypeName = "decimal(18, 2)")]
        [DisplayName("交易后库存")]
        public decimal AfterQuantity { get; set; }

        /// <summary>
        /// 储位编码
        /// </summary>
        [MaxLength(50)]
        [DisplayName("储位编码")]
        public string LocationCode { get; set; }

        /// <summary>
        /// 目标储位编码，用于库内移位操作
        /// </summary>
        [MaxLength(50)]
        [DisplayName("目标储位")]
        public string TargetLocationCode { get; set; }

        /// <summary>
        /// 批次号
        /// </summary>
        [MaxLength(50)]
        [DisplayName("批次号")]
        public string BatchNumber { get; set; }

        /// <summary>
        /// 操作人ID
        /// </summary>
        [MaxLength(50)]
        [DisplayName("操作人ID")]
        public string OperatorId { get; set; }

        /// <summary>
        /// 操作人姓名
        /// </summary>
        [MaxLength(50)]
        [DisplayName("操作人")]
        public string OperatorName { get; set; }

        /// <summary>
        /// 出库原因
        /// </summary>
        [MaxLength(100)]
        [DisplayName("出库原因")]
        public string OutReason { get; set; }

        /// <summary>
        /// 相关任务ID，如果由任务触发
        /// </summary>
        [DisplayName("任务ID")]
        public int? TaskId { get; set; }

        /// <summary>
        /// 相关任务编号
        /// </summary>
        [MaxLength(50)]
        [DisplayName("任务编号")]
        public string TaskCode { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        [MaxLength(500)]
        [DisplayName("备注")]
        public string Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [DisplayName("创建时间")]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 导航属性 - 关联物料信息
        /// </summary>
        [ForeignKey("MaterialId")]
        public virtual RCS_Materials Material { get; set; }
    }
} 