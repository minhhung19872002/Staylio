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
    for x in items[:10]:
        _, d = call(opener(), "/api/experiences/%s" % x["slug"])
        for s in (d or {}).get("slots", []):
            starts = datetime.datetime.fromisoformat(s["startsAt"].replace("Z", "+00:00"))
            if starts - datetime.datetime.now(datetime.timezone.utc) > datetime.timedelta(days=3) \
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
    op = sign_in("khach6@staylio.vn") if not LOCAL else register("sh%s@staylio.vn" % RUN, "Nguoi La")[0]
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
    op = sign_in("khach6@staylio.vn")
    if op is None:
        return skip("H8. Link xác thực email không nằm trong phản hồi", "không đăng nhập được")
    st, r = call(op, "/api/account/send-verification", m="POST")
    link = (r or {}).get("verifyLink") if isinstance(r, dict) else None
    ok("H8. Link xác thực email không nằm trong phản hồi", st == 200 and not link, "http=%s link=%s" % (st, link))


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


def main():
    print("Staylio · nghiệm thu đợt soát 17/09/2026 — %s (%s)\n" % (B, "local" if LOCAL else "prod, chỉ HTTP"))
    scenarios = [s_security_headers, s_secure_cookie, s_pay_refuses_unknown_methods,
                 s_catalogue_only_takes_money, s_hosting_links, s_experience_goes_to_gateway,
                 s_shield_is_private, s_verify_link_not_in_response, s_ical_is_public_only]
    if LOCAL:
        scenarios += [l_cohost_needs_confirmed_email, l_phone_change_resets_confirmation,
                      l_host_cancel_blocks_dates, l_min_nights_keeps_price, l_gift_card_redeemed_once,
                      l_credit_not_spent_twice, l_turnover_back_to_back]
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
