using System;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Models.Mail;
using Vnptthongbaocuoc.Services;
using Vnptthongbaocuoc.Data;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class PrintController : Controller
    {
        private readonly IConfiguration _config;
        private readonly PdfExportService _pdfExportService;
        private readonly ISmtpEmailSender _smtpEmailSender;
        private readonly ApplicationDbContext _dbContext;

        public PrintController(
            IConfiguration config,
            PdfExportService pdfExportService,
            ISmtpEmailSender smtpEmailSender,
            ApplicationDbContext dbContext)
        {
            _config = config;
            _pdfExportService = pdfExportService;
            _smtpEmailSender = smtpEmailSender;
            _dbContext = dbContext;
        }

        // ===== ViewModels =====
        public sealed class PrintRow
        {
            public string? MA_TT { get; set; }
            public string? ACCOUNT { get; set; }
            public string? TEN_TT { get; set; }
            public string? DIACHI_TT { get; set; }
            public string? DCLAPDAT { get; set; }
            public decimal TIEN_TTHUE { get; set; }
            public decimal THUE { get; set; }
            public decimal TIEN_PT { get; set; }
            public string? SOHD { get; set; }
            public string? NGAY_IN { get; set; }
            public string? MA_TRACUUHD { get; set; }
        }

        public sealed class PrintPageModel
        {
            public string Table { get; set; } = "";
            public string File { get; set; } = "";
            public string? ChuKyNo { get; set; }
            public string? TenKhachHang { get; set; }
            public string? DiaChiKhachHang { get; set; }
            public string? EmailKhachHang { get; set; }
            public long SoDong { get; set; }
            public decimal TongPT { get; set; }
            public List<PrintRow> Rows { get; set; } = new();
        }

        // GET /Print/File?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> File(string table, string file, CancellationToken cancellationToken)
        {
            if (!IsSafeImportTableName(table))
                return BadRequest("Tên bảng không hợp lệ (yêu cầu Vnpt_...).");
            if (string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số TEN_FILE.");

            var model = await LoadPrintPageModelAsync(table, file, cancellationToken);

            return View(model); // Views/Print/File.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMail(string table, string file, CancellationToken cancellationToken)
        {
            if (!IsSafeImportTableName(table) || string.IsNullOrWhiteSpace(file))
            {
                TempData["MailError"] = "Tham số gửi mail không hợp lệ.";
                return RedirectToAction(nameof(File), new { table, file });
            }

            var model = await LoadPrintPageModelAsync(table, file, cancellationToken);

            if (string.IsNullOrWhiteSpace(model.EmailKhachHang))
            {
                TempData["MailError"] = "Khách hàng chưa cung cấp email, không thể gửi thông báo.";
                return RedirectToAction(nameof(File), new { table, file });
            }

            var pdfBytes = await _pdfExportService.GeneratePdfAsync(table, file);
            if (pdfBytes is null || pdfBytes.Length == 0)
            {
                TempData["MailError"] = "Không thể tạo file PDF đính kèm.";
                return RedirectToAction(nameof(File), new { table, file });
            }

            var subject = "Thông báo và đề nghị thanh toán cước" +
                          (string.IsNullOrWhiteSpace(model.ChuKyNo) ? string.Empty : $" {model.ChuKyNo.Trim()}");
            var body = "Mail gửi tự động, vui lòng xem file đính kèm.";

            var attachments = new[]
            {
                new EmailAttachment($"ThongBao_{model.File}.pdf", pdfBytes, "application/pdf")
            };

            var senderEmail = await _dbContext.SmtpConfigurations
                .AsNoTracking()
                .Select(x => x.FromAddress)
                .FirstOrDefaultAsync(cancellationToken);

            var log = new MailLog
            {
                SenderEmail = senderEmail,
                RecipientEmail = model.EmailKhachHang!,
                SentAt = DateTime.Now,
                Body = body,
                Status = "Pending",
                FileName = model.File
            };

            try
            {
                await _smtpEmailSender.SendEmailAsync(model.EmailKhachHang!, subject, body, attachments, cancellationToken);
                TempData["MailSuccess"] = $"Đã gửi email đến {model.EmailKhachHang}.";
                log.Status = "Success";
            }
            catch (Exception ex)
            {
                TempData["MailError"] = "Gửi mail thất bại: " + ex.Message;
                log.Status = "Failed";
                log.ErrorMessage = ex.Message;
            }

            try
            {
                _dbContext.MailLogs.Add(log);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Không ném ngoại lệ để tránh ảnh hưởng đến luồng gửi mail
            }

            return RedirectToAction(nameof(File), new { table, file });
        }

        // Validate tên bảng import an toàn
        private async Task<PrintPageModel> LoadPrintPageModelAsync(string table, string file, CancellationToken cancellationToken)
        {
            var cnnStr = _config.GetConnectionString("DefaultConnection");
            var model = new PrintPageModel { Table = table, File = file };

            await using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync(cancellationToken);

            var sqlHeader = $@"
SELECT
    MAX(CHUKYNO),
    MAX(TEN_TT),
    MAX(DIACHI_TT),
    MAX(EMAIL),
    COUNT(1),
    COALESCE(SUM(TRY_CONVERT(DECIMAL(38,0), TIEN_PT)),0)
FROM [dbo].[{table}] WHERE TEN_FILE=@f;";

            await using (var cmd = new SqlCommand(sqlHeader, cnn))
            {
                cmd.Parameters.AddWithValue("@f", file);
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await rd.ReadAsync(cancellationToken))
                {
                    model.ChuKyNo = rd.IsDBNull(0) ? null : rd.GetString(0);
                    model.TenKhachHang = rd.IsDBNull(1) ? null : rd.GetString(1);
                    model.DiaChiKhachHang = rd.IsDBNull(2) ? null : rd.GetString(2);
                    model.EmailKhachHang = rd.IsDBNull(3) ? null : rd.GetString(3);
                    model.SoDong = rd.IsDBNull(4) ? 0L : Convert.ToInt64(rd.GetValue(4));
                    model.TongPT = rd.IsDBNull(5) ? 0M : Convert.ToDecimal(rd.GetValue(5));
                }
            }

            var sqlRows = $@"
SELECT
    MA_TT, ACCOUNT, TEN_TT, DIACHI_TT, DCLAPDAT,
    COALESCE(TRY_CONVERT(DECIMAL(38,0), TIEN_TTHUE),0) AS TIEN_TTHUE,
    COALESCE(TRY_CONVERT(DECIMAL(38,0), THUE),0)       AS THUE,
    COALESCE(TRY_CONVERT(DECIMAL(38,0), TIEN_PT),0)    AS TIEN_PT,
    SOHD, NGAY_IN,
    MA_TRACUUHD
FROM [dbo].[{table}]
WHERE TEN_FILE=@f
ORDER BY MA_TT, ACCOUNT;";

            await using (var cmd = new SqlCommand(sqlRows, cnn))
            {
                cmd.Parameters.AddWithValue("@f", file);
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rd.ReadAsync(cancellationToken))
                {
                    model.Rows.Add(new PrintRow
                    {
                        MA_TT = rd["MA_TT"] as string,
                        ACCOUNT = rd["ACCOUNT"] as string,
                        TEN_TT = rd["TEN_TT"] as string,
                        DIACHI_TT = rd["DIACHI_TT"] as string,
                        DCLAPDAT = rd["DCLAPDAT"] as string,
                        TIEN_TTHUE = rd["TIEN_TTHUE"] is DBNull ? 0 : Convert.ToDecimal(rd["TIEN_TTHUE"]),
                        THUE = rd["THUE"] is DBNull ? 0 : Convert.ToDecimal(rd["THUE"]),
                        TIEN_PT = rd["TIEN_PT"] is DBNull ? 0 : Convert.ToDecimal(rd["TIEN_PT"]),
                        SOHD = rd["SOHD"] as string,
                        NGAY_IN = rd["NGAY_IN"] as string,
                        MA_TRACUUHD = rd["MA_TRACUUHD"] as string
                    });
                }
            }

            return model;
        }

        private static bool IsSafeImportTableName(string table)
        {
            if (string.IsNullOrWhiteSpace(table)) return false;
            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase)) return false;
            var rest = table.Substring("Vnpt_".Length);
            return Regex.IsMatch(rest, @"^[A-Za-z0-9_]+$");
        }
    }
}
