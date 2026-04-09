using System.ComponentModel.DataAnnotations;

namespace WarehouseManagementSystem.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "用户名不能为空")]
        [StringLength(50, ErrorMessage = "用户名长度不能超过50个字符")]
        public string Username { get; set; }
        
        [Required(ErrorMessage = "密码不能为空")]
        [StringLength(100, ErrorMessage = "密码长度不能超过100个字符")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        
        public bool RememberMe { get; set; }
        
        public string? ReturnUrl { get; set; }
    }
}
