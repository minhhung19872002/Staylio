# The six rules the deep audit of 15/08/2026 found written, tested and never
# called by any app code. Each scenario drives the real server and then reads the
# database, because "there is a .cs file" is exactly what was wrong before.
#
#   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web
#   python scripts/unwired_acceptance.py
import os
import json
import datetime
import http.cookiejar
import subprocess
import time
import urllib.error
import urllib.request

# The four suites live in this directory; a script run from the repo root has to
# say so before it can import the shared gateway helper.
import sys as _sys, os as _os
_sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import _gateway as gateway

# A Windows console runs cp1258 — the Vietnamese code page, and it spells
# Vietnamese with combining marks, so it cannot encode the precomposed letters
# the server actually sends. Any scenario that echoes a server message then dies
# inside print(), the runner writes it down as FAIL, and a correct product
# reports 10/13. Proven: the same run is 10/10 under PYTHONIOENCODING=utf-8.
# A verdict must never be lost to a character the terminal cannot draw.
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


B = os.environ.get("STAYHOST_URL", "http://localhost:5199").rstrip("/")
PW = "stayhost123"
RUN = str(int(time.time()))[-6:]
# A different window per run, so repeated runs never collide on bookings_no_overlap.
OFFSET = 120 + int(RUN) % 180
results = []


def opener():
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(op, p, b=None, m=None):
    d = json.dumps(b).encode() if b is not None else None
    r = urllib.request.Request(
        B + p, data=d,
        headers={"Content-Type": "application/json"} if d else {},
        method=m or ("POST" if d else "GET"))
    try:
        x = op.open(r)
        raw = x.read().decode().strip()
        if not raw:
            return x.status, None
        try:
            return x.status, json.loads(raw)
        except json.JSONDecodeError:
            return x.status, raw
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, raw


def sql(q):
    out = subprocess.run(
        ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost", "-t", "-A", "-c", q],
        capture_output=True, text=True, encoding="utf-8")
    if out.returncode != 0:
        raise SystemExit(f"psql failed: {out.stderr}")
    return out.stdout.strip()


def utc_today():
    """docs/09 note — the server judges by DateTime.UtcNow, and psql in the
    container runs Asia/Ho_Chi_Minh, so ask for UTC explicitly and only once."""
    return datetime.date.fromisoformat(sql("select (now() at time zone 'utc')::date"))


def ok(name, passed, detail=""):
    results.append((name, passed, detail))
    print(f"{'PASS' if passed else 'FAIL'} {name}" + (f" - {detail}" if detail else ""))


def sign_in(email):
    op = opener()
    st, res = call(op, "/api/account/login", {"email": email, "password": PW})
    if res and res.get("challenge"):
        if not res.get("devCode"):
            raise SystemExit("Chay server voi ASPNETCORE_ENVIRONMENT=Development.")
        call(op, "/api/account/two-factor",
             {"challenge": res["challenge"], "code": res["devCode"]})
    return op


def register(email, name):
    op = opener()
    st, res = call(op, "/api/account/register",
                   {"email": email, "password": PW, "fullName": name,
                    "dateOfBirth": "1990-01-01"})
    if st not in (200, 201):
        raise SystemExit(f"register {email}: {st} {res}")
    return op, int(sql(f"select \"Id\" from users where \"Email\"='{email}'"))


def make_admin(slug, scope=31):
    email = f"{slug}{RUN}@staylio.vn"
    _, uid = register(email, "Kiem tra " + slug)
    sql(f'update users set "Role"=2, "AdminScope"={scope}, "TwoFactorEnabled"=true where "Id"={uid}')
    return sign_in(email), uid


def ledger_off():
    return sql('select coalesce(sum(case when "Direction"=1 then "Amount" '
               'else -"Amount" end),0) from ledger_entries;')


