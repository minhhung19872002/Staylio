# The deep audit of 17/09/2026: money that moved without anybody paying,
# doors that opened without a key, and rules that were written but never ran.
#
# Two modes, chosen by the address:
#   * local (http://localhost…) — every scenario, reading the database to be sure
#   * anything else (https://staylio.vn) — only what can be proved over HTTP
#     with the demo accounts, and every row a scenario creates is undone
#
#   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web
#   python scripts/audit0917_acceptance.py
#   STAYHOST_URL=https://staylio.vn python scripts/audit0917_acceptance.py
import os
import json
import datetime
import http.cookiejar
import subprocess
import threading
import time
import urllib.error
import urllib.request

import sys as _sys, os as _os
_sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import _gateway as gateway

import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

B = os.environ.get("STAYHOST_URL", "http://localhost:5199").rstrip("/")
LOCAL = "localhost" in B or "127.0.0.1" in B
PW = os.environ.get("STAYHOST_DEMO_PASSWORD", "stayhost123")
RUN = str(int(time.time()))[-6:]
results = []
skipped = []


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def opener(follow=True):
    handlers = [urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())]
    if not follow:
        handlers.append(NoRedirect())
    return urllib.request.build_opener(*handlers)


def call(op, p, b=None, m=None, headers=None, full=False):
    d = json.dumps(b).encode() if b is not None else None
    h = {"Content-Type": "application/json"} if d else {}
    h["User-Agent"] = "staylio-audit/1.0"
    h.update(headers or {})
    r = urllib.request.Request(B + p, data=d, headers=h, method=m or ("POST" if d else "GET"))
    try:
        x = op.open(r, timeout=60)
        raw = x.read().decode(errors="replace").strip()
        body = None
        if raw:
            try:
                body = json.loads(raw)
            except json.JSONDecodeError:
                body = raw
        return (x.status, body, x.headers) if full else (x.status, body)
    except urllib.error.HTTPError as e:
        raw = e.read().decode(errors="replace")
        try:
            body = json.loads(raw)
        except json.JSONDecodeError:
            body = raw
        return (e.code, body, e.headers) if full else (e.code, body)


def sql(q):
    out = subprocess.run(
        ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost", "-t", "-A", "-c", q],
        capture_output=True, text=True, encoding="utf-8")
    if out.returncode != 0:
        raise SystemExit("psql failed: " + out.stderr)
    return out.stdout.strip()


def ok(name, passed, detail=""):
    results.append((name, passed, detail))
    print(("PASS " if passed else "FAIL ") + name + (" - " + detail if detail else ""))


def skip(name, why):
    skipped.append((name, why))
    print("SKIP " + name + " - " + why)


def sign_in(email):
    op = opener()
    st, res = call(op, "/api/account/login", {"email": email, "password": PW})
    if isinstance(res, dict) and res.get("challenge"):
        if not res.get("devCode"):
            return None
        call(op, "/api/account/two-factor", {"challenge": res["challenge"], "code": res["devCode"]})
    return op if st in (200, 202) else None


def register(email, name, confirm=True):
    op = opener()
    st, res = call(op, "/api/account/register",
                   {"email": email, "password": PW, "fullName": name, "dateOfBirth": "1990-01-01"})
    if st not in (200, 201):
        raise SystemExit("register %s: %s %s" % (email, st, res))
    if confirm:
        _, sent = call(op, "/api/account/send-code", {"kind": "email"})
        call(op, "/api/account/confirm-code", {"kind": "email", "code": (sent or {}).get("devCode")})
    return op, int(sql("select \"Id\" from users where \"Email\"='%s'" % email))


def today():
    return datetime.date.today()


def hold(op, listing_id, start, nights=2, **extra):
    """A held stay, moved on a week at a time past dates other suites took."""
    for week in range(12):
        ci = today() + datetime.timedelta(days=start + 7 * week)
        body = {"listingId": listing_id, "checkIn": ci.isoformat(),
                "checkOut": (ci + datetime.timedelta(days=nights)).isoformat(),
                "guests": 2, "adults": 2, "agreedToRules": True,
                "guestName": "Kiem Tra", "guestEmail": "kiemtra%s@vidu.vn" % RUN, "guestPhone": "0901%s" % RUN}
        body.update(extra)
        st, res = call(op, "/api/bookings", body)
        if st == 409 and isinstance(res, dict) and res.get("reason") in ("DatesTaken", "TurnoverTime"):
            continue
        return st, res
    return st, res


_listing = None


def instant_listing():
    """An instant-book place that takes a guest with no preconditions (docs/01 ĐP-10)."""
    global _listing
    if _listing:
        return _listing
    st, res = call(opener(), "/api/listings?take=40")
    items = (res or {}).get("items", []) if isinstance(res, dict) else []
    for c in items:
        if not c.get("instantBook") or c.get("typeKey") == "hotel":
            continue
        op = opener()
        s2, b = hold(op, c["id"], 120 + int(RUN) % 20)
        if s2 in (200, 201) and isinstance(b, dict) and b.get("status") == "PendingPayment":
            call(op, "/api/bookings/%d/release" % b["id"], m="POST")
            _listing = c
            return c
    return None


