using System;

namespace Vnptthongbaocuoc.Models.Mail;

public class MailLog
{
    public int Id { get; set; }

    public string? SenderEmail { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }

    public string? Body { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public string? ErrorMessage { get; set; }
}
