using System.ComponentModel.DataAnnotations;

namespace Vnptthongbaocuoc.Models.Mail;

public class MailTestViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email nhận thử.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [Display(Name = "Email nhận thử")]
    public string? Recipient { get; set; }

    [StringLength(256, ErrorMessage = "Tiêu đề tối đa 256 ký tự.")]
    [Display(Name = "Tiêu đề")]
    public string? Subject { get; set; }

    [Display(Name = "Nội dung")]
    public string? Body { get; set; }

    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public SmtpConfiguration? Configuration { get; set; }

    public bool HasConfiguration => Configuration is not null;
}