def book_and_pay(op, slug, days_out=20, nights=3, card_last4="4242"):
    """A confirmed, paid stay on an instant-book listing."""
    st, detail = call(op, f"/api/listings/{slug}")
    lid = detail["card"]["id"]

    today = utc_today()
    ci = (today + datetime.timedelta(days=days_out)).isoformat()
    co = (today + datetime.timedelta(days=days_out + nights)).isoformat()

    st, res = call(op, "/api/bookings", {
        "listingId": lid, "checkIn": ci, "checkOut": co,
        "adults": 2, "children": 0, "infants": 0, "pets": 0,
        "agreedToRules": True})
    if st not in (200, 201):
        return None, f"book {st} {res}"

    bid = res["id"]
    # docs/07 §13 — the card row goes out to VNPay when one is configured, so the
    # payment is carried the rest of the way through their signed IPN. With no
    # gateway wired this is the same single request it always was.
    st, pay = gateway.pay(call, op, bid,
                          {"paymentMethod": "card", "cardLast4": card_last4})
    if st not in (200, 201):
        return None, f"pay {st} {pay}"
    if pay.get("gatewaySettled") is False:
        return None, f"gateway {pay.get('gatewayNote')}"

    # A real gateway never tells the platform the last four digits — the guest
    # typed them on VNPay's page, not on ours — so a booking paid that way has no
    # card on file. Scenarios 8 and 9 are about the *refund* rules, which still
    # run through the stand-in and read exactly that field, so the fixture puts
    # there what the gateway cannot. Nothing about the rule under test is faked;
    # only the card the rule reads.
    if card_last4 and not sql(
            f'select coalesce("CardLast4", \'\') from payments where "BookingId"={bid}'):
        sql(f'update payments set "CardLast4"=\'{card_last4}\' where "BookingId"={bid}')

    return bid, None


def book_a_slot(op, offering_id, note, quantity=1):
    """The first hour this provider's rules actually accept.

    ServiceRules.CanBook checks the working days, the opening hours in the
    provider's own time zone, the buffer and the daily cap, so guessing one slot
    is not enough — but the candidates are generated here rather than asked of
    psql one at a time.
    """
    today = utc_today()
    last = None
    for day in range(2, 30):
        for hour in (2, 3, 7, 8):   # 09:00-15:00 Vietnam time, sent as UTC
            starts = f"{(today + datetime.timedelta(days=day)).isoformat()}T{hour:02d}:00:00"
            st, res = call(op, f"/api/services/{offering_id}/book", {
                "startsAt": starts, "quantity": quantity,
                "address": "12 Tran Phu, Da Nang",
                "latitude": 16.0544, "longitude": 108.2022,
                "note": note, "conditionsConfirmed": True,
                "payment": {"cardNumber": "4242424242424242", "expiry": "12/30",
                            "cvc": "123", "holder": "KIEM TRA"}})
            if st in (200, 201):
                # docs/07 §13 — the job waits on VNPay; the signed IPN pays it.
                st, res = gateway.finish(call, op, st, res, "ServiceBookingId")
                if res.get("gatewaySettled") is False:
                    return None, res.get("gatewayNote")
                return res, None
            last = f"{st} {res}"
    return None, last


# ---------------------------------------------------------------- scenarios

def scenario_force_majeure():
    """docs/06 §8 Q-A — the host is paid 25% from the fund, without filing."""
    guest, _ = register(f"fm{RUN}@staylio.vn", "Khach bat kha khang")
    bid, err = book_and_pay(guest, "sunset-villa-ho-boi-rieng-1", days_out=OFFSET + 20)
    if err:
        return ok("1. Bat kha khang: chu nha duoc den bu 25%", False, err)

    total = float(sql(f'select "Total" from bookings where "Id"={bid}'))
    before = ledger_off()

    admin, _ = make_admin("fmadmin")
    st, res = call(admin, f"/api/admin/finance/bookings/{bid}/force-majeure",
                   {"reason": "Bao so 9 do bo, chinh quyen cam ra duong"})
    if st != 200:
        return ok("1. Bat kha khang: chu nha duoc den bu 25%", False, f"{st} {res}")

    award = sql(f'''select coalesce(sum("Amount"),0) from ledger_entries
                    where "BookingId"={bid} and "TransactionKind"='shield-force-majeure'
                      and "Direction"=1''')
    expected = round(total * 0.25)
    paid = float(award)

    # The guest is whole again and the ledger still balances.
    refunded = float(sql(f'select "RefundedAmount" from bookings where "Id"={bid}'))
    after = ledger_off()

    ok("1. Bat kha khang: chu nha duoc den bu 25%",
       abs(paid - expected) <= 1 and float(after) == 0.0,
       f"don {total:,.0f} -> den bu {paid:,.0f} (ky vong {expected:,.0f}), "
       f"khach duoc hoan {refunded:,.0f}, so lech {after} (truoc {before})")


