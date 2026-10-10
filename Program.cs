using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TrashToRewardsV2.Models;
using TrashToRewardsV2.Services;

var builder = WebApplication.CreateBuilder(args);

// Thêm localization
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.AddScoped<IThongBaoService, ThongBaoService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
    });

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthorization();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddTransient<IEmailService, EmailService>();

var app = builder.Build();

// Cấu hình ngôn ngữ hỗ trợ
var supportedCultures = new[]
{
    new CultureInfo("en"),
    new CultureInfo("vi"),
    new CultureInfo("ja")
};

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("vi"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};

app.UseRequestLocalization(localizationOptions);

// Middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication(); // QUAN TRỌNG: Bổ sung middleware xác thực
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();

    if (!context.ADMIN.Any())
    {
        context.ADMIN.Add(new ADMIN
        {
            HoTenAD = "Admin",
            SDTAD = "0123456789",
            EmailAD = "admin@ttr.com",
            MatKhauAD = "admin123" // Gợi ý: nên mã hóa mật khẩu sau
        });
        context.SaveChanges();
    }
}

app.Run();
