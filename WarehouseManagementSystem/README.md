# 仓库管理系统 (Warehouse Management System)

## 项目简介

这是一个基于ASP.NET Core的仓库管理系统，提供储位管理、任务管理、PLC信号监控等功能。

## 功能特性

- ✅ **储位管理**：实时显示储位状态，支持空闲、占用、锁定状态
- ✅ **任务管理**：任务创建、执行、监控和统计
- ✅ **PLC信号管理**：PLC信号状态监控和交互
- ✅ **权限系统**：用户登录、权限管理和会话控制
- ✅ **数据导出**：支持Excel格式导出
- ✅ **实时监控**：系统状态实时更新

## 技术栈

- **后端**：ASP.NET Core 6.0
- **数据库**：SQL Server
- **ORM**：Dapper
- **前端**：Bootstrap 5, jQuery, Chart.js
- **样式**：Font Awesome, 自定义CSS

## 快速开始

### 环境要求

- .NET 6.0 SDK
- SQL Server
- Visual Studio 2022 或 VS Code

### 安装步骤

1. **克隆仓库**
   ```bash
   git clone https://github.com/yourusername/warehouse-management-system.git
   cd warehouse-management-system
   ```

2. **配置数据库**
   ```bash
   # 执行数据库迁移脚本
   sqlcmd -S your-server -d your-database -i Migrations/CreateAuthTables.sql
   ```

3. **配置连接字符串**
   ```json
   // appsettings.json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=your-server;Database=your-database;Trusted_Connection=true;"
     }
   }
   ```

4. **运行应用**
   ```bash
   dotnet run
   ```

5. **访问应用**
   - 默认地址：http://localhost:5055
   - 默认账号：admin / admin123

## 项目结构

```
WarehouseManagementSystem/
├── Controllers/          # 控制器
├── Services/            # 业务服务
├── Models/              # 数据模型
├── Views/               # 视图文件
├── wwwroot/            # 静态资源
├── Migrations/         # 数据库迁移
├── Middleware/         # 中间件
└── Logs/              # 日志文件
```

## 权限系统

### 默认账号
- **用户名**：admin
- **密码**：admin123
- **权限**：所有模块权限

### 密码编码标准
- **算法**：SHA256哈希 + Base64编码
- **示例**：admin123 → JAv1GPq9JyTdtvB06x211nRI1+gxwIyPqCKAn3THIKk=

## 开发指南

### 分支策略
- `main`：生产环境代码
- `develop`：开发环境代码
- `feature/*`：功能开发分支
- `hotfix/*`：紧急修复分支

### 提交规范
```bash
# 功能开发
git commit -m "feat: 添加用户管理功能"

# 问题修复
git commit -m "fix: 修复登录重定向问题"

# 文档更新
git commit -m "docs: 更新README文档"

# 样式调整
git commit -m "style: 优化登录页面样式"
```

## 部署说明

### 生产环境配置
1. 更新 `appsettings.Production.json`
2. 配置生产数据库连接
3. 设置日志级别
4. 配置HTTPS证书

### Docker部署
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:6.0
COPY . /app
WORKDIR /app
EXPOSE 80
ENTRYPOINT ["dotnet", "WarehouseManagementSystem.dll"]
```

## 常见问题

### Q: 登录后页面样式异常？
A: 检查 `_Layout.cshtml` 中的CSS引用路径是否正确。

### Q: 数据库连接失败？
A: 检查连接字符串和数据库服务是否正常运行。

### Q: 权限验证失败？
A: 确认用户权限已正确分配，检查中间件配置。

## 更新日志

### v1.0.0 (2024-01-XX)
- ✅ 基础储位管理功能
- ✅ 权限系统集成
- ✅ 任务管理模块
- ✅ PLC信号监控

## 贡献指南

1. Fork 本仓库
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

## 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 联系方式

- 项目维护者：[您的姓名]
- 邮箱：[your-email@example.com]
- 项目地址：[https://github.com/yourusername/warehouse-management-system](https://github.com/yourusername/warehouse-management-system)
