-- =============================================
-- 仓库管理系统权限数据库初始化脚本 (Dapper版本)
-- 创建时间: 2024年12月
-- 说明: 使用Dapper ORM的权限系统数据库表结构
-- =============================================

-- 检查并创建数据库表
-- =============================================

-- 1. 创建用户表
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
    PRINT '用户表 Users 创建成功';
END
ELSE
BEGIN
    PRINT '用户表 Users 已存在';
END
GO

-- 2. 创建权限表
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
    PRINT '权限表 Permissions 创建成功';
END
ELSE
BEGIN
    PRINT '权限表 Permissions 已存在';
END
GO

-- 3. 创建用户权限关联表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UserPermissions' AND xtype='U')
BEGIN
    CREATE TABLE [dbo].[UserPermissions] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [UserId] INT NOT NULL,
        [PermissionId] INT NOT NULL,
        [AssignedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([Id])
    );
    PRINT '用户权限关联表 UserPermissions 创建成功';
END
ELSE
BEGIN
    PRINT '用户权限关联表 UserPermissions 已存在';
END
GO

-- =============================================
-- 创建索引
-- =============================================

-- 用户表索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_Username')
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [dbo].[Users] ([Username]);
    PRINT '用户表用户名唯一索引创建成功';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_Email')
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [dbo].[Users] ([Email]) WHERE [Email] IS NOT NULL;
    PRINT '用户表邮箱唯一索引创建成功';
END
GO

-- 权限表索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Permissions_Code')
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [dbo].[Permissions] ([Code]);
    PRINT '权限表代码唯一索引创建成功';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Permissions_Controller_Action')
BEGIN
    CREATE INDEX [IX_Permissions_Controller_Action] ON [dbo].[Permissions] ([Controller], [Action]);
    PRINT '权限表控制器动作索引创建成功';
END
GO

-- 用户权限关联表索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissions_UserId_PermissionId')
BEGIN
    CREATE UNIQUE INDEX [IX_UserPermissions_UserId_PermissionId] ON [dbo].[UserPermissions] ([UserId], [PermissionId]);
    PRINT '用户权限关联表组合唯一索引创建成功';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissions_UserId')
BEGIN
    CREATE INDEX [IX_UserPermissions_UserId] ON [dbo].[UserPermissions] ([UserId]);
    PRINT '用户权限关联表用户ID索引创建成功';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissions_PermissionId')
BEGIN
    CREATE INDEX [IX_UserPermissions_PermissionId] ON [dbo].[UserPermissions] ([PermissionId]);
    PRINT '用户权限关联表权限ID索引创建成功';
END
GO

-- =============================================
-- 创建外键约束
-- =============================================

-- 用户权限关联表外键
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_UserPermissions_Users_UserId')
BEGIN
    ALTER TABLE [dbo].[UserPermissions]
    ADD CONSTRAINT [FK_UserPermissions_Users_UserId] 
    FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE;
    PRINT '用户权限关联表用户外键创建成功';
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_UserPermissions_Permissions_PermissionId')
BEGIN
    ALTER TABLE [dbo].[UserPermissions]
    ADD CONSTRAINT [FK_UserPermissions_Permissions_PermissionId] 
    FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions] ([Id]) ON DELETE CASCADE;
    PRINT '用户权限关联表权限外键创建成功';
END
GO

-- =============================================
-- 插入默认数据
-- =============================================

-- 插入默认权限
IF NOT EXISTS (SELECT * FROM [dbo].[Permissions] WHERE [Code] = 'TASK_MANAGEMENT')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Name], [Code], [Controller], [Action], [Description], [SortOrder]) VALUES
    ('任务管理', 'TASK_MANAGEMENT', 'Tasks', 'Index', '查看和管理AGV任务', 1),
    ('储位管理', 'LOCATION_MANAGEMENT', 'DisplayLocation', 'Index', '查看和管理仓库储位', 2),
    ('PLC信号管理', 'PLC_SIGNAL_MANAGEMENT', 'PlcSignalStatus', 'Index', '监控PLC设备信号状态', 3),
    ('物料管理', 'MATERIAL_MANAGEMENT', 'Material', 'Index', '管理物料信息', 4),
    ('用户管理', 'USER_MANAGEMENT', 'UserManagement', 'Index', '管理用户和权限分配', 5),
    ('系统设置', 'SETTINGS', 'Setting', 'Index', '系统配置和设置', 6);
    PRINT '默认权限数据插入成功';
