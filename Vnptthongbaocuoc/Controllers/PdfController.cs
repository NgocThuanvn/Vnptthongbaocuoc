using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vnptthongbaocuoc.Services;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class PdfController : Controller
    {
        private readonly PdfExportService _pdfExport;

        public PdfController(PdfExportService pdfExport)
        {
            _pdfExport = pdfExport;
        }

        // /Pdf/FromFile?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> FromFile(string table, string file)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số table hoặc file.");

            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Tên bảng không hợp lệ.");

            var pdfBytes = await _pdfExport.GeneratePdfAsync(table, file);
            if (pdfBytes == null)
                return BadRequest("Không có dữ liệu cho TEN_FILE này.");

            var downloadName = $"ThongBao_{file}.pdf";
            return File(pdfBytes, "application/pdf", downloadName);
        }
    }
}
