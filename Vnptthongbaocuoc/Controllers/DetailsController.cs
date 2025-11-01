using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class DetailsController : Controller
    {
        private readonly IConfiguration _config;
        public DetailsController(IConfiguration config) => _config = config;

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

        // Helpers
        private static bool IsSafeImportTableName(string table)
        {
            if (string.IsNullOrWhiteSpace(table)) return false;
            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase)) return false;
            var rest = table.Substring("Vnpt_".Length);
            return Regex.IsMatch(rest, @"^[A-Za-z0-9_]+$");
        }
    }
}
