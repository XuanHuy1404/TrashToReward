using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TrashToRewardsV2.Models;
using TrashToRewardsV2.Services;
using System.Security.Claims;

namespace TrashToRewardsV2.Controllers
{
    
    public class PhieuDoiQuaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IThongBaoService _thongBaoService;

        public PhieuDoiQuaController(ApplicationDbContext context, IThongBaoService thongBaoService)
        {
            _context = context;
            _thongBaoService = thongBaoService;
        }
        private async Task UpdateSoLuongConPhanThuong(int maQua)
        {
            var tong = await _context.CTKHOQUA
                .Where(c => c.MaQua == maQua)
                .SumAsync(c => (int?)c.SoLuongTrongKho) ?? 0;

            var phanThuong = await _context.PHANTHUONG.FindAsync(maQua);
            if (phanThuong != null)
            {
                phanThuong.SoLuongCon = tong;
                await _context.SaveChangesAsync();
            }
        }

        // Hiển thị form chọn Kho
        [Authorize(Roles = "NGUOIDUNG")]
        public async Task<IActionResult> Create()
        {
            ViewBag.KhoList = new SelectList(await _context.KHOQUA.ToListAsync(), "MaKQ", "TenKQ");
            return View();
        }

        // Khi chọn Kho xong, Load Quà trong Kho
        [Authorize(Roles = "NGUOIDUNG")]
        [HttpGet]
        public async Task<IActionResult> LoadQuas(int maKQ)
        {
            Console.WriteLine($"[DEBUG] LoadQuas bắt đầu. MaKQ nhận được: {maKQ}");

            // In ra toàn bộ danh sách kho hiện có để kiểm tra
            var allKho = await _context.KHOQUA.AsNoTracking().ToListAsync();
            foreach (var k in allKho)
            {
                Console.WriteLine($"[DEBUG] Kho hiện có: MaKQ={k.MaKQ}, TenKQ={k.TenKQ}");
            }

            var kho = await _context.KHOQUA
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.MaKQ == maKQ);

            if (kho == null)
            {
                Console.WriteLine($"[DEBUG] Không tìm thấy kho MaKQ = {maKQ}");
                return Json(new
                {
                    Kho = (object)null,
                    Quas = new List<object>()
                });
            }

            Console.WriteLine($"[DEBUG] Tìm thấy kho: {kho.TenKQ}");

            var quaList = await _context.CTKHOQUA
                .Include(c => c.PHANTHUONG)
                .Where(c => c.MaKQ == maKQ && c.SoLuongTrongKho > 0)
                .Select(c => new {
                    c.MaQua,
                    TenQua = c.PHANTHUONG.TenQua,
                    HinhAnh = c.PHANTHUONG.HinhAnh,
                    DiemDoi = c.PHANTHUONG.DiemDoi,
                    SoLuongTrongKho = c.SoLuongTrongKho
                })
                .ToListAsync();
            Console.WriteLine($"[DEBUG] Số lượng phần thưởng tìm thấy: {quaList.Count}");
            foreach (var q in quaList)
            {
                Console.WriteLine($"[DEBUG] Quà: {q.TenQua}, Điểm đổi: {q.DiemDoi}, SL kho: {q.SoLuongTrongKho}");
            }

