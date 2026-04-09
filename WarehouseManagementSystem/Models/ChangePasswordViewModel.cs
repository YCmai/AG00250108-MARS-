using System.ComponentModel.DataAnnotations;

namespace WarehouseManagementSystem.Models
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "当前密码不能为空")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; }
        
        [Required(ErrorMessage = "新密码不能为空")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "密码长度必须在6-100个字符之间")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }
        
        [Required(ErrorMessage = "确认密码不能为空")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "确认密码与新密码不匹配")]
        public string ConfirmPassword { get; set; }
    }
}
