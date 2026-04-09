using Microsoft.AspNetCore.Mvc;
using WarehouseManagementSystem.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace WarehouseManagementSystem.Controllers
{
    public class DatabaseController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DatabaseController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            try
            {
                using var connection = _context.GetConnection();
                connection.Open();
                
                // 测试数据库连接
                var result = connection.QueryFirstOrDefault<int>("SELECT 1");
                
                ViewBag.Message = "数据库连接成功！";
                ViewBag.ConnectionString = _context.GetConnectionString();
                ViewBag.TestResult = result;
                
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"数据库连接失败：{ex.Message}";
                ViewBag.ConnectionString = _context.GetConnectionString();
                ViewBag.Error = ex.ToString();
                
                return View();
            }
        }

        public IActionResult CreateTables()
        {
            try
            {
                using var connection = _context.GetConnection();
                connection.Open();
                
                // 创建用户表
                connection.Execute(@"
                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
                    BEGIN
                        CREATE TABLE [dbo].[Users] (
                            [Id] INT IDENTITY(1,1) NOT NULL,
                            [Username] NVARCHAR(50) NOT NULL,
                            [Password] NVARCHAR(256) NOT NULL,
                            [Email] NVARCHAR(256) NULL,
                            [IsAdmin] BIT NOT NULL DEFAULT 0,
                            [IsActive] BIT NOT NULL DEFAULT 1,
                            [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            [LastLoginAt] DATETIME2 NULL,
                            CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
                        );
                    END");
                
                // 创建权限表
                connection.Execute(@"
                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Permissions' AND xtype='U')
                    BEGIN
                        CREATE TABLE [dbo].[Permissions] (
                            [Id] INT IDENTITY(1,1) NOT NULL,
                            [Name] NVARCHAR(100) NOT NULL,
                            [Code] NVARCHAR(50) NOT NULL,
                            [Controller] NVARCHAR(100) NULL,
                            [Action] NVARCHAR(100) NULL,
                            [Description] NVARCHAR(MAX) NULL,
                            [SortOrder] INT NOT NULL DEFAULT 0,
                            [IsActive] BIT NOT NULL DEFAULT 1,
                            CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
                        );
                    END");
                
                // 创建用户权限关联表
                connection.Execute(@"
                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UserPermissions' AND xtype='U')
                    BEGIN
                        CREATE TABLE [dbo].[UserPermissions] (
                            [Id] INT IDENTITY(1,1) NOT NULL,
                            [UserId] INT NOT NULL,
                            [PermissionId] INT NOT NULL,
                            [AssignedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([Id])
                        );
                    END");
                
                // 插入默认权限
                connection.Execute(@"
                    IF NOT EXISTS (SELECT * FROM [dbo].[Permissions] WHERE [Code] = 'TASK_MANAGEMENT')
                    BEGIN
                        INSERT INTO [dbo].[Permissions] ([Name], [Code], [Controller], [Action], [Description], [SortOrder]) VALUES
                        ('任务管理', 'TASK_MANAGEMENT', 'Tasks', 'Index', '查看和管理AGV任务', 1),
                        ('储位管理', 'LOCATION_MANAGEMENT', 'DisplayLocation', 'Index', '查看和管理仓库储位', 2),
                        ('PLC信号管理', 'PLC_SIGNAL_MANAGEMENT', 'PlcSignalStatus', 'Index', '监控PLC设备信号状态', 3),
                        ('物料管理', 'MATERIAL_MANAGEMENT', 'Material', 'Index', '管理物料信息', 4),
                        ('用户管理', 'USER_MANAGEMENT', 'UserManagement', 'Index', '管理用户和权限分配', 5),
                        ('系统设置', 'SETTINGS', 'Setting', 'Index', '系统配置和设置', 6);
                    END");
                
                // 插入默认管理员用户
                connection.Execute(@"
                    IF NOT EXISTS (SELECT * FROM [dbo].[Users] WHERE [Username] = 'admin')
                    BEGIN
                        -- 密码: admin123 (SHA256加密)
                        INSERT INTO [dbo].[Users] ([Username], [Password], [Email], [IsAdmin], [IsActive], [CreatedAt])
                        VALUES ('admin', '240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9', 'admin@example.com', 1, 1, GETUTCDATE());
                    END");
                
                // 为管理员分配所有权限
                connection.Execute(@"
                    IF NOT EXISTS (SELECT * FROM [dbo].[UserPermissions] WHERE [UserId] = 1)
                    BEGIN
                        INSERT INTO [dbo].[UserPermissions] ([UserId], [PermissionId])
                        SELECT u.Id, p.Id
                        FROM [dbo].[Users] u, [dbo].[Permissions] p
                        WHERE u.Username = 'admin' AND u.IsAdmin = 1;
                    END");
                
                ViewBag.Message = "数据库表创建成功！";
                return View("Index");
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"创建表失败：{ex.Message}";
                ViewBag.Error = ex.ToString();
                return View("Index");
            }
        }
    }
}
