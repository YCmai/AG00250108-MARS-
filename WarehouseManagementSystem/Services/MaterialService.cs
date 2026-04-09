using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WarehouseManagementSystem.Models;
using WarehouseManagementSystem.Models.Common;
using WarehouseManagementSystem.Models.DTOs;
using WarehouseManagementSystem.Models.Enums;
using WarehouseManagementSystem.Db;
using Dapper;

namespace WarehouseManagementSystem.Services
{
    /// <summary>
    /// 物料服务实现类
    /// </summary>
    public class MaterialService : IMaterialService
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<MaterialService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">数据库服务</param>
        /// <param name="logger">日志记录器</param>
        public MaterialService(IDatabaseService db, ILogger<MaterialService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// 获取物料信息
        /// </summary>
        /// <param name="materialCode">物料编码</param>
        /// <returns>物料信息</returns>
        public async Task<RCS_Materials> GetMaterialByCodeAsync(string materialCode)
        {
            try
            {
                using var conn = _db.CreateConnection();
                return await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                    "SELECT * FROM RCS_Materials WHERE Code = @Code", 
                    new { Code = materialCode });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料信息失败，编码：{MaterialCode}", materialCode);
                return null;
            }
        }

        /// <summary>
        /// 获取所有物料
        /// </summary>
        /// <returns>物料列表</returns>
        public async Task<List<RCS_Materials>> GetAllMaterialsAsync()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var result = await conn.QueryAsync<RCS_Materials>("SELECT * FROM RCS_Materials");
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有物料失败");
                return new List<RCS_Materials>();
            }
        }

        /// <summary>
        /// 添加物料
        /// </summary>
        /// <param name="material">物料信息</param>
        /// <returns>操作结果</returns>
        public async Task<Result> AddMaterialAsync(RCS_Materials material)
        {
            try
            {
                using var conn = _db.CreateConnection();
                using var transaction = conn.BeginTransaction();
                
                try
                {
                    // 检查物料编码是否已存在
                    var existing = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                        "SELECT TOP 1 * FROM RCS_Materials WHERE Code = @Code", 
                        new { Code = material.Code },
                        transaction);
                        
                    if (existing != null)
                    {
                        return Result.Failure($"物料编码 {material.Code} 已存在");
                    }

                    // 设置创建时间
                    material.CreateTime = DateTime.Now;
                    
                    // 添加物料
                    const string sql = @"
                    INSERT INTO RCS_Materials (
                        Code, Name, Specification, Unit, Quantity, 
                        MinStock, MaxStock, LocationCode, ImageUrl,
                        CreateTime, Remark)
                    VALUES (
                        @Code, @Name, @Specification, @Unit, @Quantity,
                        @MinStock, @MaxStock, @LocationCode, @ImageUrl,
                        @CreateTime, @Remark);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                    
                    var id = await conn.QuerySingleAsync<int>(sql, material, transaction);
                    material.Id = id;
                    
                    transaction.Commit();
                    
                    _logger.LogInformation("添加物料成功，编码：{MaterialCode}", material.Code);
                    return Result.Success(material, "添加物料成功");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "添加物料事务失败，编码：{MaterialCode}", material.Code);
                    return Result.Failure($"添加物料失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加物料失败，编码：{MaterialCode}", material.Code);
                return Result.Failure($"添加物料失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 更新物料
        /// </summary>
        /// <param name="material">物料信息</param>
        /// <returns>操作结果</returns>
        public async Task<Result> UpdateMaterialAsync(RCS_Materials material)
        {
            try
            {
                using var conn = _db.CreateConnection();
                using var transaction = conn.BeginTransaction();
                
                try
                {
                    // 获取当前物料信息
                    var existingMaterial = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                        "SELECT * FROM RCS_Materials WHERE Id = @Id", 
                        new { Id = material.Id },
                        transaction);
                        
                    if (existingMaterial == null)
                    {
                        return Result.Failure($"物料不存在，ID：{material.Id}");
                    }

                    // 如果修改了物料编码，检查新编码是否已存在
                    if (material.Code != existingMaterial.Code)
                    {
                        var codeExists = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                            "SELECT TOP 1 * FROM RCS_Materials WHERE Code = @Code AND Id != @Id", 
                            new { Code = material.Code, Id = material.Id },
                            transaction);
                            
                        if (codeExists != null)
                        {
                            return Result.Failure($"物料编码 {material.Code} 已存在");
                        }
                    }

                    // 更新物料信息
                    material.UpdateTime = DateTime.Now;
                    
                    const string sql = @"
                    UPDATE RCS_Materials 
                    SET Code = @Code,
                        Name = @Name,
                        Specification = @Specification,
                        Unit = @Unit,
                        MinStock = @MinStock,
                        MaxStock = @MaxStock,
                        LocationCode = @LocationCode,
                        ImageUrl = @ImageUrl,
                        UpdateTime = @UpdateTime,
                        Remark = @Remark
                    WHERE Id = @Id";
                    
                    await conn.ExecuteAsync(sql, material, transaction);
                    
                    transaction.Commit();
                    
                    _logger.LogInformation("更新物料成功，编码：{MaterialCode}", material.Code);
                    return Result.Success(material, "更新物料成功");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "更新物料事务失败，编码：{MaterialCode}", material.Code);
                    return Result.Failure($"更新物料失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新物料失败，编码：{MaterialCode}", material.Code);
                return Result.Failure($"更新物料失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 执行入库操作
        /// </summary>
        /// <param name="dto">入库信息</param>
        /// <returns>操作结果</returns>
        public async Task<Result> InStockAsync(MaterialTransactionDto dto)
        {
            try
            {
                using var conn = _db.CreateConnection();
                using var transaction = conn.BeginTransaction();
                
                try
                {
                    // 获取物料信息
                    var material = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                        "SELECT * FROM RCS_Materials WHERE Code = @Code", 
                        new { Code = dto.MaterialCode },
                        transaction);

                    if (material == null)
                    {
                        return Result.Failure("物料不存在");
                    }

                    // 记录入库前库存
                    decimal beforeQuantity = material.Quantity;
                    
                    // 更新库存
                    material.Quantity += dto.Quantity;
                    material.UpdateTime = DateTime.Now;
                    
                    await conn.ExecuteAsync(
                        "UPDATE RCS_Materials SET Quantity = @Quantity, UpdateTime = @UpdateTime WHERE Id = @Id",
                        new { Quantity = material.Quantity, UpdateTime = material.UpdateTime, Id = material.Id },
                        transaction);

                    // 创建入库交易记录
                    var transactionCode = GenerateTransactionCode();
                    var materialTransaction = new RCS_MaterialTransactions
                    {
                        TransactionCode = transactionCode,
                        MaterialId = material.Id,
                        MaterialCode = material.Code,
                        Type = TransactionType.InStock,
                        Quantity = dto.Quantity,
                        BeforeQuantity = beforeQuantity,
                        AfterQuantity = material.Quantity,
                        LocationCode = dto.LocationCode,
                        BatchNumber = dto.BatchNumber,
                        OperatorId = dto.OperatorId,
                        OperatorName = dto.OperatorName,
                        TaskId = dto.TaskId,
                        TaskCode = dto.TaskCode,
                        Remark = dto.Remark,
                        CreateTime = DateTime.Now
                    };

                    // 保存入库记录
                    const string insertSql = @"
                    INSERT INTO RCS_MaterialTransactions (
                        TransactionCode, MaterialId, MaterialCode, Type, Quantity,
                        BeforeQuantity, AfterQuantity, LocationCode, BatchNumber,
                        OperatorId, OperatorName, TaskId, TaskCode, Remark, CreateTime)
                    VALUES (
                        @TransactionCode, @MaterialId, @MaterialCode, @Type, @Quantity,
                        @BeforeQuantity, @AfterQuantity, @LocationCode, @BatchNumber,
                        @OperatorId, @OperatorName, @TaskId, @TaskCode, @Remark, @CreateTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                    
                    var id = await conn.QuerySingleAsync<int>(insertSql, materialTransaction, transaction);
                    materialTransaction.Id = id;
                    
                    transaction.Commit();

                    _logger.LogInformation("物料入库成功，编码：{MaterialCode}，数量：{Quantity}，当前库存：{CurrentQuantity}", 
                        material.Code, dto.Quantity, material.Quantity);
                    
                    return Result.Success(materialTransaction, "入库成功");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "物料入库事务失败，编码：{MaterialCode}", dto.MaterialCode);
                    return Result.Failure($"入库操作失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物料入库失败，编码：{MaterialCode}", dto.MaterialCode);
                return Result.Failure($"入库失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 执行出库操作
        /// </summary>
        /// <param name="dto">出库信息</param>
        /// <returns>操作结果</returns>
        public async Task<Result> OutStockAsync(MaterialTransactionDto dto)
        {
            try
            {
                using var conn = _db.CreateConnection();
                using var transaction = conn.BeginTransaction();
                
                try
                {
                    // 获取物料信息
                    var material = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                        "SELECT * FROM RCS_Materials WHERE Code = @Code", 
                        new { Code = dto.MaterialCode },
                        transaction);

                    if (material == null)
                    {
                        return Result.Failure("物料不存在");
                    }

                    // 检查库存是否足够
                    if (material.Quantity < dto.Quantity)
                    {
                        return Result.Failure($"库存不足，当前库存：{material.Quantity}，需要：{dto.Quantity}");
                    }

                    // 记录出库前库存
                    decimal beforeQuantity = material.Quantity;
                    
                    // 更新库存
                    material.Quantity -= dto.Quantity;
                    material.UpdateTime = DateTime.Now;
                    
                    await conn.ExecuteAsync(
                        "UPDATE RCS_Materials SET Quantity = @Quantity, UpdateTime = @UpdateTime WHERE Id = @Id",
                        new { Quantity = material.Quantity, UpdateTime = material.UpdateTime, Id = material.Id },
                        transaction);

                    // 创建出库交易记录
                    var transactionCode = GenerateTransactionCode();
                    var materialTransaction = new RCS_MaterialTransactions
                    {
                        TransactionCode = transactionCode,
                        MaterialId = material.Id,
                        MaterialCode = material.Code,
                        Type = TransactionType.OutStock,
                        Quantity = dto.Quantity,
                        BeforeQuantity = beforeQuantity,
                        AfterQuantity = material.Quantity,
                        LocationCode = dto.LocationCode,
                        BatchNumber = dto.BatchNumber,
                        OperatorId = dto.OperatorId,
                        OperatorName = dto.OperatorName,
                        TaskId = dto.TaskId,
                        TaskCode = dto.TaskCode,
                        OutReason = dto.OutReason,
                        Remark = dto.Remark,
                        CreateTime = DateTime.Now
                    };

                    // 保存出库记录
                    const string insertSql = @"
                    INSERT INTO RCS_MaterialTransactions (
                        TransactionCode, MaterialId, MaterialCode, Type, Quantity,
                        BeforeQuantity, AfterQuantity, LocationCode, BatchNumber,
                        OperatorId, OperatorName, TaskId, TaskCode, OutReason, Remark, CreateTime)
                    VALUES (
                        @TransactionCode, @MaterialId, @MaterialCode, @Type, @Quantity,
                        @BeforeQuantity, @AfterQuantity, @LocationCode, @BatchNumber,
                        @OperatorId, @OperatorName, @TaskId, @TaskCode, @OutReason, @Remark, @CreateTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                    
                    var id = await conn.QuerySingleAsync<int>(insertSql, materialTransaction, transaction);
                    materialTransaction.Id = id;
                    
                    transaction.Commit();

                    _logger.LogInformation("物料出库成功，编码：{MaterialCode}，数量：{Quantity}，当前库存：{CurrentQuantity}", 
                        material.Code, dto.Quantity, material.Quantity);
                    
                    return Result.Success(materialTransaction, "出库成功");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "物料出库事务失败，编码：{MaterialCode}", dto.MaterialCode);
                    return Result.Failure($"出库操作失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物料出库失败，编码：{MaterialCode}", dto.MaterialCode);
                return Result.Failure($"出库失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 执行库存调整
        /// </summary>
        /// <param name="dto">调整信息</param>
        /// <returns>操作结果</returns>
        public async Task<Result> AdjustStockAsync(MaterialTransactionDto dto)
        {
            try
            {
                using var conn = _db.CreateConnection();
                using var transaction = conn.BeginTransaction();
                
                try
                {
                    // 获取物料信息
                    var material = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                        "SELECT * FROM RCS_Materials WHERE Code = @Code", 
                        new { Code = dto.MaterialCode },
                        transaction);

                    if (material == null)
                    {
                        return Result.Failure("物料不存在");
                    }

                    // 记录调整前库存
                    decimal beforeQuantity = material.Quantity;
                    
                    // 更新库存为指定数量
                    material.Quantity = dto.Quantity;
                    material.UpdateTime = DateTime.Now;
                    
                    await conn.ExecuteAsync(
                        "UPDATE RCS_Materials SET Quantity = @Quantity, UpdateTime = @UpdateTime WHERE Id = @Id",
                        new { Quantity = material.Quantity, UpdateTime = material.UpdateTime, Id = material.Id },
                        transaction);

                    // 创建库存调整交易记录
                    var remark = $"库存调整 {beforeQuantity} -> {dto.Quantity} {(dto.Remark != null ? "，" + dto.Remark : "")}";
                    var materialTransaction = new RCS_MaterialTransactions
                    {
                        TransactionCode = GenerateTransactionCode(),
                        MaterialId = material.Id,
                        MaterialCode = material.Code,
                        Type = TransactionType.Adjustment,
                        Quantity = Math.Abs(dto.Quantity - beforeQuantity), // 调整数量为变化的绝对值
                        BeforeQuantity = beforeQuantity,
                        AfterQuantity = material.Quantity,
                        LocationCode = dto.LocationCode,
                        BatchNumber = dto.BatchNumber,
                        OperatorId = dto.OperatorId,
                        OperatorName = dto.OperatorName,
                        TaskId = dto.TaskId,
                        TaskCode = dto.TaskCode,
                        Remark = remark,
                        CreateTime = DateTime.Now
                    };

                    // 保存调整记录
                    const string insertSql = @"
                    INSERT INTO RCS_MaterialTransactions (
                        TransactionCode, MaterialId, MaterialCode, Type, Quantity,
                        BeforeQuantity, AfterQuantity, LocationCode, BatchNumber,
                        OperatorId, OperatorName, TaskId, TaskCode, Remark, CreateTime)
                    VALUES (
                        @TransactionCode, @MaterialId, @MaterialCode, @Type, @Quantity,
                        @BeforeQuantity, @AfterQuantity, @LocationCode, @BatchNumber,
                        @OperatorId, @OperatorName, @TaskId, @TaskCode, @Remark, @CreateTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                    
                    var id = await conn.QuerySingleAsync<int>(insertSql, materialTransaction, transaction);
                    materialTransaction.Id = id;
                    
                    transaction.Commit();

                    _logger.LogInformation("物料库存调整成功，编码：{MaterialCode}，从：{BeforeQuantity} 调整到：{AfterQuantity}", 
                        material.Code, beforeQuantity, material.Quantity);
                    
                    return Result.Success(materialTransaction, "库存调整成功");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "物料库存调整事务失败，编码：{MaterialCode}", dto.MaterialCode);
                    return Result.Failure($"库存调整操作失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物料库存调整失败，编码：{MaterialCode}", dto.MaterialCode);
                return Result.Failure($"库存调整失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 执行库内移位
        /// </summary>
        /// <param name="dto">移位信息</param>
        /// <returns>操作结果</returns>
        public async Task<Result> TransferStockAsync(MaterialTransactionDto dto)
        {
            if (string.IsNullOrEmpty(dto.TargetLocationCode))
            {
                return Result.Failure("目标储位不能为空");
            }

            try
            {
                using var conn = _db.CreateConnection();
                using var transaction = conn.BeginTransaction();
                
                try
                {
                    // 获取物料信息
                    var material = await conn.QueryFirstOrDefaultAsync<RCS_Materials>(
                        "SELECT * FROM RCS_Materials WHERE Code = @Code", 
                        new { Code = dto.MaterialCode },
                        transaction);

                    if (material == null)
                    {
                        return Result.Failure("物料不存在");
                    }

                    // 检查移动数量是否合理
                    if (dto.Quantity <= 0)
                    {
                        return Result.Failure("移动数量必须大于0");
                    }

                    if (material.Quantity < dto.Quantity)
                    {
                        return Result.Failure($"库存不足，当前库存：{material.Quantity}，需要：{dto.Quantity}");
                    }

                    // 记录移位信息
                    string originalLocation = material.LocationCode;
                    
                    // 更新物料储位
                    material.LocationCode = dto.TargetLocationCode;
                    material.UpdateTime = DateTime.Now;
                    
                    await conn.ExecuteAsync(
                        "UPDATE RCS_Materials SET LocationCode = @LocationCode, UpdateTime = @UpdateTime WHERE Id = @Id",
                        new { LocationCode = material.LocationCode, UpdateTime = material.UpdateTime, Id = material.Id },
                        transaction);

                    // 创建库内移位交易记录
                    var remark = $"库内移位 {originalLocation} -> {dto.TargetLocationCode} {(dto.Remark != null ? "，" + dto.Remark : "")}";
                    var materialTransaction = new RCS_MaterialTransactions
                    {
                        TransactionCode = GenerateTransactionCode(),
                        MaterialId = material.Id,
                        MaterialCode = material.Code,
                        Type = TransactionType.Transfer,
                        Quantity = dto.Quantity,
                        BeforeQuantity = material.Quantity,
                        AfterQuantity = material.Quantity, // 移位不改变总数量
                        LocationCode = originalLocation, // 原储位
                        TargetLocationCode = dto.TargetLocationCode, // 目标储位
                        BatchNumber = dto.BatchNumber,
                        OperatorId = dto.OperatorId,
                        OperatorName = dto.OperatorName,
                        TaskId = dto.TaskId,
                        TaskCode = dto.TaskCode,
                        Remark = remark,
                        CreateTime = DateTime.Now
                    };

                    // 保存移位记录
                    const string insertSql = @"
                    INSERT INTO RCS_MaterialTransactions (
                        TransactionCode, MaterialId, MaterialCode, Type, Quantity,
                        BeforeQuantity, AfterQuantity, LocationCode, TargetLocationCode, BatchNumber,
                        OperatorId, OperatorName, TaskId, TaskCode, Remark, CreateTime)
                    VALUES (
                        @TransactionCode, @MaterialId, @MaterialCode, @Type, @Quantity,
                        @BeforeQuantity, @AfterQuantity, @LocationCode, @TargetLocationCode, @BatchNumber,
                        @OperatorId, @OperatorName, @TaskId, @TaskCode, @Remark, @CreateTime);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                    
                    var id = await conn.QuerySingleAsync<int>(insertSql, materialTransaction, transaction);
                    materialTransaction.Id = id;
                    
                    transaction.Commit();

                    _logger.LogInformation("物料库内移位成功，编码：{MaterialCode}，从：{OriginalLocation} 移动到：{TargetLocation}", 
                        material.Code, originalLocation, dto.TargetLocationCode);
                    
                    return Result.Success(materialTransaction, "库内移位成功");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "物料库内移位事务失败，编码：{MaterialCode}", dto.MaterialCode);
                    return Result.Failure($"库内移位操作失败：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物料库内移位失败，编码：{MaterialCode}", dto.MaterialCode);
                return Result.Failure($"库内移位失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取物料交易历史记录
        /// </summary>
        /// <param name="materialCode">物料编码</param>
        /// <returns>交易历史记录</returns>
        public async Task<List<RCS_MaterialTransactions>> GetTransactionHistoryAsync(string materialCode)
        {
            try
            {
                using var conn = _db.CreateConnection();
                var result = await conn.QueryAsync<RCS_MaterialTransactions>(
                    "SELECT * FROM RCS_MaterialTransactions WHERE MaterialCode = @MaterialCode ORDER BY CreateTime DESC",
                    new { MaterialCode = materialCode });
                    
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料交易历史失败，编码：{MaterialCode}", materialCode);
                return new List<RCS_MaterialTransactions>();
            }
        }

        /// <summary>
        /// 获取低库存预警物料列表
        /// </summary>
        /// <returns>低库存物料列表</returns>
        public async Task<List<RCS_Materials>> GetLowStockMaterialsAsync()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var result = await conn.QueryAsync<RCS_Materials>(
                    "SELECT * FROM RCS_Materials WHERE Quantity <= MinStock");
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取低库存预警物料列表失败");
                return new List<RCS_Materials>();
            }
        }

        /// <summary>
        /// 生成唯一的交易单号
        /// </summary>
        /// <returns>交易单号</returns>
        private string GenerateTransactionCode()
        {
            // 生成格式：T + 年月日时分秒 + 4位随机数
            return $"T{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }
    }
} 