# ---------------------------------------------------------------- over HTTP
def s_security_headers():
    st, _, h = call(opener(), "/", full=True)
    ok("H1. Header bảo mật trên trang",
       h.get("X-Content-Type-Options") == "nosniff" and h.get("X-Frame-Options") == "SAMEORIGIN"
       and bool(h.get("Referrer-Policy")) and (LOCAL or "max-age" in (h.get("Strict-Transport-Security") or "")),
       "nosniff=%s frame=%s referrer=%s hsts=%s" % (
           h.get("X-Content-Type-Options"), h.get("X-Frame-Options"),
           h.get("Referrer-Policy"), h.get("Strict-Transport-Security")))


def s_secure_cookie():
    extra = {"X-Forwarded-Proto": "https"} if LOCAL else {}
    op = opener()
    st, _, h = call(op, "/api/account/login", {"email": "guest@staylio.vn", "password": PW},
                    headers=extra, full=True)
    cookies = h.get_all("Set-Cookie") or []
    auth = [c for c in cookies if c.lower().startswith("sh_auth=")]
    ok("H2. Cookie đăng nhập có cờ Secure sau proxy HTTPS",
       st == 200 and auth and all("secure" in c.lower() for c in auth),
       "http=%s, %s" % (st, auth[0][:40] + "…" if auth else "không có sh_auth"))


def s_pay_refuses_unknown_methods():
    lst = instant_listing()
    if not lst:
        return skip("H3. /pay từ chối phương thức lạ", "không tìm được tin đặt ngay")
    op = opener()
    st, b = hold(op, lst["id"], 150 + int(RUN) % 30)
    if st not in (200, 201):
        return ok("H3. /pay từ chối phương thức lạ", False, "giữ chỗ %s %s" % (st, b))
    results_by_method = {}
    for method in ("applepay", "xyz", "balance", "installment"):
        s2, r2 = call(op, "/api/bookings/%d/pay" % b["id"], {"paymentMethod": method, "cardLast4": "4242"})
        results_by_method[method] = s2
    _, after = call(op, "/api/bookings/%d" % b["id"])
    call(op, "/api/bookings/%d/release" % b["id"], m="POST")
    ok("H3. /pay từ chối phương thức lạ, đơn không được xác nhận",
       all(v == 400 for v in results_by_method.values()) and (after or {}).get("status") == "PendingPayment",
       "%s, trạng thái sau=%s" % (results_by_method, (after or {}).get("status")))


def s_catalogue_only_takes_money():
    _, cat = call(opener(), "/api/payment-methods/catalogue")
    methods = (cat or {}).get("methods", [])
    bad = [m["key"] for m in methods
           if m["key"] in ("card", "napas", "momo", "zalopay") and not m.get("live") and not LOCAL]
    ok("H4. Danh mục chỉ mời phương thức thu được tiền",
       not bad and all(m["key"] not in ("applepay", "googlepay", "paypal", "installment") for m in methods),
       ", ".join("%s%s" % (m["key"], "*" if m.get("live") else "") for m in methods))


def s_hosting_links():
    op = opener()
    codes = {p: call(op, p)[0] for p in ("/hosting/earnings", "/hosting/reviews", "/hosting?tab=team")}
    ok("H5. Liên kết trong thư chuyển tiền/đánh giá mở được", all(c == 200 for c in codes.values()), str(codes))


def s_experience_goes_to_gateway():
    op = sign_in("guest@staylio.vn")
    if op is None:
        return skip("H6. Vé trải nghiệm đi qua cổng thật", "không đăng nhập được guest demo")
    _, cat = call(opener(), "/api/payment-methods/catalogue")
    card = next((m for m in (cat or {}).get("methods", []) if m["key"] == "card"), None)
    if not card or not card.get("live"):
        return skip("H6. Vé trải nghiệm đi qua cổng thật", "ô thẻ chưa nối cổng thật ở đây")
    _, xs = call(opener(), "/api/experiences")
    items = xs if isinstance(xs, list) else (xs or {}).get("items", [])
    slot = None
    for x in items[:30]:
        _, d = call(opener(), "/api/experiences/%s" % x["slug"])
        for s in (d or {}).get("slots", []):
            starts = datetime.datetime.fromisoformat(s["startsAt"].replace("Z", "+00:00"))
            if starts - datetime.datetime.now(datetime.timezone.utc) > datetime.timedelta(hours=30) \
                    and s.get("seatsLeft", 1) >= 1:
                slot = s
                break
        if slot:
            break
    if not slot:
        return skip("H6. Vé trải nghiệm đi qua cổng thật", "không có suất nào còn chỗ")
    st, r = call(op, "/api/experiences/slots/%d/book" % slot["id"],
                 {"seats": 1, "paymentMethod": "card", "cardLast4": "4242"})
    status = (r or {}).get("status") if isinstance(r, dict) else None
    redirect = (r or {}).get("gatewayRedirectUrl") if isinstance(r, dict) else None
    if isinstance(r, dict) and r.get("id"):
        call(op, "/api/experiences/bookings/%d/cancel" % r["id"], m="POST")
    ok("H6. Vé trải nghiệm đi qua cổng thật, chưa xác nhận khi chưa trả",
       st == 200 and status == "AwaitingPayment" and bool(redirect),
       "http=%s status=%s redirect=%s" % (st, status, (redirect or "")[:50]))


