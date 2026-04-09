using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using WarehouseManagementSystem.Models;

namespace WarehouseManagementSystem.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly string _connectionString;

        public UserManagementService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            
            var users = await connection.QueryAsync<User>(
                "SELECT * FROM Users WHERE IsActive = 1 ORDER BY CreatedAt DESC");
            
            return users.ToList();
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id",
                new { Id = userId });
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var result = await connection.ExecuteAsync(@"
                INSERT INTO Users (Username, Password, DisplayName, Email, IsActive, CreatedAt, IsAdmin)
                VALUES (@Username, @Password, @DisplayName, @Email, @IsActive, @CreatedAt, @IsAdmin)",
                user);
            
            return result > 0;
        }

       

        public async Task<bool> DeleteUserAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var result = await connection.ExecuteAsync(
                "UPDATE Users SET IsActive = 0 WHERE Id = @Id",
                new { Id = userId });
            
            return result > 0;
        }

        public async Task<bool> AssignPermissionAsync(int userId, int permissionId, int assignedBy)
        {
            using var connection = new SqlConnection(_connectionString);
            
            // 检查是否已经分配
            var exists = await connection.QuerySingleAsync<int>(@"
                SELECT COUNT(1) FROM UserPermissions 
                WHERE UserId = @UserId AND PermissionId = @PermissionId",
                new { UserId = userId, PermissionId = permissionId });
            
            if (exists > 0)
                return true;
            
            var result = await connection.ExecuteAsync(@"
                INSERT INTO UserPermissions (UserId, PermissionId, GrantedAt, GrantedBy,AssignedBy)
                VALUES (@UserId, @PermissionId, @GrantedAt, @GrantedBy,@AssignedBy)",
                new { UserId = userId, PermissionId = permissionId, GrantedAt = DateTime.Now, GrantedBy = assignedBy, AssignedBy = assignedBy });
            
            return result > 0;
        }

        public async Task<bool> RemovePermissionAsync(int userId, int permissionId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var result = await connection.ExecuteAsync(@"
                DELETE FROM UserPermissions 
                WHERE UserId = @UserId AND PermissionId = @PermissionId",
                new { UserId = userId, PermissionId = permissionId });
            
            return result > 0;
        }

        public async Task<List<Permission>> GetAllPermissionsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            
            var permissions = await connection.QueryAsync<Permission>(
                "SELECT * FROM Permissions WHERE IsActive = 1 ORDER BY SortOrder, Name");
            
            return permissions.ToList();
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

        public async Task<bool> UpdateUserPasswordAsync(int userId, string newPasswordHash)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var result = await connection.ExecuteAsync(@"
                UPDATE Users 
                SET Password = @Password, UpdatedAt = @UpdatedAt
                WHERE Id = @UserId",
                new { UserId = userId, Password = newPasswordHash, UpdatedAt = DateTime.Now });
            
            return result > 0;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            using var connection = new SqlConnection(_connectionString);
            
            var result = await connection.ExecuteAsync(@"
                UPDATE Users 
                SET Username = @Username, DisplayName = @DisplayName, Email = @Email, 
                    IsActive = @IsActive, IsAdmin = @IsAdmin, UpdatedAt = @UpdatedAt
                WHERE Id = @Id",
                new { 
                    Id = user.Id,
                    Username = user.Username, 
                    DisplayName = user.DisplayName,
                    Email = user.Email, 
                    IsActive = user.IsActive,
                    IsAdmin = user.IsAdmin,
                    UpdatedAt = DateTime.Now 
                });
            
            return result > 0;
        }

       
    }
}
