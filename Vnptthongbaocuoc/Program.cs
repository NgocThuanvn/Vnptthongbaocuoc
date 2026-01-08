using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vnptthongbaocuoc.Data;
using Vnptthongbaocuoc.Models;
using Vnptthongbaocuoc.Services;
using QuestPDF.Infrastructure;   // <-- thêm

var builder = WebApplication.CreateBuilder(args);

// 1) Kết nối DB
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2) Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Cho dev: không quá khắt khe, vẫn đảm bảo mạnh
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Cookie đường dẫn Login/AccessDenied
builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Account/Login";
    opt.AccessDeniedPath = "/Account/AccessDenied";
    opt.ExpireTimeSpan = TimeSpan.FromDays(7);
    opt.SlidingExpiration = true;
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<PdfExportServiceUNT>();
builder.Services.AddScoped<PdfExportServiceUntNhdt>();
builder.Services.AddScoped<PdfExportServiceUntNhnn>();
builder.Services.AddScoped<PdfExportServiceNnbx>();
builder.Services.AddScoped<ISmtpEmailSender, SmtpEmailSender>();

var app = builder.Build();
// Đặt license QuestPDF (Community)
QuestPDF.Settings.License = LicenseType.Community;   // <-- thêm
// 3) Seed Role + Admin user mặc định
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    var adminRole = "Admin";
    if (!await roleManager.RoleExistsAsync(adminRole))
    {
        await roleManager.CreateAsync(new IdentityRole(adminRole));
    }

    var adminEmail = "admin@sys.com";
    var adminPass = "Admin@123123";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Quản trị hệ thống"
        };
        var createRes = await userManager.CreateAsync(adminUser, adminPass);
        if (createRes.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, adminRole);
        }
        else
        {
            // Ghi log lỗi tạo user nếu cần
            Console.WriteLine("Seed admin failed: " + string.Join("; ", createRes.Errors.Select(e => $"{e.Code}:{e.Description}")));
        }
    }
    else
    {
        // Đảm bảo có role Admin
        if (!await userManager.IsInRoleAsync(adminUser, adminRole))
            await userManager.AddToRoleAsync(adminUser, adminRole);
    }
}

// Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();   // <— BẮT BUỘC: trước Authorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