def s_shield_is_private():
    op = ((sign_in("khach6@staylio.vn") or sign_in("guest@staylio.vn")) if not LOCAL
          else register("sh%s@staylio.vn" % RUN, "Nguoi La")[0])
    if op is None:
        return skip("H7. Hồ sơ Shield không đọc được của người khác", "không đăng nhập được")
    _, mine = call(op, "/api/shield")
    own = {c["id"] for c in (mine or [])} if isinstance(mine, list) else set()
    leaked = []
    for cid in range(1, 16):
        if cid in own:
            continue
        st, _ = call(op, "/api/shield/%d" % cid)
        if st == 200:
            leaked.append(cid)
    ok("H7. Hồ sơ Shield không đọc được của người khác", not leaked, "đọc được: %s" % leaked)


def s_verify_link_not_in_response():
    if LOCAL:
        return skip("H8. Link xác thực email không nằm trong phản hồi", "Development trả link có chủ đích")
    op = sign_in("khach6@staylio.vn") or sign_in("guest@staylio.vn")
    if op is None:
        return skip("H8. Link xác thực email không nằm trong phản hồi", "không đăng nhập được")
    st, r = call(op, "/api/account/send-verification", m="POST")
    link = (r or {}).get("verifyLink") if isinstance(r, dict) else None
    ok("H8. Link xác thực email không nằm trong phản hồi", st == 200 and not link, "http=%s link=%s" % (st, link))


def s_stay_details_reach_the_host():
    """Arrival hour, who is staying, work trip, special requests — kept, shown, corrected."""
    name = "H10. Giờ đến, người lưu trú, yêu cầu đặc biệt được lưu và sửa được"
    lst = instant_listing()
    if not lst:
        return skip(name, "không tìm được tin đặt ngay")
    op = opener()
    bad, bad_res = hold(op, lst["id"], 180 + int(RUN) % 30, specialRequests=["jacuzzi"])
    st, b = hold(op, lst["id"], 180 + int(RUN) % 30, estimatedArrivalHour=14,
                 stayingGuestName="Tran Thi Luu Tru", isBusinessTrip=True,
                 specialRequests=["crib", "quiet-room"])
    if st not in (200, 201):
        return ok(name, False, "giữ chỗ %s %s" % (st, b))
    try:
        _, got = call(op, "/api/bookings/%d" % b["id"])
        d = (got or {}).get("details") or {}
        s2, edited = call(op, "/api/bookings/%d/details" % b["id"],
                          {"estimatedArrivalHour": 23, "stayingGuestName": None,
                           "isBusinessTrip": False, "specialRequests": ["late-check-out"]}, m="PUT")
        s3, _ = call(op, "/api/bookings/%d/details" % b["id"],
                     {"estimatedArrivalHour": 24, "specialRequests": []}, m="PUT")
        host_sees = None
        if LOCAL:
            host = sign_in(sql("select u.\"Email\" from listings l join hosts h on h.\"Id\"=l.\"HostId\" "
                               "join users u on u.\"Id\"=h.\"UserId\" where l.\"Id\"=%d" % lst["id"]))
            _, dash = call(host, "/api/host/dashboard")
            row = next((x for x in (dash or {}).get("bookings", []) if x["id"] == b["id"]), None)
            host_sees = ((row or {}).get("details") or {}).get("specialRequestLabels")
    finally:
        call(op, "/api/bookings/%d/release" % b["id"], m="POST")
    ok(name,
       bad == 400 and "jacuzzi" in (bad_res or {}).get("message", "")
       and d.get("arrivalLabel") == "14:00–15:00" and d.get("stayingGuestName") == "Tran Thi Luu Tru"
       and d.get("isBusinessTrip") is True and d.get("specialRequests") == ["quiet-room", "crib"]
       and s2 == 200 and (edited or {}).get("arrivalLabel") == "23:00–00:00"
       and (edited or {}).get("specialRequests") == ["late-check-out"] and s3 == 400
       and (not LOCAL or host_sees == ["Phòng yên tĩnh", "Cũi cho em bé"] or host_sees == ["Trả phòng muộn"]),
       "lạ=%s, lưu=%s, sửa=%s→%s, giờ 24=%s, chủ nhà thấy=%s"
       % (bad, d, s2, (edited or {}).get("specialRequests"), s3, host_sees))


