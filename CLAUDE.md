# CLAUDE.md — Staylio

Đọc file này trước khi làm bất cứ việc gì trong repo.

---

## 1. Tài liệu là nguồn sự thật

`docs/00` → `docs/05` là **đặc tả nghiệp vụ do khách hàng đưa**. Khi code khác tài liệu
thì **code sai**, không phải tài liệu sai.

| File | Nội dung |
|---|---|
| `docs/00-TONG-QUAN.md` | Mô hình kinh doanh, các bên tham gia, 8 nguyên tắc xuyên suốt |
| `docs/01-DANH-MUC-CHUC-NANG.md` | ~200 yêu cầu có mã `FR` + ưu tiên P0/P1/P2 |
| `docs/02-MAN-HINH.md` | Từng màn hình có gì, làm được gì |
| `docs/03-QUY-TAC-NGHIEP-VU.md` | **Quan trọng nhất** — giá, huỷ/hoàn tiền, vòng đời đơn, xếp hạng |
| `docs/04-QUY-TRINH.md` | 14 quy trình đầu-cuối + 10 tình huống nghiệm thu |
| `docs/05-THUC-THE.md` | Sàn cần biết thông tin gì về từng đối tượng |
| `docs/06-STAYSHIELD.md` | Chương trình bảo vệ hai đầu. **§10 là bảng tham số đã chốt** |
| `docs/07-THANH-TOAN.md` | Thanh toán đầu-cuối. §15 là danh sách chức năng, §18 là 11 kịch bản |
| `docs/08-QUAN-TRI-NGUOI-DUNG.md` | Quyền admin, thang xử lý tài khoản, khiếu nại, chống lạm quyền |
| `docs/09-TRAI-NGHIEM-DICH-VU.md` | Trải nghiệm & Dịch vụ — **quy tắc khác hẳn chỗ ở**. §7 là bảng tham số đã chốt |
| `docs/PLAN.md` | **Đối chiếu hiện trạng ↔ spec.** Bắt đầu phiên mới thì đọc file này trước |

---

## 2. Những việc khách đã quyết

1. **Tên hiển thị là "Staylio"** (khách chốt 26/08/2026, cùng ngày đổi tên miền sang
   `staylio.vn`). Trước đó là "StayHost OS" — khách từng chốt giữ tên ấy và không đổi
   sang StayHub, nay đổi hẳn. Chương trình bảo vệ gọi là **"Staylio Shield"**.
   Danh hiệu "Siêu chủ nhà" / "Khách yêu thích" giữ nguyên.
   **Chỉ chữ hiển thị đổi.** Namespace `StayHost.Domain`/`StayHost.Web`, `StayHostDbContext`,
   tên project, tên container `stayhost-db`/`stayhost-web`, tên file migration
   `..._StayShield.cs` và class `StayShieldTests` **giữ nguyên** — đổi tên một migration
   là đổi một dòng mà cơ sở dữ liệu đang chạy đã ghi. Đổi lại thì sửa chữ, đừng sửa mã.
2. **Phí dịch vụ 14% khách / 3% chủ nhà** theo `docs/03 §1`, để trong cấu hình
   `Pricing:` chứ không rải hằng số khắp nơi.
3. **Bồi thường hư hỏng không đi qua sàn** (17/08/2026). Chủ nhà phải báo cho khách
   **lúc khách trả phòng**, khách đưa **tiền mặt** tại chỗ. Sàn ra phán quyết và ghi
   nhận, **không thu tiền của khách và không chuyển tiền cho chủ nhà**. Cửa sổ mở hồ
   sơ hư hỏng vì thế rút từ 14 ngày xuống **24 giờ**.
   **Khách không chịu trả, hoặc phát hiện sau 24 giờ → chủ nhà chịu.** Quỹ Staylio Shield
   **không chi** cho C1/C2, nên hạn mức `C-A`/`C-B` và mức tự chịu `C-C` cũng không áp
   cho hai nhóm đó (`Shield.FundCovers`). Quỹ vẫn đứng sau C3 và C4.
   Xem `docs/06 §3.3`, `§3.4`.
4. **Đặt không cần tài khoản, và trả tiền tại nơi ở** (khách chốt 28/08/2026).
   Đảo lại `docs/07 §2.4`, chỗ từng từ chối "trả khi nhận phòng" trong cùng một
   dòng với tiền mặt. Quyết định và toàn bộ hệ quả nằm ở **`docs/07 §2.5`**.
   Điểm phải nhớ: **tiền không đi qua sàn nên không có bút toán nào** — cùng
   hình dạng với quyết định bồi thường hư hỏng ở mục 3. Phí dịch vụ ghi vào
   `HostProfile.OwedToPlatform`, trừ vào lần chuyển tiền kế tiếp, dùng chung cơ
   chế với chargeback thua. Chủ nhà tự bật theo từng tin đăng; sàn không bao giờ
   tự bật.
5. **Co-host chia % thu nhập, làm giống Airbnb** (khách chốt 03/09/2026).
   `docs/02 G8` có bốn chữ "tuỳ chọn chia % thu nhập" mà `docs/01 QL-19` không
   nhắc; khách chốt theo đúng chính sách công khai của Airbnb. Quy tắc đầy đủ ở
   **`docs/07 §19`**. Điểm phải nhớ: **chia trên thu nhập thực nhận sau phí 3%**
   nên `Pricing.cs` không đổi; **không bao giờ chia quá số đơn đó kiếm được**;
   co-host được trả **như một chủ nhà** (hồ sơ nhận tiền, tài khoản, sổ nợ, lệnh
   chuyển riêng); hoàn tiền sau khi đã chia thì thu lại qua `OwedToPlatform`,
   cùng cơ chế với chargeback và với `§2.5`.
6. **14 tham số Staylio Shield** của `docs/06 §10` đã chốt: bù đổi chỗ 40%, tặng số dư
   10%, trần chi phí phát sinh 3 triệu; chủ nhà 75 triệu/đơn, 350 triệu/năm, tự chịu
   500k, 5 đêm mất thu nhập, 15 triệu mỗi món giá trị cao; quỹ trích 5% phí dịch vụ,
   cảnh báo ở 80%, gắn cờ từ hồ sơ thứ 4. **Có trực 24/7**, **có làm nhánh C4**.
   Tất cả nằm trong `ShieldSettings`, một nơi duy nhất.

---

## 3. Hiện trạng

**Toàn bộ xanh (16/09/2026).** 1223 test nghiệp vụ + **7 test cổng thanh toán**
(`tests/StayHost.Web.Tests`) · **30/30** kịch bản cổng thanh
toán thật (`scripts/gateway_acceptance.py`, gọi sandbox VNPay/MoMo/ZaloPay ngoài
đời) · **34/34** kịch bản chuyển tiền cho chủ nhà và đối chiếu sao kê (`scripts/payout_acceptance.py`) ·
**14/14** một giao dịch VNPay trả xong trên chính trang của họ, qua trình duyệt thật
(`scripts/vnpay_browser_acceptance.py`) · **11/11** hoàn tiền thật qua VNPay
(`scripts/refund_acceptance.py`) ·
**11/11** kịch bản của `docs/04` — 10 của tài liệu, cộng `1b` đối chiếu giá khi
đoàn khách có thú cưng
(`scripts/acceptance.py`) · **10/10** kịch bản quản trị của `docs/08 §13`
(`scripts/admin_acceptance.py`) · **19/19** kịch bản của `docs/09`
(`scripts/doc09_acceptance.py`, gồm cả 12 tình huống bắt buộc của `docs/09 §9`) ·
**10/10** kịch bản của `scripts/unwired_acceptance.py` (`docs/PLAN.md §9.6` — các
quy tắc từng có mã mà không đường nào gọi tới) ·
**14/14** kịch bản của `scripts/rolegaps_acceptance.py` (`docs/PLAN.md §9.8` và `§9.9`
— soát theo **vai** khách/chủ nhà rồi soát ngược theo `docs/02`; 16 chỗ hở là mã
hoàn chỉnh mà không màn hình nào gọi).
**12/12** kịch bản của `scripts/guestcheckout_acceptance.py` (`docs/07 §2.5` — đặt
không cần tài khoản và trả tiền tại nơi ở) ·
**31/31** kịch bản của `scripts/cohost_share_acceptance.py` (`docs/07 §19` — chia
thu nhập cho người đồng quản lý) ·
**7/7** kịch bản `TK-09` của `scripts/preferences_acceptance.py` (tuỳ chọn trên tài khoản) ·
**7/7** kịch bản `TK-09` của `scripts/email_language_acceptance.py` (email theo ngôn ngữ người đọc) ·
**7/7** kịch bản `QT-06`/`TC-12` của `scripts/fx_acceptance.py` (tỉ giá là cấu hình) ·
**8/8** kịch bản `docs/02 F1` của `scripts/settings_acceptance.py` (trang cài đặt + lịch sử trả) ·
**8/8** kịch bản `docs/02 G7` của `scripts/hostreport_acceptance.py` (báo cáo chủ nhà) ·
**8/8** kịch bản `TC-08` của `scripts/giftcard_acceptance.py` (thẻ quà tặng phải có người trả tiền) ·
**10/10** kịch bản SEO của `scripts/seo_acceptance.py` (`docs/PLAN.md §9.12` — mã
trạng thái, thẻ chia sẻ, sitemap, và liên kết thật vào cả ba dòng sản phẩm; đây là
bộ duy nhất chạm tới `ShellSeo.cs`/`PageExistence.cs`, hai file quyết định mã trạng
thái và thẻ chia sẻ cho **mọi** địa chỉ mà không có test nào khác).
Sổ sách lệch 0. Cả 203 mã của `docs/01` đã làm xong (`docs/PLAN.md §9`).

> **`acceptance.py` cần DB sạch.** Nó ra 8/10 trên DB đã chạy nhiều lần — **không phải
> lỗi code**: dữ liệu tích luỹ làm bước lùi ngày một đơn về quá khứ đụng ràng buộc GiST
> `bookings_no_overlap`. Reset DB theo §5 rồi chạy lại là 10/10 (xác nhận lại 12/08/2026,
> lần này cùng lượt với `service_reviews` mới nên migration cũng đã chạy từ DB trắng).
> Trong lúc truy đã sửa **hai lỗi thật của chính script**: `bookable()` gọi dry-run rồi
> **bỏ qua kết quả** (nên hứa những tin đã bị đặt), và chỉ thử **một khung ngày**. Giờ nó
> đọc kết quả dry-run (đạt là **201**, không phải 200) và thử tám khung ngày.

> **OnePay: `7 đạt · 0 hỏng · 11 bỏ qua` (16/09/2026), và đó là đúng.** Sandbox MTF
> của họ **đã bật 3-D Secure 2** cho thẻ quốc tế, và nó chạy **không có bước nào cho
> người dùng**: `mpgs3ds2.op` → `mpgs3ds2-authenticate-payer.op` chỉ có input ẩn
> (`threeDSMethodData`, `sessionId`, `response.gatewayRecommendation`), rồi trang
> "Xác thực giao dịch không thành công". Thẻ `4005550000000001` vì thế **không còn trả
> được nữa** — `payment_sessions.ResponseCode` trả `F` (3DS hỏng) hoặc `7`. Không có ô
> nào để script điền; muốn chạy lại phải xin OnePay một thẻ miễn 3DS hoặc tắt 3DS cho
> merchant demo. **Sàn xử lý đúng**: đơn không được xác nhận, phiên không ghi là đã trả,
> không giữ lại gì về thẻ bị từ chối, sổ lệch 0 — ba điều đó giờ là khẳng định thật
> trong bộ nghiệm thu. Trước 16/09 bộ này báo **8 kịch bản hỏng**, đọc y như lỗi sản
> phẩm, cho một cái thẻ mà chính cổng từ chối.

### Nền

.NET 9 + EF Core 9 + PostgreSQL 17 (cổng 5544). Frontend React 19 + Vite +
React Router 7 + Leaflet trong `src/StayHost.Web/ClientApp`, build ra
`wwwroot/assets`. Bản vanilla JS cũ đã xoá.

### Đã có

