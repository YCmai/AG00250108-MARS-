using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace WarehouseManagementSystem.Models.PLC
{

    public class RCS_AutoTaskNodeSettings
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 工位节点
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Node { get; set; }

        /// <summary>
        /// 目标点
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string TargetPoint { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [MaxLength(200)]
        public string Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdateTime { get; set; }
    }
} 