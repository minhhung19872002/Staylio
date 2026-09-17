using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/06 — Staylio Shield. Filing a case, answering one, and the admin side that
/// decides and pays. The wording throughout is "chính sách hỗ trợ": §11 forbids
/// insurance language anywhere a user can read it.
/// </summary>
[ApiController]
[Route("api/shield")]
public class ShieldController(
    StayHostDbContext db, AuthService auth, AdminAudit audit, ShieldService shield,
    CatalogService catalog) : ControllerBase
{
    /* ------------------------------------------------- AT-06-01: the terms */

    [HttpGet("terms")]
    public ActionResult<ShieldTermsDto> Terms([FromQuery] string? side)
    {
        var s = ShieldSettings.Current;
        var forHost = string.Equals(side, "host", StringComparison.OrdinalIgnoreCase);

        return Ok(forHost
            ? new ShieldTermsDto(
                "host",
                "Staylio Shield cho Chủ nhà",
                "Khi khách làm hỏng đồ hoặc khiến bạn phải huỷ đơn kế tiếp, Staylio đứng ra xử lý " +
                "và bù đắp trong hạn mức dưới đây. Đây là chính sách hỗ trợ của sàn, không thay thế " +
                "bảo vệ tài sản mà bạn tự thu xếp cho công trình và tài sản lớn.",
                [
                    new ShieldTermsSectionDto("Được hỗ trợ những gì", [
                        "Hư hỏng, mất mát nội thất, thiết bị, đồ dùng do khách hoặc người khách mời gây ra",
                        "Chi phí khắc phục: dọn sâu, giặt là đặc biệt, khử mùi thuốc lá, thay khoá khi mất chìa",
                        $"Mất thu nhập khi phải huỷ đơn kế tiếp để sửa chữa, tối đa {s.LostIncomeNights} đêm",
                        .. s.ThirdPartyBranch
                            ? new[]
                            {
                                "Thiệt hại khách gây ra cho hàng xóm hoặc tài sản chung của toà nhà — " +
                                "bạn mở hồ sơ, Staylio trả thẳng cho bên bị thiệt hại và bạn không phải tự chịu phần đầu"
                            }
                            : []
                    ]),
                    new ShieldTermsSectionDto("Hạn mức", [
                        $"Tối đa {Vnd.Format(s.HostClaimCeiling)} mỗi đơn",
                        $"Tối đa {Vnd.Format(s.HostYearlyCeiling)} mỗi chủ nhà mỗi năm",
                        $"Bạn tự chịu {Vnd.Format(s.HostDeductible)} đầu tiên của mỗi hồ sơ",
                        $"Đồ giá trị cao: tối đa {Vnd.Format(s.HighValueItemCeiling)} mỗi món, và phải khai báo " +
                        "trong tin đăng từ trước"
                    ]),
                    new ShieldTermsSectionDto("Thứ tự thu tiền", [
                        "Trừ vào tiền đặt cọc của khách, nếu bạn có thu cọc",
                        "Thu từ phương thức thanh toán của khách khi khách đồng ý hoặc Staylio phân xử buộc chịu",
                        "Phần còn thiếu mới chi từ quỹ Staylio Shield"
                    ]),
                    new ShieldTermsSectionDto("Thời hạn và bằng chứng", [
                        "Mở hồ sơ trong 14 ngày kể từ khi khách trả phòng, và trước khi khách tiếp theo nhận phòng",
                        "Ảnh hoặc video hiện trạng có mốc thời gian",
                        "Bằng chứng tình trạng trước đó: ảnh trong tin đăng hoặc ảnh sau lần dọn gần nhất",
                        "Chứng từ giá trị: hoá đơn mua, báo giá sửa chữa hoặc bảng giá thay thế",
                        "Đã nhắn tin cho khách trong Staylio trước khi mở hồ sơ"
                    ])
                ],
                [
                    "Hao mòn thông thường, hỏng do tuổi thọ thiết bị",
                    "Hư hỏng đã có từ trước chuyến này",
                    "Tiền mặt, giấy tờ tuỳ thân, đồ vật vô hình",
                    $"Đồ giá trị cao không khai báo trước (vượt {Vnd.Format(s.HighValueItemCeiling)})",
                    "Công trình, kết cấu nhà, mái, tường — thuộc phần bạn tự thu xếp",
                    "Xe cộ, tàu thuyền",
                    "Thiệt hại do chính bạn hoặc người của bạn gây ra",
                    "Đơn đặt hoặc thanh toán ngoài Staylio",
                    "Tin đăng đang bị đình chỉ hoặc bạn đang bị xem xét kỷ luật"
                ],
                Disclaimer)
            : new ShieldTermsDto(
                "guest",
                "Staylio Shield cho Khách",
                "Khi chỗ ở không có, không vào được hoặc khác xa mô tả, Staylio đứng ra tìm chỗ khác " +
                "hoặc hoàn tiền cho bạn. Đây là chính sách hỗ trợ của sàn, áp dụng cho đơn đặt và " +
                "thanh toán qua Staylio.",
                [
                    new ShieldTermsSectionDto("Bốn tình huống được hỗ trợ", [
                        "Chủ nhà huỷ đơn đã xác nhận trong vòng 30 ngày trước ngày nhận phòng",
                        "Bạn tới nơi nhưng không vào được và chủ nhà không xử lý",
                        "Chỗ ở thiếu hoặc sai lệch nghiêm trọng so với tin đăng",
                        "Chỗ ở không ở được: mất vệ sinh nặng, có sinh vật gây hại, hỏng điện nước, không an toàn"
                    ]),
                    new ShieldTermsSectionDto("Chúng tôi làm gì, theo thứ tự", [
                        "Tìm chỗ tương đương hoặc tốt hơn cùng khu vực — bạn trả đúng số tiền đã trả, " +
                        $"Staylio bù chênh lệch tới {s.RehousingTopUpRate * 100:0}% giá trị đơn",
                        "Bạn tự tìm chỗ khác và gửi hoá đơn, Staylio hoàn đơn gốc và bù chênh lệch trong hạn mức đó",
                        "Không tìm được chỗ nào phù hợp thì hoàn tiền: phần chưa ở, hoặc toàn bộ đơn kể cả " +
                        "phí dịch vụ nếu chủ nhà huỷ hoặc bạn không vào được"
                    ]),
                    new ShieldTermsSectionDto("Khoản thêm", [
                        $"Chủ nhà huỷ: tặng thêm {s.HostCancelCreditRate * 100:0}% giá trị đơn vào số dư cho lần sau",
                        $"Chi phí đi lại phát sinh và một đêm ở khẩn cấp, tối đa {Vnd.Format(s.ExpenseCeiling)} mỗi đơn, " +
                        "phải có hoá đơn"
                    ]),
                    new ShieldTermsSectionDto("Cần làm gì để được hỗ trợ", [
                        "Báo trong 72 giờ kể từ giờ nhận phòng ghi trên đơn",
                        "Nhắn cho chủ nhà trong Staylio trước, chờ 1 giờ (không vào được) hoặc 3 giờ (các trường hợp khác)",
                        "Không phải chờ nếu có nguy hiểm về an toàn hoặc chỗ ở đang có người lạ",
                        "Gửi kèm ảnh hoặc video"
                    ])
                ],
                [
                    "Bạn tự gây ra tình trạng đó",
                    "Vấn đề đã được nêu rõ trong tin đăng hoặc phần khai báo an toàn",
                    "Đã ở qua đêm rồi mới báo mà không có lý do chính đáng",
                    "Từ chối mọi phương án thay thế hợp lý mà không nêu lý do",
                    "Vi phạm nội quy và bị chủ nhà mời ra",
                    "Đơn đặt hoặc thanh toán ngoài Staylio",
                    "Bất khả kháng — xử lý theo chính sách huỷ riêng"
                ],
                Disclaimer));
    }

    /// <summary>
    /// docs/06 §11 — the programme is a platform policy the platform decides
    /// case by case, and it can be changed or ended. Saying so is the point.
    /// </summary>
    private const string Disclaimer =
        "Staylio Shield là chính sách hỗ trợ do Staylio tự nguyện áp dụng, không phải hợp đồng, " +
        "không thu phí riêng và nằm trong phí dịch vụ chung. Staylio xem xét từng trường hợp " +
        "và có quyền sửa đổi hoặc chấm dứt chương trình, có thông báo trước.";

    /* ---------------------------------------------------- filing and reading */

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShieldClaimDto>>> Mine(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await shield.MineAsync(user.Id, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShieldClaimDto>> One(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var claim = await shield.OneAsync(id, user, ct);
        return claim is null ? NotFound() : Ok(claim);
    }

    [HttpPost("bookings/{bookingId:int}")]
    public async Task<ActionResult<ShieldClaimDto>> Open(
        int bookingId, [FromBody] OpenShieldClaimRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var (claim, error) = await shield.FileAsync(user, bookingId, req, ct);
        if (claim is null) return BadRequest(new { message = error });

        return Ok(await shield.OneAsync(claim.Id, user, ct));
    }

    [HttpPost("{id:int}/respond")]
    public async Task<ActionResult<ShieldClaimDto>> Respond(
        int id, [FromBody] RespondShieldRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await shield.RespondAsync(user, id, req, ct);
        if (error is not null) return BadRequest(new { message = error });

        return Ok(await shield.OneAsync(id, user, ct));
    }

    [HttpPost("{id:int}/appeal")]
    public async Task<ActionResult<ShieldClaimDto>> Appeal(
        int id, [FromBody] RespondShieldRequest? req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await shield.AppealAsync(user, id, req?.Note, ct);
        if (error is not null) return BadRequest(new { message = error });

        return Ok(await shield.OneAsync(id, user, ct));
    }

    /* ------------------------------------------------------------- admin */

    [HttpGet("admin/queue")]
    public async Task<ActionResult<IReadOnlyList<ShieldClaimDto>>> Queue(
        [FromQuery] bool includeClosed = false, CancellationToken ct = default)
    {
        var admin = await audit.RequireAsync(AdminScope.Arbitration, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền phân xử." });

        return Ok(await shield.QueueAsync(includeClosed, ct));
    }

    [HttpGet("admin/fund")]
    public async Task<ActionResult<ShieldFundDto>> Fund(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Finance, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền xem tài chính." });

        return Ok(await shield.FundAsync(ct));
    }

    /// <summary>
    /// docs/06 AT-06-08 — what support offers before anybody talks about refunds.
    /// Level 1 of section 2.3 comes first for a reason: a guest with nowhere to sleep
    /// wants a bed tonight, not their money back next week.
    /// </summary>
    [HttpGet("admin/{id:int}/rehousing")]
    public async Task<ActionResult<RehousingDto>> Rehousing(int id, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Support, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền hỗ trợ." });

        var options = await shield.RehousingAsync(id, catalog, HttpContext.SessionId(), ct);
        return options is null ? NotFound() : Ok(options);
    }

    [HttpPost("admin/{id:int}/decide")]
    public async Task<ActionResult<ShieldClaimDto>> Decide(
        int id, [FromBody] DecideShieldRequest req, [FromServices] AdminGate gate, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Arbitration, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền phân xử." });

        var before = await shield.OneAsync(id, admin, ct);
        if (before is null) return NotFound();

        var parties = await db.ShieldClaims
            .Where(c => c.Id == id)
            .Select(c => new { Guest = c.Booking!.GuestUserId, Host = (int?)c.Booking.Listing!.Host!.UserId })
            .FirstAsync(ct);
        if (await gate.PartyConflictAsync(admin, parties.Guest, parties.Host, ct) is { } refusal)
            return StatusCode(403, new { message = refusal });

        var error = await shield.DecideAsync(admin, id, req, ct);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(admin, "shield.decide", $"shield:{id}",
            before.Status, req.Approve ? "Settled" : "Rejected", req.Reason);
        await db.SaveChangesAsync(ct);

        return Ok(await shield.OneAsync(id, admin, ct));
    }

    /// <summary>docs/06 §5 — money chased down after the fund paid goes back to it.</summary>
    [HttpPost("admin/{id:int}/recover")]
    public async Task<IActionResult> Recover(
        int id, [FromBody] RecoverShieldRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Finance, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền xem tài chính." });

        var error = await shield.RecoverAsync(id, req.Amount, ct);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(admin, "shield.recover", $"shield:{id}", null, $"{Vnd.Format(req.Amount)}");
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
