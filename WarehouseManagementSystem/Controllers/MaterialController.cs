using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WarehouseManagementSystem.Models;
using WarehouseManagementSystem.Models.DTOs;
using WarehouseManagementSystem.Models.Enums;
using WarehouseManagementSystem.Services;

namespace WarehouseManagementSystem.Controllers
{
    /// <summary>
    /// 物料管理控制器 - 简化版，只处理基本的点位记录和物料出入库
    /// </summary>
    public class MaterialController : Controller
    {
        private readonly IMaterialService _materialService;
        private readonly ILogger<MaterialController> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="materialService">物料服务</param>
        /// <param name="logger">日志记录器</param>
        public MaterialController(IMaterialService materialService, ILogger<MaterialController> logger)
        {
            _materialService = materialService;
            _logger = logger;
        }

        /// <summary>
        /// 物料管理页面
        /// </summary>
        public async Task<IActionResult> Index(string searchString, int page = 1)
        {
            try
            {
                int pageSize = 10;
                var materials = await _materialService.GetAllMaterialsAsync();
                
                // 处理搜索过滤
                if (!string.IsNullOrEmpty(searchString))
                {
                    materials = materials.Where(m => 
                        m.Code.Contains(searchString, StringComparison.OrdinalIgnoreCase) || 
                        m.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                        (m.Specification != null && m.Specification.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                }
                
                // 计算总页数
                var totalItems = materials.Count;
                var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                
                // 分页
                var pagedMaterials = materials
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                // 准备视图模型
                var viewModel = new PagedResult<RCS_Materials>
                {
                    Items = pagedMaterials,
                    TotalItems = totalItems,
                    PageNumber = page,
                    PageSize = pageSize,
                    TotalPages = totalPages
                };
                
                ViewData["SearchString"] = searchString;
                
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料列表失败");
                return View(new PagedResult<RCS_Materials>());
            }
        }
        
        /// <summary>
        /// 库存管理页面
        /// </summary>
        public async Task<IActionResult> Inventory(string searchString, int page = 1)
        {
            try
            {
                int pageSize = 10;
                
                // 获取所有物料
                var materials = await _materialService.GetAllMaterialsAsync();
                
                // 处理搜索过滤
                if (!string.IsNullOrEmpty(searchString))
                {
                    materials = materials.Where(m => 
                        m.Code.Contains(searchString, StringComparison.OrdinalIgnoreCase) || 
                        m.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }
                
                // 获取低库存预警物料
                var lowStockMaterials = await _materialService.GetLowStockMaterialsAsync();
                
                // 计算总页数
                var totalItems = materials.Count;
                var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                
                // 分页
                var pagedMaterials = materials
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                // 准备视图模型
                var viewModel = new InventoryViewModel
                {
                    Materials = new PagedResult<RCS_Materials>
                    {
                        Items = pagedMaterials,
                        TotalItems = totalItems,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalPages = totalPages
                    },
                    LowStockMaterials = lowStockMaterials,
                    TotalMaterialCount = materials.Count,
                    LowStockCount = lowStockMaterials.Count
                };
                
                ViewData["SearchString"] = searchString;
                
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取库存信息失败");
                return View(new InventoryViewModel());
            }
        }

        /// <summary>
        /// 物料列表API
        /// </summary>
        [HttpGet("api/[controller]")]
        public async Task<ActionResult<IEnumerable<RCS_Materials>>> GetMaterials()
        {
            try
            {
                var materials = await _materialService.GetAllMaterialsAsync();
                return Ok(materials);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料列表失败");
                return StatusCode(500, new { success = false, message = "获取物料列表失败" });
            }
        }

        /// <summary>
        /// 根据编码获取物料API
        /// </summary>
        [HttpGet("api/[controller]/{code}")]
        public async Task<ActionResult<RCS_Materials>> GetMaterial(string code)
        {
            try
            {
                var material = await _materialService.GetMaterialByCodeAsync(code);

                if (material == null)
                {
                    return NotFound();
                }

                return material;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料信息失败: {Code}", code);
                return StatusCode(500, new { success = false, message = "获取物料信息失败" });
            }
        }

        /// <summary>
        /// 物料入库页面
        /// </summary>
        public async Task<IActionResult> InStock()
        {
            try
            {
                var materials = await _materialService.GetAllMaterialsAsync();
                return View(materials);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料列表失败");
                return View(new List<RCS_Materials>());
            }
        }

        /// <summary>
        /// 物料出库页面
        /// </summary>
        public async Task<IActionResult> OutStock()
        {
            try
            {
                var materials = await _materialService.GetAllMaterialsAsync();
                return View(materials);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料列表失败");
                return View(new List<RCS_Materials>());
            }
        }

        /// <summary>
        /// 物料入库API
        /// </summary>
        [HttpPost("api/[controller]/instock")]
        public async Task<IActionResult> InStockApi([FromBody] MaterialTransactionDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // 确保交易类型正确
                dto.Type = TransactionType.InStock;

                var result = await _materialService.InStockAsync(dto);
                if (result.Succeeded)
                {
                    return Ok(result);
                }
                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "物料入库操作失败");
                return StatusCode(500, new { success = false, message = "入库操作失败: " + ex.Message });
            }
        }

        /// <summary>
        /// 物料出库API
        /// </summary>
        [HttpPost("api/[controller]/outstock")]
        public async Task<ActionResult<object>> OutStockApi([FromBody] OutStockRequest request)
        {
            try
            {
                // 验证请求数据
                if (string.IsNullOrEmpty(request.MaterialCode) || request.Quantity <= 0)
                {
                    return BadRequest(new { success = false, message = "物料编码和出库数量是必须的" });
                }

                // 使用服务查找物料
                var material = await _materialService.GetMaterialByCodeAsync(request.MaterialCode);
                if (material == null)
                {
                    return NotFound(new { success = false, message = "未找到物料信息" });
                }

                // 创建出库DTO
                var dto = new MaterialTransactionDto
                {
                    MaterialCode = material.Code,
                    Quantity = request.Quantity,
                    LocationCode = request.LocationCode,
                    OperatorName = request.OperatorName,
                    Remark = request.Remark,
                    Type = TransactionType.OutStock,
                    OutReason = request.OutReason
                };

                // 使用服务执行出库操作
                var result = await _materialService.OutStockAsync(dto);

                if (result.Succeeded)
                {
                    return Ok(new { success = true, transaction = result.Data });
                }
                else
                {
                    return BadRequest(new { success = false, message = result.Message });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "出库操作失败: " + ex.Message });
            }
        }