| Nhóm | Nội dung |
|---|---|
| Tiền | `Pricing.cs` chạy đủ 11 bước của `03 §1`, một nơi duy nhất. Thuế theo khu vực, 6 chính sách huỷ, trần giảm 60% |
| Sổ sách | Ghi hai chiều, bất biến, `SaveChanges` chặn UPDATE/DELETE. Mọi luồng tiền đều kiểm tra lệch = 0 |
| Vòng đời đơn | 9 trạng thái + lịch sử chỉ-thêm, giữ chỗ 15 phút, yêu cầu hết hạn 24h, 9 bước kiểm tra đặt được |
| Chống đặt trùng | Ràng buộc GiST trong PostgreSQL — **chỉ áp cho tin nguyên căn** (`RoomTypeId IS NULL`); khách sạn đếm tồn kho theo loại phòng |
| Khám phá | Tìm không dấu, ngày là bộ lọc thật, ngày linh hoạt ±1–7, cuối tuần/tuần/tháng, chọn theo tháng |
| Chủ nhà | Đăng tin theo bước, lịch nhiều tin, giá mùa/theo ngày, đồng bộ iCal hai chiều, co-host có phạm vi quyền |
| Thanh toán | Trả đủ, trả một phần (cọc ≥50% + tự thu trước 14 ngày), chia hoá đơn tối đa 16 người |
| Cổng thanh toán thật | `docs/07 §13` phương án A, `§15.3`. **VNPay** (thẻ quốc tế `INTCARD` + thẻ ATM nội địa `VNBANK`), **MoMo**, **ZaloPay**. Khách rời sang trang của cổng — sàn **không có ô nhập thẻ nào** khi cổng đã bật (`§14.2`). Ba đường báo tin về: khách quay lại (**không tin**), IPN có chữ ký, và `PspSweeper` mỗi phút tự hỏi lại cổng — cái đầu tiên có chữ ký thật thì thắng, hai cái sau không làm gì. **Chữ ký sai bị bỏ qua, không bị coi là thất bại**; số tiền cổng báo khác đơn thì không xác nhận. Khoá để trống = phương thức đó vẫn chạy bằng bản giả lập, y như cũ. Sandbox MoMo/ZaloPay nằm trong `appsettings.Development.json`; VNPay cần `TmnCode` từ **sandbox.vnpayment.vn/devreg/** (trang gốc là 404) |
| Cổng thứ hai cho thẻ quốc tế | **OnePay** (`docs/07 §13`), cùng ô "Thẻ tín dụng / ghi nợ". Có vì **sandbox VNPay không có thẻ quốc tế nào để thử** — thẻ test họ công bố là NCB nội địa, nên nhánh Visa mở được trang mà không trả xong được. Sandbox OnePay cho thẻ Visa `4005550000000001` và trả `vpc_TxnResponseCode=0`. Chữ ký là **HMAC-SHA256**, khoá đọc theo **byte hex** chứ không phải chữ, và `AgainLink` **không** nằm trong phần được ký — sai một trong ba thì mọi giao dịch trông như bị giả mạo. **Hai cổng, cùng merchant cùng khoá, chỉ khác địa chỉ**: quốc tế `vpcpay/vpcpay.op`, nội địa `onecomm-pay/vpc.op` (`Psp.OnePayIsDomestic`). Gửi nhầm thì chữ ký vẫn đúng và OnePay vẫn nhận — khách chỉ gặp biểu mẫu thẻ của mình không điền được. Đổi bằng biến: `Psp:Methods:card=onepay`, `Psp:Methods:napas=onepay`; mặc định vẫn VNPay. **OnePay cho 4 số cuối ngay trong giao dịch thường**, không cần token hoá. `queryDR` của `docs/07 §5` chạy thật (tài khoản demo `op01`/`op123456`, endpoint **`vpcpay/Vpcdps.op`** — bản `onecomm-pay` trả `vpc_TransactionNo=0`). **Hoàn tiền thì chưa**: lệnh `refund` đòi một chữ ký mà khoá thanh toán không tạo ra được, nhiều khả năng merchant demo không có quyền hoàn |
| Thẻ đã lưu với cổng thật | `docs/07 §4`, `§15.5`. Khách tick "Lưu thẻ này" thì đơn đi qua **API token của VNPay** (`pay_and_create`) — đường **duy nhất** sàn biết được bốn số cuối sau khi `§14.2` bỏ ô nhập thẻ. Token lưu **mã hoá** bằng khoá dẫn xuất riêng (`DataSecrets`), bốn số cuối lưu thường. Thẻ do cổng giữ **không có ngày hết hạn** ở đây — `SavedCards.ExpiryKnown` nói không biết thay vì bịa. Tắt mặc định (`Psp:Vnpay:Tokens`) vì VNPay bật tính năng này theo từng merchant |
| Hoàn tiền qua cổng thật | `docs/07 §10`, `§15.6`. Cả **năm** đường huỷ đơn đi qua `RefundGateway`; trước đây một đường hỏi bản giả lập và **bốn đường mặc định `true`**. Phân biệt ba kết quả chứ không phải hai: **từ chối vĩnh viễn** → số dư (đúng ca §10), **chưa biết** → thử lại 3 lần *cùng `vnp_RequestId`* rồi mới chuyển số dư kèm log Error. Trộn hai cái đó là khách được hoàn hai lần. Câu trả lời của cổng lưu ở `payment_sessions.RefundCode/RefundTxnId` — không có nó thì đối soát §7 lệch mà không truy được |
| Chuyển tiền cho chủ nhà | `docs/07 §13`, `§15.4`. Cổng trả **toàn bộ** tiền đơn về tài khoản sàn; phần chủ nhà sàn tự chuyển. Số tài khoản lưu **mã hoá** AES-GCM (`SecretText`, khoá `Payouts:AccountKey`) — trước đây chỉ giữ 4 số cuối nên **không chuyển được cho ai**. Vòng quét sinh `payout_batches` gom theo chủ nhà theo ngày, đơn ở `PayoutStatus.Sent`, **chưa ghi bút toán nào**; quản trị tải `.csv` sáu cột, đưa lên internet banking, rồi bấm *Đã chuyển* — **đó mới là lúc ghi sổ**. Bị từ chối thì quay lại thang thử lại 1/3/7 ngày, không có gì phải đảo |
| Chuyển khoản VietQR | `docs/07 §2.3`, cả ba dòng đơn. Đơn ở trạng thái **chờ chuyển khoản** giữ ngày/ghế **2 giờ** (`BankTransfers.Window`) và **chưa ghi bút toán nào**; người trực dán sao kê ở trang quản trị, `BankTransfers.Judge` khớp mã đơn trong nội dung, rồi đơn đi qua **đúng đường xác nhận mà thẻ đang đi**. Sáu phán quyết, chỉ "khớp" và "đã nhập trước đó" là im lặng. Hết hạn thì trả lại chỗ, không có gì phải đảo. **Chỉ hiện khi có `BankTransfer:AccountNumber`; prod chưa bật** — xem `docs/PLAN.md §9.3` |
| Đánh giá & tin nhắn | Đánh giá mù hai chiều, sửa trong 48h, gửi ảnh, thẻ đơn trong hội thoại, mẫu trả lời nhanh |
| An toàn | Trung tâm giải quyết, trung tâm trợ giúp 14 bài, phát hiện bất thường, nhật ký quản trị chỉ-thêm |
| Quản trị người dùng | Ma trận quyền §2, thang bậc §5 tuần tự, **khoá tài khoản thực thi đúng bảng §6** (huỷ + hoàn tiền + giữ tiền, khách đang ở không bị đụng), khiếu nại người dùng tự nộp được, ảnh giấy tờ không nằm ở thư mục công khai, phiên admin hết hạn sau 30 phút, cấm đăng ký lại sau khoá vĩnh viễn |
| Tài khoản | Đăng ký bằng SĐT hoặc email + OTP 6 số, đăng nhập Google/Apple/Facebook, chặn dưới 18 tuổi |
| Gửi email | Hàng đợi `EmailMessages` được `EmailWorker` gửi thật qua SMTP (MailKit) mỗi 15 giây. Retry 1-5-15-60-240 phút (`EmailDelivery.cs`); 5xx là từ chối vĩnh viễn, bỏ ngay. Mã OTP không bao giờ nằm trong subject. Chưa cấu hình `Email:Host` thì thư nằm chờ, không giả vờ gửi; mật khẩu đặt qua `Email__Password` |
| Hồ sơ | Ảnh đại diện, tên hiển thị, ngôn ngữ nói, nơi ở, nghề nghiệp, sở thích; trang công khai `/users/:id` có huy hiệu xác minh và đánh giá hai chiều |
| Nhận phòng | Hướng dẫn nhận phòng đầy đủ trên trang chuyến đi; địa chỉ và số điện thoại chỉ hiện sau khi đơn được xác nhận, **mã cửa chỉ hiện từ 48 giờ trước giờ nhận** |
| Bảo mật tài khoản | Xác minh danh tính có người duyệt, bảo mật 2 lớp bằng mã 6 số, ma trận thông báo loại × kênh, tải toàn bộ dữ liệu cá nhân |
| Danh hiệu | Siêu chủ nhà xét mỗi quý, Khách chọn xét hằng tuần — cấp và thu hồi tự động. Ngưỡng chỉ nằm trong `Badges.cs` |
| Xếp hạng | Điểm tổng hợp 7 yếu tố của `docs/03 §6` trong `Ranking.cs`, có trừ điểm và đa dạng hoá 12 kết quả đầu ≤ 2 chỗ mỗi chủ nhà |
| Bồi thường hư hỏng | **Khách đền trực tiếp chủ nhà bằng tiền mặt lúc trả phòng — sàn không thu, và không gánh** (khách chốt 17/08/2026, `docs/06 §3.3`). Cửa sổ mở hồ sơ **C1/C2 chỉ 24 giờ** sau trả phòng (`Shield.DamageReportWindow`) — quá đó khách đã đi, không ai đối chất được; C3/C4 vẫn 14 ngày. Ô "thu từ khách" ở màn hình quản trị giờ là **biên bản khách đã đưa bao nhiêu tiền mặt**, quỹ chỉ bù phần còn thiếu, và **không ghi bút toán nào** cho khoản đó. Trung tâm giải quyết cũng vậy: `claim-to-host` không còn ghi sổ — nó từng trừ `GuestFunds` là **tiền của khách khác** |
| Staylio Shield | Hai nhánh K1–K4 / C1–C4 (kể cả bên thứ ba), cửa sổ khiếu nại, thứ tự thu tiền, quỹ trích từ phí dịch vụ, khiếu nại một lần do người khác xét |
| Chia thu nhập co-host | `docs/07 §19`. Chủ nhà đề nghị (phí dọn dẹp · phí dọn dẹp + % · % · % gồm dọn dẹp · số tiền cố định), **người nhận tiền phải đồng ý** trong **14 ngày** rồi mới có hiệu lực. Chia trên **thu nhập sau phí 3%**, phần phí dọn dẹp tính **net** của phí đánh trên chính nó. Nhiều người thì theo thứ tự cố định, **không bao giờ vượt thu nhập của đơn** — hết thì chủ nhà nhận 0. Tiền đi **lệnh chuyển riêng tới ngân hàng của họ**, bút toán ghi lúc ngân hàng thực hiện, và tổng ghi nợ `HostPayable` vẫn đúng bằng thu nhập như trước. Hoàn tiền sau khi đã chia → `OwedToPlatform` của chính co-host, phát hiện bằng **vòng quét** chứ không phải móc vào đường huỷ đơn |
| Mở rộng | Khách sạn (nhiều loại phòng có tồn kho), thẻ quà tặng, số dư, giới thiệu bạn bè |
| Trải nghiệm (`docs/09`) | Thẩm định có người duyệt + phân loại rủi ro theo danh mục, hàng chờ kiểm duyệt, suất lặp lại và chặn chồng giờ, **giữ chỗ 10 phút**, nhiều đơn chung một suất, thuê trọn nhóm, tự huỷ khi thiếu người + gợi ý suất khác, điểm danh, huỷ theo bậc 7 ngày/50%, đánh giá 4 tiêu chí riêng |
| Dữ liệu mẫu | `ReviewSeeder` dựng lịch sử có thật cho hai dòng này: 6 buổi/đơn đã hoàn tất mỗi tin, khách được điểm danh, người cung cấp đã nhận tiền, **bút toán đi qua `Ledger` nên sổ vẫn cân bằng**, rồi 6 đánh giá và điểm sao tính lại từ chính chúng |
| Dịch vụ (`docs/09`) | Chủ nhà tự đăng, chứng chỉ hành nghề có hạn **tự ẩn tin khi hết hạn**, tuỳ chọn thêm có giá riêng, phí di chuyển ngoài bán kính, lịch theo thứ + đệm + chặn hai đơn quá xa, ghi chú bắt buộc theo danh mục, xác nhận điều kiện tại chỗ, huỷ theo bậc 72 giờ, **đánh giá 4 tiêu chí riêng** (tay nghề / đúng như mô tả / đúng giờ / đáng giá tiền — `docs/09 §5`, bảng `service_reviews`) |
| Tiền hai dòng mới | **Dịch vụ có mức phí riêng 0% khách / 15% NCC**; Trải nghiệm giữ 14%/3% như chỗ ở (khách chốt). Trả tiền người cung cấp **sau khi buổi kết thúc 24 giờ**, không phải từ lúc bắt đầu |
| Tuỳ chọn tài khoản (lõi `TK-09`) | `users.Language/Currency/TimeZoneId` + `PUT /api/account/preferences` (endpoint riêng, giá trị sai **từ chối có tên**); picker đẩy lên tài khoản, `loadMe` áp xuống — chọn 한국어 rồi xoá sạch localStorage, tải lại vẫn 한국어. **Chưa có ai đọc `Language` phía server** — email vẫn tiếng Việt cứng; đó là nửa còn lại, đừng tick ké |
| Tỉ giá hiển thị (`QT-06`/`TC-12`) | Bảng `exchange_rates` seed **trong migration**, sửa ở trang quản trị (scope Tài chính, có nhật ký, đặt tay → `Manual`, VND ghim = 1); `/api/meta` đọc DB nên đổi là thấy ngay; đơn đặt **đóng băng tỉ giá của sàn** phía server (`DisplayRate` bỏ khỏi request); tổng quy đổi kèm giá gốc VND theo `docs/07 §6`. Nguồn cấp tự động chờ khách chọn feed |
| Trang cài đặt (`docs/02 F1`) | `/cai-dat` chín nhóm — modal Tài khoản cũ gỡ hẳn, panel export dùng lại; **lịch sử trả tiền** đọc 4 bảng và trả số đã lưu không tính lại; **múi giờ hiển thị** (vế 3 của `TK-09`) chỉ áp cho mốc thời gian, ngày nhận/trả phòng giữ đồng hồ máy; "hoá đơn công ty" cố ý chưa có vì chờ khách chốt theo NĐ 123/2020 |
| Báo cáo chủ nhà (`docs/02 G7`) | Thu nhập theo tháng **tách đã trả / sắp trả**, gộp theo tháng trả phòng và giữ cả tháng rỗng; giá bán thật của từng tin cạnh **mặt bằng khu vực** (dùng chung `Performance.Percentile` với `CN-10`/`QL-09`); **6 hạng mục đánh giá theo tháng**; cộng báo cáo thuế `TC-04` và danh sách cải thiện `QL-18` đã có |
| Đa ngôn ngữ | 8 thứ tiếng (vi/en/ja/ko/zh/fr/de/es), **2149 khoá mỗi thứ, không thiếu khoá nào**. Dịch cả **chữ do server sinh** (trạng thái, dòng hoá đơn, tiện nghi, nhóm tiện nghi, loại chỗ ở, lời khuyên chủ nhà) nhờ từ điển khoá bằng chính chuỗi tiếng Việt. Ngày/giờ/số theo ngôn ngữ đang chọn. Nội dung **người dùng tự viết** (tên tin, mô tả, đánh giá, tiểu sử, nội quy chủ nhà tự gõ) được **máy dịch tự động** kèm dòng "Đã dịch tự động · Xem bản gốc" |
| Email theo ngôn ngữ người đọc | Nửa máy-chủ-đọc của `TK-09` (`docs/PLAN.md §9.18`). **Lằn ranh cứng:** khung thư + mọi thư mang bí mật (OTP, link đặt lại) dịch **tay** trong `Emails.cs` — máy dịch "sửa giúp" một chữ số OTP là một người bị khoá ngoài tài khoản; nội dung thông báo dịch **máy lúc gửi** (`EmailDispatcher.TranslatePendingAsync`, đọc `RawTitle`/`RawBody`, đóng dấu `TranslatedAt` kể cả khi hỏng — bản Việt là dự phòng có chủ đích). Thư bí mật có `RawTitle = null` nên vòng dịch không thấy nó từ câu truy vấn. Tên nằm **trong** mẫu chào (`{0}님`). `EmailsTests` chặn trôi: thêm thứ tiếng vào `Translations.Targets` mà thiếu khung email là build đỏ |
| Máy dịch | `libretranslate` tự host trong cả hai compose — **không cần khoá API, không tính tiền theo ký tự**. Đủ 8 thứ tiếng, trùng khít danh sách giao diện. Kết quả cache trong `translation_caches`, mỗi (chuỗi × ngôn ngữ) chỉ dịch một lần |
| Hạn dùng số dư | `docs/07 §16` đã chốt (11/08/2026): bù đắp / giới thiệu bạn / hoàn khi huỷ **12 tháng**, thẻ quà tặng **không hết hạn**. Hạn đóng dấu **lúc cấp**, nên đổi tham số về sau không với ngược lại số dư khách đang giữ |
| Cẩm nang chủ nhà (`TĐ-22`) | Chủ nhà tự viết danh sách chỗ nên đi cho từng tin: tám nhóm (quán ăn / cà phê / tham quan / thiên nhiên / mua sắm / về đêm / đi lại / lời khuyên), mỗi mục có lý do giới thiệu, địa chỉ và toạ độ tuỳ chọn. Toạ độ **phải đủ cả hai nửa** (`Guidebooks.HasPin`) — nửa vĩ độ đơn độc rơi xuống biển ngoài châu Phi. Chữ do người viết nên đi qua `TranslatedText`, không vào từ điển giao diện |
| SEO cho sàn đặt phòng | `robots.txt` + `sitemap.xml` **sinh từ DB** (`SeoController`) — tin mới đăng có mặt **ngay lần gọi kế tiếp**, không chờ vòng quét nào. Quy tắc "đường nào không được cho Google thấy" nằm trong `Seo.cs` với 31 test: vài đường **mang bí mật ngay trong địa chỉ** (`/split/`, `/wishlist/`, `/chuyen-khoan/`), lọt vào sitemap là công bố link của người khác. `lib/seo.js` lo canonical + og:url theo từng địa chỉ, tiêu đề/mô tả riêng cho trang thành phố và trang tin, và JSON-LD (`Product` + giá + `AggregateRating`, `ItemList`, `BreadcrumbList`, `WebSite`+`SearchAction`) |
| Đường cho Google đi | Thứ quyết định không phải sitemap mà là **liên kết**. `Card` giờ là `<a href="/rooms/…">` thật — trước đó là `div onClick`, nên **không tin đăng nào có một liên kết nào trỏ tới**. Footer lấy danh sách thành phố từ `meta.cityLinks` (server sinh cả slug) thay vì 6 dòng đóng cứng trong khi sàn có 18. Trang thành phố **phân trang** (`?trang=N`, 12 tin/trang, `Seo.CityPageSize`) nên tin thứ 13 trở đi vẫn có liên kết; dải phân trang là `<a href>` thật, và `?trang=N` là **query duy nhất canonical giữ lại** — gộp nó về trang 1 là khai với Google rằng trang 2 trùng nội dung |
| Mã trạng thái đúng cho từng địa chỉ | `SpaRoutes` + `PageExistence`: địa chỉ không có trang nào phía sau trả **404** thay vì 200 kèm shell rỗng. Địa chỉ dưới `/api/` không khớp controller trả 404 **rỗng**, không trả HTML — trả HTML là cách một lời gọi sai động từ đọc thành thành công. Trang `NotFound` có `noindex, follow`, và app tự gỡ thẻ đó khi rời trang |
| Thẻ chia sẻ do máy chủ sinh | `ShellSeo.cs` thay khối `<!--seo:start-->…<!--seo:end-->` của `index.html` theo từng địa chỉ, **ngay câu trả lời đầu tiên**: tiêu đề, mô tả, canonical, `og:url`, `og:image`. Facebook/Zalo/Messenger không chạy JS nên đây là bản duy nhất chúng đọc. Ảnh mặc định `wwwroot/og-default.png` (1200×630); tin đăng dùng ảnh của chính nó |
| Người đang trên sàn | Trang quản trị đếm **số người đang truy cập** theo cookie phiên `sh_sid` — khách chưa đăng nhập cũng tính, vì phần lớn lưu lượng của một sàn đặt phòng là người chưa có tài khoản. Cửa sổ 5 phút (`Presence.Window`), tách **đã đăng nhập / khách**, kèm đỉnh kể từ lần khởi động. Đếm **trong bộ nhớ** (`PresenceTracker`), không ghi bảng: đây là đường ghi bận nhất của app mà con số thì không ai cần giữ lại. Bot bị loại ba lớp — User-Agent, đường máy gọi máy (`/health`, IPN của cổng thanh toán), và **cookie phải được gửi trả lại** (crawler không giữ cookie nên mỗi request của nó là một danh tính mới) |
| Hiếm có & sắp hết phòng | `Scarcity.cs` là **một ngưỡng cho hai chỗ**: dấu "Hiếm có" trên trang chi tiết (`TĐ-23`) và thông báo "sắp hết phòng" cho chỗ đã lưu (`YT-08`). Dưới 25% đêm trống trong 60 ngày tới, và bỏ qua khi cửa sổ chưa đủ 14 đêm — tin mới khoá sạch lịch là *trống*, không phải *đắt khách*. `ScarcitySweeper` chỉ báo **lúc vượt ngưỡng**, cột `LowAvailabilityNotifiedAt` xoá về null khi lịch mở lại |

---

## 4. Bài học đã trả giá — đừng lặp lại

- **Dừng `dotnet run` trước khi build lại.** Tiến trình đang chạy giữ DLL, MSBuild
  báo `MSB3027`/`MSB3021`. Dùng `TaskStop` rồi mới build.
- **Sau `dotnet ef migrations add` phải `dotnet build` lại** trước khi chạy, nếu không
  app báo `PendingModelChangesWarning`.
- **`{action}` là token dành riêng trong route ASP.NET.** `[HttpPost("{id:int}/{action}")]`
  bị viết lại thành tên method và trả 405. Dùng `{decision}`.
- **`Forbid()` ném exception ở app này.** Phiên đăng nhập là cookie tự phát, không có
  authentication scheme, nên `Forbid()` thành 500. Dùng `this.Denied()`
  (`Infrastructure/SessionAccessor.cs`).
- **EF không project được positional record ra khỏi `GroupBy`** — project sang anonymous
  type rồi map trong bộ nhớ.
- Entity `Host` từng đụng `Microsoft.Extensions.Hosting.Host` → đã đổi thành `HostProfile`.
- `/api/account/me` trả **204** khi chưa đăng nhập (không phải 200 rỗng).
- **Đơn đặt đừng đếm chính nó.** Tạo giữ chỗ đã tính là một lượt bán, nên nếu lúc thanh
  toán tính lại giá mà vẫn đếm nó thì đơn tự làm mất giảm giá tin mới rồi tự fail.
- **Số dư đã cam kết lúc giữ chỗ phải được đưa vào lần tính giá lại lúc thanh toán**,
  nếu không đơn dùng số dư luôn trượt kiểm tra "giá có đổi không".
- Vite 8 (rolldown) chỉ nhận `manualChunks` dạng hàm.
- **Có file `.cs` không có nghĩa là có chạy.** Soát `docs/08` ngày 08/08 phát hiện
  `SuspensionImpact` tính đúng cả bảng §6 mà chưa bao giờ được gọi khi khoá thật,
  `Appeals` đủ luật mà người dùng không có đường nộp. Kiểm chứng bằng **kết quả
  trong cơ sở dữ liệu**, đừng kiểm chứng bằng màn hình xem trước.
- **Đơn do sàn huỷ đừng ghi sang phía khách.** `PostCancellation` map
  Platform/ForceMajeure sang `CancelledByHost`; mọi chỗ đếm số lần huỷ để đánh giá
  một người phải lọc thêm `CancelledBy`. Ghi nhầm là khách mất quyền hoàn phí dịch
  vụ 3 lần/năm của `docs/03 §4` vì lỗi của người khác.
- **Đừng mượn quy tắc §8 (khiếu nại phải người khác xét) áp cho §1.3.** Chặn admin
  "đã từng ra quyết định với người này" làm thang bậc §5 không đi được: leo thang
  bắt buộc phải cùng một người viết bước tiếp theo.
- **`ledger_entries.BookingId` là khoá ngoại tới bảng `bookings` (chỗ ở).** Trải nghiệm
  và dịch vụ có cột riêng (`ExperienceBookingId`, `ServiceBookingId`) — truyền nhầm id
  vào `BookingId` thì mọi lần chi tiền đều ném, và vì nó ném trong tick của worker nên
  **các sweep phía sau cũng chết theo** mà không ai thấy.
- **Không `dotnet ef migrations add --no-build`.** Migration khi đó sinh từ assembly cũ,
  chạy lên là `PendingModelChangesWarning`. Phải dừng app → build → mới `migrations add`.
  Và khi lọc log build, đừng chỉ `grep "error CS"`: lỗi khoá DLL là `MSB3027`, build coi
  như hỏng mà mình tưởng xanh.
- **Giữ chỗ rồi thì lúc thanh toán đừng kiểm tra như người lạ.** `CanBook` thấy ghế đã bị
  trừ sẽ từ chối chính đơn mà lượt giữ chỗ sinh ra để bảo vệ — phải cộng lại phần mình
  đang giữ trước khi kiểm tra.
- **Dịch giao diện (`lib/i18n.js`): từ điển khoá bằng chính chuỗi tiếng Việt.** `t(s, ctx?)`
  tra theo thứ tự `"s|ctx"` → khớp đúng → **mẫu số `{}`** → trả nguyên bản. Chữ **do
  server sinh** cũng dịch được bằng cách bọc `t(field)` ở chỗ render rồi thêm cặp vào từ
  điển. Bản dịch **phải giữ đúng số lượng và thứ tự `{}`**, thiếu một cái là thay số sai.
  Không hard-code `'vi-VN'` cho `Intl` — dùng `dateFormat()`/`number()` của `format.js`.
  Nội dung **người dùng tự viết** (tên tin, mô tả, đánh giá, tiểu sử) **không vào từ
  điển** — `TranslatedText.jsx` máy dịch tự động và ghi rõ "Đã dịch tự động".
- **Coi chừng biến tên `t` che mất hàm dịch.** Đã gặp `const t = encodeURIComponent(...)`
  và `map(t => …)`; khi đó `t('…')` âm thầm chạy sai chứ không báo lỗi.
- **Thiếu khoá dịch không báo lỗi ở đâu cả** — `t()` trả nguyên bản, console sạch,
  test xanh, build xanh. Đó là lý do khách phải tự mắt tìm ra **năm đợt** liên tiếp.
  Chạy `python scripts/i18n_audit.py` (phải ra **0**). Nó chỉ soát được literal;
  `t(giá_trị_server)` thì phải mở trang bằng ngôn ngữ khác rồi tìm chữ còn dấu tiếng
  Việt trong DOM. **Có component chưa từng import `t`** — `PhotoMosaic`,
  `CardCarousel`, `Maps` đứng ngoài toàn bộ hệ dịch mà không ai biết.
- **Chữ do server sinh phải bọc `t()` ở *mọi* chỗ render, không chỉ chỗ hay nhìn.**
  Khách báo hộp "Nơi này có những gì" hiện từng món bằng tiếng Nhật nhưng **tên nhóm**
  ("Tiện nghi", "Ngoài trời") vẫn tiếng Việt: `SearchModals` viết `t(group)`, còn
  `ListingModals` và `ListingWizard` viết thẳng `{group}`. Khoá đã có sẵn trong từ
  điển — thiếu đúng cái bọc. Cùng lỗi ở nhãn loại chỗ ở (`Header`, `Browse`, wizard).
  Khi thêm chỗ render dữ liệu server, hỏi "cái này có nằm trong từ điển không?"
- **`LT_LOAD_ONLY` chỉ có tác dụng lần chạy đầu của volume.** Thêm `de,es` rồi restart
  thì container vẫn **healthy**, `/languages` vẫn 6 thứ tiếng, và `/translate` trả
  `"de is not supported"` — hỏng mà không có dấu hiệu nào. Đã bật `LT_UPDATE_MODELS`
  ở cả hai compose. Danh sách này phải khớp `Translations.Targets`, khớp luôn cả danh
  sách ngôn ngữ giao diện (`CatalogService.Languages`).
- **Script nghiệm thu phải nói cùng đồng hồ với server.** `psql` trong container
  chạy `Asia/Ho_Chi_Minh`, còn app xét ngày bằng `DateTime.UtcNow`. Nên
  `current_date - 1` của script, **từ 00:00 tới 07:00 giờ VN**, chính là *hôm nay*
  của server: kịch bản 9 của `docs/09` dựng chứng chỉ "đã hết hạn" mà server thấy
  chưa hết, sweep không ẩn tin, và script báo FAIL trong khi sản phẩm đúng. Bảy
  tiếng mỗi ngày. Dùng `(now() at time zone 'utc')::date`, đừng dùng
  `current_date`. `now()` thì không sao — `timestamptz` so sánh không lệch.
- **Giờ mở cửa là giờ của nhà cung cấp, không phải giờ UTC.** `CanBook` từng so
  `req.StartsAt.Hour` (UTC) với `OpensAtHour`, trong khi picker sinh khung giờ
  theo giờ máy khách. Ở Việt Nam lệch 7 tiếng: khách bấm "10:00" thì server nhận
  03:00Z và trả "chỉ nhận từ 6:00 đến 18:00" — **mọi giờ picker mời đều là giờ
  server từ chối**. `TimeZoneId` đã nằm sẵn trên entity từ đầu mà chưa ai dùng.
  Giờ `ServiceRules.LocalTime` quy đổi một lần, và giờ/thứ/`MaxJobsPerDay` đều
  đọc theo lịch của chính người làm.
- **`AddColumn` với `defaultValue: 0` có thể làm hỏng dữ liệu đang chạy.**
  `WorkingDaysMask` thêm vào `service_offerings` với mặc định 0, nghĩa là mọi
  dịch vụ đang bán lúc đó **không làm ngày nào trong tuần**: `WorksOn` trả false
  mọi ngày → `CanBook` từ chối mọi lần đặt → picker trống trơn, mà **không có
  lỗi ở đâu cả**. Nằm im từ 10/08 tới 12/08, chỉ lộ ra khi khách mở modal chọn
  giờ trên bản chạy thật. Reset DB xong thì không thấy, vì dòng mới lấy mặc định
  của entity. Thêm cột có ý nghĩa "mọi/tất cả" thì mặc định phải là giá trị đó,
  không phải 0; và chỗ đọc nên tự chuẩn hoá (`ServiceRules.WorkingDays`).
- **Điểm sao seed sẵn sẽ biến mất khi có đánh giá thật.** `Rating`/`ReviewCount`
  được **tính lại từ chính bảng đánh giá** mỗi lần ai đó chấm điểm, nên một tin
  seed "4.85 · 27 đánh giá" mà chưa có dòng nào trong `experience_reviews` /
  `service_reviews` sẽ tụt về "5.0 · 1" ngay sau đánh giá đầu tiên. Vì thế
  `ReviewSeeder` seed cả **đơn đã hoàn tất + bút toán + đánh giá**, rồi tự tính
  lại điểm — chứ không bịa một con số rồi để trống khối đánh giá bên dưới.
- **Đừng ghép tên người vào giữa một câu đã dịch.** `{t('Nhắn cho')} {tên}` ra
  "Message Binn" đúng, nhưng tiếng Hàn/Nhật đặt tân ngữ trước động từ nên thành
  "메시지 보내기 Binn". Hoặc dùng nhãn không có tên (`Nhắn cho nhà cung cấp`), hoặc
  tách thành tên một dòng và vai trò một dòng như `xp-hero-facts` đang làm.
- **Chữ viết trên màn hình phải khớp luật đang chạy.** Trang dịch vụ hứa "huỷ
  trước 24 giờ hoàn toàn bộ" suốt từ đầu, trong khi `ServiceRules` là 72 giờ 100%
  / 24 giờ 50% / sát giờ 0. Không test nào bắt được vì đó chỉ là một chuỗi.
- **`HoldExpiresAt` bị xoá khi đơn rời trạng thái chờ.** `BookingLifecycle.Transition`
  set nó về null cho mọi trạng thái khác `PendingPayment`, nên câu truy vấn "đơn vừa
  hết hạn trong 7 ngày qua" lọc theo cột đó **không bao giờ khớp dòng nào** — và
  không có lỗi ở đâu cả, chỉ là một phán quyết không bao giờ chạy. Đã đổi sang
  `CreatedAt`. Muốn biết một đơn *đã* hết hạn lúc nào thì đọc `BookingEvents`.
- **Đừng tự dựng `CreditEntry` bằng tay.** `docs/01 TC-07` đóng dấu hạn dùng **lúc cấp**;
  đường hoàn số dư khi huỷ đơn trong `BookingsController` tự `new CreditEntry` nên bỏ
  qua bước đó, và số dư hoàn lại **không bao giờ hết hạn** dù cấu hình nói 12 tháng.
  Giờ mọi dòng số dư đi qua `CreditLedger.Grant`, có test chặn.
- **Một nửa yêu cầu vẫn bị đếm là cả yêu cầu.** `YT-08` viết "báo khi chỗ đã lưu
  giảm giá **hoặc sắp hết phòng**". Nửa giảm giá có sự kiện rõ ràng (chủ nhà lưu giá
  thấp hơn) nên làm xong từ 10/08 và mã được tick; nửa "sắp hết phòng" **không có sự
  kiện nào để móc vào** — lịch kín là do người khác đặt — nên bị bỏ quên suốt, mà
  `PLAN.md` vẫn ghi 201/201. Soát mã có chữ "hoặc"/"và" thì soát **từng vế**.
- **`Wishlists.jsx` chưa từng import bản đồ nào** dù `YT-04` nói "xem danh sách trên
  bản đồ", và `ResultsMap` không dùng lại được vì nó đọc thẳng `state.results`.
  Giờ có `CardsMap` nhận thẳng danh sách thẻ. Đây là **lần thứ tư** `PLAN.md §9`
  đếm lệch — trước khi tin con số, `grep` tên component ở chỗ đáng lẽ phải dùng nó.
- **Soát "có ai gọi không" phải soát cả hai tầng.** Liệt kê thành viên `public static`
  của `StayHost.Domain` rồi hỏi cái nào không được `StayHost.Web`/`Infrastructure`
  gọi **mà cũng không được gọi từ chỗ khác trong Domain** — lần đầu chạy ra 112 cái,
  gần hết là hàm nội bộ (`Ranking.Nearness`, `Quality`…) nên vô nghĩa; lọc thêm vế
  thứ hai còn **36**, và **sáu trong đó là lỗi thật**. Ngược lại, **không phải hàm
  nào không ai gọi cũng là lỗi**: `Sanctions.BanBlocks` nhìn như `docs/08 §5.4` bỏ
  sót "tài khoản nhận tiền", nhưng chỗ đó đã chặn ở `HostOperationsController` lúc
  đặt tài khoản. Phải đi xem chỗ khác có làm việc đó không rồi mới kết luận.
- **Tham số khách đã chốt vẫn có thể chỉ là chữ trên trang điều khoản.** Trong 14
  tham số của `docs/06 §10`, sáu cái chỉ xuất hiện ở `ShieldController` để **in ra
  cho khách đọc**; enforcement nằm trong `Shield.SettleHost`/`SettleGuest`. Hai cái
  thì không có gì thật cả: `LostIncomeNights` (C-D) có `Shield.LostIncome` mà không
  ai gọi, và `ForceMajeureHostRate` (Q-A) **không có một chỗ đọc nào**. Muốn biết
  một tham số có sống không thì truy: tham số → hàm domain đọc nó → có app code gọi
  hàm đó không.
- **Một `enum` có giá trị không có nghĩa là có đường sinh ra giá trị đó.**
  `CancelledBy.ForceMajeure` tồn tại, `Cancellation.Refund` có sẵn nhánh hoàn 100%
  cho nó, `PostCancellation` map nó sang `CancelledByHost` — nhưng **cả ba call site
  chỉ truyền `Host`/`Guest`/`Platform`**, nên toàn bộ nhánh bất khả kháng chưa bao
  giờ chạy được. Thêm mã vào một `enum` thì phải hỏi luôn "ai bấm nút này?".
- **Nhà cung cấp dịch vụ từng không có màn hình nào xem đơn của mình.** Console chủ
  nhà chỉ liệt kê dịch vụ **đang bán**. Ghi chú **bắt buộc** của `docs/09 §3.5` (dị
  ứng đồ ăn, vùng cần tránh khi massage) được thu vào DB rồi không hiện cho đúng
  người cần đọc. Khi bắt khách nhập gì đó "cho bên kia", kiểm luôn bên kia có chỗ
  đọc chưa.
- **Một cổng đang rảnh hôm qua không có nghĩa hôm nay còn rảnh.** Ba script nghiệm
  thu đóng cứng `localhost:5199`, và cổng đó đã bị **một project khác** (`BlueOne.Web`)
  chiếm. Nó là SPA nên trả **200 cho mọi đường dẫn**, thành ra cả bước "chờ server
  sẵn sàng" lẫn script đều tưởng đang nói chuyện với Staylio. Giờ cả ba đọc
  `STAYHOST_URL`, và bước chờ phải kiểm tra **nội dung**, không phải mã 200.
  Route đúng là **`/api/meta`** (`ListingsController` gắn `[Route("api")]`, không
  phải `api/listings`) — chờ ở `/api/listings/meta` thì nhận 404 vĩnh viễn và vòng
  lặp không bao giờ thoát:
  `until curl -s $URL/api/meta | grep -q '"categories"'; do sleep 5; done`
- **Một container `docker compose` cũ vẫn chạy vòng quét trên cùng cơ sở dữ liệu.**
  `stayhost-web` từ lần `docker compose up -d --build` trước đó vẫn sống, chạy bản
  **Release cũ**, và mỗi phút vẫn quét đơn/chi trả trên đúng DB mà `dotnet run`
  đang dùng. Nó ghi thông báo bằng chữ **không còn tồn tại trong mã nguồn** — mất
  một lúc lâu mới tin nổi. Trước khi kết luận "code không chạy", chạy
  `docker ps` và `Get-Process StayHost.Web`; và `TaskStop` chỉ giết `dotnet run`,
  tiến trình `StayHost.Web.exe` con có thể sống tiếp (chính nó khoá DLL gây `MSB3027`).
- **Đếm số thứ tự theo một cột vừa đổi nghĩa là hỏng ngay.** Mã lệnh chuyển tiền
  đếm `PaidOutAt` để ra số thứ tự trong ngày. Khi `PaidOutAt` chuyển nghĩa thành
  "ngân hàng đã thực hiện" (null với lệnh còn chờ), hai lệnh cùng chủ nhà cùng ngày
  ra **trùng mã**, ràng buộc duy nhất ném, và vì ném trong tick của worker nên
  **các vòng quét phía sau chết theo, im lặng**. Đổi nghĩa một cột thì grep hết chỗ
  đọc nó.
- **Trừ vào `GuestFunds` sau khi khách đã trả phòng là tiêu tiền của khách khác.**
  `Ledger.SettleClaim(toHost:)` và `Shield.ChargeCounterparty` đều trừ tài khoản gộp
  "tiền sàn giữ hộ khách" để trả chủ nhà. Đến lúc phân xử xong thì tiền của **chính** khách
  đó đã chuyển cho chủ nhà từ lâu, nên khoản trừ ấy ăn vào số dư của người khác và không
  bao giờ có ai thu lại. Sổ vẫn cân nên không có gì kêu. Trước khi ghi một bút toán trừ
  tài khoản gộp, hỏi "tiền của **ai** đang nằm ở đó lúc này?".
- **VNPay trả 403 cho request không có `User-Agent`.** `merchant_webapi` đáp lại bằng
  **HTML 403** cho mọi request thiếu header đó, mà `HttpClient` mặc định không gửi. Log chỉ
  nói "không parse được JSON" — không giống lỗi chữ ký, không giống lỗi gì cả. Nó âm thầm
  tắt **cả `refund` lẫn `querydr`**, tức cả lưới an toàn của `docs/07 §5`. Đã đặt UA cho
  HttpClient `"psp"`. Cùng một request: có UA → 200, bỏ đi → 403.
- **"Chưa biết" không phải "bị từ chối".** Cổng từ chối hoàn tiền là ca `docs/07 §10`
  (thẻ đã đóng → chuyển số dư). Gọi không được thì **chưa biết**, và đẩy sang số dư ngay là
  cách khách **được hoàn hai lần**. Thử lại cùng `vnp_RequestId` — VNPay nhận ra là một yêu
  cầu và trả `94` thay vì hoàn lần nữa.
- **Đối soát đọc `gateway_charges` cho cả hai vế là so sổ mình với sổ mình.** Nó cân mỗi
  ngày và không chứng minh gì. `docs/07 §7` bảo so với **danh sách của cổng**; giờ
  `GatewayStatement` hỏi lại từng phiên đã chốt trong ngày.
- **Callback sai chữ ký phải bị *bỏ qua*, không phải bị coi là *thất bại*.** Bản
  đầu của `PspVerdict.Forged` mang `Status = Failed`, nên `SettleAsync` ghi phiên
  thành hỏng. Không có gì xác thực người gọi vào `/api/payments/momo/ipn` — ai đoán
  ra mã đơn cũng giết được lượt thanh toán của người lạ, và khách trả tiền thật sau
  đó quay về một đơn đã bị xoá sổ. `scripts/gateway_acceptance.py` bắt được nhờ bắn
  callback giả vào **một đơn thật đang chờ**, không phải vào mã bịa: kiểm chữ ký mà
  chỉ từ chối được mã không tồn tại thì không kiểm gì cả.
- **Cổng thanh toán ngoài đời không gọi được vào `localhost`.** IPN của VNPay /
  MoMo / ZaloPay không bao giờ tới máy lập trình, nên trên máy mình **`PspSweeper`
  là đường duy nhất chốt được một lượt thanh toán**. Đó cũng chính là lý do
  `docs/07 §5` bắt sàn tự hỏi lại thay vì tin trình duyệt — nên đừng "sửa" bằng
  cách tin trang khách quay về.
- **Trả lời IPN bằng `new { RspCode = … }` là VNPay không đọc được.** App này
  serialise theo camelCase, nên `RspCode` ra dây là `rspCode`; VNPay tìm đúng tên
  hoa, không thấy, và theo tài liệu của họ **retry 10 lần cách nhau 5 phút** — cho
  *mọi* giao dịch thành công, im lặng, trong khi đơn đã xác nhận và phía mình
  không có gì trông sai cả. Giờ là record `VnPayReply` có `[JsonPropertyName]`.
  ZaloPay thoát nạn vì `return_code` vốn đã thường.
- **`vnp_IpAddr` phải dài 7–45 ký tự.** Kestrel trả `::1` cho mọi thứ chạy cùng
  máy, VNPay đáp lại bằng trang lỗi trắng **không nói field nào sai** — đọc y hệt
  như sai chữ ký. `Psp.ClientIp` quy đổi, có test.
- **VNPay cần cookie phiên; curl không có cookie thì báo lỗi giả.** `vpcpay.html`
  cấp token rồi 302 sang `PaymentMethod.html`; không giữ cookie thì trang đó đá
  sang `Payment/Error.html`. Chữ ký đúng hoàn toàn. Dấu hiệu phân biệt: **có tới
  được `PaymentMethod.html?token=…` hay không** — tới được nghĩa là VNPay đã nhận
  chữ ký, lỗi sau đó là chuyện khác.
- **Bật một cổng thật là bẻ gãy mọi script nghiệm thu trả tiền bằng thẻ.** `/pay`
  không còn xác nhận đơn nữa mà trả về địa chỉ chuyển hướng, và **`acceptance.py`
  vẫn báo 10/10** vì kịch bản 3 chỉ kiểm `st == 200` chứ không kiểm đơn đã
  `Confirmed` — một bộ nghiệm thu xanh mà đơn chưa ai trả tiền. Giờ ba bộ đi qua
  `scripts/_gateway.py`, ký IPN đúng như VNPay ký, nên chạy được cả khi có lẫn khi
  không có cổng. Đổi cách thanh toán thì soát lại **cái mà script khẳng định**,
  đừng chỉ soát số PASS.
- **Cổng thật thì sàn không biết bốn số cuối của thẻ — trừ khi khách lưu thẻ.**
  Khách gõ thẻ ở trang VNPay nên `payments.CardLast4` là null, kéo theo
  `SavedCards`, nhắc thẻ sắp hết hạn, và nhánh "thẻ đã đóng" của `Refunds.Redirect`
  **không có gì để đọc**. Đường duy nhất lấy lại là **API token** (`pay_and_create`,
  `docs/07 §15.5`) — chỉ lúc đó VNPay mới nói cho sàn biết gì về cái thẻ. Khách
  không tick lưu thẻ thì vẫn null, và `unwired_acceptance.py` tự đặt cột đó để
  thử được luật hoàn tiền.
- **API token của VNPay viết tên tham số kiểu khác.** `vnp_command` chứ không phải
  `vnp_Command`, và đường dẫn riêng `/token_ui/`. Trộn hai kiểu thì nhận trang lỗi
  trắng không nói field nào sai. Quy tắc ký thì **tài liệu của họ không nói** — đã
  thử nghiệm ra: **sorted-query giống API thanh toán**, hai biến thể pipe-joined
  đều rơi `error.html`. Đừng đoán, gửi thử rồi xem đáp lại gì.
- **Khớp chuỗi con khi tự động hoá trang tiếng Việt là bấm nhầm nút.** Trong một
  buổi dựng `vnpay_browser_acceptance.py` nó bấm **"Hủy thanh toán"** vì khớp
  "Thanh toán", rồi bấm **"Không đồng ý"** vì khớp "Đồng ý" — hai lần chọn đúng cái
  ngược lại. Loại trừ "hủy"/"không" trước khi khớp.
- **`app_trans_id` của ZaloPay phải mang ngày của Việt Nam.** Lại đúng bảy tiếng cũ:
  một đơn mở lúc 18:00 UTC là 01:00 hôm sau ở TP.HCM, và ZaloPay từ chối mọi mã
  không mở đầu bằng ngày hôm nay của **họ**. `Psp.ZaloTransId` quy đổi, có test.
- **Một tên miền viết thẳng vào mã nguồn không có gì báo sai.** `NotificationService`
  dựng dòng "Xem chi tiết" bằng `https://stayhost.vn{link}` — một tên miền sàn không
  sở hữu — nên **mọi email thông báo** gửi khách đều dẫn vào hư không. Thư vẫn rời
  hàng đợi, log vẫn sạch, test vẫn xanh: không có ai ở phía nào để kêu. Chỉ lộ ra khi
  đi soát tên miền để đổi. Giờ là `Site:PublicUrl`, và nó **rơi về `Psp:PublicUrl`**
  khi để trống — cùng một địa chỉ, nên không đẻ ra biến thứ hai phải nhớ sửa cùng lúc.
  Không có địa chỉ nào thì **bỏ hẳn dòng link** chứ không đoán tên miền.
- **Canonical mà cắt sạch query sẽ giết luôn phân trang.** Bản đầu của
  `lib/seo.js` bỏ toàn bộ query — đúng với bộ lọc ngày/khách (cùng một chỗ, chỉ khác
  điều kiện), nhưng **sai với `?trang=2`**, vì trang 2 chứa những căn *khác hẳn* trang 1.
  Gộp nó về địa chỉ trần là khai với Google rằng các trang sau là bản sao, và mọi tin từ
  thứ 13 trở đi rơi khỏi chỉ mục — đúng cái lỗ mà phân trang sinh ra để bịt. Giữ đúng một
  tham số (`trang`), và `?trang=1` vẫn phải gộp về địa chỉ trần vì đó là cùng một trang.
- **`div onClick` không phải liên kết, và Google chỉ đi theo liên kết.** `Card.jsx`
  điều hướng bằng `navigate()` trong `onClick`, comment ngay trên đó còn viết "the card
  is a link" — nhưng trong HTML thì không có `<a href>` nào, nên **không một tin đăng
  nào có đường vào**. Người dùng bấm vẫn chạy, test vẫn xanh, màn hình vẫn đúng; chỉ có
  Google là không vào được, và chuyện đó không hiện ra ở đâu cả. Cùng loại lỗi ở
  `Footer`: 6 thành phố đóng cứng trong khi sàn có 18, tức 12 trang không ai trỏ tới.
  Hỏi "cái này có phải thẻ `<a href>` thật không?" chứ đừng hỏi "bấm vào có chạy không".
- **Đừng viết lại chuẩn hoá slug ở phía JS.** Breadcrumb suýt sinh
  `/thanh-pho/tp-ho-chi-minh` vì bản JS không cắt tiền tố như `Cities.Key` — địa chỉ đó
  404, và **dữ liệu có cấu trúc sai còn hại hơn không có**. Slug giờ lấy từ
  `meta.cityLinks` do chính server sinh ra.
- **`aggregateRating` chỉ được xuất khi có đánh giá thật.** Google coi review markup bịa
  là spam và phạt **cả tên miền** chứ không riêng trang đó. Tin mới đăng có `ReviewCount = 0`
  nên khối `Product` bỏ hẳn `aggregateRating` — đã thử bằng cách đăng một tin thật rồi mở
  bằng trình duyệt, không suy luận.
- **Một `canonical` cố định trong SPA sẽ xoá sổ toàn bộ danh mục khỏi Google.** Mọi
  địa chỉ ở đây đều nhận cùng một `index.html`, nên đặt sẵn `<link rel="canonical">`
  trong file đó là **khai với Google rằng mọi tin đăng đều là bản sao của trang chủ** —
  và chúng rơi khỏi chỉ mục. Canonical phải theo từng địa chỉ (`lib/canonical.js` đặt
  lại sau mỗi lần chuyển trang) hoặc không có. Với sàn đặt phòng thì bắt buộc phải có:
  bộ lọc ngày/khách sinh ra vô số địa chỉ cùng nội dung, và Google chia điểm cho tất cả
  rồi không tin cái nào. Kiểm bằng **trình duyệt thật** — thẻ do JS đặt, `curl` không thấy.
- **Chữ trong `meta description` cũng là chữ hiển thị, và nó hiện trên Google.**
  `index.html` hứa "huỷ miễn phí trước 48 giờ" từ đầu, trong khi **không bậc huỷ nào là
  48 giờ**: Linh hoạt 24 giờ, Vừa phải 5 ngày (mặc định), Chặt 30/7 ngày, Rất chặt 7
  ngày, hoặc không hoàn (`Cancellation.cs`). Cùng loại lỗi với trang dịch vụ hứa 24 giờ
  khi luật là 72 giờ — nhưng nặng hơn, vì đây là dòng chữ người ta đọc **trước khi** bấm
  vào, tức một lời hứa đưa ra trước cả khi khách tới sàn.
- **Tài liệu viết `sudo` không có nghĩa là tài khoản đó sudo được.** `DEPLOY.md §4`
  bảo `sudo bash deploy/setup-runner-service.sh`, nhưng trên `bluestar01` thì `hung`
  không nằm trong sudoers và nhóm `sudo` **rỗng** — máy được quản trị bằng root trực
  tiếp. Cả một bước cài đặt vì thế **chưa bao giờ chạy**, và hậu quả chỉ hiện ra nhiều
  tháng sau dưới dạng một chữ "offline" trên GitHub, đúng lúc cần deploy gấp. Đáng nói
  hơn: `hung` **có** nhóm `docker`, mà nhóm docker là root trá hình (mount `/` vào một
  container là xong) — nên chỗ thiếu sudo ấy **không phải hàng rào bảo mật**, chỉ là
  bất tiện. Trước khi viết `sudo` vào tài liệu, kiểm `id` của tài khoản thật sẽ chạy nó.
- **`pgrep -f` khớp luôn chính câu lệnh đang gọi nó.** Dòng cron canh runner viết
  `pgrep -f "Runner.Listener" || khoi_dong_lai` **không bao giờ khởi động lại**: cron
  chạy nó qua `sh -c '...'`, mà dòng lệnh của chính `sh` đó có chứa chuỗi tìm kiếm, nên
  `pgrep` luôn tự thấy mình và kết luận "đang chạy". Không có lỗi, không có log, chỉ là
  một lưới an toàn không bao giờ bung. Cùng cái bẫy đó, `pkill -f "Runner.Listener"` gõ
  qua SSH **tự cắt phiên SSH của mình**. Dùng ngoặc vuông: `pgrep -f "[R]unner.Listener"`
  — regex khớp `Runner.Listener` nhưng dòng lệnh chứa `[R]unner.Listener` thì không khớp.
  Kiểm chứng bằng cách **giết tiến trình thật rồi xem lưới có bung không**, đừng đọc dòng
  cron rồi tin.
- **Mật khẩu seed + 2FA tắt + repo công khai = trang quản trị bỏ ngỏ.** Ngày 26/08
  phát hiện `admin@stayhost.vn` / `stayhost123` **đăng nhập được vào prod**: `README.md`
  in nguyên bảng đó trên một repo PUBLIC, còn `ADMIN_REQUIRE_2FA=false` — ngoại lệ khẩn
  cấp của `DEPLOY.md §2.1` cho máy chủ chưa gửi được email. Từng mảnh đều "đúng theo tài
  liệu"; ghép lại thì ai đọc README cũng vào được hồ sơ, ảnh giấy tờ và lệnh chuyển tiền.
  Comment trong `AccountModals.jsx` đã lường trước đúng tình huống này mà không ai nối
  hai đầu lại. Bật một ngoại lệ bảo mật thì phải hỏi luôn "cái gì đang che thay nó?".
- **`MapFallbackToFile` trả 200 cho *mọi* địa chỉ, và đó là soft 404.**
  `/rooms/khong-co-that-999` về 200 kèm tiêu đề trang chủ và thân trang rỗng; Google
  hoặc lập chỉ mục trang trắng ấy, hoặc sau vài lần thì bớt tin những địa chỉ nó chưa
  bò tới. Không có dấu hiệu nào ở phía mình: người dùng thấy "không tìm thấy" rồi quay
  ra. Giờ `SpaRoutes.Resolve` nói địa chỉ đó là loại trang gì, `PageExistence` hỏi DB,
  và fallback đặt đúng mã. **Cái đắt hơn là nó che lỗi suốt nhiều tháng:** ba lời gọi
  trong `acceptance.py` gửi **GET vào endpoint POST** (`/api/favorites/{id}`,
  `/api/account/send-verification`) và vẫn "đạt", vì shell trả 200 nên `st == 200`.
  Dòng chi tiết in ra `? chỗ đã lưu` mỗi lần chạy — dấu hiệu duy nhất, và không ai đọc.
  `/api/` giờ trả 404 rỗng chứ không trả HTML.
- **Thẻ `og:*` đặt bằng JavaScript không bao giờ tới Facebook, Zalo hay Messenger.**
  Chúng đọc HTML như nhận được, **không chạy JS**. Nên `setPageMeta` của `lib/seo.js`
  — vốn đặt đúng tiêu đề, mô tả và ảnh cho từng tin — chưa từng có tác dụng với kênh
  mà **phần lớn khách Việt Nam bấm vào**: mọi lần dán một tin vào khung chat đều ra
  tiêu đề trang chủ và một thẻ trắng (không có `og:image` nào cả). Giờ `ShellSeo.cs`
  thay khối giữa `<!--seo:start-->`/`<!--seo:end-->` **ngay trong câu trả lời đầu
  tiên**. Hai bản không thừa: bản server cho scraper, bản JS cho lúc người dùng đi lại
  trong app mà tài liệu không tải lại. Google chạy JS nhưng chậm — có sẵn ở lần đầu
  vẫn hơn.
- **`www.` và tên miền trần là hai bản sao, mỗi bên tự nhận là bản gốc.** Cả hai đều
  trả 200, và canonical do JS đặt bằng `window.location.origin` nên trang ở `www` khai
  canonical trỏ về chính `www`. Google thấy hai catalogue giống hệt và chia điểm.
  Trong mã đã cắt tiền tố `www.` ở cả hai đường (`canonicalOrigin()` và fallback của
  server), nhưng **chỗ sửa thật là 301 ở proxy** — chưa làm, xem `§8.0`.
- **Canonical giữ `?trang=N` thì phải giữ số trang *có thật*.** Máy chủ kẹp `?trang=99`
  về trang cuối rồi trả nội dung trang 1 — đúng với người đọc — nhưng địa chỉ vẫn là
  99 và canonical trỏ về 99. Mọi con số một crawler nghĩ ra thành một địa chỉ nữa tự
  xưng bản gốc của cùng mười hai căn. Mà hiện **không thành phố nào đủ 12 tin**, nên
  *mọi* `?trang=N` đều là bản sao. Giờ `City.jsx` `navigate(replace)` về trang thật và
  `ShellSeo` tự kẹp trước khi in canonical.
- **`[HttpGet]` không trả lời HEAD.** `HEAD /robots.txt` và `HEAD /sitemap.xml` cùng
  trả **404** trong khi GET trả 200 — và phần lớn công cụ soát SEO hỏi bằng HEAD, nên
  câu trả lời chúng nhận được đọc y như "trang này không có sitemap".
- **Ngày trong fixture là `date`, còn luật đọc theo giờ.** `unwired_acceptance.py` đặt
  `CheckOut = now() - interval '2 hours'`, nhưng cột ấy là `date` nên Postgres ép về
  **hôm nay**, và `ShieldService` đọc trả phòng là **12:00 UTC** của ngày đó. Trước
  12:00 UTC thì "đơn chưa trả phòng" → kịch bản 10 hỏng mỗi buổi sáng, đạt mỗi buổi
  chiều, và trông hệt như một test chập chờn. Cửa sổ đúng là `CheckOut@12:00` nằm
  trong `(now-24h, now]`.
- **Nhà mạng Việt Nam chặn `openstreetmap.org` ở tầng DNS, nên bản đồ trắng trơn với
  phần lớn khách.** Đo ngày 27/08/2026 trên `tile.openstreetmap.org`: Viettel
  (`123.23.23.23`) không phân giải, FPT (`210.245.0.100`) không phân giải, VNPT
  (`203.113.131.1`) trả **`127.0.0.1`** — bản ghi bị đầu độc, tức chặn có chủ đích chứ
  không phải sự cố. Cloudflare và Google phân giải bình thường, và **đó chính là lý do
  không ai phát hiện**: máy lập trình viên nào cũng đã đổi DNS. Khách dùng mạng nhà
  mặc định chỉ thấy một ô xám kèm vòng tròn đỏ. Trình duyệt báo `ERR_NAME_NOT_RESOLVED`
  chứ không báo lỗi ảnh — nhìn giống lỗi CSS hơn là lỗi mạng.
  Giờ `Maps.jsx` có `PROVIDERS`: **ArcGIS** (chính, tên đường tiếng Việt) và
  **`tile.openstreetmap.de`** (dự phòng), tự đổi sau 4 tile lỗi. **Carto đã thử và bỏ**
  — tile miễn phí của họ giờ đóng dấu chìm "API KEY REQUIRED" giữa ảnh, đọc như trang
  hỏng chứ không phải thiếu bản đồ, mà request vẫn trả **200**. Chọn nhà cung cấp tile
  thì phải **nhìn tận mắt một tile**, đừng tin mã 200.
- **Đổi tên repo thì runner tự-host không đổi theo** (đã đăng ký lại 02/09/2026).
  Sau khi `StayHost` thành `Staylio` (27/08/2026), `~/actions-runner/.runner` trên VPS
  vẫn ghi URL cũ và vẫn `online` — nhưng chỉ vì **GitHub tự chuyển hướng tên cũ sang
  tên mới**. Chuyển hướng ấy biến mất ngay khi có ai tạo repo mới trùng tên `StayHost`
  dưới cùng tài khoản, và khi đó deploy ngừng chạy mà không báo gì. Package GHCR thì tự
  bám theo repo mới (`ghcr.io/minhhung19872002/stayhost` vẫn đúng). Lúc đăng ký lại
  dính hai bẫy: `./config.sh remove` trả **404** vì nó gửi URL cũ trong `.runner` lên
  API — API **không** đi theo redirect, đúng cái redirect đang nuôi listener; và xoá
  `.runner`/`.credentials` rồi mà `config.sh` vẫn bảo "already configured" vì còn
  **`.runner_migrated`** (runner 2.337). Vì `set -e` dừng script ngay ở bước remove,
  cron đã gỡ tạm mà runner chưa đăng ký — đừng để remove là bước chặn. Công thức đúng
  ở `DEPLOY.md §3.2`.
- **GitHub có thể tạo workflow run trễ cả tiếng, và "chưa có run" không phải "hỏng".**
  Ngày 26/08, `gh api ".../actions/runs?head_sha=b30ebd5"` trả `total_count: 0` ngay
  sau khi push, rồi trả `0` lần nữa ở lần push kế tiếp. Workflow vẫn `active`, Actions
  vẫn `enabled`, không có `paths` filter, không phải `Unstick deploys` (nó chỉ đụng
  run đã queued ≥20 phút) — nên kết luận lúc đó là "GitHub bỏ qua push". **Sai.** Một
  tiếng sau, run cho `b30ebd5` xuất hiện lúc 16:53 và run cho `a8a1de3` lúc 16:55, cả
  hai `event=push`, cả hai `success`. Cái giá của việc kết luận vội là `b30ebd5` được
  **deploy hai lần** (một do kích thủ công, một do run push tới muộn) — không hại, vì
  cùng một image và runner chạy tuần tự, nhưng vô ích.
  Thứ đáng tin duy nhất là **prod đang chạy sha nào**, không phải danh sách run:
  `ssh hung@14.225.83.93 'docker inspect stayhost-web --format "{{.Config.Image}}"'`.
  Chỉ khi sha đó lệch và đã đợi đủ lâu thì mới `gh workflow run ci-cd.yml --ref main`.
- **`slice(0, 5)` để "lấy giờ" chỉ đúng với ngôn ngữ đặt giờ ở cuối.** Ô "Số liệu
  lúc" cắt năm ký tự đầu của `dateTime()`: tiếng Việt ra `22:42` (chuỗi là
  "22:42 26-08"), tiếng Nhật ra **`08/26`** vì `Intl` đặt ngày trước. Không có lỗi,
  không có test nào bắt được — chỉ là một nửa số người đọc thấy ngày ở ô ghi "lúc".
  Giờ có `clockTime()` trong `format.js`. Cắt chuỗi ngày giờ theo vị trí ký tự là
  hard-code định dạng của một ngôn ngữ.
- **`scripts/i18n_audit.py` quét cả comment, và không đọc được template literal.**
  Viết `t(`trong ${n} phút qua`)` thì nó thấy hai mảnh "trong" và "phút qua" rồi báo
  thiếu khoá — mà ghép ba mảnh như vậy **cũng sai thật** với tiếng Nhật/Hàn. Cách
  đúng là một khoá nguyên câu có `{}` và tự `.replace('{}', n)`: literal nên audit
  soát được, và mỗi thứ tiếng giữ trật tự của nó. Ngược lại, một ví dụ `t('...')`
  viết trong **comment** cũng bị đếm là khoá thiếu.
- **Quy tắc "đừng đổi identifier" cũng che mất chữ hiển thị.** Đổi tên thương hiệu
  bằng regex `StayHost(?![.\w])` — cốt giữ `StayHost.Domain` và `StayHostDbContext` —
  bỏ sót **166 dòng**: `StayHost.` cuối câu (dấu chấm câu, không phải namespace),
  `StayHost.org` ở footer, và **toàn bộ bản dịch tiếng Nhật/Hàn** (`\w` của Python
  khớp cả chữ CJK và Hangul, nên `StayHost残高`, `StayHost에서` đều thoát). Cách đúng
  là **kể tên thứ cần giữ**, đừng đoán theo ký tự liền sau. Tệ hơn: lần soát đầu ra
  "0 dòng còn sót" vì `grep -v "StayHost.Domain"` lọc theo **cả dòng output kèm đường
  dẫn**, mà mọi file trong `src/StayHost.Domain/` đều có chuỗi đó trong tên — nên nó
  giấu sạch thư mục lớn nhất. Soát tên trong repo thì lọc **nội dung**, đừng lọc dòng
  có lẫn đường dẫn.
- **"Có mã, có test" vẫn có thể là "không màn hình nào bấm được".** Soát ngày 28/08
  đi theo **vai** (khách, chủ nhà) thay vì theo mã của `docs/01`, và ra **12 chỗ hở**
  mà `PLAN.md` vẫn đếm là xong — vì mỗi cái *đều có* mã nguồn đầy đủ. Khách nộp giấy
  tờ mà **không ai duyệt được** (`TK-06`), chủ nhà **không có ô trả lời đánh giá**
  (`ĐG-07`), trải nghiệm tự đăng **không thêm được suất nào** nên không bán được vé
  (`MR-02`), quản trị **không tạo được mã giảm giá** nên ô "Mã giảm giá" của khách
  vĩnh viễn vô dụng (`TC-09`). Phép soát ra chúng là **hai phép trừ**: endpoint của
  controller trừ đi đường dẫn `lib/api.js` thật sự gọi, và hàm `api.js` trừ đi
  component thật sự dùng. Nhanh, và bắt được thứ mà đọc theo danh sách mã không bắt
  được. Xem `docs/PLAN.md §9.8`.
- **`?? 0` cho một khoá ngoại nullable là một lỗi đang chờ tới lượt.** Khi mở
  đường đặt chỗ cho khách không có tài khoản, `PaymentCompletion.ConfirmAsync`
  nhận `int guestUserId` và **bốn** chỗ gọi truyền `booking.GuestUserId ?? 0`.
  Không có ngoại lệ nào: nó đi tra `Users` tìm id 0, không thấy ai, rồi **im lặng
  không gửi gì** — nên khách ẩn danh trả tiền xong không nhận được thư nào, mà mã
  đơn trong thư là đường duy nhất quay lại đơn. Đổi tham số thành `int?` là
  compiler chỉ ngay ra cả bốn. `grep "?? 0"` cạnh một khoá ngoại nullable đáng
  chạy trước khi tin là xong.
- **Fixture tự bịa dữ liệu đầu vào chỉ kiểm được luật với chính nó.** Kịch bản
  `TK-06` gửi thẳng ba đường dẫn `/uploads/…` cho `/api/account/identity` vì đó là
  thứ `Profiles.IsOwnUpload` muốn — và nó xanh. Nhưng ảnh giấy tờ đã chuyển ra
  ngoài `wwwroot` từ lâu, `UploadsController` trả `/api/identity-files/…`, nên
  **mọi lần nộp thật đều bị từ chối** bằng "Cần ảnh mặt trước giấy tờ" — nghe như
  lỗi tấm ảnh của khách. `IdentityChecksTests` cũng đóng đinh đúng cái sai đó bằng
  ba hằng số `/uploads/`. Kịch bản nghiệm thu phải đi qua **đúng đường mà người
  dùng đi**, kể cả khi đường đó dài hơn hai dòng.
- **Ghi bút toán không phải là cộng số dư.** `PriceMatchController` duyệt bù chênh
  lệch thì cộng `booking.GoodwillCredit` và ghi `Ledger.GrantCredit` — nhưng số dư
  tiêu được là tổng các `CreditEntry` (`WalletService.BalanceAsync`), và không dòng
  nào được tạo. Sổ nói khách có tiền, ví nói không, thông báo thì đã hứa "đã được
  cộng vào số dư của bạn". **Sổ vẫn cân nên không có gì kêu.** Cấp số dư thì phải đi
  qua `wallet.Add` — cũng chính là chỗ đóng dấu hạn dùng của `docs/07 §16`.
- **Giờ trong lịch lặp cũng là giờ của người làm.** `ExperienceRules.ExpandRecurrence`
  đóng dấu `DateTimeKind.Utc` lên giờ chủ nhà gõ, nên chọn 09:00 thì suất rơi vào
  16:00 giờ Việt Nam — đúng bảy tiếng đã làm hỏng picker dịch vụ. `Experience.TimeZoneId`
  nằm sẵn trên entity từ đầu mà chưa ai dùng, y hệt lần trước. Khi một quy tắc đọc
  **mặt đồng hồ** chứ không phải một mốc thời gian, hỏi ngay "đồng hồ của ai?".
- **Đổi tên miền thì DB đang chạy không đổi theo.** Email tài khoản seed nằm trong
  `DbSeeder`, mà seeder chỉ chạy trên DB trắng — bản prod vẫn giữ `admin@stayhost.vn`
  cũ sau khi deploy. Đường đổi tài khoản quản trị là `ADMIN_EMAIL`; các tài khoản
  demo còn lại thì đổi bằng `UPDATE` hoặc chấp nhận giữ nguyên.
- **Sổ cân bằng không có nghĩa là tiền có thật.** Bán thẻ quà tặng ghi
  `Nợ GuestFunds / Có GiftCardLiability` — một bút toán **khẳng định** tiền đã vào
  két — mà đường mua **không gọi cổng thanh toán nào**: không `Payment`, không
  `PaymentSession`, không `PspCheckout`. Ai đăng nhập cũng tạo được thẻ 20 triệu,
  đổi ra số dư và tiêu vào một đơn thật của một chủ nhà thật. Không có gì kêu, vì
  mọi cái chuông đều quay hướng khác: hai vế bút toán tự cân nên đối soát ngày của
  `docs/07 §5` đọc ra 0, còn thẻ quà tặng **không sinh `GatewayCharge`** nên nó
  không nằm ở vế nào của `§7`. Sổ không phải là không thấy — nó đang **lặp lại một
  lời khai chưa ai kiểm**. Trước một bút toán ghi "tiền đã vào", hỏi **ai đã trả**,
  và đi tìm lời gọi cổng chứ đừng tìm dòng bút toán.
- **Câu hỏi tìm ra nó không phải "mã này có ai gọi không" mà "thứ này ai *tạo* ra".**
  Tám lượt soát trước đều đi theo hướng thứ nhất và không thấy: `TC-08` có đủ
  `GiftCard`, `WalletController`, `CreditRules`, và `grep TC-08` ra kết quả. Đếm
  **producer** của một năng lực — bao nhiêu chỗ trong mã thực sự sinh ra nó — rẻ và
  bắt được đúng loại lỗ hổng này. `NotificationKind.StayReminder` có **0 producer**
  là cách nhanh nhất thấy cả hàng nhắc trước ngày nhận phòng của `docs/03 §11`
  chưa từng chạy.
- **Sửa một lỗi cho một dòng sản phẩm thì phải hỏi hai dòng kia.** `0e8a10e` đổi
  `Card.jsx` thành `<a href>` thật vì "Google chỉ đi theo liên kết" — và dừng ở đó.
  `Experiences.jsx`, `Services.jsx` và `Browse.jsx` vẫn là `<button onClick>`, nên
  **trải nghiệm và dịch vụ nằm trong sitemap với thẻ chia sẻ đúng mà không một liên
  kết nào trỏ tới**. Cùng một lỗi, ở hai dòng không ai soát lại. Ba thẻ, sửa một.
- **Bản vá đúng có thể làm lộ ra một lỗi nặng hơn — đó là phần có ích.** `lib/seo.js`
  đọc head lúc nạp module rồi gọi đó là `DEFAULTS`; từ khi `ShellSeo` thay head theo
  từng địa chỉ thì "mặc định" thành ra **chính trang khách vào đầu tiên**, nên mở một
  tin rồi đi tiếp là tiêu đề và ảnh của tin đó theo sang trang sau. Sửa cho `DEFAULTS`
  trung thực xong thì **tệ hơn**: `resetPageMeta()` chạy ở lần render đầu và ném đi
  đúng thẻ máy chủ vừa gửi — vĩnh viễn với `/experiences/:slug`, `/services/:slug`,
  `/help/:slug` vì chúng không có `setPageMeta` nào. Lỗi cũ đang **che** lỗi này.
  Giờ lần đi đầu tiên không reset: tài liệu vừa tới đã mang sẵn thẻ của địa chỉ đó.
- **`cp1258` là codepage tiếng Việt nhưng không viết được tiếng Việt dựng sẵn.** Nó
  đánh vần bằng **dấu tổ hợp**, còn server trả chữ NFC, nên `print()` chết ở đúng
  những ký tự nó sinh ra để phục vụ. `unwired_acceptance.py` vì thế ra **10/13** trên
  một sản phẩm đúng: `ok()` ghi verdict rồi mới `print`, nên verdict thật *đã* vào
  `results`, `print` ném, kịch bản đứt, và `main()` ghi thêm một FAIL ma nữa — 13 dòng
  cho 10 kịch bản. Ai chỉ đọc con số sẽ đi truy ba luật không hỏng. Cả chín script giờ
  ép `sys.stdout` sang UTF-8 trước khi in.
- **Đổi tên thương hiệu: kiểm ở ngôn ngữ mình không đọc.** `f36fea2` khẳng định đã quét
  sạch "StayHost"; `grep` ra **146 chỗ** còn lại. Luật bảo vệ viết là "StayHost theo sau
  bởi dấu chấm và một định danh", mà mọi chuỗi sót lại đều **kết câu bằng thương hiệu**
  (`…hãy liên hệ hỗ trợ StayHost.`) — dấu chấm hết câu trông y như dấu chấm của mã. Nặng
  hơn: **135 chỗ nằm trong 8 từ điển dịch**, đã đổi *khoá* mà không đổi *giá trị*
  (`'Số dư Staylio': 'StayHost残高'`), nên tên đúng ở tiếng Việt và sai ở bảy thứ tiếng
  còn lại — đúng thứ tiếng mà người đi kiểm đang đọc.

- **Xoá file migration bằng tay thì snapshot không quay lại theo.**
  `StayHostDbContextModelSnapshot.cs` vẫn giữ trạng thái của bản vừa xoá, nên
  `migrations add` lần sau chỉ sinh **phần chênh** — bản `CoHostRevenueShare` đầu
  ra **55 dòng và không có lệnh `CreateTable` nào**. Chạy lên thì app xanh, migration
  "đã áp dụng", và cái bảng ấy không bao giờ tồn tại. Cách đúng là
  `dotnet ef migrations remove`, hoặc `git checkout` snapshot rồi mới sinh lại; và
  **đếm số dòng của migration mới** — một bảng mới không thể gói trong 55 dòng.
- **Bộ nghiệm thu gửi GET vào endpoint POST đọc y như route hỏng.** Hàm `call()`
  của mọi script ở đây mặc định `GET` khi không có body, nên
  `POST /api/host/co-hosts/{id}/accept` gọi thiếu `m="POST"` rơi xuống fallback và
  trả **404 rỗng**. Mất một vòng dài đi truy "vì sao route không khớp" — trong khi
  route vẫn đúng. Đây là **cùng cái bẫy** `acceptance.py` từng mắc (§4 ở trên), chỉ
  khác là hồi đó shell trả 200 nên nó *đạt*, còn giờ `/api/` trả 404 nên nó *hỏng*.
  Bật log `Logging__LogLevel__Microsoft.AspNetCore=Debug` rồi đọc dòng
  `Request starting HTTP/1.1 **GET** …` là thấy ngay; đoán thì không bao giờ thấy.
- **Đừng ghi bút toán lúc ghi nhận một khoản nợ.** Nợ không phải một lần tiền đi.
  Bút toán thuộc về **lần chuyển tiền thu hồi được nó** — đó là chỗ
  `Ledger.RecoverFromHost`/`RecoverFromCoHost` được gọi. Ghi thêm một cái lúc phát
  sinh nợ là đếm hai lần, và sổ hết khớp với thứ ngân hàng đã làm. Chargeback thua
  và phí của đơn trả tại nơi ở đều đã có sẵn hình dạng này.
- **Một trang lỗi hợp lệ về mặt JSON đọc thành "khách chưa trả tiền".** Cả ba provider
  đều bọc lời gọi trong `catch` rồi hạ xuống *chưa biết* — đúng tinh thần `docs/07 §5`.
  Nhưng lưới ấy **chỉ bung khi parse hỏng**, nên một lỗi HTTP có thân đúng dạng JSON
  (`{"message":"Not Found"}` là kiểu rất thường của proxy và API gateway) lọt qua sạch
  sẽ, mọi trường đọc ra rỗng, rồi rơi xuống đáy `QueryAsync` thành
  **`PaymentSessionStatus.Failed`**. Đo được trước khi vá: **404 kèm thân JSON** và
  **502 từ proxy** đều ra `Failed`; hai ca HTML thoát nạn chỉ vì parser ném. Mà `Failed`
  không phải một từ trung tính — `PspSweeper` là một trong ba đường chốt một lượt thanh
  toán, nên nó quyết định giữa "hỏi lại phút sau" và "lượt này hỏng", cho một người có
  thể đã trả tiền thật. Giờ `GatewayReply.JsonAsync` kiểm status trước khi đọc thân, và
  `OnePayProvider.AskAsync` cũng vậy — `ParseQuery` **không bao giờ ném**, nên trang HTML
  ở đó còn tệ hơn: nó trả về một dictionary rác trông như một câu trả lời.
  Đi kèm là nửa thứ hai: `ReadFromJsonAsync` trên trang lỗi chỉ nói
  *"'<' is an invalid start of a value"* — không nêu mã trạng thái, không nêu cổng nào,
  đúng hình dạng đã làm mất một ngày với cái 403 vì thiếu `User-Agent` của VNPay.
- **`payload.title` không phải chữ của sàn, mà là chữ tiếng Anh của ASP.NET.** `api.js`
  mở đầu bằng lời hứa "failures surface the server's Vietnamese message", rồi đọc
  `payload?.message || payload?.title || 'Yêu cầu thất bại…'`. Trên **mọi** `NotFound()`
  trần, ASP.NET đáp `application/problem+json` với `"title":"Not Found"` — reason phrase
  của RFC 9110. Không controller nào ở đây tự đặt `title` bao giờ, nên nhánh ấy **chỉ có
  thể** đặt một từ tiếng Anh lên trang tiếng Việt, và vì đứng trước nên nó **che luôn câu
  dự phòng ngay dưới**. Có **114 chỗ** trả lỗi trần đi tới được nó. Lộ ra ở chỗ đắt nhất:
  khách vừa rời cổng thanh toán về, trang kết quả ghi *"Chưa thanh toán được · Không đọc
  được đơn · **Not Found**"*. `i18n_audit.py` không bắt được vì nó chỉ soát literal trong
  `t()`. Giờ chỉ `message` — thứ viết cho người đọc — được dùng, còn lại là câu tiếng Việt
  theo mã trạng thái.
- **Bộ nghiệm thu bỏ qua kịch bản trong im lặng thì tệ hơn bộ báo hỏng.** Mục 4 và 5 của
  `gateway_acceptance.py` đều treo vào một đơn ZaloPay mở được, canh bằng
  `if live.get("zalopay") and zalo_ref:` **không có `else`**. Hôm sandbox ZaloPay chết,
  hai mục in tiêu đề, chạy **không một khẳng định nào**, và tổng vẫn đọc như một lượt
  chạy đủ — thiếu **sáu** khẳng định mà con số vẫn hợp lý. Giờ có `skip()`: mỗi kịch bản
  không chạy được đều **được gọi tên** và đếm riêng, nên tổng không thể tụt mà không nói.
  Cùng một phép ấy áp cho `onepay_acceptance.py`.
- **Phân biệt "cổng từ chối" với "sàn ghi sót" phải hỏi cổng, đừng đọc trình duyệt.**
  Lượt đầu định bắt lỗi 3DS của OnePay bằng URL trang lỗi; chuỗi redirect của họ **đổi
  theo từng lượt**, trang lỗi lại tự chuyển tiếp sau `fail_delay` giây nên biến mất trước
  khi kịp đọc `page.url`, và `AgainLink` cũng trỏ về chính host của sàn nên vòng chờ
  thoát sớm với một địa chỉ trông như đã quay về. Tín hiệu chắc chắn nằm ở
  **`payment_sessions.ResponseCode`** — phán quyết của chính cổng — và phải **chờ** nó,
  vì câu trả lời có thể tới bằng vòng quét `docs/07 §5` chứ không theo chân khách. Cột đó
  tên là `ResponseCode`, **không phải `Code`**: đoán sai tên cột thì cờ luôn luôn tắt và
  bộ nghiệm thu lặng lẽ chạy nhánh sai.
- **Thanh tìm kiếm cho chọn thú cưng, mà số ấy không bao giờ tới được máy chủ.**
  `totalGuests()` là `adults + children`, và cả `searchParams()`, lời gọi trang chi
  tiết lẫn `loadHome()` đều chỉ gửi `guests`. `/api/quote` thì nhận đủ
  `adults/children/infants/pets`. Nên `Pricing` luôn thấy `Pets = 0` ở ba mặt đầu và
  thấy đúng ở mặt thứ tư: khách chọn "2 khách, 1 thú cưng" đọc **3.512.586₫** trên mọi
  thẻ và trên đầu trang chi tiết, rồi khung đặt ngay bên dưới ghi **3.758.826₫**. Đúng
  cái `docs/00 §6.8` cấm, và comment ngay trên `searchParams` còn viết "price every card
  with the same engine checkout uses" — lời hứa ấy đã sai từ lúc viết. Thẻ **có** tính
  phí dọn dẹp, phí dịch vụ, thuế và phí khách thêm, nên đây là bỏ sót chứ không phải
  chủ ý. Kịch bản `1b` của `acceptance.py` giờ neo cả hai vế: ba con số bằng nhau **và**
  phí thú cưng thật sự được tính — vá bằng cách bỏ luôn phí thì nó vẫn hỏng.
  Đi kèm hai điều đáng nhớ: `/api/listings` **không nhận `adults`** trong khi `/api/quote`
  **có**, nên cùng một chuỗi truy vấn cho hai con số khác nhau; và bảng tổng của
  `acceptance.py` đóng cứng `/10`, thêm một kịch bản là nó in "11/10". Giờ tổng tự đếm.
- **Phép soát của chính mình cũng phải kiểm là nó có soát gì không.** Lượt đầu đối chiếu
  giá ba mặt dùng nhầm tên trường (`totalForStay` thay vì `stayTotal`, và trang chi tiết
  không có khoá `quote`), nên hai trong ba giá trị là `None`, bị lọc đi, và script báo
  **"223 tin · 0 lệch"** trong khi thật ra nó **so một con số với chính nó**. Cùng hình
  dạng với `bookable()` gọi dry-run rồi bỏ qua kết quả. Sau khi bắt buộc đủ cả ba nguồn
  mới đếm thì ra ngay 90 chỗ lệch. Một phép soát ra 0 thì hỏi trước: nó đã thật sự so
  được bao nhiêu thứ?

---

## 5. Chạy dự án

```bash
docker compose up -d db                                   # Postgres cổng 5544
dotnet run --project src/StayHost.Web --urls http://localhost:5199
cd src/StayHost.Web/ClientApp && npm run dev              # dev frontend, cổng 5273
docker compose up -d --build                              # tất cả, web: http://localhost:8090
```

**Reset DB khi đổi schema:**
```bash
docker exec stayhost-db psql -U stayhost -d stayhost -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
```

### Tài khoản demo (mật khẩu `stayhost123`)
`guest@staylio.vn` · `host1@staylio.vn` … `host10@staylio.vn` · `admin@staylio.vn`
`khach1@staylio.vn` … `khach6@staylio.vn` — sáu khách đã ký tên dưới các đánh giá
trải nghiệm và dịch vụ được seed sẵn (`ReviewSeeder`).

**Tài khoản admin bắt buộc có bảo mật 2 lớp** (`docs/08 §3`, không có ngoại lệ), nên
đăng nhập admin đi qua hai bước. Chạy server với `ASPNETCORE_ENVIRONMENT=Development`
thì API trả luôn mã trong `devCode`; đó cũng là điều kiện để
`scripts/admin_acceptance.py` chạy được.

### Máy dịch (`TĐ-03`, `TN-06`) — tự host, không cần khoá API

`docker compose up -d` đã kéo luôn `libretranslate` và trỏ app vào đó, nên bản chạy
bằng compose **có sẵn nút "Dịch"**, không phải mua gì. Đủ **8 thứ tiếng**, đúng bằng
danh sách giao diện cho chọn.

Chạy app bằng `dotnet run` thì trỏ tay vào container (compose đã publish cổng 5555
ở loopback):

```bash
docker compose up -d libretranslate
dotnet run --project src/StayHost.Web --urls http://localhost:5199
# với Translation__Provider=libretranslate Translation__Url=http://localhost:5555
```

Không cấu hình gì thì `/api/translate/config` trả `enabled:false` và giao diện **không
đổi gì** — đó là mặc định đúng, không phải lỗi. Muốn chất lượng cao hơn thì đổi
`Translation__Provider=google` + `Translation__ApiKey` (biến môi trường, không để trong
`appsettings.json`).

### Cổng thanh toán thật (`docs/07 §13`, `§15.3`)

`appsettings.Development.json` đã có sẵn khoá **sandbox công khai của MoMo và
ZaloPay** (do chính họ công bố trong tài liệu, chỉ chạy tiền giả), nên chạy với
`ASPNETCORE_ENVIRONMENT=Development` là hai ô ví **đi ra cổng thật** ngay:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web --urls http://localhost:5199
python scripts/gateway_acceptance.py            # 19 kịch bản, gọi sandbox thật
```

**Hai ô thẻ đi qua VNPay và khoá nằm ngoài repo.** `HashSecret` của VNPay là của
một người chứ không phải khoá vendor công bố, nên nó ở `dotnet user-secrets`
(`UserSecretsId` = `stayhost-web-psp`), tự nạp ở Development:

```bash
cd src/StayHost.Web
dotnet user-secrets set Psp:Vnpay:TmnCode <mã website>
dotnet user-secrets set Psp:Vnpay:HashSecret <chuỗi bí mật>
```

Máy mới thì đăng ký ở **`https://sandbox.vnpayment.vn/devreg/`** — đường dẫn
`devreg` là một phần của địa chỉ, **gõ mỗi tên miền thì ra 404**. Không có khoá
thì `/api/payment-methods/catalogue` trả `live: false` cho ô đó và mọi thứ chạy y
như trước — đó là mặc định đúng, không phải lỗi.

Lên prod: đổi ba `Endpoint`/`PayUrl`/`ApiUrl` sang bản không có `sandbox`/`test`,
đặt `Psp:PublicUrl` thành tên miền thật (IPN phải gọi vào được), và **bí mật đặt
bằng biến môi trường** — `Psp__Vnpay__HashSecret`, `Psp__Momo__SecretKey`,
`Psp__Zalopay__Key1`, `Psp__Zalopay__Key2`.

### Thẻ thử nghiệm
Mọi thẻ đều thành công, **trừ thẻ kết thúc `0000`** — thẻ đó luôn bị từ chối. Đó là cách
duy nhất chạy được nhánh "thu lần hai thất bại" của `docs/03 §1`.

Thẻ kết thúc **`0002`** luôn đòi OTP (`docs/07 §5`), mã đúng là **`123456`**. Trang ngân
hàng là một chặng riêng — `POST /api/bookings/{id}/bank-otp` — vì ngoài đời nó là một
trang khác: ngân hàng trừ tiền ở đó rồi sàn mới biết. Gọi chặng đó xong mà **không** quay
lại `/pay` chính là kịch bản 3 của `docs/07 §18`; `CardAuthSweeper` hỏi lại cổng thanh
toán rồi tự xác nhận đơn.

Thẻ kết thúc **`0009`** nhận tiền bình thường nhưng **luôn trả lại khoản hoàn** — nó đóng
vai "thẻ đã hết hạn hoặc đã đóng" của `docs/07 §10`. Huỷ một đơn trả bằng thẻ này thì tiền
**vào số dư Staylio** thay vì về thẻ, và khách được báo. Đây là cách duy nhất chạy được
nhánh `Refunds.Redirect`, thứ vốn không có sự kiện nào sinh ra vì cổng mô phỏng **không
hề có hàm hoàn tiền** cho tới 16/08/2026.

> **API nhận `cardLast4`, không nhận số thẻ.** `PayBookingRequest` chỉ có bốn số cuối và
> mặc định `"4242"`, nên gửi `cardNumber` đầy đủ thì trường đó bị bỏ qua và **mọi thẻ thử
> nghiệm đều thành thẻ thường**. Đã mất một lượt chạy vì chuyện này.

### Đăng nhập Google / Apple / Facebook (`docs/01 TK-02`)

Nút của nhà cung cấp nào **chưa có mã thì không hiện** trên hộp đăng nhập — thà thiếu nút
còn hơn có nút bấm vào không chạy. Điền vào `ExternalLogin:` trong `appsettings.json`,
riêng `FacebookAppSecret` đặt qua biến môi trường `ExternalLogin__FacebookAppSecret`.

| Khoá | Lấy ở đâu |
|---|---|
| `GoogleClientId` | console.cloud.google.com → Credentials → OAuth client ID (Web). Khai **Authorised JavaScript origins**: `https://staylio.vn` và `http://localhost:5199` |
| `AppleServicesId` + `AppleRedirectUri` | developer.apple.com → Services ID. Return URL phải **trùng từng ký tự** với `AppleRedirectUri`. Cần tài khoản Apple Developer trả phí |
| `FacebookAppId` + `FacebookAppSecret` | developers.facebook.com → App → Facebook Login for Web |

Máy chủ **không tin gì trình duyệt gửi lên**: token của Google/Apple được kiểm chữ ký
RS256 theo bộ khoá công khai của chính họ (`ExternalTokenVerifier`), token Facebook được
đem hỏi lại Graph `debug_token`. Email chưa được nhà cung cấp xác thực thì **không**
được phép ghép vào tài khoản sẵn có.

---

## 6. Kiểm chứng trước khi commit

```bash
dotnet test tests/StayHost.Domain.Tests            # 1223 test nghiệp vụ
dotnet test tests/StayHost.Web.Tests               # 7 test cổng thanh toán (GatewayReply)
python scripts/acceptance.py                       # 11 tình huống của docs/04
python scripts/admin_acceptance.py                 # 10 tình huống của docs/08 §13
python scripts/doc09_acceptance.py                 # 19 kịch bản của docs/09
python scripts/unwired_acceptance.py               # 10 quy tắc từng không ai gọi (PLAN §9.6)
python scripts/rolegaps_acceptance.py              # 14 chỗ hở của hai lượt soát vai (PLAN §9.8, §9.9)
python scripts/guestcheckout_acceptance.py         # 12 kịch bản của docs/07 §2.5
python scripts/cohost_share_acceptance.py          # 31 kịch bản chia thu nhập co-host (docs/07 §19)
python scripts/gateway_acceptance.py               # 30 kịch bản cổng thanh toán, gọi sandbox thật
python scripts/payout_acceptance.py                # 34 kịch bản chuyển tiền + đối chiếu sao kê (docs/07 §15.4)
python scripts/vnpay_browser_acceptance.py         # 14 kịch bản: trả tiền THẬT trên trang VNPay (cần playwright)
python scripts/refund_acceptance.py                # 11 kịch bản hoàn tiền thật qua VNPay (docs/07 §15.6)
python scripts/onepay_acceptance.py                # 15 kịch bản: trả bằng thẻ VISA THẬT qua OnePay
                                                   # (chạy app với Psp__Methods__card=onepay)
python scripts/preferences_acceptance.py           # 7 kịch bản TK-09: tuỳ chọn trên tài khoản,
                                                   # thiết bị mới đọc lại được
python scripts/email_language_acceptance.py        # 7 kịch bản TK-09: email theo ngôn ngữ người
                                                   # đọc (cần libretranslate cho kịch bản dịch máy)
python scripts/fx_acceptance.py                    # 7 kịch bản QT-06/TC-12: tỉ giá trong DB,
                                                   # đơn đặt đóng băng tỉ giá của sàn
python scripts/settings_acceptance.py              # 8 kịch bản docs/02 F1: trang cài đặt,
                                                   # lịch sử trả tiền đối chiếu DB
python scripts/hostreport_acceptance.py            # 8 kịch bản docs/02 G7: báo cáo chủ nhà,
                                                   # đối chiếu thẳng với DB
python scripts/giftcard_acceptance.py              # 8 kịch bản TC-08: thẻ quà tặng chỉ có giá trị
                                                   # khi đã có người trả tiền
python scripts/seo_acceptance.py                   # 10 kịch bản SEO: mã trạng thái, thẻ chia sẻ,
                                                   # sitemap, và liên kết thật vào cả ba dòng
                                                   # (kịch bản 10 cần playwright)
python scripts/i18n_audit.py                       # khoá dịch còn thiếu (phải ra 0)
cd src/StayHost.Web/ClientApp && npm run build && npx oxlint src

# Sổ sách phải luôn cân bằng: kết quả duy nhất chấp nhận được là 0
docker exec stayhost-db psql -U stayhost -d stayhost -t \
  -c 'select coalesce(sum(case when "Direction"=1 then "Amount" else -"Amount" end),0) from ledger_entries;'
```

---

## 7. Quy ước

- **Giao tiếp với khách bằng tiếng Việt.** Code, comment, commit message bằng tiếng Anh.
- Nội dung hiển thị trên UI: **tiếng Việt**.
- Mọi quy tắc tính tiền chỉ định nghĩa **một lần** trong `StayHost.Domain/Pricing.cs`
  (`docs/00 §6.8`) — tìm kiếm, trang chi tiết và thanh toán phải ra **cùng một con số**.
- Sổ sách: mọi khoản tiền ghi hai chiều, **bất biến**, không sửa không xoá (`docs/05`).
- Số dư khách cũng là sổ chỉ-thêm: số dư là tổng các dòng, không phải một cột bị ghi đè.
- **Staylio Shield không bao giờ được gọi là bảo hiểm** (`docs/06 §11`). Mọi chữ hiển thị
  là "chính sách hỗ trợ". Có `Shield.ReadsAsInsurance` và test chặn từ ngữ này.
- **`docs/PLAN.md §9` đã soát đủ cả 203 mã: 203 xong · 0 một phần · 0 chưa có**
  (soát 07/08/2026, dọn nốt 10/08/2026). Hai việc từng "chờ khách quyết" đã xong
  ngày 11/08/2026: `TĐ-03`/`TN-06` chạy bằng máy dịch tự host, `TC-07` chốt tham số
  ở `docs/07 §16`. Plan đã ba lần đếm lệch (hai lần bỏ sót việc thật, một lần kê
  tám mã đã xong vào bảng "làm một phần"), nên thêm mã mới thì sửa §9.1/§9.2 ngay
  lúc đó.
- Kiểm chứng bằng app đang chạy thật, không chỉ đọc code.
- Commit theo từng mốc có nghĩa, push lên `origin main`.

Remote: **https://github.com/minhhung19872002/Staylio**

---

## 8. Làm tiếp từ đâu (chốt cuối phiên 17/08/2026)

Cả ngày 17/08 làm **tiền thật**: nối ba cổng thanh toán, chuyển tiền cho chủ nhà,
token hoá thẻ, hoàn tiền, và đưa bồi thường ra khỏi sàn theo quyết định của khách.
Tám commit, từ `a8ee0a3` tới `904cce2`. Mọi bộ nghiệm thu xanh.

### 8.0. Đổi tên miền sang `staylio.vn` (26/08/2026)

Khách chốt bỏ `staylio.bluestar.com.vn`, và bỏ luôn domain email `stayhost.vn`.
Trong repo đã đổi hết: tài khoản seed (`guest@staylio.vn`…), `Email:FromAddress`,
User-Agent gọi VNPay, mọi script nghiệm thu, `DEPLOY.md`, `README.md`.

**Đã làm xong trên máy chủ** (deploy `764160c` lúc 26/08, container `healthy`):

- Bản ghi A của `staylio.vn` và `www` (P.A Việt Nam) trỏ về `14.225.83.93`. Trước đó
  cả hai là `127.0.0.1` — bản ghi mặc định của nhà đăng ký, **không** phải chưa cấu hình.
- `proxy/sites/stayhost.caddy` phục vụ `staylio.vn` + `www`; Caddy đã xin xong chứng
  chỉ Let's Encrypt cho cả hai (hạn 24/11/2026). **`staylio.bluestar.com.vn` đã gỡ hẳn
  theo yêu cầu của khách** — giờ nó không trả lời gì, ai còn giữ đường dẫn cũ thì hỏng.
  Sao lưu ở `stayhost.caddy.bak-truoc-go-ten-mien-cu`, muốn khôi phục là thêm lại tên
  đó vào đầu khối rồi `caddy reload`.
- `~/deploy/stayhost.env`: `PSP_PUBLIC_URL=https://staylio.vn` (bản sao lưu
  `stayhost.env.bak-truoc-doi-ten-mien-20260826`), đã vào container — kiểm bằng
  `docker exec stayhost-web printenv Psp__PublicUrl`. Đây cũng là địa chỉ đặt trước
  link trong email, vì `Site:PublicUrl` rơi về nó khi để trống.

**Máy chủ không giống tài liệu cũ.** `DEPLOY.md` từng ghi IP `45.119.215.96` và TLS
bằng nginx + certbot. Thật ra là `14.225.83.93`, hostname `bluestar01`, và cổng 443 do
**một container Caddy dùng chung với `bluedental`/`blueidea`/`foodsafe`/`starlab`** giữ
— host **không có** `/etc/nginx` lẫn `/etc/letsencrypt`. Ai tin tài liệu cũ mà chạy
`deploy/setup-nginx.sh` sẽ giành cổng 443 và **làm sập cả năm dự án**. Đã sửa
`DEPLOY.md`: cảnh báo ở đầu, `§2.7` là cách thêm tên miền thật, `§4` đánh dấu bước nào
không áp dụng.

**Còn lại, đều nằm ngoài tầm với của repo:**

| Việc | Vì sao chưa xong |
|---|---|
| ~~Runner thành systemd service~~ | **Không làm được, và đã chốt bỏ (26/08).** `hung` **không nằm trong sudoers**, mà nhóm `sudo` trên máy **rỗng** — `bluestar01` xưa nay quản trị bằng root trực tiếp, và khách không giữ mật khẩu root. Đó cũng là lý do runner chưa bao giờ thành service: `DEPLOY.md §4` bảo `sudo bash deploy/setup-runner-service.sh`, tài khoản được cấp không chạy nổi dòng đó. Thay bằng **crontab của `hung`** — xem dưới |

| Google origins + Apple Return URL | Khai ở console của nhà cung cấp. Quên thì báo `origin_mismatch`, **không có log nào bên mình** |
| Địa chỉ website ở cổng VNPay/MoMo/ZaloPay/OnePay | Mỗi bên chặn IPN theo tên miền đã đăng ký |
| `EMAIL_HOST` | **Không có trong env file** — nên thư nằm im trong hàng đợi, kể cả mã 6 số đăng nhập quản trị. Tên miền mới đã có sẵn SPF + DKIM trỏ `maychuemail.com`, nên chỉ cần tạo hòm thư rồi điền SMTP |

**Prod đang chạy toàn khoá sandbox** (`VNPAY_TMN_CODE=GLQWM7J8`, MoMo `MOMOBKUN…`,
ZaloPay `2553`, OnePay `TESTONEPAY`) — chưa đồng nào là tiền thật, đúng như `§8.1`.

**Đã dọn trong DB prod (26/08):** 12 tài khoản demo đổi sang `@staylio.vn` bằng
`UPDATE` — `DbSeeder` chỉ chạy trên DB trắng nên deploy không tự đổi, mà hộp "Tài khoản
dùng thử" trên giao diện thì đã đổi rồi, để lệch là nút "Điền tài khoản chủ nhà" điền
vào một tài khoản không tồn tại. 77 thư chưa gửi cũng đã thay link `stayhost.vn` →
`staylio.vn`; để nguyên thì ngày bật `EMAIL_HOST` chúng bay đi kèm link chết.

**Mật khẩu quản trị prod đã đổi (26/08)**, không còn là `stayhost123`. Xem bài học ở
`§4`. Vẫn nên đặt `ADMIN_EMAIL` về hòm thư thật rồi bật lại `ADMIN_REQUIRE_2FA=true` —
nhưng **chỉ sau khi `EMAIL_HOST` chạy**, bật trước thì chính mình cũng không vào được.

### 8.0b. Soát theo vai khách & chủ nhà (28/08/2026)

Mười hai chỗ hở, tất cả là **mã hoàn chỉnh có test mà không màn hình nào gọi tới**;
đã vá hết trong ngày. Bảng đầy đủ ở `docs/PLAN.md §9.8`. Đáng nhớ nhất:

- **`TK-06`** chặn nhiều thứ hơn vẻ ngoài của nó: không ai duyệt được giấy tờ thì
  `IsIdentityVerified` không bao giờ bật, nên điều kiện "chỉ khách đã xác minh" của
  `ĐP-03`/`ĐP-10` đọc một cờ không đường nào đặt.
- **`MR-10`** hỏng cả ở tầng tiền, không chỉ ở giao diện — xem bài học ở `§4`.
- **Trang mời làm chủ nhà** hứa "bảo vệ tới 1 tỷ đồng cho thiệt hại tài sản" trong
  khi trần là 75 triệu/hồ sơ và quỹ **không chi cho hư hỏng** từ 17/08. Đã viết lại
  theo đúng `Shield.SettledAtCheckout` và `ShieldSettings`.
- **`QT-08`** giờ gác hai thứ có thật (`price-match`, `trip-plans`). Hai cờ được seed
  trước đó đặt tên cho hai tính năng chưa từng được xây, và **không mã nào đọc cờ**;
  chúng đã bị xoá khỏi bảng khi ứng dụng khởi động.

### 8.0c. Soát lại lần hai, theo `docs/02` (28/08/2026)

Bốn chỗ nữa, xem `docs/PLAN.md §9.9`. `TK-06` **hỏng ngay bước nộp** (bộ nghiệm
thu của lượt trước đã che mất — xem bài học ở `§4`); `TK-12` mới làm nửa "xoá",
thiếu hẳn nửa "tạm vô hiệu hoá"; đơn của chủ nhà và trang đánh giá đều thiếu cách
gom nhóm mà `docs/02 G6`/`H1` mô tả.

~~**Cố ý chưa làm:** chia % thu nhập cho co-host (`docs/02 G8`)~~ — **đã hỏi và
đã làm, 03/09/2026.** Xem `§8.0e`. Bốn việc nhỏ khác của `docs/02` vẫn liệt kê
trong `PLAN §9.9`.

### 8.0d. Đặt không cần tài khoản & trả tiền tại nơi ở (28/08/2026)

Khách yêu cầu, và nó **đảo lại `docs/07 §2.4`** — đã ghi thẳng vào tài liệu ở
`§2.5` kèm ngày, chứ không để mã nguồn nói khác tài liệu.

`Booking.SessionId` và đường nhận nuôi đơn khi đăng nhập **đã có sẵn từ đầu**;
chỉ mỗi `Create` chặn bằng một dòng 401. Ba thứ gắn với tài khoản (số dư, mã
giảm giá, ưu đãi riêng) bị **từ chối có nêu tên**; tin đăng bật yêu cầu của
`ĐP-10` thì không đặt ẩn danh được; điều kiện *Đặt ngay* của `ĐP-03` thì chuyển
đơn thành yêu cầu đặt như mọi khách chưa xác minh khác.

Trả tại nơi ở là **sàn đứng ngoài luồng tiền**: không bút toán, không có gì để
hoàn khi huỷ, phí ghi vào `OwedToPlatform`, thuế do chủ nhà tự nộp. Luật hoàn
tiền đặt trong **chính `Cancellation.Refund`** chứ không vá bảy chỗ gọi, và đọc
`Booking.PaidAtProperty` chứ không đọc `Payment.Method` — một nửa số chỗ gọi
không `Include` bảng payment, và luật im lặng không chạy vì thiếu navigation
đúng là loại lỗi repo này trả giá nhiều lần.

**Rủi ro đã ghi rõ trong `docs/07 §2.5`:** `OwedToPlatform` chỉ thu được nếu chủ
nhà còn đơn khác để trừ. Đây là rủi ro sẵn có của đường chargeback, dùng chung
một cơ chế thay vì đẻ ra cơ chế thứ hai.

### 8.0e. Chia thu nhập cho người đồng quản lý (03/09/2026)

Việc duy nhất mà lượt soát `docs/02` để lại ở diện "phải hỏi khách". Khách đối
chiếu với Airbnb rồi chốt **"làm giống nó luôn"**, nên quy tắc là của Airbnb,
ghi lại đầy đủ ở **`docs/07 §19`** kèm ngày — không để mã nguồn nói khác tài liệu.

Bốn điều đáng nhớ nhất:

- **Chia trên thu nhập thực nhận của chủ nhà**, tức sau phí dịch vụ 3% và không
  gồm thuế. Nghĩa là **`Pricing.cs` không bị đụng tới**: phí sàn không đổi, không
  chia; chủ nhà chịu trước rồi mới chia phần còn lại. Đây là lý do việc này nhẹ
  hơn nhiều so với ước lượng ban đầu.
- **Không bao giờ chia quá số một đơn kiếm được.** Hết thì người đứng sau nhận
  thiếu và chủ nhà có thể nhận 0. Nghe hà khắc, nhưng lựa chọn còn lại là tiêu
  tiền của người khác — đúng cái sai `GuestFunds` đã mắc một lần.
- **Co-host được trả như một chủ nhà**: hồ sơ nhận tiền riêng (dùng lại
  `EnsureHostProfileAsync` nếu họ vốn đã là chủ nhà), tài khoản ngân hàng riêng
  đã mã hoá, sổ nợ riêng, lệnh chuyển riêng — qua đúng `payout_batches`, đúng
  file `.csv` sáu cột, đúng nút *Đã chuyển*. Không có đường tiền thứ hai nào.
- **Hoàn tiền sau khi đã chia** thì thu lại qua `OwedToPlatform` của chính họ,
  và việc phát hiện là **một vòng quét** chứ không phải một cái móc vào
  `PostCancellation` — hàm đó tĩnh, gọi từ bảy chỗ, mà tiền còn hoàn được do
  admin phân xử, do Shield, do chargeback.

Nghiệm thu `scripts/cohost_share_acceptance.py` — **31/31**, gồm phép cộng
khẳng định *phần chủ nhà + phần co-host = đúng thu nhập của đơn* và ba lần kiểm
sổ lệch 0.

**Chưa làm, cố ý:** sàn không thu phí trên phần chia của co-host. Airbnb có thu
với co-host tìm qua mạng lưới của họ; ở đây chưa có mạng lưới nào, và khách chưa
yêu cầu.

### 8.1. Đang chờ khách

| Việc | Cần gì |
|---|---|
| **VNPay lên prod** | Hợp đồng chính thức + giấy phép kinh doanh. Hiện chỉ có sandbox (`TmnCode GLQWM7J8`, khoá nằm trong `dotnet user-secrets`, **không ở trong repo**) |
| **Quỹ có còn bù C3 (mất thu nhập) không** | Khách đã chốt bỏ quỹ cho C1/C2 (hư hỏng, dọn dẹp). **C3 và C4 em giữ nguyên quỹ vì khách chưa nói tới.** Nếu khách muốn bỏ luôn C3 thì sửa một dòng: `Shield.FundCovers` |
| **Bật MoMo/ZaloPay prod** | Hợp đồng riêng với từng bên. Sandbox của họ là khoá công khai, nằm trong `appsettings.Development.json` |

### 8.2. Việc còn lại, theo thứ tự

1. ~~Đối chiếu sao kê ngân hàng với lệnh chuyển tiền~~ — **xong 18/08/2026.**
   Người trực dán các dòng **chuyển đi**; dòng nào ngân hàng ghi đúng mã lệnh và
   đúng số tiền thì lệnh đó tự xác nhận qua **đúng lời gọi mà nút bấm dùng**, nên bút
   toán, thông báo và nhật ký giống hệt. Nó **chỉ xác nhận, không bao giờ đánh hỏng** —
   một lệnh vắng mặt trong sao kê hôm nay thường là sao kê chưa kịp, chứ không phải
   ngân hàng từ chối. Kết quả còn kèm **danh sách lệnh đã tải file mà ngân hàng chưa
   xác nhận**: đó là nửa mà sao kê không bao giờ nói ra. `PayoutStatements` +
   `PayoutStatementService`, khung dán ở `pages/admin/PayoutReconcile.jsx`.
2. ~~`token_remove`~~ — **xong 18/08/2026.** Endpoint là `token_ui/remove-token.html`,
   trả lời bằng query string server-to-server (không phải trang chuyển hướng). Đã kiểm
   thật: VNPay trả **mã 00**. `vnp_app_user_id` lúc xoá **phải trùng** lúc tạo —
   `Psp.AppUserRef` giữ định dạng đó một nơi, vì lệch là VNPay trả `11 token not found`,
   đúng cái mã mà thẻ đã xoá thật cũng trả, nên sai sẽ **im lặng**.
3. **Trả góp qua thẻ** (`docs/07 §2.3`, nhóm P2). Chưa ai yêu cầu.

### 8.3. Đừng làm lại những thứ này

- **Không có API tra cứu riêng cho giao dịch hoàn của VNPay.** `querydr` chỉ trả về
  **giao dịch thanh toán gốc** — nó báo `TransactionType=01, Status=00` kể cả sau khi
  đã hoàn. Bằng chứng hoàn tiền là **câu trả lời của chính lệnh refund**, đã lưu ở
  `payment_sessions.RefundCode` / `RefundTxnId`. Đã mất một vòng vì tưởng sản phẩm sai.
- **`token_pay` là một lần chuyển hướng**, không phải server-to-server. Nên nó **không
  dùng để thu tiền khi khách không có mặt** — và đó cũng là lý do bồi thường phải là
  tiền mặt tại chỗ (`§2` mục 3).
- Ba script cần Playwright (`vnpay_browser_acceptance.py`, `refund_acceptance.py` gọi
  nó, và kịch bản 10 của `seo_acceptance.py`). Dòng "đã cài trên máy này" ở đây **sai
  suốt từ lúc viết** — `python -m pip show playwright` không thấy gói nào, nghĩa là hai
  script cổng thanh toán cũng chưa từng chạy được trên máy này. Đã cài thật ngày
  05/09/2026:

  ```bash
  pip install playwright
  setx PLAYWRIGHT_BROWSERS_PATH D:\Users\pc\playwright-browsers   # 701 MB, C: gần đầy
  playwright install chromium
  ```

  Trình duyệt nằm ở **ổ D:** theo storage policy; biến `PLAYWRIGHT_BROWSERS_PATH` đã đặt
  ở mức User nên terminal mới tự có, còn terminal đang mở từ trước thì phải truyền tay.
  Không có biến đó thì Playwright đi tìm ở `%LOCALAPPDATA%` và báo "browser not found"
  dù gói đã cài.

### 8.4. Trước khi chạy lại

```bash
docker ps                      # stayhost-web đang DỪNG — cố ý, xem §4
docker compose up -d db
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web --urls http://localhost:5199
```

**Đừng bật lại container `stayhost-web`** cùng lúc với `dotnet run`: nó chạy bản
Release cũ trên **cùng một database** và cũng chạy vòng quét mỗi phút — xem bài học
ở `§4`. Mất một tiếng mới tin nổi chuyện đó.