def s_search_filters_like_booking():
    """Review score, no prepayment, distance to centre, and the distance sort."""
    name = "H11. Lọc theo điểm, không trả trước, khoảng cách tới trung tâm; xếp theo khoảng cách"
    op = opener()
    _, rated = call(op, "/api/listings?pageSize=60&minRating=4.5")
    _, cash = call(op, "/api/listings?pageSize=60&payAtProperty=true")
    _, near = call(op, "/api/listings?pageSize=60&maxCentreKm=3")
    _, counted = call(op, "/api/listings/count?maxCentreKm=3")
    _, by_km = call(op, "/api/listings?pageSize=60&sort=distance")
    kms = [c.get("fromCentreKm") for c in by_km.get("items", [])]
    known = [k for k in kms if k is not None]
    ok(name,
       all(c["rating"] >= 4.5 and c["reviewCount"] > 0 for c in rated["items"])
       and all(c["payAtProperty"] for c in cash["items"])
       and all(c.get("fromCentreKm") is not None and c["fromCentreKm"] <= 3.05 for c in near["items"])
       and counted["total"] == near["total"]
       and known == sorted(known) and kms[:len(known)] == known,
       "điểm≥4.5: %d, trả tại chỗ: %d, ≤3km: %d (đếm %d), xếp: %s…"
       % (rated["total"], cash["total"], near["total"], counted["total"], kms[:6]))


def s_ical_is_public_only():
    op = sign_in("host1@staylio.vn")
    if op is None:
        return skip("H9. Lịch iCal không trỏ vào mạng nội bộ", "không đăng nhập được host1")
    _, dash = call(op, "/api/host/dashboard")
    listings = (dash or {}).get("listings", []) if isinstance(dash, dict) else []
    if not listings:
        return skip("H9. Lịch iCal không trỏ vào mạng nội bộ", "host1 không có tin")
    lid = listings[0]["id"]
    st, board = call(op, "/api/host/listings/%d/feeds" % lid,
                     {"url": "http://127.0.0.1:8080/health", "label": "kiem-tra-%s" % RUN})
    feed = next((f for f in (board or {}).get("feeds", []) if f["label"] == "kiem-tra-%s" % RUN), None) \
        if isinstance(board, dict) else None
    error = (feed or {}).get("lastError") or ""
    if feed:
        call(op, "/api/host/listings/%d/feeds/%d" % (lid, feed["id"]), m="DELETE")
    ok("H9. Lịch iCal không trỏ vào mạng nội bộ",
       feed is not None and "internet" in error.lower() and (feed.get("eventCount") or 0) == 0,
       "http=%s lỗi='%s'" % (st, error[:70]))


# --------------------------------------------------------------- local only
def l_cohost_needs_confirmed_email():
    owner = sign_in("host2@staylio.vn")
    email = "cohost%s@staylio.vn" % RUN
    st, inv = call(owner, "/api/host/co-hosts", {"email": email, "scopes": ["calendar"]})
    intruder, _ = register(email, "Ke Mao Danh", confirm=False)
    st2, _ = call(intruder, "/api/host/co-hosts/%d/accept" % inv["id"], m="POST")
    status = sql('select "Status" from co_hosts where "Id"=%d' % inv["id"])
    ok("L1. Email chưa xác thực không nhận được lời mời đồng quản lý",
       st2 == 403 and status == "0", "http=%s status=%s" % (st2, status))


def l_phone_change_resets_confirmation():
    op, uid = register("ph%s@staylio.vn" % RUN, "Doi So")
    sql('update users set "Phone"=\'0912%s\', "PhoneConfirmed"=true where "Id"=%d' % (RUN, uid))
    st, _ = call(op, "/api/account/profile", {"fullName": "Doi So", "phone": "0913%s" % RUN, "bio": None}, m="PUT")
    row = sql('select "Phone" || \'|\' || "PhoneConfirmed" from users where "Id"=%d' % uid)
    ok("L2. Đổi số điện thoại thì phải xác minh lại", st == 200 and row == "0913%s|false" % RUN,
       "http=%s %s" % (st, row))


def l_host_cancel_blocks_dates():
    lst = instant_listing()
    guest, _ = register("hc%s@staylio.vn" % RUN, "Khach Bi Huy")
    st, b = hold(guest, lst["id"], 260 + int(RUN) % 20)
    if st not in (200, 201):
        return ok("L3. Chủ nhà huỷ thì ngày bị chặn", False, "giữ chỗ %s %s" % (st, b))
    s2, paid = gateway.pay(call, guest, b["id"], {"paymentMethod": "card", "cardLast4": "4242"})
    host_email = sql('select u."Email" from listings l join hosts h on h."Id"=l."HostId" '
                     'join users u on u."Id"=h."UserId" where l."Id"=%d' % lst["id"])
    host = sign_in(host_email)
    s3, _ = call(host, "/api/host/bookings/%d/cancel" % b["id"], {"reason": "Kiem tra"})
    block = sql('select "Id" from calendar_blocks where "ExternalUid"=\'host-cancel:%d\'' % b["id"])
    again, again_res = call(opener(), "/api/bookings", {
        "listingId": lst["id"], "checkIn": b["checkIn"], "checkOut": b["checkOut"],
        "guests": 2, "adults": 2, "agreedToRules": True,
        "guestName": "Khac", "guestEmail": "khac%s@vidu.vn" % RUN, "guestPhone": "0908%s" % RUN})
    s5, _ = call(host, "/api/host/blocks/%s" % block, m="DELETE") if block else (0, None)
    ok("L3. Chủ nhà huỷ thì ngày bị chặn và không mở lại được",
       s3 == 200 and bool(block) and again == 409 and s5 == 400,
       "huỷ=%s block=%s đặt lại=%s gỡ=%s" % (s3, block, again, s5))


