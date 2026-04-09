using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using WarehouseManagementSystem.Models;

namespace WarehouseManagementSystem.Services
{
    public class AuthService : IAuthService
    {
        private readonly string _connectionString;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<User?> ValidateUserAsync(string username, string password)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var user = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Username = @Username AND IsActive = 1",
                new { Username = username });

            if (user == null)
            {
                return null;
            }

            var isValid = VerifyPassword(password, user.Password);

            if (isValid)
            {
                await UpdateLastLoginAsync(user.Id);
                return user;
            }

            return null;
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id AND IsActive = 1",
                new { Id = userId });
        }

        public async Task<List<Permission>> GetUserPermissionsAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var permissions = await connection.QueryAsync<Permission>(@"
                SELECT p.* FROM Permissions p
                INNER JOIN UserPermissions up ON p.Id = up.PermissionId
                WHERE up.UserId = @UserId AND p.IsActive = 1
                ORDER BY p.SortOrder, p.Name",
                new { UserId = userId });

            return permissions.ToList();
        }

        public async Task<bool> HasPermissionAsync(int userId, string permissionCode)
        {
            using var connection = new SqlConnection(_connectionString);
            
            // 检查用户是否有指定权限
            var hasPermission = await connection.QuerySingleAsync<int>(@"
                SELECT COUNT(1) 
                FROM UserPermissions up
                INNER JOIN Permissions p ON up.PermissionId = p.Id
                WHERE up.UserId = @UserId AND p.Code = @PermissionCode",
                new { UserId = userId, PermissionCode = permissionCode });
            
            return hasPermission > 0;
        }

        public async Task UpdateLastLoginAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            await connection.ExecuteAsync(
                "UPDATE Users SET LastLoginAt = @LastLoginAt WHERE Id = @Id",
                new { Id = userId, LastLoginAt = DateTime.Now });
        }

        public string HashPassword(string password)
        {
            // 使用SHA256哈希，然后转换为Base64字符串
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        public bool VerifyPassword(string password, string hash)
        {
            // 统一使用SHA256哈希验证
            var hashedPassword = HashPassword(password);
            var isValid = hashedPassword == hash;
            
            
            return isValid;
        }

     
    }
}
