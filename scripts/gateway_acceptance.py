"""docs/07 §13 — the licensed gateways, against the real sandboxes.

Everything here talks to somebody else's server. It opens genuine orders at
VNPay, MoMo and ZaloPay, follows the addresses they hand back — VNPay's all the
way onto their own payment page, which is the only proof that every one of the
fifteen signed parameters was acceptable — and then asks each of them what it
thinks happened, the check docs/07 §5 says the platform must make rather than
believing the browser.

What it does not do is finish a payment: that needs a human with a card or the
MoMo app. So the confirming half is proved by signing a callback the way the
gateway signs it, which exercises the whole production path from the signature
check to the ledger. And the half that costs money if it is wrong — that an
unsigned callback confirms nothing and, just as importantly, breaks nothing —
is proved against a real pending order rather than an invented reference.

    STAYHOST_URL=http://localhost:5199 python scripts/gateway_acceptance.py

The server must run with ASPNETCORE_ENVIRONMENT=Development. MoMo's and
ZaloPay's sandbox keys are published by the vendors and live in
appsettings.Development.json; VNPay's belong to a person, so they live in
`dotnet user-secrets` and this script reads the HashSecret from there (or from
VNPAY_HASH_SECRET) only to sign the IPN it pretends to be. A gateway with no
keys reports itself not live and the run says so rather than failing.
"""
import datetime
import hashlib
import hmac
import http.cookiejar
import io
import json
import os
import random
import re
import shlex
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

# A Windows console runs cp1258 — the Vietnamese code page, and it spells
# Vietnamese with combining marks, so it cannot encode the precomposed letters
# the server actually sends. Any scenario that echoes a server message then dies
# inside print(), the runner writes it down as FAIL, and a correct product
# reports 10/13. Proven: the same run is 10/10 under PYTHONIOENCODING=utf-8.
# A verdict must never be lost to a character the terminal cannot draw.
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


BASE = os.environ.get("STAYHOST_URL", "http://localhost:5199").rstrip("/")
# Where the database lives, when the server under test is not this machine.
# Reading the local container while BASE points elsewhere reports on two
# databases at once — see vnpay_browser_acceptance.py for the same fix.
DB_SSH = os.environ.get("STAYHOST_DB_SSH")

HERE = os.path.dirname(os.path.abspath(__file__))
DEV_SETTINGS = os.path.join(HERE, os.pardir, "src", "StayHost.Web", "appsettings.Development.json")


USER_SECRETS = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~/.microsoft/usersecrets")),
    "Microsoft", "UserSecrets", "stayhost-web-psp", "secrets.json")


def dev_key2():
    """ZaloPay's callback key, as configured for this machine.

    Only a development script may do this. In production the key is an
    environment variable and nothing reads it back — the point of key2 is that
    only ZaloPay and the server know it.
    """
    try:
        with io.open(DEV_SETTINGS, encoding="utf-8") as f:
            text = re.sub(r'"//[^"]*"\s*:\s*\[[^\]]*\],?', "", f.read())
        return json.loads(text).get("Psp", {}).get("Zalopay", {}).get("Key2")
    except (OSError, ValueError):
        return None


def vnpay_secret():
    """VNPay's HashSecret, from wherever this machine keeps it.

    Unlike MoMo's and ZaloPay's, this one belongs to a person rather than being
    published by the vendor, so it lives in user-secrets outside the repo
    (`dotnet user-secrets`, see StayHost.Web.csproj). The environment wins, for a
    CI box that has no such store.
    """
    if os.environ.get("VNPAY_HASH_SECRET"):
        return os.environ["VNPAY_HASH_SECRET"]

    try:
        with io.open(USER_SECRETS, encoding="utf-8-sig") as f:
            return json.load(f).get("Psp:Vnpay:HashSecret")
    except (OSError, ValueError):
        return None


