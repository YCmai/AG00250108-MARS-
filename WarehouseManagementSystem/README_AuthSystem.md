# 仓库管理系统 - 权限系统 (Dapper版本)

## 概述

本权限系统为仓库管理系统提供了完整的用户认证和权限管理功能，使用Dapper ORM进行数据库操作，包括用户登录、权限分配、模块访问控制等。

## 功能特性

### 1. 用户认证
- **登录页面**：美观的登录界面，支持用户名/密码登录
- **会话管理**：基于Session的用户会话管理
- **密码加密**：使用SHA256加密存储用户密码
- **记住我**：支持用户选择记住登录状态

### 2. 权限管理
- **模块级权限**：按功能模块分配权限（任务管理、储位管理、PLC信号管理等）
- **管理员权限**：管理员拥有所有权限，可以管理用户和权限分配
- **权限验证**：中间件自动验证用户访问权限
- **动态菜单**：根据用户权限动态显示导航菜单

### 3. 用户管理（管理员功能）
- **用户创建**：创建新用户账户
- **用户编辑**：修改用户信息和权限
- **用户删除**：软删除用户（设置为非活跃状态）
- **权限分配**：为用户分配或取消权限
- **批量操作**：支持批量权限管理

### 4. 个人设置
- **个人资料**：查看个人信息和权限
- **密码修改**：用户可以修改自己的密码
- **登录历史**：显示最后登录时间

## 数据库结构

### 用户表 (Users)
```sql
- Id: 主键
- Username: 用户名（唯一）
- PasswordHash: 密码（SHA256加密）
- Email: 邮箱（可选，唯一）
- IsAdmin: 是否为管理员
- IsActive: 是否启用
- CreatedAt: 创建时间
- LastLoginAt: 最后登录时间
```

### 权限表 (Permissions)
```sql
- Id: 主键
- Code: 权限代码（唯一）
- Name: 权限名称
- Description: 权限描述
- Controller: 控制器名称
- Action: 动作名称
- SortOrder: 排序顺序
- IsActive: 是否启用
```

### 用户权限关联表 (UserPermissions)
```sql
- Id: 主键
- UserId: 用户ID
- PermissionId: 权限ID
- AssignedAt: 分配时间
```

## 默认数据

系统初始化时会创建以下默认数据：

### 默认权限
1. **任务管理** (TASK_MANAGEMENT) - Tasks/Index
2. **储位管理** (LOCATION_MANAGEMENT) - DisplayLocation/Index
3. **PLC信号管理** (PLC_SIGNAL_MANAGEMENT) - PlcSignalStatus/Index
4. **物料管理** (MATERIAL_MANAGEMENT) - Material/Index
5. **用户管理** (USER_MANAGEMENT) - UserManagement/Index
6. **系统设置** (SETTINGS) - Setting/Index

### 默认管理员
- **用户名**: admin
- **密码**: admin123
- **权限**: 所有权限

## 安装和配置

### 1. 数据库设置
运行 `Migrations/CreateAuthTables_Dapper.sql` 脚本来创建权限相关的数据库表：

```sql
-- 在SQL Server Management Studio中执行
-- 或使用命令行工具执行SQL脚本
```

### 2. 服务注册
权限系统已在 `Program.cs` 中注册了必要的服务：

```csharp
// 数据库连接管理
builder.Services.AddScoped<ApplicationDbContext>();

// 认证和权限服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();

// Session支持
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// HttpContextAccessor
builder.Services.AddHttpContextAccessor();
```

### 3. 中间件配置
权限验证中间件已在 `Program.cs` 中配置：

```csharp
// 启用Session
app.UseSession();

// 使用权限验证中间件
app.UsePermissionMiddleware();
```

## 使用方法

### 1. 首次使用
1. 运行数据库脚本创建表结构
2. 启动应用程序
3. 使用默认管理员账户登录：
   - 用户名：admin
   - 密码：admin123
4. 进入用户管理页面创建新用户

### 2. 创建用户
1. 以管理员身份登录
2. 进入"用户管理"页面
3. 点击"新建用户"按钮
4. 填写用户信息并分配权限
5. 保存用户

### 3. 权限分配
1. 在用户管理页面选择要编辑的用户
2. 在权限分配区域选择/取消权限
3. 保存修改

### 4. 用户登录
1. 访问应用程序首页
2. 输入用户名和密码
3. 系统会根据用户权限显示相应的功能模块

## Dapper ORM 特性

### 1. 数据库连接管理
```csharp
public class ApplicationDbContext
{
    private readonly string _connectionString;
    
    public ApplicationDbContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }
    
    public SqlConnection GetConnection()
    {
        return new SqlConnection(_connectionString);
    }
}
```

### 2. 数据访问示例
```csharp
// 用户登录验证
using var connection = _context.GetConnection();
await connection.OpenAsync();

var user = await connection.QueryFirstOrDefaultAsync<User>(@"
    SELECT Id, Username, PasswordHash, Email, IsAdmin, CreatedAt, LastLoginAt
    FROM Users 
    WHERE Username = @Username AND IsActive = 1", 
    new { Username = request.Username });
```

### 3. 存储过程支持
系统提供了以下存储过程：
- `sp_ValidateUserLogin`: 用户登录验证
- `sp_GetUserPermissions`: 获取用户权限

### 4. 视图支持
- `vw_UserPermissions`: 用户权限视图，便于复杂查询

## 安全特性

### 1. 密码安全
- 使用SHA256加密存储密码
- 支持密码修改功能
- 密码强度验证

### 2. 会话安全
- 基于Session的会话管理
- 会话超时自动登出
- 安全的Cookie设置

### 3. 权限验证
- 中间件级别的权限验证
- 防止未授权访问
- 动态菜单显示

### 4. 数据安全
- 软删除用户（不物理删除）
- 外键约束保证数据完整性
- 索引优化查询性能

## 扩展功能

### 1. 添加新权限
1. 在数据库中添加新的权限记录
2. 在相应的控制器中添加权限检查
3. 更新导航菜单显示逻辑

### 2. 自定义权限验证
可以在控制器或Action方法中添加自定义权限检查：

```csharp
[HttpGet]
public async Task<IActionResult> SomeAction()
{
    var currentUser = await _authService.GetCurrentUserAsync();
    if (currentUser == null || !currentUser.IsAdmin)
    {
        return RedirectToAction("AccessDenied", "Auth");
    }
    
    // 业务逻辑
    return View();
}
```

### 3. 权限日志
可以扩展系统添加权限操作日志：

```csharp
// 记录权限分配操作
_logger.LogInformation("用户 {UserId} 被分配了权限 {PermissionId}", userId, permissionId);
```

## 故障排除

### 1. 登录问题
- 检查用户名和密码是否正确
- 确认用户账户是否启用
- 检查Session配置是否正确

### 2. 权限问题
- 确认用户是否被分配了相应权限
- 检查权限中间件是否正确配置
- 验证数据库中的权限数据

### 3. 数据库问题
- 确认数据库表是否正确创建
- 检查外键约束是否正常
- 验证默认数据是否正确插入

### 4. Dapper相关问题
- 确认数据库连接字符串配置正确
- 检查SQL查询语法是否正确
- 验证参数绑定是否正确

## 技术栈

- **后端**: ASP.NET Core MVC
- **数据库**: SQL Server
- **ORM**: Dapper
- **前端**: Bootstrap 5 + Font Awesome
- **认证**: Session-based Authentication
- **加密**: SHA256

## 版本信息

- **版本**: 1.0.0 (Dapper版本)
- **创建日期**: 2024年12月
- **兼容性**: .NET 6.0+
- **数据库**: SQL Server 2016+
