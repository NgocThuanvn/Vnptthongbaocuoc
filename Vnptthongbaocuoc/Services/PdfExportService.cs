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
                    NGAY_IN = rd["NGAY_IN"]?.ToString() ?? string.Empty,
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
            var headerLogoPath = Path.Combine(_env.WebRootPath, "image", "Mocnentrang.png");
            var headerLogoImg = File.Exists(headerLogoPath) ? File.ReadAllBytes(headerLogoPath) : null;

            var stampPath = Path.Combine(_env.WebRootPath, "image", "Mocnentrang.png");
            var signPath = Path.Combine(_env.WebRootPath, "image", "chuky.png");
            var stampImg = File.Exists(stampPath) ? File.ReadAllBytes(stampPath) : null;
            var signImg = File.Exists(signPath) ? File.ReadAllBytes(signPath) : null;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(t => t.FontSize(10));

                    page.Foreground()
                        .AlignLeft()
                        .PaddingRight(10)
                        .PaddingBottom(130)
                        .Column(cc =>
                        {
                            if (stampImg != null)
                                cc.Item().AlignLeft().Width(90).Image(stampImg);
                        });

                    page.Header().Column(h =>
                    {
                        h.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(5);
                                columns.RelativeColumn(5);
                            });

                            table.Cell().Row(1).Column(1).AlignCenter().Column(col =>
                            {
                                col.Item().Text("VNPT CẦN THƠ").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("VNPT SÓC TRĂNG").SemiBold().FontSize(11).AlignCenter();
                            });

                            table.Cell().Row(1).Column(2).AlignCenter().Column(col =>
                            {
                                col.Item().Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").SemiBold().FontSize(11).AlignCenter();
                                col.Item().Text("Độc Lập - Tự Do - Hạnh Phúc").SemiBold().FontSize(11).AlignCenter().Underline();
                                col.Item().Text("-------------------").SemiBold().FontSize(11).AlignCenter().Underline();
                            });
                        });

                        h.Item().PaddingTop(15).Column(col =>
                        {
                            col.Item().AlignCenter().Text("THÔNG BÁO CƯỚC").SemiBold().FontSize(16);
                            col.Item().AlignCenter().Text($"Chu kỳ nợ: {m.ChuKyNo}").FontSize(11);
                            col.Item().AlignCenter().Text($"Tổng tiền phải thu: {m.TongPT:N0} đ").FontSize(11);
                        });
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Row(row =>
                        {
                            row.RelativeColumn().Column(c =>
                            {
                                c.Item().Text(text =>
                                {
                                    text.Span("Khách hàng: ").SemiBold();
                                    text.Span(m.TenKhachHang);
                                });
                                c.Item().Text(text =>
                                {
                                    text.Span("Địa chỉ: ").SemiBold();
                                    text.Span(m.DiaChiKhachHang);
                                });
                                c.Item().Text(text =>
                                {
                                    text.Span("Mã tra cứu hóa đơn: ").SemiBold();
                                    text.Span(m.Rows.FirstOrDefault()?.MA_TRACUUHD ?? string.Empty);
                                });
                            });

                            row.RelativeColumn().AlignRight().Column(c =>
                            {
                                if (headerLogoImg != null)
                                {
                                    c.Item().Width(120).Height(50).Image(headerLogoImg, ImageScaling.FitArea);
                                }
                            });
                        });

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(3);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("#");
                                header.Cell().Element(CellStyle).Text("Mã TT");
                                header.Cell().Element(CellStyle).Text("Account");
                                header.Cell().Element(CellStyle).Text("Tên thuê bao");
                                header.Cell().Element(CellStyle).Text("Địa chỉ");
                                header.Cell().Element(CellStyle).AlignRight().Text("Tiền trước thuế");
                                header.Cell().Element(CellStyle).AlignRight().Text("Thuế");
                                header.Cell().Element(CellStyle).AlignRight().Text("Tiền phải thu");
                                header.Cell().Element(CellStyle).Text("Số HĐ");
                            });

                            var index = 0;
                            foreach (var row in m.Rows)
                            {
                                index++;
                                table.Cell().Element(CellStyle).Text(index.ToString());
                                table.Cell().Element(CellStyle).Text(row.MA_TT);
                                table.Cell().Element(CellStyle).Text(row.ACCOUNT);
                                table.Cell().Element(CellStyle).Text(row.TEN_TT);
                                table.Cell().Element(CellStyle).Text(row.DIACHI_TT);
                                table.Cell().Element(CellStyle).AlignRight().Text(row.TIEN_TTHUE.ToString("N0"));
                                table.Cell().Element(CellStyle).AlignRight().Text(row.THUE.ToString("N0"));
                                table.Cell().Element(CellStyle).AlignRight().Text(row.TIEN_PT.ToString("N0"));
                                table.Cell().Element(CellStyle).Text(row.SOHD);
                            }
                        });

                        col.Item().AlignRight().Column(c =>
                        {
                            c.Item().Text($"Tổng dòng: {m.SoDong:N0}");
                            c.Item().Text($"Tổng tiền trước thuế: {m.TongTienTruocThue:N0} đ");
                            c.Item().Text($"Tổng thuế: {m.TongTienThue:N0} đ");
                            c.Item().Text($"Tổng tiền phải thu: {m.TongPT:N0} đ");
                        });

                        col.Item().PaddingTop(20).AlignRight().Column(c =>
                        {
                            if (signImg != null)
                            {
                                c.Item().Width(150).Image(signImg, ImageScaling.FitArea);
                            }

                            c.Item().AlignRight().Text("Người lập").Italic();
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(t => t.FontSize(9));
                        text.Span("Trang ").SpanCurrentPageNumber();
                        text.Span(" / ").SpanTotalPages();
                    });
                });
            });

            return doc.GeneratePdf();
        }

        private static IContainer CellStyle(IContainer container)
        {
            return container.Border(0.5f)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingVertical(4)
                .PaddingHorizontal(6)
                .AlignMiddle();
        }
    }
}
