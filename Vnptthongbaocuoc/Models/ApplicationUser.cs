using Microsoft.AspNetCore.Identity;

namespace Vnptthongbaocuoc.Models
{
    // Có thể mở rộng thêm thuộc tính hồ sơ sau này
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
    }
}
