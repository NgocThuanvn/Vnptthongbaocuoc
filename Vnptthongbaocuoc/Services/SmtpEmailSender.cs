using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Vnptthongbaocuoc.Data;
using Vnptthongbaocuoc.Models.Mail;

namespace Vnptthongbaocuoc.Services;

public interface ISmtpEmailSender
{
    Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        IEnumerable<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default);
}

public class SmtpEmailSender(ApplicationDbContext context, ILogger<SmtpEmailSender> logger) : ISmtpEmailSender
{
    private readonly ApplicationDbContext _context = context;
    private readonly ILogger<SmtpEmailSender> _logger = logger;

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        IEnumerable<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        var config = await _context.SmtpConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (config is null)
        {
            throw new InvalidOperationException("Chưa cấu hình SMTP trong hệ thống.");
        }

        if (string.IsNullOrWhiteSpace(config.FromAddress))
        {
            throw new InvalidOperationException("Cấu hình SMTP thiếu email người gửi.");
        }

        var message = new MimeMessage();
        var displayName = string.IsNullOrWhiteSpace(config.FromName)
            ? config.FromAddress
            : config.FromName;
        message.From.Add(new MailboxAddress(displayName, config.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody
        };

        if (attachments is not null)
        {
            foreach (var attachment in attachments)
            {
                if (attachment is null)
                {
                    continue;
                }

                if (attachment.Content is null || attachment.Content.Length == 0)
                {
                    continue;
                }

                var fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? "attachment"
                    : attachment.FileName;

                ContentType contentType;
                if (!string.IsNullOrWhiteSpace(attachment.ContentType))
                {
                    try
                    {
                        contentType = ContentType.Parse(attachment.ContentType);
                    }
                    catch
                    {
                        contentType = new ContentType("application", "octet-stream");
                    }
                }
                else
                {
                    contentType = new ContentType("application", "octet-stream");
                }

                bodyBuilder.Attachments.Add(fileName, attachment.Content, contentType);
            }
        }

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var secureSocket = config.EncryptionMode switch
            {
                SmtpEncryptionMode.None => SecureSocketOptions.None,
                SmtpEncryptionMode.StartTls => SecureSocketOptions.StartTls,
                SmtpEncryptionMode.SslTls => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls
            };

            await client.ConnectAsync(config.Host, config.Port, secureSocket, cancellationToken);

            if (config.UseAuthentication)
            {
                if (string.IsNullOrWhiteSpace(config.UserName) || string.IsNullOrWhiteSpace(config.Password))
                {
                    throw new InvalidOperationException("Cấu hình SMTP thiếu tên đăng nhập hoặc mật khẩu.");
                }

                await client.AuthenticateAsync(config.UserName, config.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        catch
        {
            _logger.LogError("Không thể gửi email tới {Recipient}", toEmail);
            throw;
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }
}
