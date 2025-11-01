using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class TablesController : Controller
    {
        private readonly IConfiguration _config;
        public TablesController(IConfiguration config) => _config = config;

        public sealed class TableSummary
        {
            public string TableName { get; set; } = "";
            public int TenFileCount { get; set; }      // số lượng TEN_FILE (X bảng kê)
            public string? ChuKyNo { get; set; }       // 1 giá trị hoặc (trống)/Nhiều (x)
            public long TotalRows { get; set; }
            public decimal SumTienPt { get; set; }     // chỉ Tổng PT
        }

        // GET: /Tables
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var items = await GetSummariesAsync();
            ViewBag.Success = TempData["Success"];
            ViewBag.Error = TempData["Error"];
            return View(items);
        }

        // POST: /Tables/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string table)
        {
            if (string.IsNullOrWhiteSpace(table))
            {
                TempData["Error"] = "Thiếu tên bảng cần xóa.";
                return RedirectToAction(nameof(Index));
            }

            if (!IsSafeImportTableName(table))
            {
                TempData["Error"] = "Tên bảng không hợp lệ hoặc không phải bảng import (Vnpt_...).";
                return RedirectToAction(nameof(Index));
            }

            var cnnStr = _config.GetConnectionString("DefaultConnection");
            try
            {
                using var cnn = new SqlConnection(cnnStr);
                await cnn.OpenAsync();

                if (!await TableExistsAsync(cnn, table))
                {
                    TempData["Error"] = $"Bảng '{table}' không tồn tại.";
                    return RedirectToAction(nameof(Index));
                }

                var sql = $"DROP TABLE [dbo].[{table}]";
                using (var cmd = new SqlCommand(sql, cnn))
                    await cmd.ExecuteNonQueryAsync();

                TempData["Success"] = $"Đã xóa bảng '{table}'.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Không thể xóa bảng: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // ===== Helpers =====
        private async Task<List<TableSummary>> GetSummariesAsync()
        {
            var list = new List<TableSummary>();
            var cnnStr = _config.GetConnectionString("DefaultConnection");
            using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync();

            // 1) Danh sách bảng Vnpt_%
            var tables = new List<string>();
            const string getTablesSql = @"
SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME LIKE 'Vnpt\_%' ESCAPE '\'
ORDER BY TABLE_NAME";
            using (var cmd = new SqlCommand(getTablesSql, cnn))
            using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                    tables.Add(rd.GetString(0));
            }

            // 2) Tổng hợp từng bảng (không có tìm kiếm)
            foreach (var tbl in tables)
            {
                var sql = $@"
SELECT 
    COUNT(1) AS TotalRows,
    COALESCE(SUM(TRY_CONVERT(DECIMAL(38,0), TIEN_PT)),0) AS SumTienPt
FROM [dbo].[{tbl}];

SELECT COUNT(DISTINCT TEN_FILE) FROM [dbo].[{tbl}];

SELECT COUNT(DISTINCT CHUKYNO) FROM [dbo].[{tbl}];

SELECT TOP 1 CHUKYNO 
FROM [dbo].[{tbl}]
WHERE CHUKYNO IS NOT NULL AND LTRIM(RTRIM(CHUKYNO))<>'';";

                using var cmd = new SqlCommand(sql, cnn);
                using var reader = await cmd.ExecuteReaderAsync();

                // (1) totals
                if (!await reader.ReadAsync()) { await reader.DisposeAsync(); continue; }
                var totalRows = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0));
                var sumTienPt = reader.IsDBNull(1) ? 0M : Convert.ToDecimal(reader.GetValue(1));
                if (totalRows == 0)
                {
                    while (await reader.NextResultAsync()) { /* skip */ }
                    continue;
                }

                // (2) distinct TEN_FILE count
                await reader.NextResultAsync();
                int tenFileDistinct = 0;
                if (await reader.ReadAsync())
                    tenFileDistinct = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));

                // (3) distinct CHUKYNO count
                await reader.NextResultAsync();
                int ckDistinct = 0;
                if (await reader.ReadAsync())
                    ckDistinct = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));

                // (4) sample CHUKYNO (non-empty)
                await reader.NextResultAsync();
                string? chuky = null;
                if (await reader.ReadAsync())
                    chuky = reader.IsDBNull(0) ? null : reader.GetString(0);

                list.Add(new TableSummary
                {
                    TableName = tbl,
                    TenFileCount = tenFileDistinct,
                    ChuKyNo = ckDistinct == 1 ? chuky : (ckDistinct == 0 ? "(trống)" : $"Nhiều ({ckDistinct})"),
                    TotalRows = totalRows,
                    SumTienPt = sumTienPt
                });
            }

            return list
                .OrderBy(x => x.TableName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSafeImportTableName(string table)
        {
            if (string.IsNullOrWhiteSpace(table)) return false;
            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase)) return false;
            var rest = table.Substring("Vnpt_".Length);
            return Regex.IsMatch(rest, @"^[A-Za-z0-9_]+$");
        }

        private static async Task<bool> TableExistsAsync(SqlConnection cnn, string table)
        {
            const string sql = @"SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME=@t";
            using var cmd = new SqlCommand(sql, cnn);
            cmd.Parameters.AddWithValue("@t", table);
            var obj = await cmd.ExecuteScalarAsync();
            return obj != null;
        }
    }
}
