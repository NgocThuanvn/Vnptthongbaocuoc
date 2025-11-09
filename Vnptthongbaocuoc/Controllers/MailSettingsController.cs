using MailKit.Security;
using MailKitAuthenticationException = MailKit.Security.AuthenticationException;
using SystemAuthenticationException = System.Security.Authentication.AuthenticationException;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Data;
using Vnptthongbaocuoc.Models.Mail;
using Vnptthongbaocuoc.Services;

namespace Vnptthongbaocuoc.Controllers;

[Authorize(Roles = "Admin")]
public class MailSettingsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ISmtpEmailSender _smtpEmailSender;
    private readonly ILogger<MailSettingsController> _logger;

    public MailSettingsController(
        ApplicationDbContext context,
        ISmtpEmailSender smtpEmailSender,
        ILogger<MailSettingsController> logger)
    {
        _context = context;
        _smtpEmailSender = smtpEmailSender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var configuration = await _context.SmtpConfigurations.AsNoTracking().FirstOrDefaultAsync()
            ?? SmtpConfiguration.CreateDefault();

        var viewModel = new MailSettingsViewModel
        {
            Configuration = configuration,
            HasExistingPassword = !string.IsNullOrWhiteSpace(configuration.Password),
            StatusMessage = TempData[nameof(MailSettingsViewModel.StatusMessage)] as string,
            ErrorMessage = TempData[nameof(MailSettingsViewModel.ErrorMessage)] as string
        };

        if (viewModel.HasExistingPassword)
        {
            viewModel.Configuration.Password = string.Empty;
        }

        ViewData["Title"] = "Cấu hình gửi mail";
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] MailSettingsViewModel model)
    {
        var existing = await _context.SmtpConfigurations.FirstOrDefaultAsync();

        if (!Enum.IsDefined(typeof(SmtpEncryptionMode), model.Configuration.EncryptionMode))
        {
            ModelState.AddModelError("Configuration.EncryptionMode", "Phương thức mã hóa không hợp lệ.");
        }

        if (model.Configuration.UseAuthentication && string.IsNullOrWhiteSpace(model.Configuration.UserName))
        {
            ModelState.AddModelError("Configuration.UserName", "Vui lòng nhập tên đăng nhập SMTP.");
        }

        if (model.Configuration.UseAuthentication)
        {
            var hasPassword = !string.IsNullOrWhiteSpace(model.Configuration.Password) || (existing != null && !string.IsNullOrWhiteSpace(existing.Password));
            if (!hasPassword)
            {
                ModelState.AddModelError("Configuration.Password", "Vui lòng nhập mật khẩu SMTP.");
            }
        }
        else
        {
            model.Configuration.UserName = null;
            model.Configuration.Password = null;
        }

        if (!ModelState.IsValid)
        {
            model.Configuration.Id = existing?.Id ?? 0;
            model.HasExistingPassword = existing != null && !string.IsNullOrWhiteSpace(existing.Password);
            model.StatusMessage = TempData[nameof(MailSettingsViewModel.StatusMessage)] as string;
            model.ErrorMessage = TempData[nameof(MailSettingsViewModel.ErrorMessage)] as string;
            model.Configuration.Password = string.Empty;
            return View("Index", model);
        }

        if (existing is null)
        {
            var entity = new SmtpConfiguration
            {
                Host = model.Configuration.Host.Trim(),
                Port = model.Configuration.Port,
                EncryptionMode = model.Configuration.EncryptionMode,
                UseAuthentication = model.Configuration.UseAuthentication,
                UserName = model.Configuration.UserName?.Trim(),
                Password = model.Configuration.UseAuthentication ? model.Configuration.Password : null,
                FromAddress = model.Configuration.FromAddress.Trim(),
                FromName = string.IsNullOrWhiteSpace(model.Configuration.FromName) ? null : model.Configuration.FromName.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.Configuration.Notes) ? null : model.Configuration.Notes.Trim()
            };

            _context.SmtpConfigurations.Add(entity);
        }
        else
        {
            existing.Host = model.Configuration.Host.Trim();
            existing.Port = model.Configuration.Port;
            existing.EncryptionMode = model.Configuration.EncryptionMode;
            existing.UseAuthentication = model.Configuration.UseAuthentication;
            existing.UserName = model.Configuration.UseAuthentication ? model.Configuration.UserName?.Trim() : null;
            existing.FromAddress = model.Configuration.FromAddress.Trim();
            existing.FromName = string.IsNullOrWhiteSpace(model.Configuration.FromName) ? null : model.Configuration.FromName.Trim();
            existing.Notes = string.IsNullOrWhiteSpace(model.Configuration.Notes) ? null : model.Configuration.Notes.Trim();

            if (model.Configuration.UseAuthentication)
            {
                if (!string.IsNullOrWhiteSpace(model.Configuration.Password))
                {
                    existing.Password = model.Configuration.Password;
                }
            }
            else
            {
                existing.Password = null;
            }
        }

        await _context.SaveChangesAsync();

        TempData[nameof(MailSettingsViewModel.StatusMessage)] = "Đã lưu cấu hình SMTP thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest([FromForm] MailSettingsViewModel model)
    {
        var configuration = await _context.SmtpConfigurations.AsNoTracking().FirstOrDefaultAsync();
        if (configuration is null)
        {
            ModelState.AddModelError(string.Empty, "Chưa có cấu hình SMTP. Vui lòng lưu cấu hình trước.");
        }

        if (string.IsNullOrWhiteSpace(model.TestRecipient))
        {
            ModelState.AddModelError("TestRecipient", "Vui lòng nhập email nhận thử.");
        }

        if (!ModelState.IsValid)
        {
            var fallback = configuration ?? SmtpConfiguration.CreateDefault();
            var viewModel = new MailSettingsViewModel
            {
                Configuration = fallback,
                HasExistingPassword = !string.IsNullOrWhiteSpace(configuration?.Password),
                ErrorMessage = TempData[nameof(MailSettingsViewModel.ErrorMessage)] as string,
                StatusMessage = TempData[nameof(MailSettingsViewModel.StatusMessage)] as string,
                TestRecipient = model.TestRecipient,
                TestSubject = model.TestSubject,
                TestBody = model.TestBody
            };

            if (viewModel.HasExistingPassword)
            {
                viewModel.Configuration.Password = string.Empty;
            }

            return View("Index", viewModel);
        }

        var subject = string.IsNullOrWhiteSpace(model.TestSubject)
            ? "Thư kiểm tra SMTP"
            : model.TestSubject.Trim();
        var body = string.IsNullOrWhiteSpace(model.TestBody)
            ? "Đây là thư kiểm tra cấu hình SMTP từ hệ thống VNPT Thông Báo Cước."
            : model.TestBody;

        try
        {
            await _smtpEmailSender.SendEmailAsync(model.TestRecipient!, subject, body);
            TempData[nameof(MailSettingsViewModel.StatusMessage)] = $"Đã gửi thư thử đến {model.TestRecipient}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể gửi thư thử đến {Recipient}", model.TestRecipient);
            var friendlyMessage = GetFriendlyErrorMessage(ex);
            TempData[nameof(MailSettingsViewModel.ErrorMessage)] = "Gửi thư thất bại: " + friendlyMessage;
        }

        return RedirectToAction(nameof(Index));
    } 

    private static string GetFriendlyErrorMessage(Exception exception)
    {
        if (exception is SslHandshakeException
            || exception is MailKitAuthenticationException
            || exception is SystemAuthenticationException)
        {
            return "Không thể thiết lập kết nối bảo mật với máy chủ SMTP. Nếu đang sử dụng cổng 587, hãy cấu hình STARTTLS (không bật SSL trực tiếp) hoặc chuyển sang cổng 465.";
        }

        if (exception.InnerException is not null)
        {
            return GetFriendlyErrorMessage(exception.InnerException);
        }

        return exception.Message;
    }
}
