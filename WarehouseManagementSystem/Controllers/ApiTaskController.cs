using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using WarehouseManagementSystem.Db;
using WarehouseManagementSystem.Models;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace WarehouseManagementSystem.Controllers
{
    public class ApiTaskController : Controller
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<ApiTaskController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public ApiTaskController(IDatabaseService db, ILogger<ApiTaskController> logger, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult ApiTest()
        {
            return View();
        }

        /// <summary>
        /// 获取分页的API任务列表
        /// </summary>
        /// <param name="pageIndex">页码，从1开始</param>
        /// <param name="pageSize">每页记录数</param>
        /// <returns>分页结果</returns>
        [HttpGet]
        public async Task<IActionResult> GetPagedTasks(int pageIndex = 1, int pageSize = 20)
        {
            try
            {
                if (pageIndex < 1) pageIndex = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                using var connection = _db.CreateConnection();
                
                // 处理分页参数
                var parameters = new DynamicParameters();
                parameters.Add("Offset", (pageIndex - 1) * pageSize);
                parameters.Add("PageSize", pageSize);
                
                var countSql = "SELECT COUNT(*) FROM RCS_ApiTask";
                
                var dataSql = @"
                    SELECT * 
                    FROM RCS_ApiTask
                    ORDER BY CreateTime DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                var totalCount = await connection.ExecuteScalarAsync<int>(countSql);
                var tasks = await connection.QueryAsync<RCS_ApiTask>(dataSql, parameters);

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
                _logger.LogError(ex, "获取API任务列表失败");
                return Json(new
                {
                    success = false,
                    message = "获取API任务列表失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 添加新的API任务
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddTask([FromBody] RCS_ApiTask task)
        {
            try
            {
                if (task == null)
                {
                    return BadRequest("任务数据不能为空");
                }

                task.CreateTime = DateTime.Now;
                task.TaskCode = Guid.NewGuid().ToString("N");
                
                using var connection = _db.CreateConnection();
                
                var sql = @"
                    INSERT INTO RCS_ApiTask (TaskCode, Excute, CreateTime, Message, TaskType)
                    VALUES (@TaskCode, @Excute, @CreateTime, @Message, @TaskType);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                
                var id = await connection.ExecuteScalarAsync<int>(sql, task);
                
                task.ID = id;
                
                return Json(new
                {
                    success = true,
                    data = task,
                    message = "添加API任务成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加API任务失败");
                return Json(new
                {
                    success = false,
                    message = "添加API任务失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 更新API任务状态
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateTaskStatus(int id, bool executed, string message = null)
        {
            try
            {
                using var connection = _db.CreateConnection();
                
                var sql = @"
                    UPDATE RCS_ApiTask
                    SET Excute = @Excute, Message = @Message
                    WHERE ID = @ID";
                
                await connection.ExecuteAsync(sql, new { ID = id, Excute = executed, Message = message });
                
                return Json(new
                {
                    success = true,
                    message = "更新API任务状态成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新API任务状态失败，ID: {Id}", id);
                return Json(new
                {
                    success = false,
                    message = "更新API任务状态失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 删除API任务
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteTask(int id)
        {
            try
            {
                using var connection = _db.CreateConnection();
                
                var sql = "DELETE FROM RCS_ApiTask WHERE ID = @ID";
                
                await connection.ExecuteAsync(sql, new { ID = id });
                
                return Json(new
                {
                    success = true,
                    message = "删除API任务成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除API任务失败，ID: {Id}", id);
                return Json(new
                {
                    success = false,
                    message = "删除API任务失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 测试API接口
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestApiEndpoint([FromBody] ApiTestRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Url))
                {
                    return Json(new
                    {
                        success = false,
                        message = "请求URL不能为空"
                    });
                }

                // 创建 HttpClient
                var httpClient = _httpClientFactory.CreateClient();
                
                // 创建请求消息
                var httpRequestMessage = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
                
                // 添加请求头
                if (request.Headers != null)
                {
                    foreach (var header in request.Headers)
                    {
                        httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                
                // 添加请求体
                if (!string.IsNullOrWhiteSpace(request.Body) && request.Method != "GET")
                {
                    HttpContent content;
                    
                    // 根据Content-Type处理请求体
                    if (request.Headers != null && request.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        if (contentType.Contains("application/json"))
                        {
                            content = new StringContent(request.Body, Encoding.UTF8, "application/json");
                        }
                        else if (contentType.Contains("application/x-www-form-urlencoded"))
                        {
                            content = new StringContent(request.Body, Encoding.UTF8, "application/x-www-form-urlencoded");
                        }
                        else
                        {
                            content = new StringContent(request.Body, Encoding.UTF8);
                        }
                    }
                    else
                    {
                        // 默认为JSON
                        content = new StringContent(request.Body, Encoding.UTF8, "application/json");
                    }
                    
                    httpRequestMessage.Content = content;
                }
                
                // 发送请求
                var response = await httpClient.SendAsync(httpRequestMessage);
                
                // 读取响应内容
                var responseContent = await response.Content.ReadAsStringAsync();
                
                // 如果需要保存到历史记录
                if (request.SaveToHistory)
                {
                    await SaveApiTaskToHistory(request, response.StatusCode, responseContent);
                }
                
                return Json(new
                {
                    success = true,
                    statusCode = (int)response.StatusCode,
                    response = responseContent,
                    headers = response.Headers.ToDictionary(h => h.Key, h => h.Value)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试API接口失败，URL: {Url}", request.Url);
                return Json(new
                {
                    success = false,
                    message = "测试API接口失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 保存API任务到历史记录
        /// </summary>
        private async Task SaveApiTaskToHistory(ApiTestRequest request, System.Net.HttpStatusCode statusCode, string responseContent)
        {
            try
            {
                var task = new RCS_ApiTask
                {
                    TaskCode = Guid.NewGuid().ToString("N"),
                    Excute = true,
                    CreateTime = DateTime.Now,
                    Message = $"状态码: {(int)statusCode}, 响应: {(responseContent.Length > 500 ? responseContent.Substring(0, 500) + "..." : responseContent)}",
                    TaskType = GetTaskTypeFromUrl(request.Url)
                };
                
                using var connection = _db.CreateConnection();
                
                var sql = @"
                    INSERT INTO RCS_ApiTask (TaskCode, Excute, CreateTime, Message, TaskType)
                    VALUES (@TaskCode, @Excute, @CreateTime, @Message, @TaskType)";
                
                await connection.ExecuteAsync(sql, task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存API任务到历史记录失败");
            }
        }

        /// <summary>
        /// 根据URL确定任务类型
        /// </summary>
        private int GetTaskTypeFromUrl(string url)
        {
            if (url.StartsWith("ws://") || url.StartsWith("wss://"))
            {
                return 2; // WebSocket
            }
            else if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                return 1; // HTTP
            }
            else
            {
                return 5; // 其他
            }
        }
    }

    /// <summary>
    /// API测试请求模型
    /// </summary>
    public class ApiTestRequest
    {
        public string Url { get; set; }
        public string Method { get; set; } = "GET";
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }
        public bool SaveToHistory { get; set; }
    }
} 