        /// <summary>
        /// 获取交易历史记录API
        /// </summary>
        [HttpGet("api/[controller]/transactions/{type}")]
        public async Task<IActionResult> GetTransactionsByType(string type)
        {
            try
            {
                TransactionType transactionType;
                switch (type.ToLower())
                {
                    case "instock":
                        transactionType = TransactionType.InStock;
                        break;
                    case "outstock":
                        transactionType = TransactionType.OutStock;
                        break;
                    default:
                        return BadRequest("未支持的交易类型");
                }

                // 使用现有方法，获取所有交易记录并在内存中筛选
                var transactions = new List<RCS_MaterialTransactions>();
                var materials = await _materialService.GetAllMaterialsAsync();
                
                foreach (var material in materials)
                {
                    var materialTransactions = await _materialService.GetTransactionHistoryAsync(material.Code);
                    if (materialTransactions != null && materialTransactions.Any())
                    {
                        transactions.AddRange(materialTransactions.Where(t => t.Type == transactionType));
                    }
                }
                
                // 按创建时间排序，最新的在前
                return Ok(transactions.OrderByDescending(t => t.CreateTime).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取{type}交易记录失败");
                return BadRequest("获取交易记录失败，请稍后再试");
            }
        }

        /// <summary>
        /// 获取物料交易历史记录API
        /// </summary>
        [HttpGet("api/[controller]/history/{materialCode}")]
        public async Task<IActionResult> GetTransactionHistory(string materialCode)
        {
            try
            {
                var transactions = await _materialService.GetTransactionHistoryAsync(materialCode);
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取物料交易历史记录失败: {MaterialCode}", materialCode);
                return StatusCode(500, new { success = false, message = "获取交易历史记录失败，请稍后再试" });
            }
        }

        /// <summary>
        /// 获取出库交易记录
        /// </summary>
        [HttpGet("api/[controller]/transactions/outstock")]
        public async Task<ActionResult<IEnumerable<RCS_MaterialTransactions>>> GetOutStockTransactions()
        {
            try
            {
                // 获取所有物料
                var materials = await _materialService.GetAllMaterialsAsync();
                var transactions = new List<RCS_MaterialTransactions>();

                // 获取每个物料的交易历史
                foreach (var material in materials)
                {
                    var materialTransactions = await _materialService.GetTransactionHistoryAsync(material.Code);
                    if (materialTransactions != null && materialTransactions.Any())
                    {
                        transactions.AddRange(materialTransactions.Where(t => t.Type == TransactionType.OutStock));
                    }
                }

                // 按创建时间排序并限制返回结果
                return transactions
                    .OrderByDescending(t => t.CreateTime)
                    .Take(100) // 限制只返回最近100条记录以提高性能
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取出库交易记录失败");
                return StatusCode(500, new { message = "获取出库交易记录失败" });
            }
        }

        public class OutStockRequest
        {
            public string MaterialCode { get; set; }
            public decimal Quantity { get; set; }
            public string LocationCode { get; set; }
            public string OutReason { get; set; }
            public string OperatorName { get; set; }
            public string Remark { get; set; }
        }
    }
} 