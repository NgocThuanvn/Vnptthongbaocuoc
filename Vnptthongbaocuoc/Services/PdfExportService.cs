using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Vnptthongbaocuoc.Services
{
    public class PdfExportService
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public PdfExportService(IConfiguration config, IWebHostEnvironment env)
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
SELECT MA_TT, ACCOUNT, TEN_TT, DIACHI_TT,
       TRY_CONVERT(DECIMAL(18,0), TIEN_TTHUE) AS TIEN_TTHUE,
       TRY_CONVERT(DECIMAL(18,0), THUE) AS THUE,
       TRY_CONVERT(DECIMAL(18,0), TIEN_PT) AS TIEN_PT,
       SOHD, NGAY_IN, MA_TRACUUHD
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file
ORDER BY TEN_TT, ACCOUNT;

SELECT TOP 1 TEN_TT AS TenKH, DIACHI_TT AS DiaChi, CHUKYNO
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
                var ngayInValue = rd["NGAY_IN"];
                var ngayIn = string.Empty;

                if (ngayInValue is DateTime ngayInDateTime)
                {
                    ngayIn = ngayInDateTime.ToString("dd/MM/yyyy");
                }
                else if (ngayInValue != DBNull.Value)
                {
                    var ngayInString = ngayInValue?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(ngayInString))
                    {
                        if (DateTime.TryParse(ngayInString, out var parsedNgayIn))
                        {
                            ngayIn = parsedNgayIn.ToString("dd/MM/yyyy");
                        }
                        else
                        {
                            var numericNgayIn = new string(ngayInString.Where(char.IsDigit).ToArray());
                            if (numericNgayIn.Length == 8)
                            {
                                var day = numericNgayIn.Substring(0, 2);
                                var month = numericNgayIn.Substring(2, 2);
                                var year = numericNgayIn.Substring(4, 4);
                                ngayIn = $"{day}/{month}/{year}";
                            }
                            else
                            {
                                ngayIn = ngayInString;
                            }
                        }
                    }
                }

                model.Rows.Add(new PdfRow
                {
                    MA_TT = rd["MA_TT"]?.ToString() ?? string.Empty,
                    ACCOUNT = rd["ACCOUNT"]?.ToString() ?? string.Empty,
                    TEN_TT = rd["TEN_TT"]?.ToString() ?? string.Empty,
                    DIACHI_TT = rd["DIACHI_TT"]?.ToString() ?? string.Empty,
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
                            });
                            // Cột 2: VNPT
                            table.Cell().Row(1).Column(2).AlignCenter().Column(col =>
                            {
                                col.Item().Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("Độc Lập - Tự Do - Hạnh Phúc").SemiBold().FontSize(11).AlignCenter().Underline();
                                col.Item().Text("-------------------").SemiBold().FontSize(11).AlignCenter().Underline();
                            });


                        });

                        // Hàng 2: Tiêu đề thông báo
                        h.Item().PaddingTop(8)
                            .Text("THÔNG BÁO VÀ ĐỀ NGHỊ THANH TOÁN CƯỚC DỊCH VỤ VIỄN THÔNG")
                            .SemiBold().FontSize(13).AlignCenter();

                        // Hàng 3: dòng phụ
                        h.Item().Text($"(V/v tiền cước sử dụng dịch vụ VT-CNTT: {m.ChuKyNo})")
                            .FontSize(9).AlignCenter();
                        // Hàng 3: dòng phụ
                        h.Item().Text("")
                            .FontSize(9).AlignCenter();
                    });

                    // ========= CONTENT =========
                    page.Content().Column(col =>
                    {
                        col.Spacing(1);
                        col.Item().Text(t =>
                        {
                            t.Span("VNPT Cần Thơ xin thông báo đến quý khách hàng tiền cước các dịch vụ VT-CNTT quý khách  đã sử dụng cụ thể như sau:")
                            .SemiBold().FontSize(10);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span(" -Kính gửi: ").SemiBold().FontSize(10);
                            t.Span(m.TenKhachHang).SemiBold().FontSize(10);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span(" -Địa chỉ: ").SemiBold().FontSize(10);
                            t.Span(m.DiaChiKhachHang).SemiBold().FontSize(10);
                        });
                        // Bảng chi tiết (cột chữ canh giữa, tiền PT canh phải)

                        col.Item()
   .Element(e => e.DefaultTextStyle(t => t
       .FontFamily("Times New Roman")
       .FontSize(9)))   // áp dụng font 10 cho toàn bảng
   .Table(table =>
   {
       table.ColumnsDefinition(cols =>
       {
           cols.ConstantColumn(25);
           cols.ConstantColumn(80);
           cols.ConstantColumn(85);
           cols.RelativeColumn(4);
           cols.ConstantColumn(70);
           cols.ConstantColumn(70);
           cols.ConstantColumn(70);
           cols.ConstantColumn(70);
           cols.ConstantColumn(70);
           cols.ConstantColumn(95);
       });

       table.Header(h =>
       {
           h.Cell().Element(CellHeaderCenter).Text("#");
           h.Cell().Element(CellHeaderCenter).Text("MÃ_TT");
           h.Cell().Element(CellHeaderCenter).Text("ACCOUNT");
           h.Cell().Element(CellHeaderCenter).Text("TÊN QUÝ KHÁCH");
           h.Cell().Element(CellHeaderRight).Text("TIỀN TRƯỚC THUẾ");
           h.Cell().Element(CellHeaderRight).Text("TIỀN THUẾ");
           h.Cell().Element(CellHeaderRight).Text("TIỀN PT");
           h.Cell().Element(CellHeaderCenter).Text("SỐ HĐ");
           h.Cell().Element(CellHeaderCenter).Text("NGÀY IN");
           h.Cell().Element(CellHeaderCenter).Text("MÃ TRA CỨU HD");
       });

       var i = 0;
       foreach (var r in m.Rows)
       {
           i++;
           table.Cell().Element(CellCenter).Text(i.ToString());
           table.Cell().Element(CellCenter).Text(r.MA_TT);
           table.Cell().Element(CellCenter).Text(r.ACCOUNT);
           table.Cell().Element(CellLeft).Text(r.TEN_TT);
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
                            t.Line("Kính đề nghị quý khách hàng vui lòng chuyển khoản thanh toán trước ngày 24/10/2025");
                            t.Line("- Tên tài khoản:  VIỄN THÔNG CẦN THƠ - TẬP ĐOÀN BƯU CHÍNH VIỄN THÔNG VIỆT NAM (CHI NHÁNH CTY TNHH)");
                            t.Line("- Số tài khoản: 7600201.009180 - Tại : Ngân Hàng Nông Nghiệp và PTNT Việt Nam - CN  Sóc Trăng");
                            t.Line("- Địa chỉ: 11 Phan Đình Phùng, Phường Ninh Kiều, Thành Phố Cần Thơ");
                            t.Line($"- Nội dung: {m.TenKhachHang} thanh toán cước DVVT");
                            t.Line("- Quý khách hàng xem hoặc tải hóa đơn tại địa chỉ https://stg-tt78.vnpt-invoice.com.vn/ (mật khẩu Vnptst@123)");
                            t.Line("Rất mong sự hỗ trợ của quý khách hàng, nhằm giúp chúng tôi hoàn thành nhiệm vụ được giao.");
                            t.Line("Trân trọng kính chào!");
                        });

                        // chữ ký
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Người nhận\n(Ký & ghi rõ họ tên)").AlignCenter();
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text($"Cần Thơ, ngày {DateTime.Now:dd} tháng {DateTime.Now:MM} năm {DateTime.Now:yyyy}")
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
