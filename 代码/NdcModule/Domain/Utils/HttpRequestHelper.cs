using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AciModule.Domain.Utils
{
    /// <summary>
    /// HTTP请求工具类
    /// </summary>
    public class HttpRequestHelper
    {
        private readonly ILogger<HttpRequestHelper> _logger;
        private readonly HttpClient _httpClient;

        public HttpRequestHelper(ILogger<HttpRequestHelper> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// 发送GET请求
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <returns>响应内容</returns>
        public async Task<string> GetAsync(string url)
        {
            try
            {
                _logger.LogInformation($"发送GET请求: {url}");
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"GET请求响应: {content}");
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError($"GET请求异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 发送POST请求
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="content">请求内容</param>
        /// <returns>响应内容</returns>
        public async Task<string> PostAsync(string url, HttpContent content)
        {
            try
            {
                _logger.LogInformation($"发送POST请求: {url}");
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"POST请求响应: {responseContent}");
                return responseContent;
            }
            catch (Exception ex)
            {
                _logger.LogError($"POST请求异常: {ex.Message}");
                throw;
            }
        }
    }
} 