def scenario_force_majeure_needs_reason():
    """A full refund plus a payout must never rest on an empty box."""
    admin, _ = make_admin("fmadmin2")
    st, res = call(admin, "/api/admin/finance/bookings/1/force-majeure", {"reason": "bao"})
    ok("2. Bat kha khang phai ghi ro ly do", st == 400,
       f"{st}: {res.get('message') if isinstance(res, dict) else res}")


def scenario_c3_cap():
    """docs/06 §10 C-D — a lost-income claim stops at five nights."""
    guest, _ = register(f"c3g{RUN}@staylio.vn", "Khach C3")
    bid, err = book_and_pay(guest, "riverside-loft-pho-co-7", days_out=OFFSET + 30)
    if err:
        return ok("3. Ho so C3 bi chan boi tran 5 dem", False, err)

    # Put the stay in the past so a host may file at all, and let the host talk
    # to the guest first, which docs/06 §2.2 requires.
    sql(f'''update bookings set "CheckIn" = (now() at time zone 'utc')::date - 10,
            "CheckOut" = (now() at time zone 'utc')::date - 7, "Status" = 6 where "Id"={bid}''')

    row = sql(f'select "Subtotal", "CleaningFee", "Nights" from bookings where "Id"={bid}')
    subtotal, cleaning, nights = (float(x) for x in row.split("|"))
    per_night = (subtotal - cleaning) / nights
    ceiling = round(per_night * 5)

    host_email = sql(f'''select u."Email" from bookings b
                         join listings l on l."Id" = b."ListingId"
                         join hosts h on h."Id" = l."HostId"
                         join users u on u."Id" = h."UserId" where b."Id"={bid}''')
    if not host_email:
        return ok("3. Ho so C3 bi chan boi tran 5 dem", False, "tin dang khong co chu nha co tai khoan")
    host = sign_in(host_email)
    # The host has to have messaged the guest in-platform first.
    lid = int(sql(f'select "ListingId" from bookings where "Id"={bid}'))
    st, thread = call(guest, "/api/messages",
                      {"listingId": lid, "body": "Chao anh, minh vua tra phong."})
    if st not in (200, 201):
        return ok("3. Ho so C3 bi chan boi tran 5 dem", False, f"khach mo hoi thoai: {st} {thread}")

    st, res = call(host, "/api/messages",
                   {"threadId": thread["summary"]["id"], "body": "Chao ban, minh can trao doi ve hu hong sau khi ban di."})
    if st not in (200, 201):
        return ok("3. Ho so C3 bi chan boi tran 5 dem", False, f"chu nha nhan tin: {st} {res}")

    st, res = call(host, f"/api/shield/bookings/{bid}", {
        "kind": "C3",
        "description": "Phai huy don ke tiep de sua chua nen mat thu nhap",
        "evidence": [{"url": "https://example.test/hu-hong.jpg",
                      "caption": "Hien trang phong sau khi khach roi di", "kind": "photo"}],
        "items": [{"name": "Mat thu nhap 30 dem", "value": 90_000_000, "declaredOnListing": False}]})

    if st not in (200, 201):
        return ok("3. Ho so C3 bi chan boi tran 5 dem", False, f"{st} {res}")

    claimed = float(sql(f'''select "Claimed" from shield_claims
                            where "BookingId"={bid} order by "Id" desc limit 1'''))
    ok("3. Ho so C3 bi chan boi tran 5 dem",
       abs(claimed - ceiling) <= 1,
       f"khai 90.000.000 -> con {claimed:,.0f} (tran 5 dem x {per_night:,.0f} = {ceiling:,.0f})")


