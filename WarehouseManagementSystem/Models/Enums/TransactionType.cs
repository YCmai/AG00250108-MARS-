using System.ComponentModel;

namespace WarehouseManagementSystem.Models.Enums
{
    /// <summary>
    /// 物料交易类型枚举
    /// </summary>
    public enum TransactionType
    {
        /// <summary>
        /// 入库操作
        /// </summary>
        [Description("入库")]
        InStock = 1,
        
        /// <summary>
        /// 出库操作
        /// </summary>
        [Description("出库")]
        OutStock = 2,
        
        /// <summary>
        /// 库存调整
        /// </summary>
        [Description("库存调整")]
        Adjustment = 3,
        
        /// <summary>
        /// 库内移位
        /// </summary>
        [Description("库内移位")]
        Transfer = 4,
        
        /// <summary>
        /// 库存盘点
        /// </summary>
        [Description("库存盘点")]
        Inventory = 5
    }
} 