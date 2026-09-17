"""docs/02 G8, docs/07 §19 — does a co-host's share of the takings actually reach
their bank, and does the owner's side shrink by exactly the same amount?

The customer chose Airbnb's model by name on 03/09/2026, so the scenarios are
theirs: the share comes out of the owner's earnings after the service fee, it is
capped at what the booking made, a stay that shrank pays a share of the smaller
figure, and a share already paid on a stay that was later refunded is taken back
off the next transfers rather than reversed.

Everything here drives the running server and then reads the database. "There is
an endpoint" is what was wrong with twelve other features in this repo — each of
them had one, and none of them had a caller.

    ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web
    STAYHOST_URL=http://localhost:5199 python scripts/cohost_share_acceptance.py
"""
import datetime
import http.cookiejar
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
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


BASE = os.environ.get("STAYHOST_URL", "http://localhost:5199").rstrip("/")
PW = "stayhost123"
RUN = int(datetime.datetime.now().timestamp()) % 1000000

passed, failed = [], []


def opener():
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(op, path, body=None, m=None):
    method = m or ("POST" if body is not None else "GET")
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    try:
        with op.open(req, timeout=40) as res:
            text = res.read().decode("utf-8-sig", "replace")
            if not text.strip():
                return res.status, None
            try:
                return res.status, json.loads(text)
            except json.JSONDecodeError:
                return res.status, {"raw": text[:200]}
    except urllib.error.HTTPError as e:
        text = e.read().decode("utf-8", "replace")
        try:
            return e.code, json.loads(text) if text.strip() else None
        except json.JSONDecodeError:
            return e.code, {"raw": text[:300]}


def check(name, ok, detail=""):
    (passed if ok else failed).append(name)
    print(("  PASS  " if ok else "  FAIL  ") + name + (" — " + detail if detail else ""))


def sql(q):
    out = subprocess.run(
        ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost",
         "-t", "-A", "-c", q],
        capture_output=True, text=True, encoding="utf-8")
    if out.returncode != 0:
        raise SystemExit("psql failed: " + out.stderr)
    return out.stdout.strip()


def num(q):
    return float(sql(q) or 0)


def ledger_off():
    """docs/00 §6.1 — the only acceptable answer is zero, always."""
    return num('select coalesce(sum(case when "Direction"=1 then "Amount" '
               'else -"Amount" end),0) from ledger_entries;')


def future(days):
    return (datetime.date.today() + datetime.timedelta(days=days)).isoformat()


def register(email, name):
    op = opener()
    st, res = call(op, "/api/account/register",
                   {"email": email, "password": PW, "fullName": name,
                    "dateOfBirth": "1990-01-01"})
    if st not in (200, 201):
        raise SystemExit("register %s: %s %s" % (email, st, res))
    # An invitation is claimed by whoever proved the address, so the new account
    # confirms it the way a person does: the six-digit code (devCode in
    # Development) sent to that mailbox.
    st, sent = call(op, "/api/account/send-code", {"kind": "email"})
    code = (sent or {}).get("devCode") if isinstance(sent, dict) else None
    st2, _ = call(op, "/api/account/confirm-code", {"kind": "email", "code": code})
    if st2 != 200:
        raise SystemExit("confirm email %s: %s %s / %s" % (email, st, sent, st2))
    return op, int(sql("""select "Id" from users where "Email"='%s'""" % email))


