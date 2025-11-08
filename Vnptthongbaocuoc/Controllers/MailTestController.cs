using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Data;
using Vnptthongbaocuoc.Models.Mail;
using Vnptthongbaocuoc.Services;

namespace Vnptthongbaocuoc.Controllers;

[Authorize(Roles = "Admin")]
public class MailTestController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ISmtpEmailSender _smtpEmailSender;
    private readonly ILogger<MailTestController> _logger;

    public MailTestController(
        ApplicationDbContext context,
        ISmtpEmailSender smtpEmailSender,
        ILogger<MailTestController> logger)
    {
        _context = context;
        _smtpEmailSender = smtpEmailSender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var configuration = await _context.SmtpConfigurations.AsNoTracking().FirstOrDefaultAsync();
        var model = new MailTestViewModel
        {
            Configuration = configuration,
            ErrorMessage = configuration is null ? "Chưa có cấu hình SMTP. Vui lòng lưu cấu hình trước." : null,
            Subject = "Thư kiểm tra SMTP",
            Body = "Đây là thư kiểm tra cấu hình SMTP từ hệ thống VNPT Thông Báo Cước."
        };

        ViewData["Title"] = "Gửi thư thử nghiệm";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromForm] MailTestViewModel model)
    {
        var configuration = await _context.SmtpConfigurations.AsNoTracking().FirstOrDefaultAsync();
        if (configuration is null)
        {
            ModelState.AddModelError(string.Empty, "Chưa có cấu hình SMTP. Vui lòng lưu cấu hình trước.");
        }

        if (!ModelState.IsValid)
        {
            model.Configuration = configuration;
            if (configuration is null)
            {
                model.ErrorMessage = "Chưa có cấu hình SMTP. Vui lòng lưu cấu hình trước.";
            }
            ViewData["Title"] = "Gửi thư thử nghiệm";
            return View("Index", model);
        }

        var subject = string.IsNullOrWhiteSpace(model.Subject)
            ? "Thư kiểm tra SMTP"
            : model.Subject.Trim();
        var body = string.IsNullOrWhiteSpace(model.Body)
            ? "Đây là thư kiểm tra cấu hình SMTP từ hệ thống VNPT Thông Báo Cước."
            : model.Body;

        try
        {
            await _smtpEmailSender.SendEmailAsync(model.Recipient!, subject, body);
            model.StatusMessage = $"Đã gửi thư thử đến {model.Recipient}.";
            model.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể gửi thư thử đến {Recipient}", model.Recipient);
            ModelState.AddModelError(string.Empty, "Gửi thư thất bại: " + ex.Message);
        }

        model.Configuration = configuration;
        model.Subject = subject;
        model.Body = body;
        ViewData["Title"] = "Gửi thư thử nghiệm";

        if (!ModelState.IsValid)
        {
            if (string.IsNullOrWhiteSpace(model.ErrorMessage))
            {
                model.ErrorMessage = "Gửi thư thất bại. Vui lòng kiểm tra lại thông tin.";
            }
            return View("Index", model);
        }

        ModelState.Clear();
        model.Recipient = string.Empty;
        return View("Index", model);
    }
}
