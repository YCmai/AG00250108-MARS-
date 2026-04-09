using Microsoft.AspNetCore.Mvc;
using WarehouseManagementSystem.Models;
using WarehouseManagementSystem.Services;

namespace WarehouseManagementSystem.Controllers
{
    // 用户管理控制器
    public class UserManagementController : Controller
    {
        private readonly IUserManagementService _userManagementService;
        private readonly IAuthService _authService;

        public UserManagementController(IUserManagementService userManagementService, IAuthService authService)
        {
            _userManagementService = userManagementService;
            _authService = authService;
        }

        // 用户列表页面
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // 检查管理员权限 - Check admin permissions
            var isAdmin = await _authService.GetUserByIdAsync(userId.Value);
            if (isAdmin?.IsAdmin != true)
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var users = await _userManagementService.GetAllUsersAsync();
            return View(users);
        }

        // 创建用户页面
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // 创建用户操作
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User user, string ConfirmPassword)
        {
            if (!ModelState.IsValid)
            {
                return View(user);
            }

            // 验证密码确认 - Validate password confirmation
            if (user.Password != ConfirmPassword)
            {
                ModelState.AddModelError("", "Password and confirm password do not match");
                return View(user);
            }

            // 验证密码长度 - Validate password length
            if (user.Password.Length < 6)
            {
                ModelState.AddModelError("Password", "Password must be at least 6 characters long");
                return View(user);
            }

            // 检查用户名是否已存在 - Check if username already exists
            var existingUser = await _userManagementService.GetAllUsersAsync();
            if (existingUser.Any(u => u.Username == user.Username))
            {
                ModelState.AddModelError("Username", "Username already exists");
                return View(user);
            }

            // 加密密码 - Encrypt password
            user.Password = _authService.HashPassword(user.Password);
            user.CreatedAt = DateTime.Now;

            var success = await _userManagementService.CreateUserAsync(user);
            if (success)
            {
                TempData["SuccessMessage"] = "User created successfully";
                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Failed to create user");
            return View(user);
        }

        // 编辑用户页面
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManagementService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // 更新用户操作
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(User user)
        {
            if (!ModelState.IsValid)
            {
                return View(user);
            }

            var success = await _userManagementService.UpdateUserAsync(user);
            if (success)
            {
                TempData["SuccessMessage"] = "User updated successfully";
                return RedirectToAction("Index");
            }

            ModelState.AddModelError("", "Failed to update user");
            return View(user);
        }

        // 删除用户操作
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var success = await _userManagementService.DeleteUserAsync(id);
            if (success)
            {
                TempData["SuccessMessage"] = "User deleted successfully";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to delete user";
            }

            return RedirectToAction("Index");
        }

        // 用户权限管理页面
        [HttpGet]
        public async Task<IActionResult> Permissions(int id)
        {
            var user = await _userManagementService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var allPermissions = await _userManagementService.GetAllPermissionsAsync();
            var userPermissions = await _userManagementService.GetUserPermissionsAsync(id);

            ViewBag.User = user;
            ViewBag.AllPermissions = allPermissions;
            ViewBag.UserPermissions = userPermissions.Select(p => p.Id).ToList();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignPermissions(int userId, List<int> permissionIds)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
            {
                return Json(new { success = false, message = "未登录" });
            }

            // 检查管理员权限
            var isAdmin = await _authService.GetUserByIdAsync(currentUserId.Value);
            if (isAdmin?.IsAdmin != true)
            {
                return Json(new { success = false, message = "权限不足" });
            }

            var user = await _userManagementService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "用户不存在" });
            }

            try
            {
                // 获取当前用户权限
                var currentPermissions = await _userManagementService.GetUserPermissionsAsync(userId);
                var currentPermissionIds = currentPermissions.Select(p => p.Id).ToList();

                // 添加新权限
                var permissionsToAdd = permissionIds?.Where(id => !currentPermissionIds.Contains(id)).ToList() ?? new List<int>();
                foreach (var permissionId in permissionsToAdd)
                {
                    await _userManagementService.AssignPermissionAsync(userId, permissionId, currentUserId.Value);
                }

                // 移除取消的权限
                var permissionsToRemove = currentPermissionIds.Where(id => !(permissionIds?.Contains(id) ?? false)).ToList();
                foreach (var permissionId in permissionsToRemove)
                {
                    await _userManagementService.RemovePermissionAsync(userId, permissionId);
                }

                return Json(new { success = true, message = "权限分配成功" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "权限分配失败：" + ex.Message });
            }
        }
    }
}