def wait_for(query, seconds=200):
    """Waits for the once-a-minute worker tick rather than for a fixed nap.

    There is no "run it now" button; the sweeps are steps of one tick, and a row
    that only became due a moment ago needs the next one. Three minutes is two
    ticks even if one has just gone past.
    """
    for _ in range(seconds // 5):
        value = sql(query)
        if value and value not in ("0", "0.00", ""):
            return value
        time.sleep(5)
    return sql(query)


print("Staylio · nghiệm thu chia thu nhập cho người đồng quản lý (docs/07 §19) — %s\n" % BASE)

opening_ledger = ledger_off()

# ---------------------------------------------------------------- the cast ---
print("0. Dựng người")

guest = opener()
st, _ = call(guest, "/api/account/login", {"email": "guest@staylio.vn", "password": PW})
if st != 200:
    raise SystemExit("Không đăng nhập được khách.")

# A brand-new person, so nothing this run does can be confused with an older row.
mate_email = "cohost%d@staylio.vn" % RUN
mate, mate_uid = register(mate_email, "Nguoi Dong Quan Ly")

# --------------------------------------------------- 1: a stay, and an owner ---
print("\n1. Một đơn đã trả tiền, và chủ nhà của nó")

booking = None
for week in range(0, 10):
    at = 40 + week * 7
    _, page = call(guest, "/api/listings?pageSize=60&checkIn=%s&checkOut=%s"
                   % (future(at), future(at + 3)))

    for listing in page.get("items", []):
        if not listing["instantBook"]:
            continue
        st, held = call(guest, "/api/bookings", {
            "listingId": listing["id"], "checkIn": future(at), "checkOut": future(at + 3),
            "guests": 1, "adults": 1, "children": 0, "infants": 0, "pets": 0,
            "guestName": "Khách Demo", "guestEmail": "guest@staylio.vn",
            "agreedToRules": True, "paymentMethod": "card"})
        if st == 201:
            booking = held
            break
    if booking:
        break

if booking is None:
    raise SystemExit("Không giữ được chỗ nào.")

st, paid = gateway.pay(call, guest, booking["id"])
check("Đơn đã thanh toán xong", (paid or {}).get("status") == "Confirmed",
      "%s · %s" % (st, (paid or {}).get("status")))

bid = booking["id"]
host_id = int(sql('select l."HostId" from bookings b join listings l on l."Id"=b."ListingId" '
                  'where b."Id"=%d' % bid))
listing_id = int(sql('select "ListingId" from bookings where "Id"=%d' % bid))
host_email = sql('select u."Email" from users u join hosts h on h."UserId"=u."Id" '
                 'where h."Id"=%d' % host_id)

owner = opener()
call(owner, "/api/account/login", {"email": host_email, "password": PW})

earnings = num('select "HostPayout" from payments where "BookingId"=%d' % bid)
cleaning = num('select "CleaningFee" from bookings where "Id"=%d' % bid)
subtotal = num('select "Subtotal" from bookings where "Id"=%d' % bid)
check("Đọc được thu nhập của chủ nhà cho đơn này", earnings > 0,
      "%.0f₫ (phí dọn dẹp %.0f₫)" % (earnings, cleaning))

# ------------------------------------------- 2: the invite, then the offer ---
print("\n2. Mời đồng quản lý, rồi đề nghị chia thu nhập")

st, invite = call(owner, "/api/host/co-hosts",
                  {"email": mate_email, "listingId": listing_id,
                   "scopes": ["calendar", "messages"]})
check("Mời được người đồng quản lý", st == 200 and invite, "HTTP %s" % st)
cohost_id = (invite or {}).get("id")

# docs/07 §19.2 — terms cannot be offered to somebody who has not agreed to help.
st, res = call(owner, "/api/host/co-hosts/%d/payout" % cohost_id,
               {"kind": "percent", "percent": 20, "amount": 0}, m="PUT")
check("Chưa nhận lời mời thì chưa đề nghị chia tiền được", st == 400,
      "HTTP %s · %s" % (st, (res or {}).get("message", "")[:60]))

# m="POST" spelled out. This helper sends GET when there is no body, and a GET
# into a POST-only route falls through to the SPA fallback — which is exactly
# how three calls in acceptance.py "passed" for months while doing nothing.
call(mate, "/api/host/co-hosts/%d/accept" % cohost_id, m="POST")

# A percentage that is not a round tenth, so a wrong base cannot coincidentally
# produce the right answer.
st, res = call(owner, "/api/host/co-hosts/%d/payout" % cohost_id,
               {"kind": "percent", "percent": 20, "amount": 0}, m="PUT")
check("Chủ nhà đề nghị được", st == 200, "HTTP %s" % st)

status = sql('select "PayoutStatus" from co_hosts where "Id"=%d' % cohost_id)
check("Đề nghị ở trạng thái chờ xác nhận, chưa áp dụng", status == "1",
      "PayoutStatus=%s (1 = Proposed)" % status)

# --------------------------------------------- 3: nothing moves unconfirmed ---
print("\n3. Chưa xác nhận thì không có đồng nào bị chia")

sql('update payments set "PayoutDueOn" = current_date - 1 where "BookingId" = %d' % bid)
sql("""update hosts set "PayoutAccountChangedAt" = now() - interval '5 days',
       "PayoutAccountVerified" = true, "PayoutBankName"='MB Bank',
       "PayoutAccountName"='NGUYEN VAN CHU NHA' where "Id" = %d""" % host_id)

# Give the owner an account the sweep can actually use.
st, _ = call(owner, "/api/host/payout",
             {"bankName": "MB Bank", "accountName": "NGUYEN VAN CHU NHA",
              "accountNumber": "0%d" % (900000000 + RUN), "schedule": "PerBooking"}, m="PUT")
sql("""update hosts set "PayoutAccountChangedAt" = now() - interval '5 days',
       "PayoutAccountVerified" = true where "Id" = %d""" % host_id)

print("     (chờ vòng quét định kỳ, tối đa ~3 phút…)")
ref = wait_for("""select coalesce("PayoutReference", '') from payments where "BookingId"=%d""" % bid)

check("Đã sinh lệnh chuyển cho chủ nhà", bool(ref), ref or "(chưa có)")

shares = int(num('select count(*) from co_host_payouts where "BookingId"=%d' % bid))
share_col = num('select "CoHostShare" from payments where "BookingId"=%d' % bid)
check("Không chia gì khi đề nghị chưa được xác nhận", shares == 0 and share_col == 0,
      "%d dòng chia, CoHostShare=%.0f" % (shares, share_col))

batch_amount = num("""select "Amount" from payout_batches where "Reference"='%s'""" % ref)
check("Chủ nhà nhận trọn thu nhập của mình", abs(batch_amount - earnings) < 1,
      "%.0f₫ ≈ %.0f₫" % (batch_amount, earnings))

# ------------------------------------- 4: confirmed, and a second stay pays ---
print("\n4. Xác nhận rồi thì đơn sau mới chia")

st, _ = call(mate, "/api/host/co-hosts/%d/payout/accept" % cohost_id, m="POST")
check("Người đồng quản lý xác nhận được", st in (200, 204), "HTTP %s" % st)

payee_id = sql('select coalesce("PayeeHostId"::text, \'\') from co_hosts where "Id"=%d' % cohost_id)
check("Nhận lời là có hồ sơ nhận tiền của riêng mình", bool(payee_id),
      "PayeeHostId=%s" % (payee_id or "(chưa có)"))

# A second stay at the same listing, this time with the terms live.
booking2 = None
for week in range(0, 12):
    at = 120 + week * 7
    st, held = call(guest, "/api/bookings", {
        "listingId": listing_id, "checkIn": future(at), "checkOut": future(at + 3),
        "guests": 1, "adults": 1, "children": 0, "infants": 0, "pets": 0,
        "guestName": "Khách Demo", "guestEmail": "guest@staylio.vn",
        "agreedToRules": True, "paymentMethod": "card"})
    if st == 201:
        booking2 = held
        break

if booking2 is None:
    raise SystemExit("Không giữ được chỗ cho đơn thứ hai.")

gateway.pay(call, guest, booking2["id"])
bid2 = booking2["id"]

earnings2 = num('select "HostPayout" from payments where "BookingId"=%d' % bid2)
cleaning2 = num('select "CleaningFee" from bookings where "Id"=%d' % bid2)
subtotal2 = num('select "Subtotal" from bookings where "Id"=%d' % bid2)

# The co-host needs somewhere to be paid, exactly as an owner does.
call(mate, "/api/host/payout",
     {"bankName": "Vietcombank", "accountName": "NGUOI DONG QUAN LY",
      "accountNumber": "0%d" % (700000000 + RUN), "schedule": "PerBooking"}, m="PUT")
sql("""update hosts set "PayoutAccountChangedAt" = now() - interval '5 days',
       "PayoutAccountVerified" = true where "UserId" = %d""" % mate_uid)

sql('update payments set "PayoutDueOn" = current_date - 1 where "BookingId" = %d' % bid2)

print("     (chờ vòng quét định kỳ, tối đa ~3 phút…)")
ref2 = wait_for("""select coalesce("PayoutReference", '') from payments where "BookingId"=%d""" % bid2)
check("Đã sinh lệnh chuyển cho đơn thứ hai", bool(ref2), ref2 or "(chưa có)")

share = num('select coalesce(sum("Amount"),0) from co_host_payouts where "BookingId"=%d' % bid2)

# docs/07 §19.1 — 20% of the earnings with the cleaning fee taken out first. The
# cleaning share is net of the service fee withheld on it, which is why it is
# derived from the subtotal rather than from the raw fee.
cleaning_share = round(earnings2 * min(cleaning2, subtotal2) / subtotal2) if subtotal2 else 0
expected = round((earnings2 - cleaning_share) * 0.20)

check("Phần chia đúng 20% phần không gồm phí dọn dẹp", abs(share - expected) <= 1,
      "%.0f₫, mong đợi %.0f₫" % (share, expected))

owner_batch = num("""select "Amount" from payout_batches where "Reference"='%s'""" % ref2)
check("Lệnh của chủ nhà đã trừ đúng phần đã chia", abs(owner_batch - (earnings2 - share)) < 1,
      "%.0f₫ = %.0f₫ − %.0f₫" % (owner_batch, earnings2, share))

# ------------------------------------------ 5: the co-host's own transfer ---
print("\n5. Phần chia đi trong lệnh chuyển riêng, tới ngân hàng của chính họ")

print("     (chờ vòng quét định kỳ, tối đa ~3 phút…)")
share_ref = wait_for("""select coalesce("PayoutReference", '') from co_host_payouts
                        where "BookingId"=%d""" % bid2)

check("Phần chia có lệnh chuyển riêng", bool(share_ref) and share_ref != ref2,
      "%s (chủ nhà: %s)" % (share_ref or "(chưa có)", ref2))

acct = sql("""select "AccountName" from payout_batches where "Reference"='%s'""" % share_ref) \
    if share_ref else ""
check("Lệnh ghi tên người đồng quản lý, không phải chủ nhà",
      acct == "NGUOI DONG QUAN LY", acct or "(trống)")

share_status = sql('select "Status" from co_host_payouts where "BookingId"=%d' % bid2)
check("Mới là đã lên lệnh, chưa phải đã trả", share_status == "3",
      "Status=%s (3 = Sent)" % share_status)

posted = int(num("""select count(*) from ledger_entries
                    where "TransactionKind"='cohost-payout' and "BookingId"=%d""" % bid2))
check("Chưa ghi bút toán nào trước khi ngân hàng thực hiện", posted == 0,
      "%d bút toán" % posted)

# ------------------------------------------------ 6: the bank, and the books ---
print("\n6. Ngân hàng thực hiện — lúc đó mới ghi sổ")

sql("""delete from one_time_codes where "UserId" in
       (select "Id" from users where "Email"='admin@staylio.vn')""")
admin = opener()
st, res = call(admin, "/api/account/login", {"email": "admin@staylio.vn", "password": PW})
if res and res.get("challenge"):
    if not res.get("devCode"):
        raise SystemExit("Chạy server với ASPNETCORE_ENVIRONMENT=Development.")
    call(admin, "/api/account/two-factor",
         {"challenge": res["challenge"], "code": res["devCode"]})

before = ledger_off()

batch_id = int(num("""select "Id" from payout_batches where "Reference"='%s'""" % share_ref))
st, _ = call(admin, "/api/admin/finance/payout-batches/%d/settled" % batch_id,
             {"note": "nghiem thu chia thu nhap"})
check("Xác nhận ngân hàng đã chuyển", st in (200, 204), "HTTP %s" % st)

share_status = sql('select "Status" from co_host_payouts where "BookingId"=%d' % bid2)
# PayoutStatus.Paid is 1, not the last value in the enum: Sent (3) was appended
# later, and the numbers are in the database. Reading the enum beats guessing it.
check("Phần chia ghi là đã trả", share_status == "1", "Status=%s (1 = Paid)" % share_status)

posted = num("""select coalesce(sum("Amount"),0) from ledger_entries
                where "TransactionKind"='cohost-payout' and "BookingId"=%d
                and "Direction"=1""" % bid2)
check("Bút toán trả người đồng quản lý đúng số", abs(posted - share) < 1,
      "%.0f₫ ≈ %.0f₫" % (posted, share))

check("Sổ sách vẫn cân bằng sau khi chia", abs(ledger_off()) < 0.01,
      "lệch %.2f" % ledger_off())

# The owner's own transfer, settled too — the two postings together must debit
# HostPayable by the whole payout and not a đồng more.
owner_batch_id = int(num("""select "Id" from payout_batches where "Reference"='%s'""" % ref2))
call(admin, "/api/admin/finance/payout-batches/%d/settled" % owner_batch_id,
     {"note": "nghiem thu chia thu nhap"})

host_posted = num("""select coalesce(sum("Amount"),0) from ledger_entries
                     where "TransactionKind"='host-payout' and "BookingId"=%d
                     and "Direction"=1""" % bid2)

check("Hai vế cộng lại đúng bằng thu nhập của đơn",
      abs((host_posted + posted) - earnings2) < 1,
      "%.0f₫ + %.0f₫ = %.0f₫" % (host_posted, posted, earnings2))

check("Sổ sách vẫn cân bằng sau cả hai lệnh", abs(ledger_off()) < 0.01,
      "lệch %.2f" % ledger_off())

# ---------------------------------------- 7: refunded after being paid out ---
print("\n7. Đơn được hoàn tiền sau khi đã chia — thu lại qua nợ sàn")

owed_before = num('select "OwedToPlatform" from hosts where "UserId"=%d' % mate_uid)

# The stay is refunded down to a third. docs/07 §19.4 — the money has left, so
# nothing is reversed; the difference becomes a debt against the next transfers.
sql('update payments set "HostPayout" = round("HostPayout" / 3) where "BookingId"=%d' % bid2)

print("     (chờ vòng quét chia lại, tối đa ~3 phút…)")
clawed = wait_for('select "ClawedBack" from co_host_payouts where "BookingId"=%d' % bid2)

owed_after = num('select "OwedToPlatform" from hosts where "UserId"=%d' % mate_uid)
new_earnings = num('select "HostPayout" from payments where "BookingId"=%d' % bid2)

new_cleaning_share = round(new_earnings * min(cleaning2, subtotal2) / subtotal2) if subtotal2 else 0
still_entitled = round((new_earnings - new_cleaning_share) * 0.20)
expected_claw = share - still_entitled

check("Phần chia thừa được ghi nhận là đã thu lại",
      abs(float(clawed or 0) - expected_claw) <= 2,
      "%.0f₫, mong đợi %.0f₫" % (float(clawed or 0), expected_claw))

check("Khoản thu lại nằm ở nợ sàn của chính người đồng quản lý",
      abs((owed_after - owed_before) - expected_claw) <= 2,
      "%.0f₫ → %.0f₫" % (owed_before, owed_after))

check("Không đảo bút toán nào — tiền đã đi thì đã đi",
      abs(posted - num("""select coalesce(sum("Amount"),0) from ledger_entries
                          where "TransactionKind"='cohost-payout' and "BookingId"=%d
                          and "Direction"=1""" % bid2)) < 1,
      "%.0f₫ giữ nguyên" % posted)

check("Sổ sách vẫn cân bằng sau khi thu lại", abs(ledger_off()) < 0.01,
      "lệch %.2f" % ledger_off())

# ------------------------------------------------- 8: revoking, and lapsing ---
print("\n8. Thu hồi quyền thì dừng chia; đề nghị quá 14 ngày thì hết hiệu lực")

st, lapsed = call(owner, "/api/host/co-hosts",
                  {"email": "lapse%d@staylio.vn" % RUN, "listingId": listing_id,
                   "scopes": ["calendar"]})
lapse_id = (lapsed or {}).get("id")

# Somebody who accepted the help but never answered the money question.
sql('update co_hosts set "Status"=1, "CoHostUserId"=%d where "Id"=%d' % (mate_uid, lapse_id))
call(owner, "/api/host/co-hosts/%d/payout" % lapse_id,
     {"kind": "fixed", "percent": 0, "amount": 300000}, m="PUT")
sql("""update co_hosts set "PayoutProposedAt" = now() - interval '15 days'
       where "Id"=%d""" % lapse_id)

print("     (chờ vòng quét hết hạn, tối đa ~3 phút…)")
for _ in range(40):
    if sql('select "PayoutStatus" from co_hosts where "Id"=%d' % lapse_id) == "4":
        break
    time.sleep(5)

check("Đề nghị quá 14 ngày tự hết hiệu lực",
      sql('select "PayoutStatus" from co_hosts where "Id"=%d' % lapse_id) == "4",
      "PayoutStatus=%s (4 = Expired)" % sql('select "PayoutStatus" from co_hosts where "Id"=%d' % lapse_id))

st, _ = call(owner, "/api/host/co-hosts/%d" % cohost_id, m="DELETE")
after_revoke = sql('select "PayoutStatus" from co_hosts where "Id"=%d' % cohost_id)
check("Thu hồi quyền thì dừng luôn phần chia", after_revoke == "0",
      "PayoutStatus=%s (0 = None)" % after_revoke)

kept = int(num('select count(*) from co_host_payouts where "CoHostId"=%d' % cohost_id))
check("Phần chia của các đơn đã qua vẫn giữ nguyên", kept > 0, "%d dòng" % kept)

# ------------------------------------------------------------------ summary ---
print("\n" + "-" * 62)
print("Sổ sách: mở %.2f · đóng %.2f" % (opening_ledger, ledger_off()))
print("%d đạt · %d không đạt" % (len(passed), len(failed)))
for name in failed:
    print("  FAIL  " + name)

raise SystemExit(1 if failed else 0)
