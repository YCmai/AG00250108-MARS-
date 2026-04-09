using System;
using System.Collections.Generic;

namespace WarehouseManagementSystem.Models
{
    public class InventoryViewModel
    {
        public PagedResult<RCS_Materials> Materials { get; set; } = new PagedResult<RCS_Materials>();
        public List<RCS_Materials> LowStockMaterials { get; set; } = new List<RCS_Materials>();
        public int TotalMaterialCount { get; set; }
        public int LowStockCount { get; set; }
    }
} 