using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using WarehouseManagementSystem.Services;
using Microsoft.Extensions.DependencyInjection;

namespace WarehouseManagementSystem.Middleware
{
    public class PermissionMiddleware
    {
        private readonly RequestDelegate _next;

        public PermissionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLower();
            
            // 跳过不需要权限检查的路径
            if (ShouldSkipAuth(path))
            {
                await _next(context);
                return;
            }

            var userId = context.Session.GetInt32("UserId");
            if (userId == null)
            {
                // 添加小延迟确保Session完全写入
                await Task.Delay(50);
                userId = context.Session.GetInt32("UserId");
                
                if (userId == null)
                {
                    // 未登录，重定向到登录页
                    context.Response.Redirect("/Auth/Login?returnUrl=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString));
                    return;
                }
            }

            // 从服务容器中获取AuthService
            var authService = context.RequestServices.GetRequiredService<IAuthService>();
            
            // 检查用户是否存在
            var user = await authService.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                context.Session.Clear();
                context.Response.Redirect("/Auth/Login");
                return;
            }

            // 管理员拥有所有权限
            if (user.IsAdmin)
            {
                await _next(context);
                return;
            }

            // 检查权限
            var hasPermission = await CheckPermission(context, userId.Value, authService);
            if (!hasPermission)
            {
                context.Response.Redirect("/Home/AccessDenied");
                return;
            }

            await _next(context);
        }

        private bool ShouldSkipAuth(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            var skipPaths = new[]
            {
                "/auth/login",
                "/auth/logout",
                "/auth/ajaxlogin",
                "/auth/passwordgenerator",
                "/home/accessdenied",
                "/css/",
                "/js/",
                "/lib/",
                "/images/",
                "/favicon.ico",
                "/api/connectionstatus"
            };

            return skipPaths.Any(skipPath => path.StartsWith(skipPath));
        }

        private async Task<bool> CheckPermission(HttpContext context, int userId, IAuthService authService)
        {
            var controller = context.Request.RouteValues["controller"]?.ToString();
            var action = context.Request.RouteValues["action"]?.ToString();

            if (string.IsNullOrEmpty(controller))
                return true;

            // 根据控制器和动作确定需要的权限
            var permissionCode = GetPermissionCode(controller, action);
            if (string.IsNullOrEmpty(permissionCode))
                return true;

            return await authService.HasPermissionAsync(userId, permissionCode);
        }

        private string GetPermissionCode(string controller, string? action)
        {
            return controller.ToLower() switch
            {
                "displaylocation" => "DISPLAY_LOCATION",
                "location" => "LOCATION_MANAGE",
                "tasks" => "TASK_MANAGE",
                "plcsignalstatus" => "PLC_SIGNAL_STATUS",
                "autoplctask" => "PLC_TASK_INTERACTION",
                "plcsignal" => "PLC_SIGNAL_MANAGE",
                "iomonitor" => "IO_SIGNAL_MANAGE",
                "apitask" => "API_TASK_MANAGE",
                "logs" => "SYSTEM_LOG",
                "usermanagement" => "USER_MANAGEMENT",
                _ => ""
            };
        }
    }
}
