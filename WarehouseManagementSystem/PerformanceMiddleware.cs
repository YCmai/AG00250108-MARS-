using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace WarehouseManagementSystem.Middleware
{
    public class PerformanceMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly IOptions<PerformanceOptions> _options;

        public PerformanceMiddleware(RequestDelegate next, IMemoryCache cache, IOptions<PerformanceOptions> options)
        {
            _next = next;
            _cache = cache;
            _options = options;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 添加性能相关的响应头
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Add("X-Frame-Options", "SAMEORIGIN");
            context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
            
            // 为静态资源添加缓存头
            if (context.Request.Path.StartsWithSegments("/css") || 
                context.Request.Path.StartsWithSegments("/js") || 
                context.Request.Path.StartsWithSegments("/images"))
            {
                context.Response.Headers.Add("Cache-Control", "public,max-age=31536000");
            }

            await _next(context);
        }
    }

    public class PerformanceOptions
    {
        public bool EnableResponseCompression { get; set; } = true;
        public bool EnableCaching { get; set; } = true;
        public int CacheTimeoutMinutes { get; set; } = 5;
        public int MaxConcurrentRequests { get; set; } = 100;
        public int DatabaseCommandTimeout { get; set; } = 30;
    }
} 