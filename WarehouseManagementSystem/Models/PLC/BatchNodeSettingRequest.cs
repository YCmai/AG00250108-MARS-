using System.ComponentModel.DataAnnotations;

namespace WarehouseManagementSystem.Models.PLC
{
    /// <summary>
    /// 批量设置目标点节点的请求
    /// </summary>
    public class BatchNodeSettingRequest
    {
        /// <summary>
        /// 目标点
        /// </summary>
        [Required(ErrorMessage = "目标点不能为空")]
        [MaxLength(50, ErrorMessage = "目标点长度不能超过50个字符")]
        public string TargetPoint { get; set; }

        /// <summary>
        /// 节点列表
        /// </summary>
        [Required(ErrorMessage = "节点列表不能为空")]
        public List<string> NodeList { get; set; }
    }
} 