def l_min_nights_keeps_price():
    lst = instant_listing()
    host_email = sql('select u."Email" from listings l join hosts h on h."Id"=l."HostId" '
                     'join users u on u."Id"=h."UserId" where l."Id"=%d' % lst["id"])
    host = sign_in(host_email)
    day = today() + datetime.timedelta(days=330 + int(RUN) % 20)
    rng = {"from": day.isoformat(), "to": (day + datetime.timedelta(days=2)).isoformat()}
    s1, _ = call(host, "/api/host/listings/%d/days" % lst["id"], dict(rng, nightlyRate=2_345_000))
    s2, _ = call(host, "/api/host/listings/%d/days" % lst["id"], dict(rng, minNights=3))
    rate = sql('select max("NightlyRate") from price_rules where "ListingId"=%d and "Kind"=1 '
               'and "From"<=\'%s\' and "To">=\'%s\'' % (lst["id"], day, day))
    mins = sql('select max("MinNights") from price_rules where "ListingId"=%d and "Kind"=2 '
               'and "From"<=\'%s\' and "To">=\'%s\'' % (lst["id"], day, day))
    ok("L4. Đổi số đêm tối thiểu không xoá giá theo ngày",
       s1 == 204 and s2 == 204 and rate.startswith("2345000") and mins == "3",
       "giá=%s tối thiểu=%s" % (rate, mins))


def l_gift_card_redeemed_once():
    code = "KT%s" % RUN
    sql('insert into gift_cards ("Code","Amount","Remaining","Status","RecipientEmail","CreatedAt") '
        'values (\'%s\', 500000, 500000, 0, \'nhan%s@vidu.vn\', now())' % (code, RUN))
    a, uid = register("gc%s@staylio.vn" % RUN, "Doi The")
    b = sign_in("gc%s@staylio.vn" % RUN)
    out = []

    def go(op):
        out.append(call(op, "/api/wallet/redeem", {"code": code})[0])

    threads = [threading.Thread(target=go, args=(o,)) for o in (a, b, a, b)]
    [t.start() for t in threads]
    [t.join() for t in threads]
    credited = sql('select coalesce(sum("Amount"),0) from credit_entries where "UserId"=%d' % uid)
    ok("L5. Thẻ quà tặng đổi song song chỉ cộng một lần",
       float(credited) == 500000.0 and out.count(200) == 1, "http=%s cộng=%s" % (out, credited))


def l_credit_not_spent_twice():
    lst = instant_listing()
    op, uid = register("cr%s@staylio.vn" % RUN, "Tieu So Du")
    sql('insert into credit_entries ("UserId","Amount","Reason","Memo","CreatedAt") '
        'values (%d, 300000, 1, \'kiem tra\', now())' % uid)
    s1, b1 = hold(op, lst["id"], 280 + int(RUN) % 10, useCredit=True)
    s2, b2 = hold(op, lst["id"], 300 + int(RUN) % 10, useCredit=True)
    used1 = float((b1 or {}).get("creditUsed") or sql('select "CreditUsed" from bookings where "Id"=%d' % b1["id"]))
    used2 = float(sql('select "CreditUsed" from bookings where "Id"=%d' % b2["id"])) if s2 in (200, 201) else -1
    for b in (b1, b2):
        if isinstance(b, dict) and b.get("id"):
            call(op, "/api/bookings/%d/release" % b["id"], m="POST")
    ok("L6. Một khoản số dư không cam kết được cho hai đơn cùng lúc",
       used1 > 0 and used2 == 0, "đơn 1 dùng %s, đơn 2 dùng %s" % (used1, used2))


def l_turnover_back_to_back():
    lst = instant_listing()
    sql('update listings set "TurnoverDays"=1 where "Id"=%d' % lst["id"])
    try:
        op = opener()
        st, first = hold(op, lst["id"], 230 + int(RUN) % 7)
        if st not in (200, 201):
            return ok("L7. Thời gian dọn dẹp chặn đơn nối liền", False, "%s %s" % (st, first))
        st2, second = call(opener(), "/api/bookings", {
            "listingId": lst["id"], "checkIn": first["checkOut"],
            "checkOut": (datetime.date.fromisoformat(first["checkOut"]) + datetime.timedelta(days=2)).isoformat(),
            "guests": 2, "adults": 2, "agreedToRules": True,
            "guestName": "Sau", "guestEmail": "sau%s@vidu.vn" % RUN, "guestPhone": "0907%s" % RUN})
        call(op, "/api/bookings/%d/release" % first["id"], m="POST")
        ok("L7. Thời gian dọn dẹp chặn đơn nối liền",
           st2 == 409 and (second or {}).get("reason") == "TurnoverTime", "http=%s %s" % (st2, second))
    finally:
        sql('update listings set "TurnoverDays"=0 where "Id"=%d' % lst["id"])