END
ELSE
BEGIN
    PRINT '默认权限数据已存在';
END
GO

-- 插入默认管理员用户
IF NOT EXISTS (SELECT * FROM [dbo].[Users] WHERE [Username] = 'admin')
BEGIN
    -- 密码: admin123 (SHA256加密)
    INSERT INTO [dbo].[Users] ([Username], [Password], [Email], [IsAdmin], [IsActive], [CreatedAt])
    VALUES ('admin', '240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9', 'admin@example.com', 1, 1, GETUTCDATE());
    PRINT '默认管理员用户创建成功';
END
ELSE
BEGIN
    PRINT '默认管理员用户已存在';
END
GO

-- 为管理员分配所有权限
IF NOT EXISTS (SELECT * FROM [dbo].[UserPermissions] WHERE [UserId] = 1)
BEGIN
    INSERT INTO [dbo].[UserPermissions] ([UserId], [PermissionId])
    SELECT u.Id, p.Id
    FROM [dbo].[Users] u, [dbo].[Permissions] p
    WHERE u.Username = 'admin' AND u.IsAdmin = 1;
    PRINT '管理员权限分配成功';
END
ELSE
BEGIN
    PRINT '管理员权限已分配';
END
GO

-- =============================================
-- 创建存储过程
-- =============================================

-- 创建用户登录验证存储过程
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'sp_ValidateUserLogin')
    DROP PROCEDURE [dbo].[sp_ValidateUserLogin]
GO

CREATE PROCEDURE [dbo].[sp_ValidateUserLogin]
    @Username NVARCHAR(50),
    @Password NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        u.Id,
        u.Username,
        u.Email,
        u.IsAdmin,
        u.IsActive,
        u.CreatedAt,
        u.LastLoginAt
    FROM [dbo].[Users] u
    WHERE u.Username = @Username 
        AND u.Password = @Password 
        AND u.IsActive = 1;
END
GO
PRINT '用户登录验证存储过程创建成功';

-- 创建获取用户权限存储过程
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'sp_GetUserPermissions')
    DROP PROCEDURE [dbo].[sp_GetUserPermissions]
GO

CREATE PROCEDURE [dbo].[sp_GetUserPermissions]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        p.Id,
        p.Name,
        p.Code,
        p.Controller,
        p.Action,
        p.Description,
        p.SortOrder
    FROM [dbo].[Permissions] p
    INNER JOIN [dbo].[UserPermissions] up ON p.Id = up.PermissionId
    WHERE up.UserId = @UserId 
        AND p.IsActive = 1
    ORDER BY p.SortOrder;
END
GO
PRINT '获取用户权限存储过程创建成功';

-- =============================================
-- 创建视图
-- =============================================

-- 创建用户权限视图
IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_UserPermissions')
    DROP VIEW [dbo].[vw_UserPermissions]
GO

CREATE VIEW [dbo].[vw_UserPermissions]
AS
SELECT 
    u.Id AS UserId,
    u.Username,
    u.Email,
    u.IsAdmin,
    u.IsActive AS UserIsActive,
    p.Id AS PermissionId,
    p.Name AS PermissionName,
    p.Code AS PermissionCode,
    p.Controller,
    p.Action,
    p.Description AS PermissionDescription,
    p.SortOrder,
    up.AssignedAt
FROM [dbo].[Users] u
LEFT JOIN [dbo].[UserPermissions] up ON u.Id = up.UserId
LEFT JOIN [dbo].[Permissions] p ON up.PermissionId = p.Id AND p.IsActive = 1
WHERE u.IsActive = 1;
GO
PRINT '用户权限视图创建成功';

-- =============================================
-- 完成提示
-- =============================================

PRINT '=============================================';
PRINT '仓库管理系统权限数据库初始化完成！';
PRINT '=============================================';
PRINT '默认管理员账户:';
PRINT '用户名: admin';
PRINT '密码: admin123';
PRINT '=============================================';
PRINT '数据库表结构:';
PRINT '- Users: 用户表';
PRINT '- Permissions: 权限表';
PRINT '- UserPermissions: 用户权限关联表';
PRINT '=============================================';
PRINT '存储过程:';
PRINT '- sp_ValidateUserLogin: 用户登录验证';
PRINT '- sp_GetUserPermissions: 获取用户权限';
PRINT '=============================================';
PRINT '视图:';
PRINT '- vw_UserPermissions: 用户权限视图';
PRINT '=============================================';
