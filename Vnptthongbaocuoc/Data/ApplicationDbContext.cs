using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Models;
using Vnptthongbaocuoc.Models.Mail;

namespace Vnptthongbaocuoc.Data
{
    // Dùng IdentityDbContext để có đầy đủ bảng Identity
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<SmtpConfiguration> SmtpConfigurations { get; set; } = default!;
        public DbSet<MailLog> MailLogs { get; set; } = default!;
    }
}