# ------------------------------------------------ the four built on 17/09 evening
def _job(op, offering_id, note="Khong di ung"):
    """A paid service job, through VNPay's signed IPN when the card row is live."""
    base = datetime.date.today()
    last = None
    for day in range(3, 30):
        for hour in (2, 3, 7, 8):
            st, res = call(op, "/api/services/%d/book" % offering_id, {
                "startsAt": "%sT%02d:00:00" % ((base + datetime.timedelta(days=day)).isoformat(), hour),
                "quantity": int(sql("select \"MinQuantity\" from service_offerings where \"Id\"=%d" % offering_id)),
                "address": "12 Tran Phu, Da Nang", "latitude": 16.0544, "longitude": 108.2022,
                "note": note, "conditionsConfirmed": True, "paymentMethod": "card", "cardLast4": "4242"})
            if st in (200, 201):
                st, res = gateway.finish(call, op, st, res, "ServiceBookingId")
                if res.get("gatewaySettled") is False:
                    raise SystemExit(res.get("gatewayNote"))
                return res
            last = "%s %s" % (st, res)
    raise SystemExit("khong dat duoc dich vu: %s" % last)


def _chef():
    oid = int(sql("select \"Id\" from service_offerings where \"IsPublished\" and \"Category\"='chef' "
                  "order by \"Id\" limit 1"))
    email = sql("select u.\"Email\" from service_offerings o join hosts h on h.\"Id\"=o.\"HostId\" "
                "join users u on u.\"Id\"=h.\"UserId\" where o.\"Id\"=%d" % oid)
    return oid, email


def ledger_ok():
    return sql("select coalesce(sum(case when \"Direction\"=1 then \"Amount\" else -\"Amount\" end),0) "
               "from ledger_entries") in ("0", "0.00")


def l_host_cancel_is_fined():
    lst = instant_listing()
    guest, _ = register("fine%s@staylio.vn" % RUN, "Khach Bi Huy Phat")
    st, b = hold(guest, lst["id"], 20 + int(RUN) % 5)
    if st not in (200, 201):
        return ok("L8. Chủ nhà tự huỷ thì chịu phí phạt", False, "giữ chỗ %s %s" % (st, b))
    gateway.pay(call, guest, b["id"], {"paymentMethod": "card", "cardLast4": "4242"})
    host_id = int(sql("select \"HostId\" from listings where \"Id\"=%d" % lst["id"]))
    before = float(sql("select \"OwedToPlatform\" from hosts where \"Id\"=%d" % host_id))
    subtotal = float(sql("select \"Subtotal\" from bookings where \"Id\"=%d" % b["id"]))
    host = sign_in(sql("select u.\"Email\" from hosts h join users u on u.\"Id\"=h.\"UserId\" "
                       "where h.\"Id\"=%d" % host_id))
    s2, preview = call(host, "/api/host/bookings/%d/cancel-preview" % b["id"])
    s3, _ = call(host, "/api/host/bookings/%d/cancel" % b["id"], {"reason": "Kiem tra phat"})
    after = float(sql("select \"OwedToPlatform\" from hosts where \"Id\"=%d" % host_id))
    fine = round(after - before)
    said = any("phí phạt" in c for c in (preview or {}).get("consequences", []))
    ok("L8. Chủ nhà tự huỷ thì chịu phí phạt (25% khi còn 2–30 ngày)",
       s3 == 200 and said and fine == round(subtotal * 0.25),
       "phạt %s trên %s, xem trước có nói: %s" % (fine, subtotal, said))


def l_service_waits_for_the_provider():
    oid, provider_email = _chef()
    sql("update service_offerings set \"RequiresConfirmation\"=true where \"Id\"=%d" % oid)
    try:
        guest, _ = register("svcq%s@staylio.vn" % RUN, "Khach Cho Xac Nhan")
        provider = sign_in(provider_email)
        first = _job(guest, oid)
        second = _job(guest, oid, "Khong an cay")
        st1 = sql("select \"Status\" from service_bookings where \"Id\"=%d" % first["id"])
        _, jobs = call(provider, "/api/services/jobs")
        can = next((j for j in jobs if j["id"] == first["id"]), {}).get("canRespond")
        a, _ = call(provider, "/api/services/jobs/%d/accept" % first["id"], m="POST")
        d, _ = call(provider, "/api/services/jobs/%d/decline" % second["id"], {"reason": "Kin lich"})
        rows = sql("select \"Status\" || '|' || \"RefundedAmount\" || '|' || \"Total\" from service_bookings "
                   "where \"Id\" in (%d,%d) order by \"Id\"" % (first["id"], second["id"])).splitlines()
        declined = rows[1].split("|")
        ok("L9. Dịch vụ chờ nhà cung cấp xác nhận: nhận thì xác nhận, từ chối thì hoàn đủ",
           st1 == "0" and can and a == 204 and d == 204 and rows[0].startswith("1|")
           and declined[0] == "4" and declined[1] == declined[2],
           "ban đầu=%s, nút=%s, nhận=%s, từ chối=%s, sau=%s" % (st1, can, a, d, rows))
    finally:
        sql("update service_offerings set \"RequiresConfirmation\"=false where \"Id\"=%d" % oid)


