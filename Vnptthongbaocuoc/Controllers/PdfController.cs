using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Vnptthongbaocuoc.Controllers
{
    [Authorize]
    public class PdfController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public PdfController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        // /Pdf/FromFile?table=Vnpt_xxx&file=BANGKE001
        [HttpGet]
        public async Task<IActionResult> FromFile(string table, string file)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(file))
                return BadRequest("Thiếu tham số table hoặc file.");

            if (!table.StartsWith("Vnpt_", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Tên bảng không hợp lệ.");

            var model = await LoadDataAsync(table, file);
            if (model.Rows.Count == 0)
                return BadRequest("Không có dữ liệu cho TEN_FILE này.");

            var pdfBytes = BuildPdf(model);
            var downloadName = $"ThongBao_{file}.pdf";
            return File(pdfBytes, "application/pdf", downloadName);
        }

        // ===== Models =====
        private sealed class PdfRow
        {
            public string MA_TT { get; set; } = "";
            public string ACCOUNT { get; set; } = "";
            public string TEN_TT { get; set; } = "";
            public string DIACHI_TT { get; set; } = "";
            public decimal TIEN_PT { get; set; }
            public string SOHD { get; set; } = "";
            public string NGAY_IN { get; set; } = "";
            public string MA_TRACUUHD { get; set; } = "";
        }

        private sealed class PdfModel
        {
            public string Table { get; set; } = "";
            public string File { get; set; } = "";
            public string TenKhachHang { get; set; } = "";
            public string DiaChiKhachHang { get; set; } = "";
            public string ChuKyNo { get; set; } = "";
            public int SoDong { get; set; }
            public decimal TongPT { get; set; }
            public List<PdfRow> Rows { get; set; } = new();
        }

        // ===== Data =====
        private async Task<PdfModel> LoadDataAsync(string table, string file)
        {
            var cnnStr = _config.GetConnectionString("DefaultConnection")!;
            var model = new PdfModel { Table = table, File = file };

            var sql = $@"
SELECT MA_TT, ACCOUNT, TEN_TT, DIACHI_TT, 
       TRY_CONVERT(DECIMAL(18,0), TIEN_PT) AS TIEN_PT,
       SOHD, NGAY_IN, MA_TRACUUHD
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file
ORDER BY TEN_TT, ACCOUNT;

SELECT TOP 1 TEN_TT AS TenKH, DIACHI_TT AS DiaChi, CHUKYNO
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file;

SELECT COUNT(*) AS [RowCount],
       SUM(TRY_CONVERT(DECIMAL(18,0), TIEN_PT)) AS [SumPT]
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file;
";

            using var cnn = new SqlConnection(cnnStr);
            await cnn.OpenAsync();

            using var cmd = new SqlCommand(sql, cnn);
            cmd.Parameters.AddWithValue("@file", file);

            using var rd = await cmd.ExecuteReaderAsync();

            // 1) detail
            while (await rd.ReadAsync())
            {
                model.Rows.Add(new PdfRow
                {
                    MA_TT = rd["MA_TT"]?.ToString() ?? "",
                    ACCOUNT = rd["ACCOUNT"]?.ToString() ?? "",
                    TEN_TT = rd["TEN_TT"]?.ToString() ?? "",
                    DIACHI_TT = rd["DIACHI_TT"]?.ToString() ?? "",
                    TIEN_PT = rd["TIEN_PT"] is DBNull ? 0 : (decimal)rd["TIEN_PT"],
                    SOHD = rd["SOHD"]?.ToString() ?? "",
                    NGAY_IN = rd["NGAY_IN"]?.ToString() ?? "",
                    MA_TRACUUHD = rd["MA_TRACUUHD"]?.ToString() ?? ""
                });
            }

            // 2) header
            if (await rd.NextResultAsync() && await rd.ReadAsync())
            {
                model.TenKhachHang = rd["TenKH"]?.ToString() ?? "";
                model.DiaChiKhachHang = rd["DiaChi"]?.ToString() ?? "";
                model.ChuKyNo = rd["CHUKYNO"]?.ToString() ?? "";
            }

            // 3) summary
            if (await rd.NextResultAsync() && await rd.ReadAsync())
            {
                model.SoDong = rd["RowCount"] is DBNull ? 0 : Convert.ToInt32(rd["RowCount"]);
                model.TongPT = rd["SumPT"] is DBNull ? 0 : Convert.ToDecimal(rd["SumPT"]);
            }

            return model;
        }

        // ===== PDF =====
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
                    page.DefaultTextStyle(t => t.FontSize(10));

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
                        h.Item().Text($"(V/v tiền cước sử dụng dịch vụ VT-CNTT tháng {m.ChuKyNo})")
                            .FontSize(9).AlignCenter();
                        // Hàng 3: dòng phụ
                        h.Item().Text("")
                            .FontSize(9).AlignCenter();
                    });

                    // ========= CONTENT =========
                    page.Content().Column(col =>
                    {
                        col.Spacing(5);
                        col.Item().Text(t =>
                        {
                            t.Span("VNPT Cần Thơ xin thông báo đến quý khách hàng tiền cước các dịch vụ VT-CNTT quý khách  đã sử dụng cụ thể như sau:")
                            .SemiBold().FontSize(12);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span(" -Kính gửi: ").SemiBold().FontSize(12);
                            t.Span(m.TenKhachHang).SemiBold().FontSize(12);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span(" -Địa chỉ: ").SemiBold().FontSize(12);
                            t.Span(m.DiaChiKhachHang).SemiBold().FontSize(12);
                        });
                        // Bảng chi tiết (cột chữ canh giữa, tiền PT canh phải)

                        col.Item()
   .Element(e => e.DefaultTextStyle(t => t.FontSize(9)))   // áp dụng font 10 cho toàn bảng
   .Table(table =>
   {
       table.ColumnsDefinition(cols =>
       {
           cols.ConstantColumn(25);
           cols.ConstantColumn(80);
           cols.ConstantColumn(85);
           cols.RelativeColumn(2);
           cols.RelativeColumn(3);
           cols.ConstantColumn(90);
           cols.ConstantColumn(80);
           cols.ConstantColumn(80);
           cols.ConstantColumn(95);
       });

       table.Header(h =>
       {
           h.Cell().Element(CellHeaderCenter).Text("#");
           h.Cell().Element(CellHeaderCenter).Text("MÃ_TT");
           h.Cell().Element(CellHeaderCenter).Text("ACCOUNT");
           h.Cell().Element(CellHeaderCenter).Text("TÊN QUÝ KHÁCH");
           h.Cell().Element(CellHeaderCenter).Text("ĐỊA CHỈ");
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
           table.Cell().Element(CellLeft).Text(r.DIACHI_TT);
           table.Cell().Element(CellRight).Text(string.Format("{0:N0}", r.TIEN_PT));
           table.Cell().Element(CellCenter).Text(r.SOHD);
           table.Cell().Element(CellCenter).Text(r.NGAY_IN);
           table.Cell().Element(CellCenter).Text(r.MA_TRACUUHD);
       }

       table.Footer(f =>
       {
           f.Cell().ColumnSpan(5).Element(CellTotalRight).Text("Tổng cộng:");
           f.Cell().Element(CellTotalRight).Text(string.Format("{0:N0}", m.TongPT));
           f.Cell().ColumnSpan(3).Element(CellTotalRight).Text(""); 
       });

       // styles
       static IContainer CellHeaderCenter(IContainer c) => c.Border(0.5f).Background(Colors.Grey.Lighten3)
           .Padding(4).AlignCenter().DefaultTextStyle(x => x.SemiBold());
       static IContainer CellHeaderRight(IContainer c) => c.Border(0.5f).Background(Colors.Grey.Lighten3)
           .Padding(4).AlignRight().DefaultTextStyle(x => x.SemiBold());
       static IContainer CellCenter(IContainer c) => c.Border(0.5f).Padding(3).AlignCenter();
       static IContainer CellRight(IContainer c) => c.Border(0.5f).Padding(3).AlignRight();
       static IContainer CellTotalRight(IContainer c) => c.Border(0.5f).Padding(4).AlignRight().DefaultTextStyle(x => x.SemiBold());
       static IContainer CellLeft(IContainer c) => c.Border(0.5f).Padding(3).AlignLeft();
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

                            });
                        });
                    });


                    // footer
                    page.Footer().AlignCenter()
                        .Text($"© VNPT Thông Báo Cước — File: {m.File} — Số dòng: {m.SoDong:N0}  ·  Tổng PT: {m.TongPT:N0}");
                });
            });

            return doc.GeneratePdf();
        }
    }
}
