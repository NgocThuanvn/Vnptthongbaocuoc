namespace Vnptthongbaocuoc.Models.Mail;

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);