def l_provider_cancel_refunds_credits_and_fines():
    oid, provider_email = _chef()
    guest, guest_id = register("svcc%s@staylio.vn" % RUN, "Khach Bi Nha Cung Cap Huy")
    provider = sign_in(provider_email)
    job = _job(guest, oid)
    host_id = int(sql("select \"HostId\" from service_offerings where \"Id\"=%d" % oid))
    before = float(sql("select \"OwedToPlatform\" from hosts where \"Id\"=%d" % host_id))
    st, _ = call(provider, "/api/services/jobs/%d/cancel" % job["id"], {"reason": "Om dot xuat"})
    status, refunded, total = sql("select \"Status\" || '|' || \"RefundedAmount\" || '|' || \"Total\" "
                                  "from service_bookings where \"Id\"=%d" % job["id"]).split("|")
    credit = float(sql("select coalesce(sum(\"Amount\"),0) from credit_entries "
                       "where \"UserId\"=%d and \"Reason\"=1" % guest_id))
    fined = float(sql("select \"OwedToPlatform\" from hosts where \"Id\"=%d" % host_id)) - before
    ok("L10. Nhà cung cấp huỷ: hoàn đủ, tặng 10% số dư, bị phạt",
       st == 204 and status == "4" and refunded == total
       and credit == round(float(total) * 0.10) and fined > 0 and ledger_ok(),
       "huỷ=%s trạng thái=%s hoàn=%s/%s số dư=%s phạt=%s" % (st, status, refunded, total, credit, fined))


def l_guest_review_has_three_headings():
    bid = sql("select b.\"Id\" from bookings b where b.\"Status\"=4 and b.\"GuestUserId\" is not null "
              "and not exists (select 1 from guest_reviews r where r.\"BookingId\"=b.\"Id\") "
              "order by b.\"Id\" desc limit 1")
    if not bid:
        # A paid stay moved to its end: the rule under test is the review form,
        # not the lifecycle that gets a stay there.
        bid = sql("select b.\"Id\" from bookings b where b.\"Status\" in (2,3) and b.\"GuestUserId\" is not null "
                  "and not exists (select 1 from guest_reviews r where r.\"BookingId\"=b.\"Id\") "
                  "order by b.\"Id\" desc limit 1")
        if not bid:
            return skip("L11. Chủ nhà chấm khách đủ ba mục", "không có đơn đã trả tiền nào")
        sql("update bookings set \"Status\"=4 where \"Id\"=%s" % bid)
    bid = int(bid)
    # Both ends move together: a range whose end comes before its start is refused.
    sql("update bookings set \"CheckIn\"=(now() at time zone 'utc')::date - 3, "
        "\"CheckOut\"=(now() at time zone 'utc')::date - 1, \"Status\"=4 where \"Id\"=%d" % bid)
    host = sign_in(sql("select u.\"Email\" from bookings b join listings l on l.\"Id\"=b.\"ListingId\" "
                       "join hosts h on h.\"Id\"=l.\"HostId\" join users u on u.\"Id\"=h.\"UserId\" "
                       "where b.\"Id\"=%d" % bid))
    body = {"rating": 5, "text": "Khach giu gin nha cua rat tot.", "wouldHostAgain": True}
    s1, _ = call(host, "/api/host/bookings/%d/review-guest" % bid, body)
    s2, _ = call(host, "/api/host/bookings/%d/review-guest" % bid,
                 dict(body, cleanliness=5, communication=4, houseRules=3))
    row = sql("select \"Rating\" || '|' || \"Cleanliness\" || '|' || \"HouseRules\" "
              "from guest_reviews where \"BookingId\"=%d" % bid)
    ok("L11. Chủ nhà chấm khách đủ ba mục, điểm tổng là trung bình",
       s1 == 400 and s2 == 200 and row == "4|5|3", "thiếu mục=%s, đủ mục=%s, lưu=%s" % (s1, s2, row))


def l_trip_shared_without_the_keys():
    name = "L12. Gửi xác nhận cho người đi cùng: có ngày và mã, không có giá/địa chỉ/mã cửa"
    lst = instant_listing()
    guest, _ = register("share%s@staylio.vn" % RUN, "Khach Chia Se")
    st, b = hold(guest, lst["id"], 60 + int(RUN) % 20)
    if st not in (200, 201):
        return ok(name, False, "giữ chỗ %s %s" % (st, b))
    early, _ = call(guest, "/api/bookings/%d/share" % b["id"], {"email": "ban%s@vidu.vn" % RUN})
    gateway.pay(call, guest, b["id"], {"paymentMethod": "card", "cardLast4": "4242"})
    bad, _ = call(guest, "/api/bookings/%d/share" % b["id"], {"email": "khong-phai-email"})
    stranger, _ = call(opener(), "/api/bookings/%d/share" % b["id"], {"email": "ban%s@vidu.vn" % RUN})
    good, _ = call(guest, "/api/bookings/%d/share" % b["id"], {"email": "ban%s@vidu.vn" % RUN})
    body = sql("select \"Body\" from email_messages where \"ToEmail\"='ban%s@vidu.vn' order by \"Id\" desc limit 1" % RUN)
    ref = sql("select \"Reference\" from bookings where \"Id\"=%d" % b["id"])
    secrets = sql("select coalesce(\"AddressLine\",'') || '|' || coalesce(\"DoorCode\",'') from listings where \"Id\"=%d" % lst["id"]).split("|")
    leaked = [x for x in secrets if x and x in body] + (["giá"] if "₫" in body else [])
    ok(name, early == 400 and bad == 400 and stranger == 404 and good == 204 and ref in body and not leaked,
       "chưa trả=%s, email sai=%s, người lạ=%s, gửi=%s, có mã=%s, lộ=%s"
       % (early, bad, stranger, good, ref in body, leaked))


