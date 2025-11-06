using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class PrintController : Controller
    {
        private readonly IConfiguration _config;
        public PrintController(IConfiguration config) => _config = config;

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
            public long SoDong { get; set; }
            public decimal TongPT { get; set; }
            public List<PrintRow> Rows { get; set; } = new();
        }

        // GET /Print/File?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> File(string table, string file)
        {
            if (!IsSafeImportTableName(table))
                return BadRequest("Tên bảng không hợp lệ (yêu cầu Vnpt_...).");
            if (string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số TEN_FILE.");

            var cnnStr = _config.GetConnectionString("DefaultConnection");
            var model = new PrintPageModel { Table = table, File = file };

            using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync();

            // Header: lấy đại diện thông tin KH + tổng PT + số dòng
            var sqlHeader = $@"
SELECT
    MAX(CHUKYNO),
    MAX(TEN_TT),
    MAX(DIACHI_TT),
    COUNT(1),
    COALESCE(SUM(TRY_CONVERT(DECIMAL(38,0), TIEN_PT)),0)
FROM [dbo].[{table}] WHERE TEN_FILE=@f;";

            using (var cmd = new SqlCommand(sqlHeader, cnn))
            {
                cmd.Parameters.AddWithValue("@f", file);
                using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    model.ChuKyNo = rd.IsDBNull(0) ? null : rd.GetString(0);
                    model.TenKhachHang = rd.IsDBNull(1) ? null : rd.GetString(1);
                    model.DiaChiKhachHang = rd.IsDBNull(2) ? null : rd.GetString(2);
                    model.SoDong = rd.IsDBNull(3) ? 0L : Convert.ToInt64(rd.GetValue(3));
                    model.TongPT = rd.IsDBNull(4) ? 0M : Convert.ToDecimal(rd.GetValue(4));
                }
            }

            // Body: toàn bộ dòng thuộc TEN_FILE
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

            using (var cmd = new SqlCommand(sqlRows, cnn))
            {
                cmd.Parameters.AddWithValue("@f", file);
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
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

            return View(model); // Views/Print/File.cshtml
        }

        // Validate tên bảng import an toàn
        private static bool IsSafeImportTableName(string table)
        {
            if (string.IsNullOrWhiteSpace(table)) return false;
            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase)) return false;
            var rest = table.Substring("Vnpt_".Length);
            return Regex.IsMatch(rest, @"^[A-Za-z0-9_]+$");
        }
    }
}
