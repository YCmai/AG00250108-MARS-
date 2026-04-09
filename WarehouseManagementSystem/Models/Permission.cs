using System.ComponentModel.DataAnnotations;

namespace WarehouseManagementSystem.Models
{
    public class Permission
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Code { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; }
        
        [StringLength(200)]
        public string? Description { get; set; }
        
        [StringLength(100)]
        public string? Controller { get; set; }
        
        [StringLength(100)]
        public string? Action { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public int SortOrder { get; set; } = 0;
    }
}