def forget_fixture_cancellations():
    """docs/03 §4 hides a listing on its host's third cancellation in a year, and
    L3/L8 cancel as the host on every run. Earlier runs' cancellations are moved
    back past the year and the listing they hid is shown again, so one run does
    not decide whether the next can book at all."""
    sql("update booking_events set \"CreatedAt\" = \"CreatedAt\" - interval '2 years' "
        "where \"ToStatus\"=9 and \"BookingId\" in (select \"Id\" from bookings where \"GuestEmail\" like '%@vidu.vn' "
        "or \"GuestUserId\" in (select \"Id\" from users where \"Email\" like 'hc%@staylio.vn' or \"Email\" like 'fine%@staylio.vn'))")
    sql("update listings set \"ReviewStatus\"=0, \"ReviewNote\"=null "
        "where \"ReviewNote\" like 'Tạm ẩn: chủ nhà đã huỷ%'")


def l_reviews_say_who_and_count_helpful():
    name = "L13. Đánh giá ghi loại khách + số đêm, nút Hữu ích một phiếu mỗi người"
    row = sql("select r.\"Id\" || '|' || l.\"Slug\" || '|' || coalesce(r.\"AuthorUserId\",0) from reviews r "
              "join listings l on l.\"Id\"=r.\"ListingId\" where r.\"BookingId\" is not null "
              "and r.\"PublishedAt\" is not null order by r.\"Id\" desc limit 1")
    if not row:
        return skip(name, "chưa có đánh giá nào gắn với đơn thật")
    rid, slug, author = row.split("|")
    rid = int(rid)
    anon, _ = call(opener(), "/api/reviews/%d/helpful" % rid, m="POST")
    reader, _ = register("vote%s@staylio.vn" % RUN, "Nguoi Doc")
    v1, r1 = call(reader, "/api/reviews/%d/helpful" % rid, m="POST")
    _, detail = call(reader, "/api/listings/%s" % slug)
    mine = next((r for r in detail["reviews"] if r["id"] == rid), {})
    v2, r2 = call(reader, "/api/reviews/%d/helpful" % rid, m="POST")
    self_vote = None
    if author != "0":
        author_op = sign_in(sql("select \"Email\" from users where \"Id\"=%s" % author))
        self_vote, _ = call(author_op, "/api/reviews/%d/helpful" % rid, m="POST")
    ok(name,
       anon == 401 and v1 == 200 and r1["votedHelpful"] and mine.get("votedHelpful") is True
       and mine.get("helpfulCount") == r1["helpfulCount"] and mine.get("travellerType")
       and mine.get("nights") and v2 == 200 and not r2["votedHelpful"]
       and r2["helpfulCount"] == r1["helpfulCount"] - 1 and self_vote in (None, 404),
       "ẩn danh=%s, bấm=%s→%s, trang thấy=%s/%s/%s đêm, bấm lại=%s, tác giả tự bấm=%s"
       % (anon, v1, r1, mine.get("votedHelpful"), mine.get("travellerLabel"), mine.get("nights"), r2, self_vote))


def main():
    print("Staylio · nghiệm thu đợt soát 17/09/2026 — %s (%s)\n" % (B, "local" if LOCAL else "prod, chỉ HTTP"))
    scenarios = [s_security_headers, s_secure_cookie, s_pay_refuses_unknown_methods,
                 s_catalogue_only_takes_money, s_hosting_links, s_experience_goes_to_gateway,
                 s_shield_is_private, s_verify_link_not_in_response, s_ical_is_public_only,
                 s_stay_details_reach_the_host, s_search_filters_like_booking]
    if LOCAL:
        forget_fixture_cancellations()
        scenarios += [l_cohost_needs_confirmed_email, l_phone_change_resets_confirmation,
                      l_host_cancel_blocks_dates, l_min_nights_keeps_price, l_gift_card_redeemed_once,
                      l_credit_not_spent_twice, l_turnover_back_to_back,
                      l_host_cancel_is_fined, l_service_waits_for_the_provider,
                      l_provider_cancel_refunds_credits_and_fines, l_guest_review_has_three_headings,
                      l_trip_shared_without_the_keys, l_reviews_say_who_and_count_helpful]
    for s in scenarios:
        try:
            s()
        except (Exception, SystemExit) as e:  # a broken fixture is a failure with a name, not a crash
            ok(s.__name__, False, "lỗi script: %r" % e)
    passed = sum(1 for _, p, _ in results if p)
    print("\nKET QUA: %d/%d dat, %d bo qua" % (passed, len(results), len(skipped)))
    sys.exit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