            return Json(new
            {
                Kho = new { TenKQ = kho.TenKQ, DiaChiKQ = kho.DiaChiKQ },
                Quas = quaList

            });
        }




        [Authorize(Roles = "NGUOIDUNG")]
        // Submit Phiếu
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PHIEUDOIQUA model, List<int> maQuas, List<int> soLuongs)
        {
            if (!ModelState.IsValid)
            {
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"[DEBUG] ModelState Error - Key: {key}, Error: {error.ErrorMessage}");
                    }
                }
            }

            var maNDStr = User.FindFirstValue("MaND");
            if (string.IsNullOrEmpty(maNDStr) || !int.TryParse(maNDStr, out int maND))
                return Unauthorized();
            if (model.HinhThucNhanQua == "TaiNha" && string.IsNullOrWhiteSpace(model.DiaDiemNhanQua))
            {
                ModelState.AddModelError("DiaDiemNhanQua", "Vui lòng nhập địa điểm nhận quà khi chọn Tại nhà.");
                ViewBag.KhoList = new SelectList(await _context.KHOQUA.ToListAsync(), "MaKQ", "TenKQ");
                return View(model);
            }
            if (maQuas == null || soLuongs == null || maQuas.Count != soLuongs.Count)
            {
                

                ModelState.AddModelError("", "Vui lòng chọn phần thưởng hợp lệ.");
                ViewBag.KhoList = new SelectList(await _context.KHOQUA.ToListAsync(), "MaKQ", "TenKQ");
                return View(model);
            }

            var kho = await _context.KHOQUA.FindAsync(model.MaKQ);
            if (kho == null) return NotFound();

            var user = await _context.NGUOIDUNG.FindAsync(maND);
            if (user == null) return NotFound();

            // Tính tổng điểm cần sử dụng
            

            // Thay cho vòng for (i = 0; i < maQuas.Count; i++)
            var selectedItems = maQuas.Zip(soLuongs, (maQua, soLuong) => new { maQua, soLuong })
                                      .Where(x => x.soLuong > 0)
                                      .ToList();

            if (selectedItems.Count == 0)
            {
                ModelState.AddModelError("", "Vui lòng chọn ít nhất một phần thưởng.");
                ViewBag.KhoList = new SelectList(await _context.KHOQUA.ToListAsync(), "MaKQ", "TenKQ");
                return View(model);
            }

            float tongDiem = 0;
            var ctpdqList = new List<CTPHIEUDOIQUA>();

            foreach (var item in selectedItems)
            {
                var ctkho = await _context.CTKHOQUA
                    .Include(c => c.PHANTHUONG)
                    .FirstOrDefaultAsync(c => c.MaKQ == model.MaKQ && c.MaQua == item.maQua);

                if (ctkho == null || item.soLuong <= 0 || ctkho.SoLuongTrongKho < item.soLuong)
                {
                    ModelState.AddModelError("", "Số lượng vượt quá kho.");
                    ViewBag.KhoList = new SelectList(await _context.KHOQUA.ToListAsync(), "MaKQ", "TenKQ");
                    return View(model);
                }

                var diemCan = ctkho.PHANTHUONG.DiemDoi * item.soLuong;
                tongDiem += diemCan;

                ctpdqList.Add(new CTPHIEUDOIQUA
                {
                    MaQua = ctkho.MaQua,
                    SoLuongDoi = item.soLuong,
                    SoDiemCan = diemCan
                });
            }


            if (user.DiemTichLuy < tongDiem)
            {
                ModelState.AddModelError("", "Bạn không đủ điểm tích lũy để đổi quà.");
                ViewBag.KhoList = new SelectList(await _context.KHOQUA.ToListAsync(), "MaKQ", "TenKQ");
                return View(model);
            }

            // Bắt đầu tạo phiếu
            model.NgayGioDoi = DateTime.Now;
            model.MaND = maND;
            model.SoDiemSuDung = tongDiem;

            if (model.HinhThucNhanQua == "TaiNha")
            {
                model.TrangThai = "ChuaXacNhan";
            }
            else if (model.HinhThucNhanQua == "TrucTiep")
            {
                model.TrangThai = "ChuaNhan";
                var selectedKho = await _context.KHOQUA.FindAsync(model.MaKQ);
                model.DiaDiemNhanQua = selectedKho.TenKQ + " - " + selectedKho.DiaChiKQ;
            }

            _context.PHIEUDOIQUA.Add(model);
            await _context.SaveChangesAsync();

            foreach (var ct in ctpdqList)
            {
                ct.MaPDQ = model.MaPDQ;
                _context.CTPHIEUDOIQUA.Add(ct);

                // Trừ số lượng trong kho
                var ctkho = await _context.CTKHOQUA.FirstOrDefaultAsync(c => c.MaKQ == model.MaKQ && c.MaQua == ct.MaQua);
                ctkho.SoLuongTrongKho -= ct.SoLuongDoi;
            }
            foreach (var ct in ctpdqList)
            {
                ct.MaPDQ = model.MaPDQ;
                _context.CTPHIEUDOIQUA.Add(ct);

                var ctkho = await _context.CTKHOQUA.FirstOrDefaultAsync(c => c.MaKQ == model.MaKQ && c.MaQua == ct.MaQua);
                ctkho.SoLuongTrongKho -= ct.SoLuongDoi;

                // 👇 Cập nhật lại số lượng còn của phần thưởng
                await UpdateSoLuongConPhanThuong(ct.MaQua);
            }

            // Trừ điểm người dùng
            user.DiemTichLuy -= tongDiem;
            _context.LICHSUTICHDIEM.Add(new LICHSUTICHDIEM
            {
                NgayGioCapNhat = DateTime.Now,
                SoDiemThayDoi = -tongDiem,
                LyDo = $"Đổi quà - Phiếu #{model.MaPDQ}",
                MaND = user.MaND
            });


            await _context.SaveChangesAsync();

            // Gửi Thông báo cho NHANVIENGQ của Kho này
            var nhanVienGQs = await _context.NHANVIENGQ.Where(nv => nv.MaKQ == model.MaKQ).ToListAsync();
            foreach (var nv in nhanVienGQs)
            {
                await _thongBaoService.ThongBaoNVGQAsync(nv.MaNVGQ, $"Có phiếu đổi quà mới #{model.MaPDQ} từ người dùng {user.HoTen}");
            }

            return RedirectToAction("Index", "LichSuTichDiem");
        }
        [Authorize(Roles = "NGUOIDUNG")]
        public async Task<IActionResult> Details(int id)
        {
            var phieu = await _context.PHIEUDOIQUA
                .Include(p => p.NGUOIDUNG)
                .Include(p => p.CTPHIEUDOIQUAs)
                .ThenInclude(ct => ct.PHANTHUONG)
                .FirstOrDefaultAsync(p => p.MaPDQ == id);

            if (phieu == null) return NotFound();

            return View(phieu);
        }
        [Authorize(Roles = "NGUOIDUNG")]
        [HttpPost]
        public async Task<IActionResult> Huy(int id)
        {
            var phieu = await _context.PHIEUDOIQUA
                .Include(p => p.CTPHIEUDOIQUAs)
                .FirstOrDefaultAsync(p => p.MaPDQ == id);

            if (phieu == null) return NotFound();
            if (!(phieu.TrangThai == "ChuaXacNhan" || phieu.TrangThai == "ChuaNhan"))
            {
                return BadRequest("Không thể hủy phiếu đã xác nhận hoặc đã giao.");
            }

            var nguoiDung = await _context.NGUOIDUNG.FindAsync(phieu.MaND);

            // Hoàn điểm
            if (nguoiDung != null) {
                nguoiDung.DiemTichLuy += phieu.SoDiemSuDung;
                _context.LICHSUTICHDIEM.Add(new LICHSUTICHDIEM
                {
                    NgayGioCapNhat = DateTime.Now,
                    SoDiemThayDoi = phieu.SoDiemSuDung,
                    LyDo = $"Hoàn điểm do hủy phiếu đổi quà #{phieu.MaPDQ}",
                    MaND = nguoiDung.MaND
                });
            }
                



            // Hoàn lại số lượng quà trong kho
            foreach (var ct in phieu.CTPHIEUDOIQUAs)
            {
                var ctkho = await _context.CTKHOQUA.FirstOrDefaultAsync(c => c.MaKQ == phieu.MaKQ && c.MaQua == ct.MaQua);
                if (ctkho != null)
                {
                    ctkho.SoLuongTrongKho += ct.SoLuongDoi;
                }
            }
            foreach (var ct in phieu.CTPHIEUDOIQUAs)
            {
                var ctkho = await _context.CTKHOQUA.FirstOrDefaultAsync(c => c.MaKQ == phieu.MaKQ && c.MaQua == ct.MaQua);
                if (ctkho != null)
                {
                    ctkho.SoLuongTrongKho += ct.SoLuongDoi;

                    //  Cập nhật lại số lượng còn của phần thưởng
                    await UpdateSoLuongConPhanThuong(ct.MaQua);
                }
            }

            // Đổi trạng thái
            phieu.TrangThai = "DaHuy";

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id });
        }
        [Authorize(Roles = "NGUOIDUNG")]
        public async Task<IActionResult> Index()
        {
            var maNDStr = User.FindFirstValue("MaND");
            if (string.IsNullOrEmpty(maNDStr) || !int.TryParse(maNDStr, out int maND))
                return Unauthorized();

            var danhSachPhieu = await _context.PHIEUDOIQUA
                .Where(p => p.MaND == maND)
                .OrderByDescending(p => p.NgayGioDoi)
                .ToListAsync();

            return View(danhSachPhieu);
        }

    }
}