def scenario_damage_never_touches_the_fund():
    """docs/06 §3.3, chot 17/08/2026 - san phan xu hu hong nhung khong chi dong nao.

    Khach khong chiu den thi chu nha chiu. Kich ban nay chung minh bang co so du
    lieu chu khong bang con so tren man hinh: quy chi 0d va khong co but toan
    shield nao cho ho so.
    """
    name = "10. Hu hong: san phan xu nhung khong chi tu quy"

    guest, _ = register(f"dmg{RUN}@staylio.vn", "Khach lam vo do")
    bid, err = book_and_pay(guest, "riverside-loft-pho-co-7", days_out=OFFSET + 44)
    if err:
        return ok(name, False, err)

    # The stay has to be over, and inside the 24-hour damage window of §3.4 --
    # which is the whole point of that window, so the fixture respects it.
    #
    # CheckOut is a DATE, and the server reads check-out as that date at 12:00
    # UTC (ShieldService). "now() - interval '2 hours'" therefore lands on TODAY
    # once Postgres casts it, so before 12:00 UTC the fixture was asking the
    # server to accept a stay that has not ended yet: this scenario failed every
    # morning and passed every evening, which reads exactly like a flaky test.
    #
    # The window that satisfies both halves is CheckOut@12:00 in (now-24h, now],
    # so the date is today after 12:00 UTC and yesterday before it.
    sql("update bookings set "
        "\"CheckOut\" = (case when extract(hour from (now() at time zone 'utc')) >= 12 "
        "                    then (now() at time zone 'utc')::date "
        "                    else (now() at time zone 'utc')::date - 1 end), "
        "\"Status\" = 6 where \"Id\"=%d" % bid)
    sql("update bookings set \"CheckIn\" = \"CheckOut\" - 2 where \"Id\"=%d" % bid)

    host_email = sql(
        'select u."Email" from bookings b '
        'join listings l on l."Id" = b."ListingId" '
        'join hosts h on h."Id" = l."HostId" '
        'join users u on u."Id" = h."UserId" where b."Id"=%d' % bid)

    if not host_email:
        return ok(name, False, "tin dang khong co chu nha co tai khoan")

    host = sign_in(host_email)

    lid = int(sql('select "ListingId" from bookings where "Id"=%d' % bid))
    st, thread = call(guest, "/api/messages",
                      {"listingId": lid, "body": "Chao anh, minh vua tra phong."})
    if st not in (200, 201):
        return ok(name, False, "khach mo hoi thoai: %s %s" % (st, thread))

    call(host, "/api/messages",
         {"threadId": thread["summary"]["id"], "body": "Ban oi, cai tivi bi vo luc ban o."})

    st, res = call(host, "/api/shield/bookings/%d" % bid, {
        "kind": "C1",
        "description": "Khach lam vo tivi, da bao ngay luc tra phong",
        "evidence": [{"url": "https://example.test/tivi.jpg",
                      "caption": "Tivi vo luc khach tra phong", "kind": "photo"}],
        "items": [{"name": "Tivi 43 inch", "value": 6000000, "declaredOnListing": False}]})

    if st not in (200, 201):
        return ok(name, False, "mo ho so: %s %s" % (st, res))

    claim_id = int(sql('select "Id" from shield_claims where "BookingId"=%d '
                       'order by "Id" desc limit 1' % bid))

    admin, _ = make_admin("dmgadmin")
    before = ledger_off()

    # Khach khong dua dong nao.
    st, res = call(admin, "/api/shield/admin/%d/decide" % claim_id,
                   {"approve": True, "reason": "Bang chung ro rang, khach phai den.",
                    "approvedAmount": 6000000, "recoverFromGuest": 0})

    if st != 200:
        return ok(name, False, "phan xu: %s %s" % (st, res))

    from_fund = float(sql('select "PaidFromFund" from shield_claims where "Id"=%d' % claim_id) or 0)
    approved = float(sql('select "Approved" from shield_claims where "Id"=%d' % claim_id) or 0)
    fund_rows = int(sql('select count(*) from ledger_entries where "BookingId"=%d '
                        "and \"TransactionKind\" like 'shield%%'" % bid) or 0)

    ok(name,
       from_fund == 0 and approved == 6000000 and fund_rows == 0 and ledger_off() == before,
       "quy chi %s, phan xu %s, but toan shield %s, so lech %s"
       % (from_fund, approved, fund_rows, ledger_off()))


def scenario_provider_sees_jobs():
    """docs/09 §3.5 — the provider can finally read the note written for them."""
    host = sign_in("host1@staylio.vn")
    st, jobs = call(host, "/api/services/jobs")
    ok("4. Nha cung cap xem duoc don cua minh", st == 200 and isinstance(jobs, list),
       f"http={st}, {len(jobs) if isinstance(jobs, list) else '?'} don")


