using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Models;

namespace Vnptthongbaocuoc.Data
{
    // Dùng IdentityDbContext để có đầy đủ bảng Identity
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSet nghiệp vụ sẽ thêm vào đây sau
        // public DbSet<...> ... { get; set; } = default!;
    }
}
