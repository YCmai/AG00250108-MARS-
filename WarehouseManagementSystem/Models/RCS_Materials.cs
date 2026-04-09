using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WarehouseManagementSystem.Models
{
    /// <summary>
    /// 物料信息模型
    /// </summary>
    public class RCS_Materials
    {
        /// <summary>
        /// 物料ID，主键
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 物料编码，唯一标识符
        /// </summary>
        [Required(ErrorMessage = "物料编码不能为空")]
        [MaxLength(50, ErrorMessage = "物料编码最大长度为50")]
        [DisplayName("物料编码")]
        public string Code { get; set; }

        /// <summary>
        /// 物料名称
        /// </summary>
        [Required(ErrorMessage = "物料名称不能为空")]
        [MaxLength(100, ErrorMessage = "物料名称最大长度为100")]
        [DisplayName("物料名称")]
        public string Name { get; set; }

        /// <summary>
        /// 规格型号
        /// </summary>
        [MaxLength(100, ErrorMessage = "规格型号最大长度为100")]
        [DisplayName("规格型号")]
        public string Specification { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        [MaxLength(20, ErrorMessage = "单位最大长度为20")]
        [DisplayName("单位")]
        public string Unit { get; set; }

        /// <summary>
        /// 当前库存数量
        /// </summary>
        [DisplayName("库存数量")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// 最小库存数量，低于此值触发库存预警
        /// </summary>
        [DisplayName("最小库存")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal MinStock { get; set; }

        /// <summary>
        /// 最大库存数量，高于此值触发库存预警
        /// </summary>
        [DisplayName("最大库存")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal MaxStock { get; set; }

        /// <summary>
        /// 储位编码，关联储位信息
        /// </summary>
        [MaxLength(50, ErrorMessage = "储位编码最大长度为50")]
        [DisplayName("储位编码")]
        public string LocationCode { get; set; }

        /// <summary>
        /// 物料图片路径
        /// </summary>
        [MaxLength(255)]
        [DisplayName("物料图片")]
        public string ImageUrl { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [DisplayName("创建时间")]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [DisplayName("更新时间")]
        public DateTime? UpdateTime { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        [MaxLength(500)]
        [DisplayName("备注")]
        public string Remark { get; set; }
    }
} 