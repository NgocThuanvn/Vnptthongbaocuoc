using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Vnptthongbaocuoc.Services
{
    public class PdfExportServiceUNT
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public PdfExportServiceUNT(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        public async Task<byte[]?> GeneratePdfAsync(string table, string file)
        {
            var model = await LoadDataAsync(table, file);
            if (model.Rows.Count == 0)
            {
                return null;
            }

            return BuildPdf(model);
        }

        private sealed class PdfRow
        {
            public string MA_TT { get; set; } = string.Empty;
            public string ACCOUNT { get; set; } = string.Empty;
            public string TEN_TT { get; set; } = string.Empty;
            public string DIACHI_TT { get; set; } = string.Empty;
            public string DCLAPDAT { get; set; } = string.Empty;
            public decimal TIEN_TTHUE { get; set; }
            public decimal THUE { get; set; }
            public decimal TIEN_PT { get; set; }
            public string SOHD { get; set; } = string.Empty;
            public string NGAY_IN { get; set; } = string.Empty;
            public string MA_TRACUUHD { get; set; } = string.Empty;
        }

        private sealed class PdfModel
        {
            public string Table { get; set; } = string.Empty;
            public string File { get; set; } = string.Empty;
            public string TenKhachHang { get; set; } = string.Empty;
            public string DiaChiKhachHang { get; set; } = string.Empty;
            public string ChuKyNo { get; set; } = string.Empty;
            public string NgayInFile { get; set; } = string.Empty;
            public string ThoiHanThanhToan { get; set; } = string.Empty;
            public int SoDong { get; set; }
            public decimal TongTienTruocThue { get; set; }
            public decimal TongTienThue { get; set; }
            public decimal TongPT { get; set; }
            public List<PdfRow> Rows { get; set; } = new();
        }

        private async Task<PdfModel> LoadDataAsync(string table, string file)
        {
            var cnnStr = _config.GetConnectionString("DefaultConnection")!;
            var model = new PdfModel { Table = table, File = file };

            var sql = $@"
SELECT MA_TT, ACCOUNT, TEN_TT, DIACHI_TT, DCLAPDAT,
       TRY_CONVERT(DECIMAL(18,0), TIEN_TTHUE) AS TIEN_TTHUE,
       TRY_CONVERT(DECIMAL(18,0), THUE) AS THUE,
       TRY_CONVERT(DECIMAL(18,0), TIEN_PT) AS TIEN_PT,
       SOHD, NGAY_IN, MA_TRACUUHD
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file
ORDER BY TEN_TT, ACCOUNT;

SELECT TOP 1 TEN_TT AS TenKH, DIACHI_TT AS DiaChi, CHUKYNO, Ngayinfile, Thoihanthanhtoan
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file;

SELECT COUNT(*) AS [RowCount],
       SUM(TRY_CONVERT(DECIMAL(18,0), TIEN_TTHUE)) AS [SumTienTruocThue],
       SUM(TRY_CONVERT(DECIMAL(18,0), THUE)) AS [SumTienThue],
       SUM(TRY_CONVERT(DECIMAL(18,0), TIEN_PT)) AS [SumPT]
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file;
";

            await using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cnn);
            cmd.Parameters.AddWithValue("@file", file);

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var ngayIn = FormatNgayIn(rd["NGAY_IN"]);

                model.Rows.Add(new PdfRow
                {
                    MA_TT = rd["MA_TT"]?.ToString() ?? string.Empty,
                    ACCOUNT = rd["ACCOUNT"]?.ToString() ?? string.Empty,
                    TEN_TT = rd["TEN_TT"]?.ToString() ?? string.Empty,
                    DIACHI_TT = rd["DIACHI_TT"]?.ToString() ?? string.Empty,
                    DCLAPDAT = rd["DCLAPDAT"]?.ToString() ?? string.Empty,
                    TIEN_TTHUE = rd["TIEN_TTHUE"] is DBNull ? 0 : (decimal)rd["TIEN_TTHUE"],
                    THUE = rd["THUE"] is DBNull ? 0 : (decimal)rd["THUE"],
                    TIEN_PT = rd["TIEN_PT"] is DBNull ? 0 : (decimal)rd["TIEN_PT"],
                    SOHD = rd["SOHD"]?.ToString() ?? string.Empty,
                    NGAY_IN = ngayIn,
                    MA_TRACUUHD = rd["MA_TRACUUHD"]?.ToString() ?? string.Empty
                });
            }

            if (await rd.NextResultAsync() && await rd.ReadAsync())
            {
                model.TenKhachHang = rd["TenKH"]?.ToString() ?? string.Empty;
                model.DiaChiKhachHang = rd["DiaChi"]?.ToString() ?? string.Empty;
                model.ChuKyNo = rd["CHUKYNO"]?.ToString() ?? string.Empty;
                model.NgayInFile = rd["Ngayinfile"]?.ToString() ?? string.Empty;
                model.ThoiHanThanhToan = rd["Thoihanthanhtoan"]?.ToString() ?? string.Empty;
            }

            if (await rd.NextResultAsync() && await rd.ReadAsync())
            {
                model.SoDong = rd["RowCount"] is DBNull ? 0 : Convert.ToInt32(rd["RowCount"]);
                model.TongTienTruocThue = rd["SumTienTruocThue"] is DBNull ? 0 : Convert.ToDecimal(rd["SumTienTruocThue"]);
                model.TongTienThue = rd["SumTienThue"] is DBNull ? 0 : Convert.ToDecimal(rd["SumTienThue"]);
                model.TongPT = rd["SumPT"] is DBNull ? 0 : Convert.ToDecimal(rd["SumPT"]);
            }

            return model;
        }

        private static string FormatNgayIn(object? value)
        {
            if (value is null || value == DBNull.Value)
            {
                return string.Empty;
            }

            if (value is DateTime ngayInDateTime)
            {
                return ngayInDateTime.ToString("dd/MM/yyyy");
            }

            var ngayInString = value.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(ngayInString))
            {
                return string.Empty;
            }

            if (ngayInString.Length >= 8)
            {
                var dd = ngayInString.Substring(0, 2);
                var mm = ngayInString.Substring(2, 2);
                var yyyy = ngayInString.Substring(4, 4);
                return $"{dd}/{mm}/{yyyy}";
            }

            return ngayInString;
        }

        private static string FormatEightDigitDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.Length >= 8)
            {
                var dd = trimmed.Substring(0, 2);
                var mm = trimmed.Substring(2, 2);
                var yyyy = trimmed.Substring(4, 4);
                return $"{dd}/{mm}/{yyyy}";
            }

            return trimmed;
        }

        private static (string Day, string Month, string Year) GetEightDigitDateParts(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmed = value.Trim();
                if (trimmed.Length >= 8)
                {
                    return (trimmed.Substring(0, 2), trimmed.Substring(2, 2), trimmed.Substring(4, 4));
                }
            }

            var now = DateTime.Now;
            return (now.ToString("dd", CultureInfo.InvariantCulture),
                now.ToString("MM", CultureInfo.InvariantCulture),
                now.ToString("yyyy", CultureInfo.InvariantCulture));
        }

        private byte[] BuildPdf(PdfModel m)
        {
            // Logo bên trái khối VNPT
            var headerLogoPath = Path.Combine(_env.WebRootPath, "image", "Mocnentrang.png");
            var headerLogoImg = System.IO.File.Exists(headerLogoPath) ? System.IO.File.ReadAllBytes(headerLogoPath) : null;

            // Stamp + signature (nếu dùng)
            var stampPath = Path.Combine(_env.WebRootPath, "image", "Mocnentrang.png");
            var signPath = Path.Combine(_env.WebRootPath, "image", "chuky.png");
            var stampImg = System.IO.File.Exists(stampPath) ? System.IO.File.ReadAllBytes(stampPath) : null;
            var signImg = System.IO.File.Exists(signPath) ? System.IO.File.ReadAllBytes(signPath) : null;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());  // A4 ngang
                    page.Margin(20);
                    page.DefaultTextStyle(t => t
                        .FontFamily("Times New Roman")
                        .FontSize(10));

                    // ========= Stamp & Signature foreground =========
                    page.Foreground()
                        .AlignLeft()
                        .PaddingRight(10)
                        .PaddingBottom(130)
                        .Column(cc =>
                        {
                            if (stampImg != null)
                                cc.Item().AlignLeft().Width(90).Image(stampImg);
                        });
                    // ========= HEADER: 1 lần duy nhất, gồm Table 4 cột + 2 dòng tiêu đề =========
                    page.Header().Column(h =>
                    {
                        // Hàng 1: Bảng 4 cột
                        h.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(5);   // Cột 2: VNPT
                                columns.RelativeColumn(5);   // Cột 3: Quốc hiệu
                            });

                            // Cột 2: VNPT
                            table.Cell().Row(1).Column(1).AlignCenter().Column(col =>
                            {
                                col.Item().Text("VNPT CẦN THƠ").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("VNPT SÓC TRĂNG").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("-------------------").SemiBold().FontSize(11).AlignCenter();
                            });
                            // Cột 2: VNPT
                            table.Cell().Row(1).Column(2).AlignCenter().Column(col =>
                            {
                                col.Item().Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("Độc Lập - Tự Do - Hạnh Phúc").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("-------------------").SemiBold().FontSize(11).AlignCenter();
                            });


                        });

                        // Hàng 2: Tiêu đề thông báo
                        h.Item().PaddingTop(8)
                            .Text($"THÔNG BÁO CƯỚC DỊCH VỤ VIỄN THÔNG {m.ChuKyNo})")
                            .SemiBold().FontSize(13).AlignCenter();

                        //// Hàng 3: dòng phụ
                        //h.Item().Text($"KỲ CƯỚC: {m.ChuKyNo}")
                        //    .FontSize(9).AlignCenter();
                        //// Hàng 3: dòng phụ
                        h.Item().Text("")
                            .FontSize(9).AlignCenter();
                    });

                    // ========= CONTENT =========
                    page.Content().Column(col =>
                    {
                       
                        // Bảng chi tiết (cột chữ canh giữa, tiền PT canh phải)

                        col.Item()
   .Element(e => e.DefaultTextStyle(t => t
       .FontFamily("Times New Roman")
       .FontSize(9)))   // áp dụng font 10 cho toàn bảng
   .Table(table =>
   {
       table.ColumnsDefinition(cols =>
       {
           cols.ConstantColumn(30);
           cols.ConstantColumn(80);
           cols.ConstantColumn(85);
           cols.RelativeColumn(6);
           cols.ConstantColumn(60);
           cols.ConstantColumn(60);
           cols.ConstantColumn(60);
           cols.ConstantColumn(60);
           cols.ConstantColumn(60);
           cols.ConstantColumn(95);
       });

       table.Header(h =>
       {
           h.Cell().Element(CellHeaderCenter).Text("Stt");
           h.Cell().Element(CellHeaderCenter).Text("Mã TT");
           h.Cell().Element(CellHeaderCenter).Text("Account");
           h.Cell().Element(CellHeaderCenter).Text("Tên Khách hàng");
           h.Cell().Element(CellHeaderCenter).Text("Tiền T.Thuế");
           h.Cell().Element(CellHeaderCenter).Text("Tiền thuế");
           h.Cell().Element(CellHeaderCenter).Text("Tiền PT");
           h.Cell().Element(CellHeaderCenter).Text("Số HĐ");
           h.Cell().Element(CellHeaderCenter).Text("Ngày In");
           h.Cell().Element(CellHeaderCenter).Text("Mã tra cứu HD");
       });

       var i = 0;
       foreach (var r in m.Rows)
       {
           i++;
           table.Cell().Element(CellCenter).Text(i.ToString());
           table.Cell().Element(CellCenter).Text(r.MA_TT);
           table.Cell().Element(CellCenter).Text(r.ACCOUNT);
           table.Cell().Element(CellLeft).Text(string.IsNullOrWhiteSpace(r.DCLAPDAT) ? r.TEN_TT : r.DCLAPDAT);
           table.Cell().Element(CellRight).Text(string.Format("{0:N0}", r.TIEN_TTHUE));
           table.Cell().Element(CellRight).Text(string.Format("{0:N0}", r.THUE));
           table.Cell().Element(CellRight).Text(string.Format("{0:N0}", r.TIEN_PT));
           table.Cell().Element(CellCenter).Text(r.SOHD);
           table.Cell().Element(CellCenter).Text(r.NGAY_IN);
           table.Cell().Element(CellCenter).Text(r.MA_TRACUUHD);
       }

       table.Cell().ColumnSpan(4).Element(CellTotalRight).Text("Tổng cộng:");
       table.Cell().Element(CellTotalRight).Text(string.Format("{0:N0}", m.TongTienTruocThue));
       table.Cell().Element(CellTotalRight).Text(string.Format("{0:N0}", m.TongTienThue));
       table.Cell().Element(CellTotalRight).Text(string.Format("{0:N0}", m.TongPT));
       table.Cell().ColumnSpan(3).Element(CellTotalRight).Text("");

       // styles
       static IContainer CellHeaderCenter(IContainer c) => c.Border(0.5f).Background(Colors.Grey.Lighten3)
           .Padding(4).AlignCenter().DefaultTextStyle(x => x
               .FontFamily("Times New Roman")
               .SemiBold());
       static IContainer CellHeaderRight(IContainer c) => c.Border(0.5f).Background(Colors.Grey.Lighten3)
           .Padding(4).AlignRight().DefaultTextStyle(x => x
               .FontFamily("Times New Roman")
               .SemiBold());
       static IContainer CellCenter(IContainer c) => c.Border(0.5f).Padding(3).AlignCenter();
       static IContainer CellRight(IContainer c) => c.Border(0.5f).Padding(3).AlignRight();
       static IContainer CellTotalRight(IContainer c) => c.Border(0.5f).Padding(4).AlignRight().DefaultTextStyle(x => x
           .FontFamily("Times New Roman")
           .SemiBold());
       static IContainer CellLeft(IContainer c) => c.Border(0.5f).Padding(3).AlignLeft();
   });

                     
                        col.Item().PaddingTop(0).Text(t =>
                        {
                            t.Line($"Tổng tiền PT bằng chữ: {DocTienBangChu(m.TongPT)}").Italic().Bold();
                        });

                        //col.Item().PageBreak();


                        // chữ ký
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text("").AlignCenter();
                            r.RelativeItem().Column(c2 =>
                            {
                                var (ngay, thang, nam) = GetEightDigitDateParts(m.NgayInFile);
                                c2.Item().Text($"Cần Thơ, ngày {ngay} tháng {thang} năm {nam}")
                                    .AlignCenter();
                                c2.Item().Text("Người giao").AlignCenter();
                                if (signImg != null)
                                {
                                    c2.Item().AlignCenter().PaddingTop(5).Width(120).Image(signImg).FitWidth();
                                }
                                else
                                {
                                    c2.Item().Height(55);
                                }

                                c2.Item().AlignCenter().Text("Trịnh Mỹ Hộ");
                                c2.Item().AlignCenter().Text("Số ĐT: 0911814999");

                            });
                        });
                    });


                    // footer
                    page.Footer().AlignCenter()
                        .Text($"© VNPT Thông Báo Cước —Số dòng: {m.SoDong:N0}  ·  Tổng PT: {m.TongPT:N0}");
                });
            });

            return doc.GeneratePdf();
        }

        private static readonly string[] ChuSo = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };

        private static string DocTienBangChu(decimal soTien)
        {
            var soTienLamTron = Math.Round(soTien, 0, MidpointRounding.AwayFromZero);
            if (soTienLamTron <= 0)
                return "Không đồng";

            var units = new[] { "", " nghìn", " triệu", " tỷ", " nghìn tỷ", " triệu tỷ" };
            var groups = new List<int>();
            var value = (long)soTienLamTron;

            while (value > 0 && groups.Count < units.Length)
            {
                groups.Insert(0, (int)(value % 1000));
                value /= 1000;
            }

            var parts = new List<string>();
            var totalGroups = groups.Count;

            for (var i = 0; i < totalGroups; i++)
            {
                var groupValue = groups[i];
                if (groupValue == 0)
                    continue;

                var unitIndex = totalGroups - i - 1;
                var docDayDu = i > 0;
                var blockText = DocSo3ChuSo(groupValue, docDayDu);
                parts.Add(($"{blockText}{units[unitIndex]}").Trim());
            }

            var result = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
            if (string.IsNullOrEmpty(result))
                result = "Không";

            result = char.ToUpper(result[0]) + result[1..];
            return result + " đồng";
        }

        private static string DocSo3ChuSo(int soBaChuSo, bool docDayDu)
        {
            var tram = soBaChuSo / 100;
            var chuc = (soBaChuSo % 100) / 10;
            var donvi = soBaChuSo % 10;

            var builder = new List<string>();

            if (tram > 0)
            {
                builder.Add(ChuSo[tram] + " trăm");
            }
            else if (docDayDu && (chuc > 0 || donvi > 0))
            {
                builder.Add("không trăm");
            }

            if (chuc > 1)
            {
                builder.Add(ChuSo[chuc] + " mươi");
                builder.Add(DocDonViKhiChucLonHonMot(donvi));
            }
            else if (chuc == 1)
            {
                builder.Add("mười");
                builder.Add(DocDonViKhiChucBangMot(donvi));
            }
            else if (donvi > 0)
            {
                if (builder.Count > 0)
                {
                    builder.Add("linh");
                }

                builder.Add(DocDonViKhiChucBangKhong(donvi));
            }

            return string.Join(" ", builder.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        }

        private static string DocDonViKhiChucLonHonMot(int donvi)
        {
            return donvi switch
            {
                0 => string.Empty,
                1 => "mốt",
                4 => "tư",
                5 => "lăm",
                _ => ChuSo[donvi]
            };
        }

        private static string DocDonViKhiChucBangMot(int donvi)
        {
            return donvi switch
            {
                0 => string.Empty,
                1 => "một",
                4 => "bốn",
                5 => "lăm",
                _ => ChuSo[donvi]
            };
        }

        private static string DocDonViKhiChucBangKhong(int donvi)
        {
            return donvi switch
            {
                0 => string.Empty,
                5 => "năm",
                _ => ChuSo[donvi]
            };
        }
    }
}
