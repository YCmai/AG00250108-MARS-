using Microsoft.AspNetCore.Mvc;
using WarehouseManagementSystem.Models;
using WarehouseManagementSystem.Services;

namespace WarehouseManagementSystem.Controllers
{
    public class AuthController : Controller
    {
        private readonly IAuthService _authService;
        private readonly IUserManagementService _userManagementService;

        public AuthController(IAuthService authService, IUserManagementService userManagementService)
        {
            _authService = authService;
           _userManagementService = userManagementService;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            // 检查是否有"记住我"Cookie
            if (Request.Cookies.ContainsKey("RememberMe"))
            {
                var userIdStr = Request.Cookies["RememberMe"];
                if (int.TryParse(userIdStr, out int userId))
                {
                    var user = await _authService.GetUserByIdAsync(userId);
                    if (user != null && user.IsActive)
                    {
                        // 自动登录
                        HttpContext.Session.SetInt32("UserId", user.Id);
                        HttpContext.Session.SetString("Username", user.Username);
                        HttpContext.Session.SetString("FullName", user.DisplayName ?? user.Username);
                        
                        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        {
                            return Redirect(returnUrl);
                        }
                        return RedirectToAction("Index", "DisplayLocation");
                    }
                }
            }
            
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _authService.ValidateUserAsync(model.Username, model.Password);
            if (user == null)
            {
                ModelState.AddModelError("", "用户名或密码错误");
                return View(model);
            }

            // 设置会话
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("FullName", user.DisplayName ?? user.Username);

            if (model.RememberMe)
            {
                var options = new CookieOptions
                {
                    Expires = DateTime.Now.AddDays(30),
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict
                };
                Response.Cookies.Append("RememberMe", user.Id.ToString(), options);
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "DisplayLocation");
        }

        [HttpPost]
        public async Task<IActionResult> AjaxLogin([FromBody] LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "输入数据无效" });
            }

            var user = await _authService.ValidateUserAsync(model.Username, model.Password);
            if (user == null)
            {
                return Json(new { success = false, message = "用户名或密码错误" });
            }
            // 设置会话
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("FullName", user.DisplayName ?? user.Username);
            
            // 更新最后登录时间
            await _authService.UpdateLastLoginAsync(user.Id);
            
            // 确保Session写入完成
            await HttpContext.Session.CommitAsync();
            
            // 验证Session是否设置成功
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            Console.WriteLine($"Session设置完成，UserId: {sessionUserId}");

            // 处理"记住我"功能
            if (model.RememberMe)
            {
                var options = new CookieOptions
                {
                    Expires = DateTime.Now.AddDays(30),
                    HttpOnly = true,
                    Secure = false, // 在开发环境中设为false
                    SameSite = SameSiteMode.Lax
                };
                Response.Cookies.Append("RememberMe", user.Id.ToString(), options);
            }

            // 处理返回URL
            var redirectUrl = "/DisplayLocation/Index";
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) && model.ReturnUrl != "/")
            {
                redirectUrl = model.ReturnUrl;
            }
            
            return Json(new { success = true, message = "登录成功", redirectUrl = redirectUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete("RememberMe");
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult Profile()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Profile", model);
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login");
            }

            var user = await _authService.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            if (!_authService.VerifyPassword(model.CurrentPassword, user.Password))
            {
                ModelState.AddModelError("CurrentPassword", "当前密码错误");
                return View("Profile", model);
            }

            // 验证新密码
            if (model.NewPassword != model.ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "新密码和确认密码不匹配");
                return View("Profile", model);
            }

            if (model.NewPassword.Length < 6)
            {
                ModelState.AddModelError("NewPassword", "新密码长度至少6位");
                return View("Profile", model);
            }

            // 更新密码
            var newPasswordHash = _authService.HashPassword(model.NewPassword);
            var success = await _userManagementService.UpdateUserPasswordAsync(userId.Value, newPasswordHash);
            
            if (success)
            {
                TempData["SuccessMessage"] = "密码修改成功";
                return RedirectToAction("Profile");
            }
            else
            {
                ModelState.AddModelError("", "密码修改失败，请稍后重试");
                return View("Profile", model);
            }
        }

        [HttpGet]
        public IActionResult PasswordGenerator()
        {
            return View();
        }
    }
}

