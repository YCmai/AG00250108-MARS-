using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using WarehouseManagementSystem.Db;


    public class AutoPlcTaskController : Controller
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<AutoPlcTaskController> _logger;

        public AutoPlcTaskController(IDatabaseService db, ILogger<AutoPlcTaskController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// 获取分页的PLC任务列表
        /// </summary>
        /// <param name="pageIndex">页码，从1开始</param>
        /// <param name="pageSize">每页记录数</param>
        /// <param name="plcRemark">PLC备注</param>
        /// <param name="plcTypeDb">PLC DB块类型</param>
        /// <returns>分页结果</returns>
        [HttpGet]
        public async Task<IActionResult> GetPagedTasks(int pageIndex = 1, int pageSize = 20, string plcRemark = null, string plcTypeDb = null)
        {
            try
            {
                if (pageIndex < 1) pageIndex = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                using var connection = _db.CreateConnection();
                
                // 构建WHERE条件
                var whereConditions = new List<string>();
                var parameters = new DynamicParameters();
                
                // 过滤掉心跳信号任务
                whereConditions.Add("t.Remark <> '心跳信号'");
                
                if (!string.IsNullOrEmpty(plcRemark))
                {
                    whereConditions.Add("d.Remark = @PlcRemark");
                    parameters.Add("PlcRemark", plcRemark);
                }
                
                if (!string.IsNullOrEmpty(plcTypeDb))
                {
                    whereConditions.Add("t.PLCTypeDb = @PLCTypeDb");
                    parameters.Add("PLCTypeDb", plcTypeDb);
                }
                
                string whereClause = whereConditions.Any() 
                    ? "WHERE " + string.Join(" AND ", whereConditions) 
                    : "";
                
                // 处理分页参数
                parameters.Add("Offset", (pageIndex - 1) * pageSize);
                parameters.Add("PageSize", pageSize);
                
                // 使用GROUP BY子句和LEFT JOIN去重
                var countSql = $@"
                    SELECT COUNT(*) FROM 
                    (
                        SELECT DISTINCT t.OrderCode 
                        FROM AutoPlcTasks t 
                        LEFT JOIN RCS_PlcDevice d ON t.PlcType = d.IpAddress 
                        {whereClause}
                    ) AS UniqueOrders";
                
                var dataSql = $@"
                    SELECT DISTINCT t.*, d.Remark AS PlcRemark 
                    FROM AutoPlcTasks t
                    LEFT JOIN RCS_PlcDevice d ON t.PlcType = d.IpAddress AND 
                        (d.ModuleAddress = t.PLCTypeDb OR (d.ModuleAddress IS NULL AND t.PLCTypeDb IS NULL))
                    {whereClause}
                    ORDER BY t.CreatingTime DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);
                var tasks = await connection.QueryAsync(dataSql, parameters);

                return Json(new
                {
                    success = true,
                    data = tasks,
                    total = totalCount,
                    pageIndex = pageIndex,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC任务列表失败");
                return Json(new
                {
                    success = false,
                    message = "获取PLC任务列表失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 获取任务状态描述
        /// </summary>
        [HttpGet]
        public IActionResult GetStatusDescription(int status)
        {
            string description = status switch
            {
               
                1 => "写入bool值",
                2 => "重置bool值",
                3 => "写入INT",
                4 => "重置INT",
                5 => "写入String值",
                6 => "重置String值",
                _ => "未知状态"
            };

            return Json(new { description });
        }

        /// <summary>
        /// 获取所有PLC设备remark（去重）
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPlcTypes()
        {
            try
            {
                using var connection = _db.CreateConnection();
                var types = await connection.QueryAsync<string>("SELECT DISTINCT Remark FROM RCS_PlcDevice WHERE Remark IS NOT NULL AND Remark <> ''");
                return Json(new { success = true, data = types });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC类型失败");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 获取所有PLC DB块类型
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPlcTypeDb()
        {
            try
            {
                using var connection = _db.CreateConnection();
                var dbTypes = await connection.QueryAsync<string>(@"
                    SELECT DISTINCT PLCTypeDb 
                    FROM AutoPlcTasks 
                    WHERE PLCTypeDb IS NOT NULL AND PLCTypeDb <> ''");
                return Json(new { success = true, data = dbTypes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC DB块类型失败");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
