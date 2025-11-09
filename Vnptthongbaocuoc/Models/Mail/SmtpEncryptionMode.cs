using System.ComponentModel.DataAnnotations;

namespace Vnptthongbaocuoc.Models.Mail;

public enum SmtpEncryptionMode
{
    [Display(Name = "Không mã hóa")]
    None = 0,

    [Display(Name = "STARTTLS (cổng 587)")]
    StartTls = 1,

    [Display(Name = "SSL/TLS ngay khi kết nối (cổng 465)")]
    SslTls = 2
}
