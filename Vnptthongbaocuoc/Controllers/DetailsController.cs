using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Services;
using Vnptthongbaocuoc.Data;
using Vnptthongbaocuoc.Models.Mail;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class DetailsController : Controller
    {
        private readonly IConfiguration _config;
        private readonly PdfExportService _pdfExport;
        private readonly ISmtpEmailSender _smtpEmailSender;
        private readonly ApplicationDbContext _dbContext;

        public DetailsController(
            IConfiguration config,
            PdfExportService pdfExport,
            ISmtpEmailSender smtpEmailSender,
            ApplicationDbContext dbContext)
        {
            _config = config;
            _pdfExport = pdfExport;
            _smtpEmailSender = smtpEmailSender;
            _dbContext = dbContext;
        }

        // ViewModel hiển thị mỗi dòng (mỗi TEN_FILE)
        public sealed class FileDetailItem
        {
            public string TenFile { get; set; } = "";
            public string? ChuKyNo { get; set; }
            public string? TenKhachHang { get; set; }
            public string? DiaChiKhachHang { get; set; }
            public long SoLuongDong { get; set; }               // N0
            public decimal TongTienTruocThue { get; set; }      // N0
            public decimal TongThue { get; set; }               // N0
            public decimal TongTienPT { get; set; }             // N0
        }

        public sealed class DetailsPageModel
        {
            public string Table { get; set; } = "";
            public string Query { get; set; } = "";
            public List<FileDetailItem> Items { get; set; } = new();
        }

        // GET /Details?table=Vnpt_xxx&q=...
        [HttpGet]
        public async Task<IActionResult> Index(string table, string? q)
        {
            if (!IsSafeImportTableName(table))
                return BadRequest("Tên bảng không hợp lệ hoặc không phải bảng import (Vnpt_...).");

            var model = new DetailsPageModel
            {
                Table = table,
                Query = q ?? ""
            };

            var cnnStr = _config.GetConnectionString("DefaultConnection");
            using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync();

            // Lọc: 1 ô tìm kiếm áp dụng OR cho TEN_FILE / TEN_TT / DIACHI_TT
            var hasQ = !string.IsNullOrWhiteSpace(q);
            var where = hasQ ? "WHERE (TEN_FILE LIKE @q OR TEN_TT LIKE @q OR DIACHI_TT LIKE @q)" : "";

            // GROUP BY theo TEN_FILE; alias đều đặt trong [] để tránh xung đột từ khóa
            var sql = $@"
SELECT 
    TEN_FILE,
    MAX(CHUKYNO)                                AS [CHUKYNO],
    MAX(TEN_TT)                                 AS [TEN_TT],
    MAX(DIACHI_TT)                              AS [DIACHI_TT],
    COUNT(1)                                    AS [RowCount],
    COALESCE(SUM(TRY_CONVERT(DECIMAL(38,0), TIEN_TTHUE)),0) AS [SumTienTruocThue],
    COALESCE(SUM(TRY_CONVERT(DECIMAL(38,0), THUE)),0)        AS [SumThue],
    COALESCE(SUM(TRY_CONVERT(DECIMAL(38,0), TIEN_PT)),0)     AS [SumTienPT]
FROM [dbo].[{table}]
{where}
GROUP BY TEN_FILE
ORDER BY TEN_FILE;";

            using var cmd = new SqlCommand(sql, cnn);
            if (hasQ) cmd.Parameters.AddWithValue("@q", $"%{q}%");

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                model.Items.Add(new FileDetailItem
                {
                    TenFile = rd.IsDBNull(0) ? "" : rd.GetString(0),
                    ChuKyNo = rd.IsDBNull(1) ? null : rd.GetString(1),
                    TenKhachHang = rd.IsDBNull(2) ? null : rd.GetString(2),
                    DiaChiKhachHang = rd.IsDBNull(3) ? null : rd.GetString(3),
                    SoLuongDong = rd.IsDBNull(4) ? 0L : Convert.ToInt64(rd.GetValue(4)),
                    TongTienTruocThue = rd.IsDBNull(5) ? 0M : Convert.ToDecimal(rd.GetValue(5)),
                    TongThue = rd.IsDBNull(6) ? 0M : Convert.ToDecimal(rd.GetValue(6)),
                    TongTienPT = rd.IsDBNull(7) ? 0M : Convert.ToDecimal(rd.GetValue(7))
                });
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DownloadSelected(string table, List<string>? files)
        {
            if (!IsSafeImportTableName(table))
                return BadRequest("Tên bảng không hợp lệ hoặc không phải bảng import (Vnpt_...).");

            if (files == null || files.Count == 0)
                return BadRequest("Vui lòng chọn ít nhất một file.");

            var distinctFiles = files
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctFiles.Count == 0)
                return BadRequest("Vui lòng chọn ít nhất một file hợp lệ.");

            var archiveStream = new MemoryStream();

            using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in distinctFiles)
                {
                    var pdfBytes = await _pdfExport.GeneratePdfAsync(table, file);
                    if (pdfBytes == null || pdfBytes.Length == 0)
                        continue;

                    var entry = zip.CreateEntry($"ThongBao_{file}.pdf", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(pdfBytes, 0, pdfBytes.Length);
                }
            }

            if (archiveStream.Length == 0)
                return BadRequest("Không tạo được file nào từ lựa chọn của bạn.");

            archiveStream.Position = 0;
            var downloadName = $"ThongBao_{table}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
            return File(archiveStream, "application/zip", downloadName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmails(string table, string? q, List<string>? files, CancellationToken cancellationToken)
        {
            if (!IsSafeImportTableName(table))
            {
                TempData["EmailSummary"] = "Tên bảng không hợp lệ hoặc không phải bảng import.";
                return RedirectToAction(nameof(Index), new { table, q });
            }

            if (files == null || files.Count == 0)
            {
                TempData["EmailSummary"] = "Vui lòng chọn ít nhất một file để gửi email.";
                return RedirectToAction(nameof(Index), new { table, q });
            }

            var distinctFiles = files
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctFiles.Count == 0)
            {
                TempData["EmailSummary"] = "Không có file hợp lệ để gửi email.";
                return RedirectToAction(nameof(Index), new { table, q });
            }

            var messages = new List<string>();
            var successCount = 0;

            var senderEmail = await _dbContext.SmtpConfigurations
                .AsNoTracking()
                .Select(x => x.FromAddress)
                .FirstOrDefaultAsync(cancellationToken);

            var cnnStr = _config.GetConnectionString("DefaultConnection");
            await using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync(cancellationToken);

            foreach (var file in distinctFiles)
            {
                var info = await LoadFileEmailInfoAsync(cnn, table, file, cancellationToken);
                if (info is null)
                {
                    messages.Add($"File {file}: Không tìm thấy thông tin dữ liệu.");
                    continue;
                }

                var recipientEmail = info.Email?.Trim();
                if (string.IsNullOrWhiteSpace(recipientEmail))
                {
                    messages.Add($"File {file}: Không có email khách hàng.");
                    continue;
                }

                var pdfBytes = await _pdfExport.GeneratePdfAsync(table, file);
                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    messages.Add($"File {file}: Không thể tạo file PDF đính kèm.");
                    continue;
                }

                var subject = "Thông báo và đề nghị thanh toán cước" +
                              (string.IsNullOrWhiteSpace(info.ChuKyNo) ? string.Empty : $" {info.ChuKyNo.Trim()}");
                const string body = "Mail gửi tự động, vui lòng xem file đính kèm.";
                var attachmentFileName = $"ThongBao_{file}.pdf";

                var attachments = new[]
                {
                    new EmailAttachment(attachmentFileName, pdfBytes, "application/pdf")
                };

                var log = new MailLog
                {
                    SenderEmail = senderEmail?.Trim(),
                    RecipientEmail = recipientEmail,
                    SentAt = DateTime.Now,
                    Body = body,
                    Status = "Pending",
                    FileName = attachmentFileName
                };

                try
                {
                    await _smtpEmailSender.SendEmailAsync(recipientEmail, subject, body, attachments, cancellationToken);
                    successCount++;
                    messages.Add($"File {file}: Đã gửi email đến {recipientEmail}.");
                    log.Status = "Success";
                }
                catch (Exception ex)
                {
                    messages.Add($"File {file}: Gửi mail thất bại - {ex.Message}");
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
                    // Bỏ qua lỗi lưu log để không ảnh hưởng luồng gửi mail
                }
            }

            TempData["EmailSummary"] = $"Đã xử lý {successCount}/{distinctFiles.Count} file.";
            TempData["EmailMessages"] = string.Join("\n", messages);

            return RedirectToAction(nameof(Index), new { table, q });
        }

        // Helpers
        private static bool IsSafeImportTableName(string table)
        {
            if (string.IsNullOrWhiteSpace(table)) return false;
            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase)) return false;
            var rest = table.Substring("Vnpt_".Length);
            return Regex.IsMatch(rest, @"^[A-Za-z0-9_]+$");
        }

        private sealed class FileEmailInfo
        {
            public string? Email { get; set; }
            public string? ChuKyNo { get; set; }
        }

        private static async Task<FileEmailInfo?> LoadFileEmailInfoAsync(SqlConnection cnn, string table, string file, CancellationToken cancellationToken)
        {
            var sql = $@"SELECT MAX(CHUKYNO), MAX(EMAIL) FROM [dbo].[{table}] WHERE TEN_FILE=@f;";

            await using var cmd = new SqlCommand(sql, cnn);
            cmd.Parameters.AddWithValue("@f", file);

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                return new FileEmailInfo
                {
                    ChuKyNo = rd.IsDBNull(0) ? null : rd.GetString(0),
                    Email = rd.IsDBNull(1) ? null : rd.GetString(1)
                };
            }

            return null;
        }
    }
}
