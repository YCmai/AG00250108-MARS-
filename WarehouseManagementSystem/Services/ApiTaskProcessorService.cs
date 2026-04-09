using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WarehouseManagementSystem.Db;
using WarehouseManagementSystem.Models;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Collections.Concurrent;

namespace WarehouseManagementSystem.Services
{
    /// <summary>
    /// API任务处理服务 - 监控并处理未执行的API任务
    /// </summary>
    public class ApiTaskProcessorService : BackgroundService
    {
        private readonly ILogger<ApiTaskProcessorService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _pollingInterval;
        private readonly TimeSpan _networkRetryInterval = TimeSpan.FromSeconds(30); // 网络重试间隔
        private readonly TimeSpan _networkCheckInterval = TimeSpan.FromSeconds(10); // 网络检查间隔
        
        // 记录不同API端点的网络状态
        private readonly ConcurrentDictionary<string, bool> _endpointNetworkStatus = new ConcurrentDictionary<string, bool>();
        
        // 默认API端点
        private string _defaultEndpoint;

        public ApiTaskProcessorService(
            ILogger<ApiTaskProcessorService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            
            // 从配置中获取轮询间隔，默认为5秒
            int pollingIntervalSeconds = _configuration.GetValue<int>("ApiTask:PollingIntervalSeconds", 5);
            _pollingInterval = TimeSpan.FromSeconds(pollingIntervalSeconds);
            
            // 获取默认API端点
            _defaultEndpoint = _configuration["ApiTask:DefaultEndpoint"];
            
            _logger.LogInformation($"API任务处理服务已初始化，轮询间隔: {_pollingInterval.TotalSeconds}秒");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("API任务处理服务已启动");

            // 启动网络监控任务
            _ = NetworkMonitoringAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理API任务时发生异常");
                }

                // 等待下一个轮询周期
                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("API任务处理服务已停止");
        }

        /// <summary>
        /// 网络连接监控任务
        /// </summary>
        private async Task NetworkMonitoringAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("API服务网络连接监控任务已启动");
            
            if (string.IsNullOrEmpty(_defaultEndpoint))
            {
                _logger.LogWarning("未配置API端点地址，无法监控网络连接");
                return;
            }
            
            // 初始化默认端点的网络状态
            Uri defaultUri = new Uri(_defaultEndpoint);
            string defaultHost = defaultUri.Host;
            _endpointNetworkStatus[defaultHost] = await CheckNetworkConnectionAsync(defaultHost);
            _logger.LogInformation("默认API端点 {Host} 的初始网络状态: {Status}", 
                defaultHost, _endpointNetworkStatus[defaultHost] ? "在线" : "离线");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 检查所有已知端点的网络状态
                    foreach (var endpoint in _endpointNetworkStatus.Keys.ToList())
                    {
                        bool previousStatus = _endpointNetworkStatus.GetValueOrDefault(endpoint, false);
                        bool currentStatus = await CheckNetworkConnectionAsync(endpoint);
                        
                        // 更新网络状态
                        _endpointNetworkStatus[endpoint] = currentStatus;
                        
                        // 如果状态发生变化，记录日志
                        if (previousStatus != currentStatus)
                        {
                            if (currentStatus)
                            {
                                _logger.LogInformation("API端点 {Host} 的网络连接已恢复", endpoint);
                            }
                            else
                            {
                                _logger.LogWarning("API端点 {Host} 的网络连接已断开", endpoint);
                            }
                        }
                    }
                    
