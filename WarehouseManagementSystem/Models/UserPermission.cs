namespace WarehouseManagementSystem.Models
{
    public class UserPermission
    {
        public int Id { get; set; }
        
        public int UserId { get; set; }
        
        public int PermissionId { get; set; }
        
        public DateTime GrantedAt { get; set; } = DateTime.Now;

        public DateTime AssignedAt { get; set; }
        
        public int? GrantedBy { get; set; }


        // 导航属性
        public User? User { get; set; }
        public Permission? Permission { get; set; }
    }
}
