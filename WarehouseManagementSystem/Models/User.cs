using System.ComponentModel.DataAnnotations;

namespace WarehouseManagementSystem.Models
{
    public class User
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Username { get; set; }
        
        [Required]
        [StringLength(255)]
        public string Password { get; set; }
        
        [StringLength(100)]
        public string? DisplayName { get; set; }
        
        [StringLength(100)]
        public string? Email { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public DateTime? LastLoginAt { get; set; }


        public DateTime? UpdatedAt { get; set; }
        
        public bool IsAdmin { get; set; } = false;
    }
}