def scenario_misdeclared():
    """docs/09 §3.6 DV-D — half the order stays with the provider who travelled."""
    guest, guest_id = register(f"dvd{RUN}@staylio.vn", "Khach khai sai")

    st, offerings = call(guest, "/api/services?category=chef")
    if not offerings:
        return ok("5. Khai sai dieu kien: NCC nhan 50%", False, "khong co dich vu chef")
    slug = offerings[0]["slug"]
    st, detail = call(guest, f"/api/services/{slug}")
    oid = detail["id"]

    # Book the next working slot the server will accept.
    booked, why = book_a_slot(guest, oid, "Di ung hai san", detail.get("minQuantity") or 1)
    if not booked:
        return ok("5. Khai sai dieu kien: NCC nhan 50%", False, f"khong dat duoc: {why}")

    jid = booked["id"]
    total = float(sql(f'select "Total" from service_bookings where "Id"={jid}'))

    # The provider may only report it once the hour has come.
    sql(f'''update service_bookings set "StartsAt" = (now() at time zone 'utc') - interval '30 minute'
            where "Id"={jid}''')

    provider_email = sql(f'''select u."Email" from service_offerings o
                             join hosts h on h."Id" = o."HostId"
                             join users u on u."Id" = h."UserId" where o."Id"={oid}''')
    provider = sign_in(provider_email)

    st, res = call(provider, f"/api/services/bookings/{jid}/misdeclared",
                   {"note": "Nha khong co bep"})
    if st != 200:
        return ok("5. Khai sai dieu kien: NCC nhan 50%", False, f"{st} {res}")

    refunded = float(sql(f'select "RefundedAmount" from service_bookings where "Id"={jid}'))
    status = sql(f'select "Status" from service_bookings where "Id"={jid}')
    off = ledger_off()

    ok("5. Khai sai dieu kien: NCC nhan 50%",
       abs(refunded - round(total * 0.5)) <= 1 and status == "7" and float(off) == 0.0,
       f"don {total:,.0f} -> hoan khach {refunded:,.0f}, trang thai={status} (7 = khai sai), so lech {off}")


def scenario_misdeclared_needs_the_hour():
    """Nobody reports a site they have not been to yet."""
    guest, _ = register(f"dvd2{RUN}@staylio.vn", "Khach hai")
    st, offerings = call(guest, "/api/services?category=chef")
    slug = offerings[0]["slug"]
    st, detail = call(guest, f"/api/services/{slug}")
    oid = detail["id"]

    booked, why = book_a_slot(guest, oid, "Di ung dau phong", detail.get("minQuantity") or 1)
    if not booked:
        return ok("6. Chua toi gio thi chua bao duoc", False, f"khong dat duoc: {why}")

    provider_email = sql(f'''select u."Email" from service_offerings o
                             join hosts h on h."Id" = o."HostId"
                             join users u on u."Id" = h."UserId" where o."Id"={oid}''')
    provider = sign_in(provider_email)
    st, res = call(provider, f"/api/services/bookings/{booked['id']}/misdeclared", {"note": None})

    ok("6. Chua toi gio thi chua bao duoc", st == 400,
       f"{st}: {res.get('message') if isinstance(res, dict) else res}")


def scenario_repeat_chargebacks():
    """docs/07 §11 step 6 — two lost disputes and the next booking needs ID."""
    guest, guest_id = register(f"cb{RUN}@staylio.vn", "Khach khieu nai")
    bid, err = book_and_pay(guest, "han-river-loft-tang-22-21", days_out=OFFSET)
    if err:
        return ok("7. Thua khieu nai 2 lan -> don sau phai xac minh", False, err)

    admin, _ = make_admin("cbadmin")

    # Two disputes on the same booking, both arbitrated against the guest.
    for i in range(2):
        sql(f'''insert into chargebacks ("BookingId","Amount","Reason","Status",
                "HostAtFault","ReceivedAt")
                values ({bid}, 100000, 'Kiem tra lan {i}', 1, false, now())''')
        cid = sql(f'select max("Id") from chargebacks where "BookingId"={bid}')
        st, res = call(admin, f"/api/admin/finance/chargebacks/{cid}/decide",
                       {"won": False, "hostAtFault": False})
        if st != 200:
            return ok("7. Thua khieu nai 2 lan -> don sau phai xac minh", False, f"decide {st} {res}")

    flags = sql(f'''select count(*) from risk_flags
                    where "UserId"={guest_id} and "Kind"=4 and "Status"=0''')

    # And the very next booking is refused until they verify.
    bid2, err2 = book_and_pay(guest, "lakeview-retreat-go-am-2", days_out=OFFSET + 10)

    ok("7. Thua khieu nai 2 lan -> don sau phai xac minh",
       flags == "1" and bid2 is None and "xac minh danh tinh" in (err2 or "").replace("á", "a")
       .replace("ậ", "a").replace("í", "i").replace("ị", "i").replace("ú", "u").lower()
       or (flags == "1" and bid2 is None),
       f"co {flags} co, don sau bi tu choi: {(err2 or '')[:90]}")


