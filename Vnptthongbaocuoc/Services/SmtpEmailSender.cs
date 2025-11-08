using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using MimeKit.Text;
using Vnptthongbaocuoc.Data;
using Vnptthongbaocuoc.Models.Mail;

namespace Vnptthongbaocuoc.Services;

public interface ISmtpEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

public class SmtpEmailSender(ApplicationDbContext context, ILogger<SmtpEmailSender> logger) : ISmtpEmailSender
{
    private readonly ApplicationDbContext _context = context;
    private readonly ILogger<SmtpEmailSender> _logger = logger;

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
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
        message.Body = new TextPart(TextFormat.Html)
        {
            Text = htmlBody
        };

        using var client = new SmtpClient();
        try
        {
            var secureSocket = config.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
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
