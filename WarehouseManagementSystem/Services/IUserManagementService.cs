using WarehouseManagementSystem.Models;

namespace WarehouseManagementSystem.Services
{
    public interface IUserManagementService
    {
        Task<List<User>> GetAllUsersAsync();
        Task<User?> GetUserByIdAsync(int userId);
        Task<bool> CreateUserAsync(User user);
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(int userId);
        Task<bool> AssignPermissionAsync(int userId, int permissionId, int assignedBy);
        Task<bool> RemovePermissionAsync(int userId, int permissionId);

        Task<bool> UpdateUserPasswordAsync(int userId, string newPasswordHash);

        Task<List<Permission>> GetAllPermissionsAsync();
        Task<List<Permission>> GetUserPermissionsAsync(int userId);
    }
}
