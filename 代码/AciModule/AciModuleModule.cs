using AciModule.Domain.BackgroundWorkers;
using AciModule.Domain.Utils;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Modularity;

namespace AciModule
{
    public class AciModuleModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            // 注册HTTP请求帮助类
            context.Services.AddTransient<HttpRequestHelper>();

            // 注册交管后台工作者
            context.Services.AddTransient<TrafficControlTaskWorker>();
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            // 启动交管后台工作者
            var backgroundWorkerManager = context.ServiceProvider.GetRequiredService<IBackgroundWorkerManager>();
            backgroundWorkerManager.AddAsync(
                context.ServiceProvider.GetRequiredService<TrafficControlTaskWorker>()
            ).Wait();
        }
    }
} 