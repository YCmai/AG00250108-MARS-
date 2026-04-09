using System.Collections.Generic;
using System.Threading.Tasks;
using WarehouseManagementSystem.Models;
using WarehouseManagementSystem.Models.Common;
using WarehouseManagementSystem.Models.DTOs;

namespace WarehouseManagementSystem.Services
{
    /// <summary>
    /// 物料服务接口
    /// </summary>
    public interface IMaterialService
    {
        /// <summary>
        /// 根据编码获取物料信息
        /// </summary>
        /// <param name="materialCode">物料编码</param>
        /// <returns>物料信息</returns>
        Task<RCS_Materials> GetMaterialByCodeAsync(string materialCode);
        
        /// <summary>
        /// 获取所有物料列表
        /// </summary>
        /// <returns>物料列表</returns>
        Task<List<RCS_Materials>> GetAllMaterialsAsync();
        
        /// <summary>
        /// 添加新物料
        /// </summary>
        /// <param name="material">物料信息</param>
        /// <returns>操作结果</returns>
        Task<Result> AddMaterialAsync(RCS_Materials material);
        
        /// <summary>
        /// 更新物料信息
        /// </summary>
        /// <param name="material">物料信息</param>
        /// <returns>操作结果</returns>
        Task<Result> UpdateMaterialAsync(RCS_Materials material);
        
        /// <summary>
        /// 执行入库操作
        /// </summary>
        /// <param name="dto">入库信息</param>
        /// <returns>操作结果</returns>
        Task<Result> InStockAsync(MaterialTransactionDto dto);
        
        /// <summary>
        /// 执行出库操作
        /// </summary>
        /// <param name="dto">出库信息</param>
        /// <returns>操作结果</returns>
        Task<Result> OutStockAsync(MaterialTransactionDto dto);
        
        /// <summary>
        /// 执行库存调整
        /// </summary>
        /// <param name="dto">调整信息</param>
        /// <returns>操作结果</returns>
        Task<Result> AdjustStockAsync(MaterialTransactionDto dto);
        
        /// <summary>
        /// 执行库内移位
        /// </summary>
        /// <param name="dto">移位信息</param>
        /// <returns>操作结果</returns>
        Task<Result> TransferStockAsync(MaterialTransactionDto dto);
        
        /// <summary>
        /// 获取物料交易历史记录
        /// </summary>
        /// <param name="materialCode">物料编码</param>
        /// <returns>交易历史记录</returns>
        Task<List<RCS_MaterialTransactions>> GetTransactionHistoryAsync(string materialCode);
        
        /// <summary>
        /// 获取低库存预警物料列表
        /// </summary>
        /// <returns>低库存物料列表</returns>
        Task<List<RCS_Materials>> GetLowStockMaterialsAsync();
    }
} 