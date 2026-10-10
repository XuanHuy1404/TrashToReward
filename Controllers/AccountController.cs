using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TrashToRewardsV2.Models;
using TrashToRewardsV2.Services; // Dùng EmailService
using Microsoft.EntityFrameworkCore;

namespace TrashToRewardsV2.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public AccountController(ApplicationDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // ===== Helper =====
        private async Task<bool> EmailExistsAsync(string email, string currentEntityType = null, int? excludeId = null)
        {
            email = email?.Trim().ToLower();
            var checkND = await _context.NGUOIDUNG.AnyAsync(u => u.Email.ToLower() == email && !(currentEntityType == "NGUOIDUNG" && u.MaND == excludeId));
            var checkNV = await _context.NHANVIEN.AnyAsync(u => u.EmailNV.ToLower() == email && !(currentEntityType == "NHANVIEN" && u.MaNV == excludeId));
            var checkNVGQ = await _context.NHANVIENGQ.AnyAsync(u => u.EmailNVGQ.ToLower() == email && !(currentEntityType == "NHANVIENGQ" && u.MaNVGQ == excludeId));
            var checkAD = await _context.ADMIN.AnyAsync(u => u.EmailAD.ToLower() == email && !(currentEntityType == "ADMIN" && u.MaAD == excludeId));
            return checkND || checkNV || checkNVGQ || checkAD;
        }

        private string GenerateOTP()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        // ====== LOGIN ======
        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            // CHỈNH SỬA: Logic cho Admin
            var admin = await _context.ADMIN.FirstOrDefaultAsync(x => x.EmailAD == email && x.MatKhauAD == password);
            if (admin != null)
            {
                // Thay vì đăng nhập ngay, tạo và gửi OTP
                var otp = GenerateOTP();
                HttpContext.Session.SetString("AdminLogin_UserId", admin.MaAD.ToString());
                HttpContext.Session.SetString("AdminLogin_Otp", otp);
                HttpContext.Session.SetString("AdminLogin_OtpExpiry", DateTime.UtcNow.AddMinutes(5).ToString("o"));

                var body = $"<h3>Mã OTP đăng nhập tài khoản Admin: <b>{otp}</b></h3><p>OTP có hiệu lực trong 5 phút.</p>";
                await _emailService.SendEmailAsync(admin.EmailAD, "TrashToRewards - OTP Đăng nhập Admin", body);

                // Chuyển hướng đến trang nhập OTP
                return RedirectToAction("VerifyAdminOTP");
            }

            var staff = await _context.NHANVIEN.FirstOrDefaultAsync(x => x.EmailNV == email && x.MatKhauNV == password);
            if (staff != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, staff.MaNV.ToString()),
                    new Claim(ClaimTypes.Name, staff.HoTenNV),
                    new Claim(ClaimTypes.Role, "NHANVIEN"),
                    new Claim("MaNV", staff.MaNV.ToString())
                };
                await SignInUser(claims);
                return RedirectToAction("Profile", "NhanVien");
            }

            var staffGQ = await _context.NHANVIENGQ.FirstOrDefaultAsync(x => x.EmailNVGQ == email && x.MatKhauNVGQ == password);
            if (staffGQ != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, staffGQ.MaNVGQ.ToString()),
                    new Claim(ClaimTypes.Name, staffGQ.HoTenNVGQ),
                    new Claim(ClaimTypes.Role, "NHANVIENGQ"),
                    new Claim("MaNVGQ", staffGQ.MaNVGQ.ToString())
                };
                await SignInUser(claims);
                return RedirectToAction("Profile", "NhanVienGQ");
            }

            var user = await _context.NGUOIDUNG.FirstOrDefaultAsync(x => x.Email == email && x.MatKhau == password);
            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.MaND.ToString()),
                    new Claim(ClaimTypes.Name, user.HoTen),
                    new Claim(ClaimTypes.Role, "NGUOIDUNG"),
                    new Claim("MaND", user.MaND.ToString())
                };
                await SignInUser(claims);
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Sai tài khoản hoặc mật khẩu!";
            return View();
        }

        // THÊM MỚI: Các Action để xác thực OTP cho Admin
        [HttpGet]
        public IActionResult VerifyAdminOTP()
        {
            // Kiểm tra xem có phiên làm việc OTP không
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("AdminLogin_UserId")))
            {
                return RedirectToAction("Login");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> VerifyAdminOTP(string otpInput)
        {
            var userId = HttpContext.Session.GetString("AdminLogin_UserId");
            var storedOtp = HttpContext.Session.GetString("AdminLogin_Otp");
            var expiry = HttpContext.Session.GetString("AdminLogin_OtpExpiry");

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(storedOtp) || string.IsNullOrEmpty(expiry) || DateTime.UtcNow > DateTime.Parse(expiry) || otpInput != storedOtp)
            {
                ViewBag.Error = "OTP không hợp lệ hoặc đã hết hạn.";
                return View();
            }

            // OTP hợp lệ, tiến hành đăng nhập
            var admin = await _context.ADMIN.FindAsync(int.Parse(userId));
            if (admin == null)
            {
                ViewBag.Error = "Lỗi: Không tìm thấy tài khoản admin.";
                return View();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "ADMIN_" + admin.MaAD),
                new Claim(ClaimTypes.Name, admin.HoTenAD),
                new Claim(ClaimTypes.Role, "ADMIN")
            };
            await SignInUser(claims);

            // Xóa session sau khi hoàn tất
            HttpContext.Session.Remove("AdminLogin_UserId");
            HttpContext.Session.Remove("AdminLogin_Otp");
            HttpContext.Session.Remove("AdminLogin_OtpExpiry");

            return RedirectToAction("Dashboard", "Admin");
        }


        private async Task SignInUser(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // ====== REGISTER + OTP ======
        // Giữ nguyên không đổi
        [HttpGet]
        public IActionResult Register() => View();

        [HttpPost]
        public async Task<IActionResult> Register(NGUOIDUNG model)
        {
            if (!ModelState.IsValid)
                return View(model);

            if (await EmailExistsAsync(model.Email))
            {
                ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống.");
                return View(model);
            }

            var otp = GenerateOTP();
            HttpContext.Session.SetString("RegisterEmail", model.Email);
            HttpContext.Session.SetString("RegisterHoTen", model.HoTen);
            HttpContext.Session.SetString("RegisterSDT", model.SDT);
            HttpContext.Session.SetString("RegisterDiaChi", model.DiaChi ?? "");
            HttpContext.Session.SetString("RegisterMatKhau", model.MatKhau);
            HttpContext.Session.SetString("RegisterOtp", otp);
            HttpContext.Session.SetString("RegisterOtpExpiry", DateTime.UtcNow.AddMinutes(5).ToString("o"));

            var body = $"<h3>Mã OTP đăng ký của bạn: <b>{otp}</b></h3><p>OTP hết hạn sau 5 phút.</p>";
            await _emailService.SendEmailAsync(model.Email, "TrashToRewards - OTP xác nhận đăng ký", body);

            return RedirectToAction("VerifyRegisterOTP");
        }

        [HttpGet]
        public IActionResult VerifyRegisterOTP() => View();

        [HttpPost]
        public async Task<IActionResult> VerifyRegisterOTP(string otpInput)
        {
            var storedOtp = HttpContext.Session.GetString("RegisterOtp");
            var expiry = HttpContext.Session.GetString("RegisterOtpExpiry");

            if (string.IsNullOrEmpty(storedOtp) || string.IsNullOrEmpty(expiry) || DateTime.UtcNow > DateTime.Parse(expiry) || otpInput != storedOtp)
            {
                ViewBag.Error = "OTP không hợp lệ hoặc đã hết hạn.";
                return View();
            }

            var user = new NGUOIDUNG
            {
                Email = HttpContext.Session.GetString("RegisterEmail"),
                HoTen = HttpContext.Session.GetString("RegisterHoTen"),
                SDT = HttpContext.Session.GetString("RegisterSDT"),
                DiaChi = HttpContext.Session.GetString("RegisterDiaChi"),
                MatKhau = HttpContext.Session.GetString("RegisterMatKhau"),
                DiemTichLuy = 0
            };

            _context.NGUOIDUNG.Add(user);
            await _context.SaveChangesAsync();

            // Clear session
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ====== FORGOT PASSWORD + OTP ======
        // Giữ nguyên không đổi
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var user = await _context.NGUOIDUNG.FirstOrDefaultAsync(x => x.Email == email);
            if (user == null)
            {
                ViewBag.Error = "Email không tồn tại!";
                return View();
            }

            var otp = GenerateOTP();
            HttpContext.Session.SetString("ResetEmail", email);
            HttpContext.Session.SetString("ResetOtp", otp);
            HttpContext.Session.SetString("ResetOtpExpiry", DateTime.UtcNow.AddMinutes(5).ToString("o"));

            var body = $"<h3>Mã OTP khôi phục mật khẩu: <b>{otp}</b></h3><p>OTP có hiệu lực trong 5 phút.</p>";
            await _emailService.SendEmailAsync(email, "TrashToRewards - OTP khôi phục mật khẩu", body);

            return RedirectToAction("VerifyOTP");
        }

        [HttpGet]
        public IActionResult VerifyOTP() => View();

        [HttpPost]
        public IActionResult VerifyOTP(string otpInput)
        {
            var storedOtp = HttpContext.Session.GetString("ResetOtp");
            var expiry = HttpContext.Session.GetString("ResetOtpExpiry");

            if (string.IsNullOrEmpty(storedOtp) || string.IsNullOrEmpty(expiry) || DateTime.UtcNow > DateTime.Parse(expiry) || otpInput != storedOtp)
            {
                ViewBag.Error = "OTP không hợp lệ hoặc đã hết hạn.";
                return View();
            }

            return RedirectToAction("ResetPassword");
        }

        [HttpGet]
        public IActionResult ResetPassword()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("ResetEmail")))
                return RedirectToAction("Login");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp.";
                return View();
            }

            var email = HttpContext.Session.GetString("ResetEmail");
            var user = await _context.NGUOIDUNG.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return RedirectToAction("Login");

            user.MatKhau = newPassword;
            await _context.SaveChangesAsync();

            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // =====
        public IActionResult AccessDenied() => View();
    }
}