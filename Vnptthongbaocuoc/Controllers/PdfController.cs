using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vnptthongbaocuoc.Services;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class PdfController : Controller
    {
        private readonly PdfExportService _pdfExport;
        private readonly PdfExportServiceUNT _pdfExportUnt;
        private readonly PdfExportServiceUntNhdt _pdfExportUntNhdt;
        private readonly PdfExportServiceUntNhnn _pdfExportUntNhnn;
        private readonly PdfExportServiceNnbx _pdfExportNnbx;

        public PdfController(
            PdfExportService pdfExport,
            PdfExportServiceUNT pdfExportUnt,
            PdfExportServiceUntNhdt pdfExportUntNhdt,
            PdfExportServiceUntNhnn pdfExportUntNhnn,
            PdfExportServiceNnbx pdfExportNnbx)
        {
            _pdfExport = pdfExport;
            _pdfExportUnt = pdfExportUnt;
            _pdfExportUntNhdt = pdfExportUntNhdt;
            _pdfExportUntNhnn = pdfExportUntNhnn;
            _pdfExportNnbx = pdfExportNnbx;
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

        // /Pdf/FromFileUnt?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> FromFileUnt(string table, string file)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số table hoặc file.");

            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Tên bảng không hợp lệ.");

            var pdfBytes = await _pdfExportUnt.GeneratePdfAsync(table, file);
            if (pdfBytes == null)
                return BadRequest("Không có dữ liệu cho TEN_FILE này.");

            var downloadName = $"ThongBao_{file}_UNT.pdf";
            return File(pdfBytes, "application/pdf", downloadName);
        }

        // /Pdf/FromFileUntNhdt?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> FromFileUntNhdt(string table, string file)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số table hoặc file.");

            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Tên bảng không hợp lệ.");

            var pdfBytes = await _pdfExportUntNhdt.GeneratePdfAsync(table, file);
            if (pdfBytes == null)
                return BadRequest("Không có dữ liệu cho TEN_FILE này.");

            var downloadName = $"UNT_NHDT_{file}.pdf";
            return File(pdfBytes, "application/pdf", downloadName);
        }

        // /Pdf/FromFileUntNhnn?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> FromFileUntNhnn(string table, string file)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số table hoặc file.");

            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Tên bảng không hợp lệ.");

            var pdfBytes = await _pdfExportUntNhnn.GeneratePdfAsync(table, file);
            if (pdfBytes == null)
                return BadRequest("Không có dữ liệu cho TEN_FILE này.");

            var downloadName = $"UNT_NHNN_{file}.pdf";
            return File(pdfBytes, "application/pdf", downloadName);
        }

        // /Pdf/FromFileNnbx?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> FromFileNnbx(string table, string file)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số table hoặc file.");

            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Tên bảng không hợp lệ.");

            var pdfBytes = await _pdfExportNnbx.GeneratePdfAsync(table, file);
            if (pdfBytes == null)
                return BadRequest("Không có dữ liệu cho TEN_FILE này.");

            var downloadName = $"NNBX_{file}.pdf";
            return File(pdfBytes, "application/pdf", downloadName);
        }
    }
}
