using System.Globalization;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Vnptthongbaocuoc.Services
{
    public class PdfExportServiceUntNhdt
    {
        private static readonly NumberFormatInfo CurrencyNumberFormat = new()
        {
            NumberGroupSeparator = ".",
            NumberDecimalSeparator = ","
        };

        private readonly IConfiguration _config;
        public PdfExportServiceUntNhdt(IConfiguration config)
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
            const float ParagraphSpacing = 6f;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(t => t.FontFamily("Times New Roman").FontSize(10).LineHeight(1));

                    page.Content().Column(col =>
                    {
                        col.Spacing(ParagraphSpacing);
                        col.Item().Row(row =>
                        {
                            row.ConstantItem(120).Border(1).Padding(6).AlignCenter().Text("Không ghi vào\nkhu vực này")
                                .FontSize(9);

                            row.RelativeItem().Column(header =>
                            {
                                header.Spacing(ParagraphSpacing);
                                header.Item().AlignCenter().Text("ỦY NHIỆM THU").SemiBold().FontSize(13);
                                header.Item().AlignCenter().Text($"Số: {m.File}").FontSize(9);
                                var (ngay, thang, nam) = GetDateParts(m.NgayInFile);
                                header.Item().AlignCenter().Text($"Lập ngày {ngay} tháng {thang} năm {nam}")
                                    .FontSize(9).FontColor(Colors.Red.Medium);
                            });

                            row.ConstantItem(150).AlignRight().Column(right =>
                            {
                                right.Spacing(ParagraphSpacing);
                                right.Item().AlignRight().Text("Mẫu số C4 - 01/KB").FontSize(9);
                                right.Item().AlignRight().Text("Số:..................").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Spacing(ParagraphSpacing);
                                left.Item().Text("Đơn vị bán hàng: VNPT CẦN THƠ").SemiBold();
                                left.Item().Text("Mã DVQHNS:...........................");
                                left.Item().Text($"Số tài khoản: {(string.IsNullOrWhiteSpace(m.SampleAccount) ? "..........................." : m.SampleAccount)}");
                                left.Item().Text("Tại: NGÂN HÀNG TMCP ĐẦU TƯ VÀ PHÁT TRIỂN VIỆT NAM - CNST")
                                    .FontSize(9);
                                left.Item().Text("Hợp đồng (hay đơn đặt hàng) số:...........................        Ngày kí:..............")
                                    .FontSize(9);
                                left.Item().Text($"Chứng từ kèm theo: 01 bảng thông báo cước tháng: {m.ChuKyNo}")
                                    .FontSize(9);
                            });

                            row.ConstantItem(165).Border(1).Padding(6).Column(box =>
                            {
                                box.Spacing(ParagraphSpacing);
                                box.Item().AlignCenter().Text("PHẦN KBNN GHI").SemiBold();
                                box.Item().PaddingTop(6).Text("Mã quỹ:......................").FontSize(9);
                                box.Item().Text("Nợ TK:........................").FontSize(9);
                                box.Item().Text("Có TK:........................").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(6).Column(info =>
                        {
                            info.Spacing(ParagraphSpacing);
                            info.Item().Text($"Đơn vị mua hàng: {m.TenKhachHang}; Mã ĐVQHNS: ");
                            info.Item().Text($"Số tài khoản: {(string.IsNullOrWhiteSpace(m.SoTaiKhoanKhachHang) ? "..........................." : m.SoTaiKhoanKhachHang)}");
                            info.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Mã chương:..............");
                                row.RelativeItem().Text("Mã ngành KT:..............");
                                row.RelativeItem().Text("Mã NDKT:..............");
                                row.RelativeItem().Text("Mã nguồn NS:..............");
                            });

                            info.Item().Text("Tại kho bạc Nhà nước, Ngân hàng: NGÂN HÀNG TMCP ĐẦU TƯ VÀ PHÁT TRIỂN VIỆT NAM - CNST")
                                .FontSize(9);

                         
                        });

                        col.Item().PaddingTop(8).Column(pay =>
                        {
                            pay.Spacing(ParagraphSpacing);
                            pay.Item().Text($"Số tiền chuyển:    Bằng số: {FormatCurrency(m.TongPT)} đ").SemiBold();
                            pay.Item().Text($"Bằng chữ: {DocTienBangChu(m.TongPT)}").FontSize(9);
                            pay.Item().Text("Số ngày chậm trả:.............................").FontSize(9);
                            pay.Item().Text("Số tiền phạt chậm trả: Bằng số:......................................................").FontSize(9);
                            pay.Item().Text("Bằng chữ:................................................................................").FontSize(9);
                        });

                        col.Item().PaddingTop(8).Column(total =>
                        {
                            total.Spacing(ParagraphSpacing);
                            total.Item().Text($"Tổng số tiền chuyển:").SemiBold();
                            total.Item().Text($"Bằng số: {FormatCurrency(m.TongPT)} đ").FontSize(9);
                            total.Item().Text($"Bằng chữ: {DocTienBangChu(m.TongPT)}").FontSize(9);
                        });

                        col.Item().PaddingTop(10).AlignCenter().Text("ĐƠN VỊ BÁN").SemiBold();
                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("Quản lý thu cước").SemiBold().FontSize(9);
                            row.RelativeItem().AlignCenter().Text("Nhân viên kinh tế").SemiBold().FontSize(9);
                            row.RelativeItem().AlignCenter().Text("Thủ trưởng đơn vị").SemiBold().FontSize(9);
                        }); col.Item().PaddingTop(26).Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("").FontSize(9);
                        });
                        col.Item().PaddingTop(26).Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("Trịnh Mỹ Hộ").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("Trịnh Mỹ Hộ").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("Trần Phước Huy").FontSize(9);
                        });

                      col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Cell().Row(1).Column(1).Border(1).Padding(4).Column(cell =>
                            {
                                cell.Spacing(4);
                                cell.Item().AlignCenter().Text("KHO BẠC NHÀ NƯỚC").SemiBold();
                                cell.Item().AlignCenter().Text("Nhận chứng từ ngày.......tháng.......năm");
                                cell.Item().PaddingTop(4).Row(r =>
                                {
                                    r.RelativeItem().AlignCenter().Text("Kế toán").SemiBold();
                                    r.RelativeItem().AlignCenter().Text("Kiểm Soát Viên").SemiBold();
                                });

                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                            });
                            table.Cell().Row(2).Column(1).Border(1).Padding(4).Column(cell =>
                            {
                                cell.Spacing(4);
                                cell.Item().AlignCenter().Text("Thanh toán ngày.......tháng.......năm");
                                cell.Item().PaddingTop(4).Row(r =>
                                {
                                    r.RelativeItem().AlignCenter().Text("Kế toán").SemiBold();
                                    r.RelativeItem().AlignCenter().Text("Kiểm Soát Viên").SemiBold();
                                    r.RelativeItem().AlignCenter().Text("Giám đốc").SemiBold();
                                });

                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                                cell.Item().AlignCenter().Text("");
                            });
                            table.Cell().Row(1).Column(2).RowSpan(2).Border(1).Padding(4).Column(cell =>
                            {
                                cell.Spacing(4);
                                cell.Item().AlignCenter().Text("NGÂN HÀNG BÊN BÁN").SemiBold();
                                cell.Item().AlignCenter().Text("Ghi sổ ngày.......tháng.......năm..........");
                                cell.Item().PaddingTop(4).Row(r =>
                                {
                                    r.RelativeItem().AlignCenter().Text("Kế toán").SemiBold();
                                    r.RelativeItem().AlignCenter().Text("Kiểm Soát Viên").SemiBold();
                                });
                            });
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
            return result + " đồng chẵn.";
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
