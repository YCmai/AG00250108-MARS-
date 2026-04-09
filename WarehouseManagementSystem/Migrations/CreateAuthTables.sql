-- 创建权限系统相关表
-- 用户表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
BEGIN
    CREATE TABLE Users (
        Id int IDENTITY(1,1) PRIMARY KEY,
        Username nvarchar(50) NOT NULL UNIQUE,
        PasswordHash nvarchar(255) NOT NULL,
        FullName nvarchar(100),
        Email nvarchar(100),
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETDATE(),
        LastLoginAt datetime2,
        IsAdmin bit NOT NULL DEFAULT 0
    );
END

-- 权限表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Permissions' AND xtype='U')
BEGIN
    CREATE TABLE Permissions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        Code nvarchar(50) NOT NULL UNIQUE,
        Name nvarchar(100) NOT NULL,
        Description nvarchar(200),
        Controller nvarchar(100),
        Action nvarchar(100),
        IsActive bit NOT NULL DEFAULT 1,
        SortOrder int NOT NULL DEFAULT 0
    );
END

-- 用户权限关联表
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UserPermissions' AND xtype='U')
BEGIN
    CREATE TABLE UserPermissions (
        Id int IDENTITY(1,1) PRIMARY KEY,
        UserId int NOT NULL,
        PermissionId int NOT NULL,
        GrantedAt datetime2 NOT NULL DEFAULT GETDATE(),
        GrantedBy int,
        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
        FOREIGN KEY (PermissionId) REFERENCES Permissions(Id) ON DELETE CASCADE,
        FOREIGN KEY (GrantedBy) REFERENCES Users(Id),
        UNIQUE(UserId, PermissionId)
    );
END

-- 插入默认权限
INSERT INTO Permissions (Code, Name, Description, Controller, Action, SortOrder) VALUES
('DISPLAY_LOCATION', '储位显示', '查看储位状态', 'DisplayLocation', 'Index', 1),
('LOCATION_MANAGE', '储位管理', '管理储位设置', 'Location', 'Index', 2),
('TASK_MANAGE', '任务管理', '管理任务', 'Tasks', 'Index', 3),
('PLC_SIGNAL_STATUS', 'PLC信号显示', '查看PLC信号状态', 'PlcSignalStatus', 'Index', 4),
('PLC_TASK_INTERACTION', 'PLC任务交互', 'PLC任务交互管理', 'AutoPlcTask', 'Index', 5),
('PLC_SIGNAL_MANAGE', 'PLC信号管理', '管理PLC信号', 'PlcSignal', 'Index', 6),
('IO_SIGNAL_MANAGE', 'IO信号管理', '管理IO信号', 'IOMonitor', 'Index', 7),
('API_TASK_MANAGE', 'API任务管理', '管理API任务', 'ApiTask', 'Index', 8),
('SYSTEM_LOG', '系统日志', '查看系统日志', 'Logs', 'Index', 9),
('USER_MANAGEMENT', '用户管理', '管理用户和权限', 'UserManagement', 'Index', 10);

-- 插入默认管理员用户
-- 密码: admin123
-- 哈希算法: SHA256 + Base64编码
-- 哈希值: JAv1GPq9JyTdtvB06x211nRI1+gxwIyPqCKAn3THIKk=
INSERT INTO Users (Username, PasswordHash, FullName, IsAdmin, IsActive) VALUES
('admin', 'JAv1GPq9JyTdtvB06x211nRI1+gxwIyPqCKAn3THIKk=', '系统管理员', 1, 1);

-- 为管理员分配所有权限
DECLARE @AdminUserId int = (SELECT Id FROM Users WHERE Username = 'admin');
DECLARE @PermissionId int;

DECLARE permission_cursor CURSOR FOR 
SELECT Id FROM Permissions WHERE IsActive = 1;

OPEN permission_cursor;
FETCH NEXT FROM permission_cursor INTO @PermissionId;

WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO UserPermissions (UserId, PermissionId) VALUES (@AdminUserId, @PermissionId);
    FETCH NEXT FROM permission_cursor INTO @PermissionId;
END

CLOSE permission_cursor;
DEALLOCATE permission_cursor;

PRINT '权限系统初始化完成！';
PRINT '默认管理员账号：admin';
PRINT '默认密码：admin123';