def vnpay_sign(fields, secret):
    """docs/07 §15.3 — sorted, URL-encoded, HMAC-SHA512, exactly as VNPay does it.

    Written out again here rather than imported, on purpose: a test that reuses
    the code under test proves the two agree with each other and nothing else.
    """
    query = "&".join(
        "%s=%s" % (urllib.parse.quote_plus(k), urllib.parse.quote_plus(v))
        for k, v in sorted(fields.items()) if v != "")
    return hmac.new(secret.encode(), query.encode(), hashlib.sha512).hexdigest()


def browser():
    """A caller that keeps cookies, which VNPay's payment pages require.

    Without a cookie jar their gateway hands out a token and then answers its own
    error page — which reads exactly like a rejected checksum and is not one.
    Cost a wrong diagnosis; hence the comment.
    """
    op = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    op.addheaders = [("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126")]
    return op


def ledger_total():
    """docs/00 §6.5 — every movement is written both ways, so this must be zero."""
    argv = ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost", "-t", "-c",
            'select coalesce(sum(case when "Direction"=1 then "Amount" else -"Amount" end),0) '
            'from ledger_entries;']
    if DB_SSH:
        argv = ["ssh", "-o", "BatchMode=yes", DB_SSH,
                " ".join(shlex.quote(a) for a in argv)]
    out = subprocess.run(argv, capture_output=True, text=True)
    try:
        return float(out.stdout.strip())
    except ValueError:
        return None

