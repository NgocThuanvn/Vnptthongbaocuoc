using System.ComponentModel.DataAnnotations;

namespace Vnptthongbaocuoc.Models.Mail;

public class MailSettingsViewModel
{
    public SmtpConfiguration Configuration { get; set; } = new();

    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [Display(Name = "Email nhận thử")]
    public string? TestRecipient { get; set; }

    [StringLength(256)]
    [Display(Name = "Tiêu đề thử")]
    public string? TestSubject { get; set; }

    [Display(Name = "Nội dung thử")]
    public string? TestBody { get; set; }

    public bool HasExistingPassword { get; set; }

    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }
}
