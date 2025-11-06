using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ExcelDataReader;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class ImportController : Controller
    {
        private readonly IConfiguration _config;

        private static readonly string[] RequiredCols = new[]
        {
            "MA_TT","ACCOUNT","TEN_TT","DIACHI_TT","TIEN_TTHUE","THUE","TIEN_PT",
            "MA_TRACUUHD","SOHD","NGAY_IN","EMAIL","TEN_FILE","CHUKYNO"
        };

        public ImportController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(string baseTableName, IFormFile? file)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(baseTableName))
                errors.Add("Vui lòng nhập tên bảng.");
            if (file == null || file.Length == 0)
                errors.Add("Vui lòng chọn file Excel (.xlsx).");

            if (errors.Count > 0)
            {
                ViewBag.Errors = errors;
                return View();
            }

            // Chuẩn hóa tên bảng
            var normalizedBase = Regex.Replace(baseTableName.Trim(), @"^\s*Vnpt_", "", RegexOptions.IgnoreCase);
            var safeName = Regex.Replace(normalizedBase, @"[^A-Za-z0-9_]", "_");
            if (string.IsNullOrWhiteSpace(safeName))
            {
                ViewBag.Errors = new List<string> { "Tên bảng không hợp lệ." };
                return View();
            }
            var finalTable = $"Vnpt_{safeName}";

            // Đọc Excel
            ExcelReadResult read;
            try
            {
                read = await ReadExcelAsync(file!);
            }
            catch (Exception ex)
            {
                ViewBag.Errors = new List<string> { "Không đọc được file Excel: " + ex.Message };
                return View();
            }

            var rows = read.Rows;
            var headersUpper = read.HeadersUpper;

            if (rows.Count == 0)
            {
                ViewBag.Errors = new List<string> { "File Excel không có dữ liệu." };
                return View();
            }

            var missing = RequiredCols.Where(rc => !headersUpper.Contains(rc)).ToList();
            if (missing.Count > 0)
            {
                ViewBag.Errors = new List<string> { "Thiếu cột bắt buộc: " + string.Join(", ", missing) };
                return View();
            }

            var incons = ValidateNameInfoConsistency(rows);
            if (incons.Count > 0)
            {
                ViewBag.Errors = incons.Select(kv =>
                    $"Dữ liệu không đồng bộ giữa tên và thông tin khách hàng với TEN_FILE='{kv.Key}'. " +
                    $"Có {kv.Value} bộ (EMAIL, TEN_TT, DIACHI_TT) khác nhau.").ToList();
                return View();
            }

            var ckErrors = ValidateChuKyNoStrict(rows);
            if (ckErrors.Count > 0)
            {
                ViewBag.Errors = ckErrors;
                return View();
            }

            // Ghi SQL
            var cnnStr = _config.GetConnectionString("DefaultConnection");
            try
            {
                using var cnn = new SqlConnection(cnnStr);
                await cnn.OpenAsync();

                if (await TableExistsAsync(cnn, finalTable))
                {
                    ViewBag.Errors = new List<string> { $"Tên bảng '{finalTable}' đã tồn tại trong cơ sở dữ liệu. Vui lòng chọn tên khác." };
                    return View();
                }

                var createSql = BuildCreateTableSql(finalTable, headersUpper);
                using (var cmdCreate = new SqlCommand(createSql, cnn))
                    await cmdCreate.ExecuteNonQueryAsync();

                var inserted = await InsertRowsAsync(cnn, finalTable, headersUpper, rows);
                ViewBag.Success = $"Import thành công {inserted} dòng vào bảng '{finalTable}'.";
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Errors = new List<string> { "Lỗi khi import vào cơ sở dữ liệu: " + ex.Message };
                return View();
            }
        }

        // ==================== Helpers ====================
        private sealed class ExcelReadResult
        {
            public List<Dictionary<string, object?>> Rows { get; }
            public List<string> HeadersUpper { get; }
            public ExcelReadResult(List<Dictionary<string, object?>> rows, List<string> headersUpper)
            {
                Rows = rows; HeadersUpper = headersUpper;
            }
        }

        // ⚙️ Đọc Excel và chuẩn hóa tiền tệ
        private static async Task<ExcelReadResult> ReadExcelAsync(IFormFile file)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var headersUpper = new List<string>();
            var rows = new List<Dictionary<string, object?>>();

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            using var reader = ExcelReaderFactory.CreateReader(ms);
            var result = reader.AsDataSet();
            if (result.Tables.Count == 0)
                throw new Exception("Không tìm thấy sheet hợp lệ trong file Excel.");

            var table = result.Tables[0];
            if (table.Rows.Count < 2)
                throw new Exception("File Excel không có dữ liệu đủ để đọc.");

            // Header
            foreach (var col in table.Rows[0].ItemArray)
            {
                var raw = col?.ToString()?.Trim() ?? "";
                var norm = NormalizeHeader(raw);
                if (string.IsNullOrEmpty(norm))
                    throw new Exception("Có cột không có tiêu đề.");
                headersUpper.Add(norm);
            }

            // Dữ liệu
            for (int r = 1; r < table.Rows.Count; r++)
            {
                var dataRow = table.Rows[r];
                bool allEmpty = true;
                for (int i = 0; i < table.Columns.Count; i++)
                    if (!string.IsNullOrWhiteSpace(dataRow[i]?.ToString())) { allEmpty = false; break; }
                if (allEmpty) continue;

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headersUpper.Count && i < table.Columns.Count; i++)
                {
                    var key = headersUpper[i];
                    var txt = dataRow[i]?.ToString()?.Trim();
                    object? val = txt;

                    if (key.Equals("NGAY_IN", StringComparison.OrdinalIgnoreCase))
                    {
                        val = string.IsNullOrWhiteSpace(txt) ? DBNull.Value : txt;
                    }
                    else if (key is "TIEN_TTHUE" or "THUE" or "TIEN_PT")
                    {
                        val = ParseMoney(txt);
                    }

                    dict[key] = val ?? DBNull.Value;
                }
                rows.Add(dict);
            }

            return new ExcelReadResult(rows, headersUpper);
        }

        // ✅ Hàm chuẩn hóa tiền — bỏ dấu, ký hiệu VNĐ, khoảng trắng, làm tròn
        private static object ParseMoney(string? txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return DBNull.Value;

            txt = txt.Trim();

            // Loại bỏ ký hiệu tiền tệ VNĐ, ₫, chữ, khoảng trắng
            txt = Regex.Replace(txt, @"[₫ĐđVvNn][Nn]?[Đđ]?", "", RegexOptions.IgnoreCase);
            txt = Regex.Replace(txt, @"\s+", "");

            // Xác định dấu thập phân
            int lastDot = txt.LastIndexOf('.');
            int lastComma = txt.LastIndexOf(',');
            if (lastComma > lastDot)
            {
                txt = txt.Replace(".", "");
                txt = txt.Replace(',', '.');
            }
            else
            {
                txt = txt.Replace(",", "");
            }

            txt = Regex.Replace(txt, @"[^0-9\.\-]", "");
            if (decimal.TryParse(txt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return Math.Round(val, 0, MidpointRounding.AwayFromZero);
            return DBNull.Value;
        }

        private static string NormalizeHeader(string raw)
        {
            raw = (raw ?? string.Empty).Trim();
            var tmp = Regex.Replace(raw, @"\s+", "_");
            tmp = Regex.Replace(tmp, @"[^A-Za-z0-9_]", "");
            return tmp.ToUpperInvariant();
        }

        private static Dictionary<string, int> ValidateNameInfoConsistency(List<Dictionary<string, object?>> rows)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                string tenFile = (r.TryGetValue("TEN_FILE", out var tf) ? tf?.ToString() : "") ?? "";
                string email = (r.TryGetValue("EMAIL", out var em) ? em?.ToString() : "") ?? "";
                string ten = (r.TryGetValue("TEN_TT", out var tn) ? tn?.ToString() : "") ?? "";
                string dc = (r.TryGetValue("DIACHI_TT", out var dcx) ? dcx?.ToString() : "") ?? "";

                var key = $"{email}||{ten}||{dc}";
                if (!map.ContainsKey(tenFile)) map[tenFile] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[tenFile].Add(key);
            }

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in map)
                if (kv.Value.Count > 1) result[kv.Key] = kv.Value.Count;

            return result;
        }

        private static List<string> ValidateChuKyNoStrict(List<Dictionary<string, object?>> rows)
        {
            var setValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var hasEmpty = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in rows)
            {
                var tenFile = (r.TryGetValue("TEN_FILE", out var tf) ? tf?.ToString() : "")?.Trim() ?? "";
                var chuky = (r.TryGetValue("CHUKYNO", out var ck) ? ck?.ToString() : "")?.Trim() ?? "";

                if (!setValues.ContainsKey(tenFile))
                    setValues[tenFile] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!hasEmpty.ContainsKey(tenFile))
                    hasEmpty[tenFile] = false;

                if (string.IsNullOrWhiteSpace(chuky))
                    hasEmpty[tenFile] = true;
                else
                    setValues[tenFile].Add(chuky);
            }

            var messages = new List<string>();
            foreach (var kv in setValues)
            {
                var tf = kv.Key;
                var distinct = kv.Value;
                var empty = hasEmpty.TryGetValue(tf, out var e) && e;

                if (distinct.Count != 1 || empty)
                {
                    if (distinct.Count == 0)
                        messages.Add($"TEN_FILE='{tf}' không có CHUKYNO hợp lệ hoặc đang để trống.");
                    else
                        messages.Add($"TEN_FILE='{tf}' có nhiều CHUKYNO: {string.Join(", ", distinct.OrderBy(x => x))}{(empty ? " (và có dòng bị trống)" : "")}.");
                }
            }
            return messages;
        }

        private static async Task<bool> TableExistsAsync(SqlConnection cnn, string table)
        {
            var sql = @"SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME=@t";
            using var cmd = new SqlCommand(sql, cnn);
            cmd.Parameters.AddWithValue("@t", table);
            var obj = await cmd.ExecuteScalarAsync();
            return obj != null;
        }

        private static string BuildCreateTableSql(string table, List<string> headersUpper)
        {
            var cols = new List<string>
            {
                "[ID] INT IDENTITY(1,1) PRIMARY KEY",
                "[ImportedAt] DATETIME2(0) NOT NULL DEFAULT (SYSUTCDATETIME())"
            };

            foreach (var h in headersUpper)
            {
                string sqlType = h switch
                {
                    "TIEN_TTHUE" or "THUE" or "TIEN_PT" => "DECIMAL(18,0) NULL",
                    "NGAY_IN" => "NVARCHAR(255) NULL",
                    "DIACHI_TT" => "NVARCHAR(500) NULL",
                    "EMAIL" or "TEN_TT" or "TEN_FILE" or "CHUKYNO" => "NVARCHAR(255) NULL",
                    _ => "NVARCHAR(255) NULL"
                };
                cols.Add($"[{h}] {sqlType}");
            }

            return $@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
    THROW 50000, 'Table existed.', 1;
CREATE TABLE [dbo].[{table}] (
    {string.Join(",\n    ", cols)}
);";
        }

        private static async Task<int> InsertRowsAsync(SqlConnection cnn, string table, List<string> headersUpper, List<Dictionary<string, object?>> rows)
        {
            var colList = string.Join(", ", headersUpper.Select(h => $"[{h}]"));
            var paramList = string.Join(", ", headersUpper.Select((h, i) => $"@p{i}"));
            var sql = $"INSERT INTO [dbo].[{table}] ({colList}) VALUES ({paramList});";

            int count = 0;
            foreach (var r in rows)
            {
                using var cmd = new SqlCommand(sql, cnn);
                for (int i = 0; i < headersUpper.Count; i++)
                {
                    var key = headersUpper[i];
                    r.TryGetValue(key, out var val);
                    cmd.Parameters.AddWithValue($"@p{i}", val ?? DBNull.Value);
                }
                count += await cmd.ExecuteNonQueryAsync();
            }
            return count;
        }
    }
}