                    // 等待下一次检查
                    await Task.Delay(_networkCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "网络连接监控任务发生错误");
                    await Task.Delay(_networkCheckInterval, stoppingToken);
                }
            }
            
            _logger.LogInformation("API服务网络连接监控任务已停止");
        }
        
        /// <summary>
        /// 检查网络连接状态
        /// </summary>
        private async Task<bool> CheckNetworkConnectionAsync(string hostNameOrAddress)
        {
            try
            {
                using Ping pinger = new Ping();
                PingReply reply = await pinger.SendPingAsync(hostNameOrAddress, 1000);
                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "检查主机 {Host} 的网络连接时出错", hostNameOrAddress);
                return false;
            }
        }

        /// <summary>
        /// 处理待执行的API任务
        /// </summary>
        private async Task ProcessPendingTasksAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
            
            using var connection = dbService.CreateConnection();
            
            // 查询未执行的任务
            var pendingTasks = await connection.QueryAsync<RCS_ApiTask>(@"
                SELECT * FROM RCS_ApiTask 
                WHERE Excute = 0
                ORDER BY CreateTime ASC");

            var taskList = pendingTasks.ToList();
            if (taskList.Count == 0)
            {
                return; // 没有待处理的任务
            }

            _logger.LogInformation($"发现 {taskList.Count} 个待处理的API任务");

            foreach (var task in taskList)
            {
                try
                {
                    // 获取任务对应的API端点
                    string endpoint = GetTaskEndpoint(task);
                    if (string.IsNullOrEmpty(endpoint))
                    {
                        _logger.LogWarning($"任务 {task.ID} 未配置API端点，跳过处理");
                        continue;
                    }
                    
                    // 检查该端点的网络连接状态
                    Uri uri = new Uri(endpoint);
                    string host = uri.Host;
                    
                    // 如果这是新的端点，先检查网络状态
                    if (!_endpointNetworkStatus.ContainsKey(host))
                    {
                        bool isConnected = await CheckNetworkConnectionAsync(host);
                        _endpointNetworkStatus[host] = isConnected;
                        _logger.LogInformation("新API端点 {Host} 的网络状态: {Status}", 
                            host, isConnected ? "在线" : "离线");
                    }
                    
                    // 如果网络不通，跳过该任务
                    if (!_endpointNetworkStatus[host])
                    {
                        _logger.LogWarning($"API端点 {host} 网络不通，跳过任务 {task.ID}，将在下次网络恢复后处理");
                        continue;
                    }
                    
                    // 处理任务
                    bool success = await ProcessTaskAsync(task);
                    
                    // 更新任务状态
                    await UpdateTaskStatusAsync(connection, task.ID, success, success ? "执行成功" : "执行失败");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"处理API任务 {task.ID} 时发生异常");
                    await UpdateTaskStatusAsync(connection, task.ID, false, $"执行异常: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 获取任务对应的API端点
        /// </summary>
        private string GetTaskEndpoint(RCS_ApiTask task)
        {
            // 这里可以根据task的属性来决定使用哪个端点
            // 例如，可以从task.Message中解析，或者根据task.TaskType选择不同的端点
            
            // 目前简单返回默认端点
            return _defaultEndpoint;
        }

        /// <summary>
        /// 处理单个API任务
        /// </summary>
        private async Task<bool> ProcessTaskAsync(RCS_ApiTask task)
        {
            _logger.LogInformation($"开始处理API任务: ID={task.ID}, 类型={task.TaskType}, 任务编号={task.TaskCode}");
            
            // 根据任务类型执行不同的处理逻辑
            switch (task.TaskType)
            {
                case 1: // HTTP请求
                    return await SendHttpRequestAsync(task);
                case 2: // WebSocket
                    _logger.LogWarning($"暂不支持WebSocket类型的任务: {task.ID}");
                    return false;
                case 3: // TCP/IP
                    _logger.LogWarning($"暂不支持TCP/IP类型的任务: {task.ID}");
                    return false;
                case 4: // UDP
                    _logger.LogWarning($"暂不支持UDP类型的任务: {task.ID}");
                    return false;
                default:
                    _logger.LogWarning($"未知的任务类型: {task.TaskType}, 任务ID: {task.ID}");
                    return false;
            }
        }

        /// <summary>
        /// 发送HTTP请求
        /// </summary>
        private async Task<bool> SendHttpRequestAsync(RCS_ApiTask task)
        {
            try
            {
                // 获取API URL
                string url = GetTaskEndpoint(task);
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogError("未配置API端点");
                    return false;
                }
                
                _logger.LogInformation($"发送API请求: {url}, 任务ID: {task.ID}");
                
                // 构建请求参数 - 这里可以根据实际需求进行修改
                var requestData = new
                {
                    taskCode = task.TaskCode,
                    taskType = task.TaskType,
                    // 其他参数可以根据需要从task.Message中解析或者从配置中获取
                };
                
                var jsonContent = JsonConvert.SerializeObject(requestData);
                
                using (var httpClient = new HttpClient())
                {
                    // 设置超时时间
                    httpClient.Timeout = TimeSpan.FromSeconds(_configuration.GetValue<int>("ApiTask:TimeoutSeconds", 30));
                    
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var resultJson = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"API请求响应: {resultJson}, 任务ID: {task.ID}");
                        
                        // 解析响应 - 根据实际接口返回格式进行解析
                        try
                        {
                            var result = JsonConvert.DeserializeObject<dynamic>(resultJson);
                            if (result != null && (result.status?.ToString() == "Success" || result.success?.ToString().ToLower() == "true"))
                            {
                                _logger.LogInformation($"API任务 {task.ID} 执行成功");
                                return true;
                            }
                            else
                            {
                                _logger.LogWarning($"API任务 {task.ID} 执行失败，响应: {resultJson}");
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"解析API响应失败: {ex.Message}, 原始响应: {resultJson}");
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"API请求失败，响应状态码: {response.StatusCode}, 任务ID: {task.ID}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"发送API请求异常，任务ID: {task.ID}");
                return false;
            }
        }

        /// <summary>
        /// 更新任务状态
        /// </summary>
        private async Task UpdateTaskStatusAsync(System.Data.IDbConnection connection, int taskId, bool success, string message)
        {
            try
            {
                await connection.ExecuteAsync(@"
                    UPDATE RCS_ApiTask
                    SET Excute = @Excute, 
                        Message = @Message
                    WHERE ID = @ID",
                    new
                    {
                        ID = taskId,
                        Excute = success,
                        Message = message
                    });
                
                _logger.LogInformation($"已更新API任务状态: ID={taskId}, 成功={success}, 消息={message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新API任务状态失败，任务ID: {taskId}");
            }
        }
    }
} 