# The same ninety-minute shift acceptance.py uses, so two runs in one session do
# not fight over the same nights.
RUN_SHIFT = int(datetime.datetime.now().timestamp() // 5400 % 90)

passed, failed, skipped = [], [], []


def opener():
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(op, path, body=None, m=None, follow=True):
    method = m or ("POST" if body is not None else "GET")
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    try:
        with op.open(req, timeout=30) as res:
            raw = res.read().decode("utf-8", "replace")
            if not raw.strip():
                return res.status, None
            try:
                return res.status, json.loads(raw)
            except json.JSONDecodeError:
                # A return route redirects into the app, so the body is the page.
                return res.status, {"raw": raw[:200], "url": res.geturl()}
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", "replace")
        try:
            return e.code, json.loads(raw) if raw.strip() else None
        except json.JSONDecodeError:
            return e.code, {"raw": raw[:400]}


def check(name, ok, detail=""):
    (passed if ok else failed).append(name)
    print(("  PASS  " if ok else "  FAIL  ") + name + (" — " + detail if detail else ""))


def skip(name, why):
    """A scenario that was never attempted.

    Sections 4 and 5 both hang off a live ZaloPay order and were guarded by a
    bare "if ... and zalo_ref:" with no else. When ZaloPay would not open one,
    the two sections printed their headings, ran nothing, and the tally still
    read like a full run - six assertions short, silently. A suite that quietly
    covers less is worse than one that fails, because the number stays
    plausible. Skips are named here and counted in the summary.
    """
    skipped.append(name)
    print("  SKIP  " + name + " - " + why)



def future(days):
    return (datetime.date.today() + datetime.timedelta(days=days + RUN_SHIFT)).isoformat()


def body_for(listing, offset, nights, **kw):
    return {"listingId": listing["id"], "checkIn": future(offset),
            "checkOut": future(offset + nights), "guests": 1, "adults": 1,
            "children": 0, "infants": 0, "pets": 0, "guestName": "Khách Demo",
            "guestEmail": "guest@staylio.vn", "guestNote": None,
            "agreedToRules": True} | kw


def hold(op, method, nights=3, offset=60):
    """Dates actually taken off the market, not merely promised.

    A dry run answering 201 is not the same thing as a hold. On a database that
    has been run against many times the GiST constraint decides between two
    checkouts a moment apart, so the only honest test of "can this be booked" is
    to book it — and to keep looking when it cannot, rather than reporting the
    gateway broken because the calendar was full.
    """
    last = None

    for week in range(0, 10):
        at = offset + week * 7
        _, page = call(op, "/api/listings?pageSize=60&checkIn=%s&checkOut=%s"
                       % (future(at), future(at + nights)))

        for listing in page.get("items", []):
            if not listing["instantBook"]:
                continue

            st, held = call(op, "/api/bookings", body_for(listing, at, nights, paymentMethod=method))
            last = st
            if st == 201:
                return held, st

    return None, last


print("Staylio · nghiệm thu cổng thanh toán (docs/07 §13) — %s\n" % BASE)

# --- 0: which gateways are wired --------------------------------------------
_, catalogue = call(opener(), "/api/payment-methods/catalogue")
live = {m["key"]: m.get("live", False) for m in catalogue["methods"]}
print("Cổng đang bật: %s\n" % (", ".join(k for k, v in live.items() if v) or "(chưa có)"))

guest = opener()
st, _ = call(guest, "/api/account/login",
             {"email": "guest@staylio.vn", "password": "stayhost123"})
if st != 200:
    print("Không đăng nhập được guest@staylio.vn — dừng.")
    sys.exit(1)

momo_ref = momo_booking = None
zalo_ref = zalo_booking = None
zalo_amount = 0

# --- 0b: VNPay carries both card rows ----------------------------------------
# The two rows at the top of the checkout. Unlike the wallets these are followed
# all the way to VNPay's own payment page, because that is the only thing that
# proves every one of the fifteen signed parameters was acceptable — a rejected
# one gets no page, just their error screen with no reason on it.
print("0b. VNPay nhận đơn cho cả hai ô thẻ")
vnpay_refs = {}

for key, label, expect in [("card", "Thẻ tín dụng / ghi nợ", "Thẻ thanh to&#225;n quốc tế"),
                           ("napas", "Thẻ ATM nội địa", None)]:
    if not live.get(key):
        check("VNPay đang bật cho ô %s" % label, False, "chưa cấu hình TmnCode")
        continue

    held, code = hold(guest, key)
    if held is None:
        check("Giữ chỗ được cho ô %s" % label, False, "HTTP %s" % code)
        continue

    st, paid = call(guest, "/api/bookings/%d/pay" % held["id"], {"paymentMethod": key})
    url = (paid or {}).get("gatewayRedirectUrl") or ""

    check("%s: nhận địa chỉ của VNPay" % label,
          st == 200 and "vnpayment.vn" in url, url[:70] or "HTTP %s: %s" % (st, paid))

    if not url:
        continue

    vnpay_refs[key] = ((paid or {}).get("gatewayOrderRef"), held["id"], (paid or {}).get("total"))

    # The bank code decides which of the two lists VNPay opens with, so the two
    # rows are not the same button wearing different words.
    check("%s: đi đúng nhánh %s" % (label, "INTCARD" if key == "card" else "VNBANK"),
          ("INTCARD" if key == "card" else "VNBANK") in url, url[:110])

    try:
        with browser().open(url, timeout=30) as res:
            landed = res.geturl()
            body = res.read().decode("utf-8", "replace")
    except urllib.error.URLError as e:
        landed, body = "", str(e)

    check("%s: VNPay nhận chữ ký và mở trang thanh toán" % label,
          "Error.html" not in landed and "PaymentMethod" in landed, landed[-70:])

    if expect:
        check("%s: VNPay mở đúng danh sách thẻ quốc tế" % label, expect in body,
              "không thấy nhãn của họ trên trang")

# --- 1: MoMo opens a real order ---------------------------------------------
print("1. MoMo mở đơn thật và trả về địa chỉ của chính họ")
if not live.get("momo"):
    check("MoMo đang bật", False, "chưa cấu hình")
else:
    held, code = hold(guest, "momo")
    if held is None:
        check("Giữ chỗ được", False, "HTTP %s" % code)
    else:
        momo_booking = held["id"]
        st, paid = call(guest, "/api/bookings/%d/pay" % held["id"], {"paymentMethod": "momo"})
        url = (paid or {}).get("gatewayRedirectUrl") or ""
        check("Đơn nhận địa chỉ chuyển hướng", st == 200 and url.startswith("http"),
              url[:70] or "HTTP %s: %s" % (st, paid))
        check("Địa chỉ là của MoMo, không phải của sàn", "momo.vn" in url, url[:70])
        # docs/07 §13 — nothing may be charged before the guest has paid.
        check("Đơn vẫn đang chờ thanh toán", (paid or {}).get("status") == "PendingPayment",
              str((paid or {}).get("status")))
        check("Có mã giao dịch để đối chiếu", bool((paid or {}).get("gatewayOrderRef")),
              str((paid or {}).get("gatewayOrderRef")))

        # docs/07 §7 — the same request twice is one order at the gateway.
        st2, again = call(guest, "/api/bookings/%d/pay" % held["id"], {"paymentMethod": "momo"})
        check("Bấm lại không mở đơn thứ hai ở cổng",
              (again or {}).get("gatewayOrderRef") == (paid or {}).get("gatewayOrderRef"),
              "%s vs %s" % ((paid or {}).get("gatewayOrderRef"), (again or {}).get("gatewayOrderRef")))
        momo_ref = (paid or {}).get("gatewayOrderRef")

# --- 2: ZaloPay opens a real order ------------------------------------------
print("\n2. ZaloPay mở đơn thật và trả về địa chỉ của chính họ")
if not live.get("zalopay"):
    check("ZaloPay đang bật", False, "chưa cấu hình")
else:
    held, code = hold(guest, "zalopay")
    if held is None:
        check("Giữ chỗ được", False, "HTTP %s" % code)
    else:
        zalo_booking = held["id"]
        st, paid = call(guest, "/api/bookings/%d/pay" % held["id"], {"paymentMethod": "zalopay"})
        url = (paid or {}).get("gatewayRedirectUrl") or ""
        check("Đơn nhận địa chỉ chuyển hướng", st == 200 and url.startswith("http"),
              url[:70] or "HTTP %s: %s" % (st, paid))
        check("Địa chỉ là của ZaloPay", "zalopay" in url or "zlp" in url, url[:70])
        check("Đơn vẫn đang chờ thanh toán", (paid or {}).get("status") == "PendingPayment",
              str((paid or {}).get("status")))
        zalo_ref = (paid or {}).get("gatewayOrderRef")
        zalo_amount = (paid or {}).get("total") or 0

# --- 3: a callback nobody signed confirms nothing -----------------------------
# The half that costs money if it is wrong. Anyone can post to these routes.
print("\n3. Callback không có chữ ký thì không xác nhận được gì")
anon = opener()

st, res = call(anon, "/api/payments/zalopay/callback",
               {"data": json.dumps({"app_trans_id": "260101_00000000000000000001",
                                    "amount": 9_999_999}),
                "mac": "0" * 64})
check("ZaloPay: mac sai bị từ chối", (res or {}).get("return_code") in (0, -1),
      json.dumps(res, ensure_ascii=False)[:70])

st, res = call(anon, "/api/payments/vnpay/ipn?vnp_TxnRef=00000000000000000001"
                     "&vnp_Amount=100&vnp_ResponseCode=00&vnp_SecureHash=" + "0" * 128)
check("VNPay: chữ ký sai không xác nhận đơn", (res or {}).get("RspCode") != "00",
      json.dumps(res, ensure_ascii=False)[:70])

# The forged MoMo IPN aims at a real, still-unpaid order, which is the case that
# actually matters: a signature check that only refuses unknown orders is no
# check at all.
if momo_ref:
    st, res = call(anon, "/api/payments/momo/ipn", {
        "partnerCode": "MOMOBKUN20180529", "orderId": momo_ref, "requestId": momo_ref,
        "amount": 1, "orderInfo": "x", "orderType": "momo_wallet", "transId": 1,
        "resultCode": 0, "message": "Successful.", "payType": "webApp",
        "responseTime": 1, "extraData": "", "signature": "0" * 64})

    _, mine = call(guest, "/api/bookings")
    row = next((b for b in (mine or []) if b["id"] == momo_booking), None)

    check("MoMo: chữ ký giả không xác nhận được đơn thật",
          st in (200, 204) and row is not None and row["status"] == "PendingPayment",
          "HTTP %s · %s" % (st, row and row["status"]))

    # And it must not kill the payment either. Nothing authenticates a caller
    # here, so a forged callback that could mark the attempt failed would let a
    # stranger stop anyone's booking — and strand a guest who then really paid.
    st, again = call(guest, "/api/bookings/%d/pay" % momo_booking, {"paymentMethod": "momo"})
    check("Chữ ký giả cũng không làm hỏng lượt thanh toán đang chờ",
          st == 200 and bool((again or {}).get("gatewayRedirectUrl")),
          "HTTP %s: %s" % (st, (again or {}).get("message")))

# --- 4: the platform asks the gateway itself ---------------------------------
# docs/07 §5 — the return route does not read what the browser brought back; it
# asks ZaloPay. A round trip that answers "still deciding" proves the query call
# is real, signed correctly and understood.
print("\n4. Sàn tự hỏi lại cổng thay vì tin trình duyệt")
if not (live.get("zalopay") and zalo_ref):
    why = ("ZaloPay chua bat" if not live.get("zalopay")
           else "khong mo duoc don ZaloPay o muc 2")
    for _n in ("Khach duoc dua ve trang ket qua cua san",
               "Cong tra loi chua xong nen chua xac nhan",
               "Don van chua duoc xac nhan"):
        skip(_n, why)
else:
    st, page = call(anon, "/api/payments/zalopay/return?ref=%s" % zalo_ref)
    landed = (page or {}).get("url", "")
    check("Khách được đưa về trang kết quả của sàn", st == 200 and "ket-qua" in landed,
          "HTTP %s · %s" % (st, landed[-60:]))
    # ZaloPay was asked and said "still deciding", so the page says pending —
    # not "paid" because the guest happened to land on the success URL.
    check("Cổng trả lời chưa xong nên chưa xác nhận", "ket-qua=pending" in landed,
          landed[-60:])
    _, mine = call(guest, "/api/bookings")
    row = next((b for b in mine if b["id"] == zalo_booking), None)
    check("Đơn vẫn chưa được xác nhận", row is not None and row["status"] == "PendingPayment",
          str(row and row["status"]))

# --- 5: a properly signed callback does confirm ------------------------------
# The other half. Finishing a payment for real needs a human with the MoMo app,
# so this stands in for ZaloPay's server the only way a test can: by signing the
# callback with the merchant key ZaloPay signs it with. Everything downstream —
# the signature check, the amount check, the ledger, the confirmation — is the
# production path, untouched.
print("\n5. Callback đúng chữ ký thì xác nhận đơn, và gọi lại lần nữa không ghi sổ hai lần")
if not (live.get("zalopay") and zalo_ref):
    why = ("ZaloPay chua bat" if not live.get("zalopay")
           else "khong mo duoc don ZaloPay o muc 2")
    for _n in ("Callback ky dung thi don duoc xac nhan",
               "So sach ghi dung mot lan",
               "Goi lai callback khong ghi so lan hai"):
        skip(_n, why)
else:
    key2 = os.environ.get("ZALOPAY_KEY2") or dev_key2()

    if not key2:
        check("Đọc được key2 để ký thay ZaloPay", False, "đặt ZALOPAY_KEY2")
    else:
        data = json.dumps({
            "app_id": 2553,
            "app_trans_id": "%s_%s" % (datetime.datetime.now(datetime.timezone.utc).strftime("%y%m%d"), zalo_ref),
            "app_time": 0, "app_user": "stayhost", "amount": int(zalo_amount),
            "zp_trans_id": random.randint(10 ** 11, 10 ** 12)
        }, separators=(",", ":"))

        mac = hmac.new(key2.encode(), data.encode(), hashlib.sha256).hexdigest()

        st, res = call(anon, "/api/payments/zalopay/callback", {"data": data, "mac": mac})
        check("ZaloPay: chữ ký đúng được chấp nhận", (res or {}).get("return_code") == 1,
              json.dumps(res, ensure_ascii=False)[:70])

        _, mine = call(guest, "/api/bookings")
        row = next((b for b in mine if b["id"] == zalo_booking), None)
        check("Đơn đã được xác nhận", row is not None and row["status"] == "Confirmed",
              str(row and row["status"]))

        # docs/07 §7 — the worst fault in the module. ZaloPay retries its callback
        # until it gets a 1, so this happens in production every time a reply is
        # slow.
        before = ledger_total()
        st, res = call(anon, "/api/payments/zalopay/callback", {"data": data, "mac": mac})
        after = ledger_total()
        check("Gọi lại callback không ghi thêm bút toán nào", before == after,
              "%s → %s" % (before, after))

# --- 5b: VNPay's IPN, signed the way VNPay signs it --------------------------
# The wallets prove the redirect; this proves the confirmation. VNPay retries its
# IPN until it gets one of the codes that means "stop", so the reply codes matter
# as much as the booking does.
print("\n5b. IPN của VNPay: ký đúng thì xác nhận đơn, và trả đúng mã cho họ")
secret = vnpay_secret()

if not vnpay_refs.get("card"):
    check("Có đơn VNPay để thử IPN", False, "chưa mở được đơn nào")
elif not secret:
    check("Đọc được HashSecret để ký thay VNPay", False,
          "đặt VNPAY_HASH_SECRET hoặc dotnet user-secrets")
else:
    ref, booking_id, amount = vnpay_refs["card"]

    def ipn(**over):
        fields = {
            "vnp_Amount": str(int(round(amount)) * 100), "vnp_BankCode": "NCB",
            "vnp_BankTranNo": "VNP" + ref[-8:], "vnp_CardType": "ATM",
            "vnp_OrderInfo": "Staylio", "vnp_PayDate": ref[:12] + "00",
            "vnp_ResponseCode": "00", "vnp_TmnCode": "GLQWM7J8",
            "vnp_TransactionNo": "14" + ref[-6:], "vnp_TransactionStatus": "00",
            "vnp_TxnRef": ref,
        }
        fields.update(over)
        fields["vnp_SecureHash"] = vnpay_sign(fields, secret)
        return call(anon, "/api/payments/vnpay/ipn?" + urllib.parse.urlencode(fields))

    # A tampered amount must be refused even though everything else is genuine —
    # the signature is over the tampered value, so this is not a forgery test, it
    # is the "gateway and booking disagree" test of docs/07 §7.
    st, res = ipn(vnp_Amount="100")
    check("Số tiền lệch bị từ chối bằng mã 04", (res or {}).get("RspCode") == "04",
          json.dumps(res, ensure_ascii=False)[:70])

    st, res = ipn()
    check("Ký đúng thì VNPay nhận mã 00", (res or {}).get("RspCode") == "00",
          json.dumps(res, ensure_ascii=False)[:70])

    _, mine = call(guest, "/api/bookings")
    row = next((b for b in (mine or []) if b["id"] == booking_id), None)
    check("Đơn đã được xác nhận", row is not None and row["status"] == "Confirmed",
          str(row and row["status"]))

    # VNPay retries until it is told to stop, so the second delivery must not
    # post the ledger again — and must say 02, their code for "already done".
    before = ledger_total()
    st, res = ipn()
    after = ledger_total()
    check("Gửi lại IPN trả mã 02 và không ghi sổ lần nữa",
          (res or {}).get("RspCode") == "02" and before == after,
          "%s · %s → %s" % ((res or {}).get("RspCode"), before, after))

# --- 6: the books still balance ----------------------------------------------
print("\n6. Sổ sách vẫn cân")
total = ledger_total()
check("Tổng nợ trừ có bằng 0", total == 0, str(total))

print("\n%d dat - %d hong - %d bo qua" % (len(passed), len(failed), len(skipped)))
if failed:
    for f in failed:
        print("  hỏng: " + f)
if skipped:
    # Named, so the total can never shrink without saying so.
    for _f in skipped:
        print("  bo qua: " + _f)
sys.exit(1 if failed else 0)
