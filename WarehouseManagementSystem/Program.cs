using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog;
using WarehouseManagementSystem.Service.Io;
using WarehouseManagementSystem.Db;
using Microsoft.Data.SqlClient;
using System.Data;
using WarehouseManagementSystem.Service.Plc;
using WarehouseManagementSystem.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using Microsoft.AspNetCore.ResponseCompression;
using WarehouseManagementSystem.Data;
using WarehouseManagementSystem.Middleware;

var builder = WebApplication.CreateBuilder(args);

// 确保日志目录存在
var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "Logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

// 配置 Serilog
var logPath = Path.Combine(logDirectory, "RCS-Pad-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Filter.ByExcluding(logEvent =>
        logEvent.MessageTemplate.Text.Contains("Request starting HTTP") ||
        logEvent.MessageTemplate.Text.Contains("Executing endpoint") ||
        logEvent.MessageTemplate.Text.Contains("Executed endpoint") ||
        logEvent.MessageTemplate.Text.Contains("Request finished") ||
        logEvent.MessageTemplate.Text.Contains("Route matched") ||
        logEvent.MessageTemplate.Text.Contains("Executing action") ||
        logEvent.MessageTemplate.Text.Contains("Executed action"))
    .Enrich.FromLogContext()
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Debug)
    .WriteTo.File(logPath,
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Debug,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 31,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1)
    )
    .CreateLogger();

// 使用 Serilog
builder.Host.UseSerilog();

// 添加数据库连接管理
builder.Services.AddScoped<ApplicationDbContext>();

// 添加数据库连接注册
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new SqlConnection(connectionString);
});

// 核心服务 - 只保留必要的服务
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
// Pending condition task promotion is disabled: mobile task assignment now fails fast with a user-facing reason.
// builder.Services.AddHostedService<PendingConditionTaskService>();

// 添加内存缓存
builder.Services.AddMemoryCache();

// 添加HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// 配置性能选项
builder.Services.Configure<WarehouseManagementSystem.Middleware.PerformanceOptions>(
    builder.Configuration.GetSection("Performance"));

#region IO服务
//IO
//方法注册
builder.Services.AddSingleton<IIOService, IOService>();
builder.Services.AddSingleton<IIODeviceService, IODeviceService>();
//服务启动
builder.Services.AddSingleton<IOAGVTaskProcessor>();
// 注册后台服务
builder.Services.AddHostedService<IOProcessorService>();
#endregion

//#region PLC服务
//builder.Services.AddSingleton<WarehouseManagementSystem.Service.Plc.IPlcSignalService, WarehouseManagementSystem.Service.Plc.PlcSignalService>();

//// 添加HttpClient工厂
//builder.Services.AddHttpClient();

//// 添加PLC通信服务
//builder.Services.AddSingleton<WarehouseManagementSystem.Service.Plc.PlcSignalUpdater>();


//builder.Services.AddSingleton<WarehouseManagementSystem.Service.Plc.IPlcCommunicationService, WarehouseManagementSystem.Service.Plc.PlcCommunicationService>();

//// 注册PLC通信后台服务--PLC信号检测入口
//builder.Services.AddHostedService<WarehouseManagementSystem.Service.Plc.PlcCommunicationHostedService>();

//// 注册PLC任务处理器--处理AutoPlcTask表中的写入任务
//builder.Services.AddHostedService<WarehouseManagementSystem.Service.Plc.PlcTaskProcessor>();

//// 注册心跳服务-每秒写入一次心跳，用true false的格式
//builder.Services.AddHostedService<WarehouseManagementSystem.Service.Plc.HeartbeatService>();
//#endregion

//#region API服务
//// 注册API任务处理服务
//builder.Services.AddHostedService<WarehouseManagementSystem.Services.ApiTaskProcessorService>();
//#endregion


// 注册物料管理模块相关服务
builder.Services.AddScoped<IMaterialService, MaterialService>();

// 添加权限系统服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddHttpContextAccessor();

// 添加会话服务
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 添加本地化服务
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// 添加视图本地化服务
builder.Services.AddMvc()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "zh-CN", "en-US" };
    options.DefaultRequestCulture = new RequestCulture("zh-CN");
    options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
});

// MVC控制器
builder.Services.AddControllersWithViews();

// 添加响应压缩 - 暂时禁用以解决解码错误
// builder.Services.AddResponseCompression(options =>
// {
//     options.EnableForHttps = true;
//     options.Providers.Add<BrotliCompressionProvider>();
//     options.Providers.Add<GzipCompressionProvider>();
// });

// 配置Kestrel服务器 - 仅在非IIS环境下使用
if (!builder.Environment.IsDevelopment() && !OperatingSystem.IsWindows())
{
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        // 仅在非Windows环境下配置端口
        serverOptions.ListenAnyIP(5056);
        
        // 性能优化配置
        serverOptions.Limits.MaxConcurrentConnections = 100;
        serverOptions.Limits.MaxConcurrentUpgradedConnections = 100;
        serverOptions.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30MB
        serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
        serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
        serverOptions.Limits.MaxRequestBufferSize = 1024 * 1024; // 1MB
        serverOptions.Limits.MaxResponseBufferSize = 64 * 1024; // 64KB
    });
}
else
{
    // Windows环境下的性能优化
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Limits.MaxConcurrentConnections = 100;
        serverOptions.Limits.MaxConcurrentUpgradedConnections = 100;
        serverOptions.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30MB
        serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
        serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
        serverOptions.Limits.MaxRequestBufferSize = 1024 * 1024; // 1MB
        serverOptions.Limits.MaxResponseBufferSize = 64 * 1024; // 64KB
    });
}

var app = builder.Build();

// 配置中间件
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// 启用响应压缩 - 暂时禁用以解决解码错误
// app.UseResponseCompression();

// 配置静态文件中间件
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // 为静态文件添加缓存头
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
    }
});

// 使用性能优化中间件
app.UseMiddleware<WarehouseManagementSystem.Middleware.PerformanceMiddleware>();

// 添加会话支持
app.UseSession();

// 使用权限中间件
app.UseMiddleware<PermissionMiddleware>();

app.UseRouting();

app.UseAuthorization();

// 启用本地化
var locOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>();
app.UseRequestLocalization(locOptions.Value);

// 简化路由
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
