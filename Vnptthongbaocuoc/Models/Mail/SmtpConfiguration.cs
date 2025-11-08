using System.ComponentModel.DataAnnotations;

namespace Vnptthongbaocuoc.Models.Mail;

public class SmtpConfiguration
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập máy chủ SMTP.")]
    [StringLength(256)]
    [Display(Name = "Máy chủ SMTP")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Cổng hợp lệ nằm trong khoảng 1 - 65535.")]
    [Display(Name = "Cổng")]
    public int Port { get; set; } = 587;

    [Display(Name = "Sử dụng SSL/TLS")]
    public bool UseSsl { get; set; } = true;

    [Display(Name = "Yêu cầu đăng nhập")]
    public bool UseAuthentication { get; set; } = true;

    [StringLength(256)]
    [Display(Name = "Tên đăng nhập")]
    public string? UserName { get; set; }

    [StringLength(256)]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string? Password { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập email người gửi.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(256)]
    [Display(Name = "Email người gửi")]
    public string FromAddress { get; set; } = string.Empty;

    [StringLength(256)]
    [Display(Name = "Tên người gửi")]
    public string? FromName { get; set; }

    [StringLength(512)]
    [Display(Name = "Ghi chú")] 
    public string? Notes { get; set; }
}
