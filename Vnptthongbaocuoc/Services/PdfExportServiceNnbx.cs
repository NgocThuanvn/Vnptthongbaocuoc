using System.Globalization;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Vnptthongbaocuoc.Services
{
    public class PdfExportServiceNnbx
    {
        private static readonly NumberFormatInfo CurrencyNumberFormat = new()
        {
            NumberGroupSeparator = ".",
            NumberDecimalSeparator = ","
        };

        private readonly IConfiguration _config;

        public PdfExportServiceNnbx(IConfiguration config)
        {
            _config = config;
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
            public decimal TIEN_PT { get; set; }
        }

        private sealed class PdfModel
        {
            public string Table { get; set; } = string.Empty;
            public string File { get; set; } = string.Empty;
            public string TenKhachHang { get; set; } = string.Empty;
            public string DiaChiKhachHang { get; set; } = string.Empty;
            public string SoTaiKhoanKhachHang { get; set; } = string.Empty;
            public string ChuKyNo { get; set; } = string.Empty;
            public string NgayInFile { get; set; } = string.Empty;
            public int SoDong { get; set; }
            public decimal TongPT { get; set; }
            public string SampleAccount { get; set; } = string.Empty;
            public List<PdfRow> Rows { get; set; } = new();
        }

        private static string FormatCurrency(decimal value)
        {
            return value.ToString("N0", CurrencyNumberFormat);
        }

        private async Task<PdfModel> LoadDataAsync(string table, string file)
        {
            var cnnStr = _config.GetConnectionString("DefaultConnection")!;
            var model = new PdfModel { Table = table, File = file };

            var sql = $@"
SELECT MA_TT, ACCOUNT, TEN_TT, DIACHI_TT, DCLAPDAT,
       TRY_CONVERT(DECIMAL(18,0), TIEN_PT) AS TIEN_PT
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file
ORDER BY TEN_TT, ACCOUNT;

SELECT TOP 1 TEN_TT AS TenKH, DIACHI_TT AS DiaChi, CHUKYNO, Ngayinfile, STK_KH
FROM [dbo].[{table}] WITH (NOLOCK)
WHERE TEN_FILE = @file;

SELECT COUNT(*) AS [RowCount],
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
                var row = new PdfRow
                {
                    MA_TT = rd["MA_TT"]?.ToString() ?? string.Empty,
                    ACCOUNT = rd["ACCOUNT"]?.ToString() ?? string.Empty,
                    TEN_TT = rd["TEN_TT"]?.ToString() ?? string.Empty,
                    DIACHI_TT = rd["DIACHI_TT"]?.ToString() ?? string.Empty,
                    DCLAPDAT = rd["DCLAPDAT"]?.ToString() ?? string.Empty,
                    TIEN_PT = rd["TIEN_PT"] is DBNull ? 0 : (decimal)rd["TIEN_PT"]
                };

                if (string.IsNullOrWhiteSpace(model.SampleAccount))
                {
                    model.SampleAccount = row.ACCOUNT;
                }

                model.Rows.Add(row);
            }

            if (await rd.NextResultAsync() && await rd.ReadAsync())
            {
                model.TenKhachHang = rd["TenKH"]?.ToString() ?? string.Empty;
                model.DiaChiKhachHang = rd["DiaChi"]?.ToString() ?? string.Empty;
                model.ChuKyNo = rd["CHUKYNO"]?.ToString() ?? string.Empty;
                model.NgayInFile = rd["Ngayinfile"]?.ToString() ?? string.Empty;
                model.SoTaiKhoanKhachHang = rd["STK_KH"]?.ToString() ?? string.Empty;
            }

            if (await rd.NextResultAsync() && await rd.ReadAsync())
            {
                model.SoDong = rd["RowCount"] is DBNull ? 0 : Convert.ToInt32(rd["RowCount"]);
                model.TongPT = rd["SumPT"] is DBNull ? 0 : Convert.ToDecimal(rd["SumPT"]);
            }

            return model;
        }

        private static (string Day, string Month, string Year) GetDateParts(string? value)
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
            const float ParagraphSpacing = 4f;
            const string BankName = "NGÂN HÀNG TMCP ĐẦU TƯ VÀ PHÁT TRIỂN VIỆT NAM - CNST";
            const string SellerAccount = "7420226886";
            const string SellerUnit = "VNPT CẦN THƠ";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(t => t.FontFamily("Times New Roman").FontSize(10).LineHeight(1.25f));

                    page.Content().Column(col =>
                    {
                        col.Spacing(ParagraphSpacing);

                        col.Item().Row(row =>
                        {
                            row.ConstantItem(120).Border(1).Padding(6).AlignCenter().Text(text =>
                            {
                                text.AlignCenter();
                                text.Span("Không ghi vào\nkhu vực này");
                            });

                            row.RelativeItem().AlignCenter().Column(center =>
                            {
                                center.Spacing(ParagraphSpacing);
                                center.Item().AlignCenter().Text("ỦY NHIỆM THU").SemiBold().FontSize(13);
                                center.Item().AlignCenter().Text($"Số: {m.SampleAccount}").FontSize(9);
                                var (ngay, thang, nam) = GetDateParts(m.NgayInFile);
                                center.Item().AlignCenter()
                                    .Text($"Lập ngày {ngay} tháng {thang} năm {nam}")
                                    .FontSize(9)
                                    .FontColor(Colors.Red.Medium);
                            });

                            row.ConstantItem(140).AlignRight().Column(right =>
                            {
                                right.Item().AlignRight().Text("Mẫu số C4 - 01/KB").SemiBold().FontSize(9);
                                right.Item().AlignRight().Text("Số:................").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Spacing(ParagraphSpacing);
                                left.Item().Text(text =>
                                {
                                    text.Span("Đơn vị bán hàng: ").SemiBold();
                                    text.Span(SellerUnit);
                                });
                                left.Item().Text("Mã ĐVQHNS:......................");
                                left.Item().Text($"Số tài khoản: {SellerAccount}");
                                left.Item().Text($"Tại: {BankName}");
                                left.Item().Text("Hợp đồng (hay đơn đặt hàng) số:..................           Ngày ký:...............");
                                left.Item().Text($"Chứng từ kèm theo: 01 bảng thông báo cước tháng: {m.ChuKyNo}");

                                left.Item().PaddingTop(4).Text(text =>
                                {
                                    text.Span("Đơn vị mua hàng: ").SemiBold();
                                    text.Span(string.IsNullOrWhiteSpace(m.TenKhachHang) ? "............................" : m.TenKhachHang);
                                    text.Span("; Mã ĐVQHNS:.................");
                                });
                                left.Item().Text($"Số tài khoản: {(string.IsNullOrWhiteSpace(m.SoTaiKhoanKhachHang) ? "................." : m.SoTaiKhoanKhachHang)}");
                                left.Item().Text("Mã chương:..............           Mã ngành KT:..............           Mã NDKT:..............           Mã nguồn NS:..............");
                                left.Item().Text($"Tại kho bạc Nhà nước, Ngân hàng: {BankName}");
                            });

                            row.ConstantItem(170).Border(1).Padding(6).Column(box =>
                            {
                                box.Spacing(ParagraphSpacing);
                                box.Item().AlignCenter().Text("PHẦN KBNN GHI").SemiBold();
                                box.Item().Text("Mã quỹ:....................");
                                box.Item().Text("Nợ TK:....................");
                                box.Item().Text("Có TK:....................");
                            });
                        });

                        col.Item().PaddingTop(6).Column(body =>
                        {
                            body.Spacing(ParagraphSpacing);
                            body.Item().Text(text =>
                            {
                                text.Span("Số tiền chuyển: ").SemiBold();
                                text.Span($"Bằng số: {FormatCurrency(m.TongPT)} đ");
                            });
                            body.Item().Text($"Bằng chữ: {DocTienBangChu(m.TongPT)}.");
                            body.Item().Text("Số ngày chậm trả:............................");
                            body.Item().Text("Số tiền phạt chậm trả: Bằng số:.................................................");
                            body.Item().Text("Bằng chữ:........................................................................");
                            body.Item().PaddingTop(4).Text(text =>
                            {
                                text.Span("Tổng số tiền chuyển:").SemiBold();
                            });
                            body.Item().Text($"Bằng số: {FormatCurrency(m.TongPT)} đ");
                            body.Item().Text($"Bằng chữ: {DocTienBangChu(m.TongPT)}.");
                        });
                    });
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