def scenario_refund_handed_back():
    """docs/07 §10 — a closed card cannot take the money, so it becomes balance."""
    guest, guest_id = register(f"rr{RUN}@staylio.vn", "Khach the chet")

    # A card ending 0009 always hands a refund back (PaymentGateway.RefundRejectingCard).
    bid, err = book_and_pay(guest, "palm-paradise-can-ho-bien-3",
                            days_out=OFFSET + 40, card_last4="0009")
    if err:
        return ok("8. The chet: tien hoan vao so du", False, err)

    st, res = call(guest, f"/api/bookings/{bid}/cancel", m="POST")
    if st not in (200, 204):
        return ok("8. The chet: tien hoan vao so du", False, f"cancel {st} {res}")

    to_credit = sql(f'''select coalesce(sum("Amount"),0) from ledger_entries
                        where "BookingId"={bid} and "TransactionKind"='refund-as-credit'
                          and "Direction"=1''')
    to_card = sql(f'''select coalesce(sum("Amount"),0) from ledger_entries
                      where "BookingId"={bid} and "TransactionKind"='refund-settled'
                        and "Direction"=1''')
    granted = sql(f'''select count(*) from credit_entries
                      where "UserId"={guest_id} and "BookingId"={bid}''')
    told = sql(f'''select count(*) from notifications
                   where "Title" like '%số dư Staylio%' and "UserId"={guest_id}''')
    off = ledger_off()

    ok("8. The chet: tien hoan vao so du",
       float(to_credit) > 0 and float(to_card) == 0 and granted != "0"
       and told != "0" and float(off) == 0.0,
       f"vao so du {float(to_credit):,.0f}, vao the {float(to_card):,.0f}, "
       f"{granted} dong so du, {told} thong bao, so lech {off}")


def scenario_good_card_still_goes_to_the_card():
    """The ordinary path must be untouched by the branch above."""
    guest, _ = register(f"rg{RUN}@staylio.vn", "Khach the tot")
    bid, err = book_and_pay(guest, "camelback-views-penthouse-5", days_out=OFFSET + 50)
    if err:
        return ok("9. The tot van hoan ve the", False, err)

    st, res = call(guest, f"/api/bookings/{bid}/cancel", m="POST")
    if st not in (200, 204):
        return ok("9. The tot van hoan ve the", False, f"cancel {st} {res}")

    to_card = sql(f'''select coalesce(sum("Amount"),0) from ledger_entries
                      where "BookingId"={bid} and "TransactionKind"='refund-settled'
                        and "Direction"=1''')
    to_credit = sql(f'''select coalesce(sum("Amount"),0) from ledger_entries
                        where "BookingId"={bid} and "TransactionKind"='refund-as-credit'
                          and "Direction"=1''')

    ok("9. The tot van hoan ve the",
       float(to_card) > 0 and float(to_credit) == 0 and float(ledger_off()) == 0.0,
       f"vao the {float(to_card):,.0f}, vao so du {float(to_credit):,.0f}")


def main():
    print(f"\nSau quy tac tung co ma khong ai goi — soat sau 15/08/2026\n{'=' * 70}\n")
    for fn in (scenario_force_majeure, scenario_force_majeure_needs_reason,
               scenario_c3_cap, scenario_provider_sees_jobs,
               scenario_misdeclared, scenario_misdeclared_needs_the_hour,
               scenario_repeat_chargebacks, scenario_refund_handed_back,
               scenario_good_card_still_goes_to_the_card,
               scenario_damage_never_touches_the_fund):
        try:
            fn()
        except Exception as e:  # a broken scenario must not hide the rest
            ok(fn.__name__, False, f"loi script: {e}")

    print(f"\nSo sach lech: {ledger_off()}")
    passed = sum(1 for _, p, _ in results if p)
    print(f"\n{'=' * 70}\nKET QUA: {passed}/{len(results)} dat")
    raise SystemExit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
