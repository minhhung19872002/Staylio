"""docs/07 §13 — does the host's money actually get out of the building?

Option A of §13 collects every guest's payment into the platform's own account
and leaves the split to a bulk bank transfer. This walks that whole path on a
running server: a guest pays, the host gives a bank account, the sweep decides a
transfer, an admin downloads the file, and the bank is confirmed. Then it checks
the two things that are easy to get wrong and expensive when you do — that
nothing is posted to the ledger before the bank acted, and that the number in
the file is the host's own account and not a masked one.

    STAYHOST_URL=http://localhost:5199 python scripts/payout_acceptance.py

Needs ASPNETCORE_ENVIRONMENT=Development (for the admin's two-factor code and the
development encryption key) and a database this can move dates around in.
"""
import datetime
import http.cookiejar
import io
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
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
RUN = int(datetime.datetime.now().timestamp()) % 100000

passed, failed = [], []


def opener():
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(op, path, body=None, m=None, raw=False):
    method = m or ("POST" if body is not None else "GET")
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    try:
        with op.open(req, timeout=40) as res:
            body_text = res.read().decode("utf-8-sig", "replace")
            if raw:
                return res.status, body_text
            if not body_text.strip():
                return res.status, None
            try:
                return res.status, json.loads(body_text)
            except json.JSONDecodeError:
                return res.status, {"raw": body_text[:200]}
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
        ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost", "-t", "-A", "-c", q],
        capture_output=True, text=True, encoding="utf-8")
    if out.returncode != 0:
        raise SystemExit("psql failed: " + out.stderr)
    return out.stdout.strip()


def ledger_total():
    return float(sql('select coalesce(sum(case when "Direction"=1 then "Amount" '
                     'else -"Amount" end),0) from ledger_entries;') or 0)


def future(days):
    return (datetime.date.today() + datetime.timedelta(days=days)).isoformat()


print("Staylio · nghiệm thu chuyển tiền cho chủ nhà (docs/07 §13) — %s\n" % BASE)

# --- the cast -----------------------------------------------------------------
guest = opener()
st, _ = call(guest, "/api/account/login", {"email": "guest@staylio.vn", "password": "stayhost123"})
if st != 200:
    raise SystemExit("Không đăng nhập được khách.")

# docs/08 §3 — an admin session is two steps, and only a development build hands
# the second one back over the API.
#
# The cooldown on resending a code is per unused code, so a run that died after
# logging in leaves one behind and the next run is refused a fresh one — and
# ChallengeDtoAsync passes the refusal's empty DevCode through without comment,
# which reads as "not running in Development". Clear the slate first.
sql("""delete from one_time_codes where "UserId" in """
    """(select "Id" from users where "Email"='admin@staylio.vn')""")

admin = opener()
st, res = call(admin, "/api/account/login", {"email": "admin@staylio.vn", "password": "stayhost123"})

if res and res.get("challenge"):
    if not res.get("devCode"):
        raise SystemExit("Không lấy được mã 2 lớp. Chạy server với ASPNETCORE_ENVIRONMENT=Development.")
    call(admin, "/api/account/two-factor",
         {"challenge": res["challenge"], "code": res["devCode"]})

st, me = call(admin, "/api/account/me")
if st != 200 or not me:
    raise SystemExit("Không đăng nhập được admin.")

# --- 1: a stay paid for, and a host with somewhere to be paid -----------------
print("1. Khách trả tiền, chủ nhà khai tài khoản nhận tiền")

booking = host_op = host_id = None

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

# Whose listing it was, so the right host can be paid.
host_id = int(sql('select l."HostId" from bookings b join listings l on l."Id"=b."ListingId" '
                  'where b."Id"=%d' % booking["id"]))
host_email = sql('select u."Email" from users u join hosts h on h."UserId"=u."Id" '
                 'where h."Id"=%d' % host_id)

host_op = opener()
st, _ = call(host_op, "/api/account/login", {"email": host_email, "password": "stayhost123"})

ACCOUNT = "0%d" % (900000000 + RUN)     # deliberately starts with a zero
st, _ = call(host_op, "/api/host/payout",
             {"bankName": "MB Bank", "accountName": "NGUYEN VAN CHU NHA",
              "accountNumber": ACCOUNT, "schedule": "PerBooking"}, m="PUT")

sealed = sql('select coalesce("PayoutAccountSealed", \'\') from hosts where "Id"=%d' % host_id)
last4 = sql('select coalesce("PayoutAccountLast4", \'\') from hosts where "Id"=%d' % host_id)

check("Số tài khoản được lưu", bool(sealed), "%d ký tự đã mã hoá" % len(sealed))
# docs/07 §14.3 — encrypted at rest. A database dump must not read as a bank list.
check("Không đọc được số tài khoản trong cơ sở dữ liệu", ACCOUNT not in sealed,
      sealed[:28] + "…")
check("Chỉ 4 số cuối là để hiện", last4 == ACCOUNT[-4:], last4)

# --- 2: the transfer is decided, and nothing is paid yet ----------------------
print("\n2. Tới hạn thì lên lệnh chuyển — nhưng chưa ghi sổ đồng nào")

# docs/07 §12.1 — payouts fall due the day after check-in, and a freshly changed
# account freezes them for three days. Both are dates, so both are moved.
sql('update payments set "PayoutDueOn" = current_date - 1 where "BookingId" = %d' % booking["id"])
sql('update hosts set "PayoutAccountChangedAt" = now() - interval \'5 days\', '
    '"PayoutAccountVerified" = true where "Id" = %d' % host_id)

before = ledger_total()
payout_rows_before = int(sql('''select count(*) from ledger_entries where "TransactionKind"='host-payout';''') or 0)

# There is no "run it now" button — the payout sweep is one step of the
# once-a-minute worker tick, and the row above only became due a moment ago. So
# this waits for a tick rather than for a fixed number of seconds; three minutes
# is two ticks even if one has just gone past.
#
# And it waits for *this booking's* transfer, not for the newest row in the
# table. A previous run leaves its own batches behind, and "the latest one" then
# reports somebody else's account number as this host's — which reads like the
# encryption is broken when it is the query that is wrong.
print("     (chờ vòng quét định kỳ, tối đa 3 phút…)")

import time

batch_ref = ""

for _ in range(36):
    time.sleep(5)
    batch_ref = sql('select coalesce("PayoutReference", \'\') from payments '
                    'where "BookingId"=%d' % booking["id"])
    if batch_ref:
        break

batch_status = sql('select "Status" from payout_batches where "Reference"=\'%s\'' % batch_ref) \
    if batch_ref else ""

check("Đã sinh lệnh chuyển", bool(batch_ref), batch_ref or "(chưa có)")
check("Lệnh ở trạng thái chờ tải file", batch_status == "0", "Status=%s" % batch_status)

pay_status = sql('select "PayoutStatus" from payments where "BookingId"=%d' % booking["id"])
check("Đơn ghi là đã lên lệnh, chưa phải đã trả", pay_status == "3",
      "PayoutStatus=%s (3 = Sent)" % pay_status)

payout_rows_after = int(sql('''select count(*) from ledger_entries where "TransactionKind"='host-payout';''') or 0)
check("Chưa có bút toán trả chủ nhà nào", payout_rows_after == payout_rows_before,
      "%d → %d" % (payout_rows_before, payout_rows_after))

# The sweep used to pick a Sent row up again once its one-day retry step had
# passed, and put it in a second file — two transfers for one stay once both
# were executed. The retry date is wound back so the next tick would do exactly
# that, and the booking must still belong to the first file only.
batches_before = int(sql('select count(*) from payout_batches where "HostId"=%d' % host_id) or 0)
sql('update payments set "PayoutLastAttemptOn" = current_date - 3 where "BookingId" = %d' % booking["id"])
print("     (chờ thêm một vòng quét…)")
time.sleep(70)
ref_again = sql('select coalesce("PayoutReference", \'\') from payments where "BookingId"=%d' % booking["id"])
batches_after = int(sql('select count(*) from payout_batches where "HostId"=%d' % host_id) or 0)
check("Đơn đã lên lệnh không bị đưa vào lệnh thứ hai",
      ref_again == batch_ref and batches_after == batches_before,
      "mã %s → %s, số lệnh %d → %d" % (batch_ref, ref_again, batches_before, batches_after))

# --- 3: the file a bank will act on -------------------------------------------
print("\n3. File chuyển tiền hàng loạt")

st, csv = call(admin, "/api/admin/finance/payout-batches/file", raw=True)
check("Tải được file", st == 200 and "SoTaiKhoan" in (csv or ""), "HTTP %s" % st)

if st == 200:
    lines = [l for l in csv.strip().split("\n") if l.strip()]

    # Exactly this reference. PO-20260817-1 is a prefix of PO-20260817-10, so a
    # loose match finds an older batch's row and then reports its account number
    # as this host's — a failure that reads like the encryption is broken.
    row = next((l for l in lines[1:] if ('"Staylio %s"' % batch_ref) in l), "")

    # The whole point of the encryption: this is the one place the number is in
    # the clear, and it has to be the real one or the bank pays nobody.
    check("File mang số tài khoản thật, không phải bản che", ACCOUNT in row, row[:80])
    check("Số tài khoản giữ nguyên số 0 đứng đầu", '"%s"' % ACCOUNT in row,
          "phải nằm trong dấu nháy, nếu không Excel ăn mất số 0")
    check("Số tiền là số nguyên đồng", ",%d," % round(float(
        sql('select "Amount" from payout_batches where "Reference"=\'%s\'' % batch_ref))) in row,
        row[:80])
    check("Nội dung chuyển mang mã lệnh", batch_ref in row, row[:80])

    status_now = sql('select "Status" from payout_batches where "Reference"=\'%s\'' % batch_ref)
    check("Tải xong thì lệnh chuyển sang 'đã tải'", status_now == "1", "Status=%s" % status_now)

# --- 4: only the bank's word posts the ledger ---------------------------------
print("\n4. Chỉ khi ngân hàng thực hiện thì mới ghi sổ")

batch_id = sql('select "Id" from payout_batches where "Reference"=\'%s\'' % batch_ref)

st, res = call(admin, "/api/admin/finance/payout-batches/%s/settled" % batch_id,
               {"note": "Ngân hàng đã thực hiện, mã GD 12345"})
check("Xác nhận được", st == 200, "HTTP %s: %s" % (st, res))

pay_status = sql('select "PayoutStatus" from payments where "BookingId"=%d' % booking["id"])
check("Giờ đơn mới ghi là đã trả", pay_status == "1", "PayoutStatus=%s (1 = Paid)" % pay_status)

payout_rows_final = int(sql('''select count(*) from ledger_entries where "TransactionKind"='host-payout';''') or 0)
check("Bút toán trả chủ nhà xuất hiện đúng lúc này",
      payout_rows_final > payout_rows_after, "%d → %d" % (payout_rows_after, payout_rows_final))

check("Sổ vẫn cân", ledger_total() == 0, str(ledger_total()))

# Confirming the same file twice is a thing a tired person does at 6pm.
st, res = call(admin, "/api/admin/finance/payout-batches/%s/settled" % batch_id,
               {"note": "Bấm nhầm lần nữa"})
after_twice = int(sql('''select count(*) from ledger_entries where "TransactionKind"='host-payout';''') or 0)
check("Xác nhận hai lần không trả tiền hai lần", after_twice == payout_rows_final,
      "%d → %d" % (payout_rows_final, after_twice))

# --- 5: two transfers to the same host on the same day ------------------------
# The one that actually bit. References are unique, and the sequence used to be
# counted off PaidOutAt — a column that now means "a bank executed this" and is
# null for a transfer still waiting. So a host owed twice in one day got the same
# reference twice, Postgres refused the second, and because that throw happens
# inside the worker's tick it took every sweep after it down with it. Silently.
print("\n5. Cùng chủ nhà, cùng ngày, hai lần tới hạn")

second = None

# The same listing, so both transfers go to the same host. A long-lived database
# fills the near weeks up, so this looks a long way out and takes whatever is
# free — the scenario is about two transfers on one day, not about which nights.
for week in range(0, 40):
    at = 200 + week * 7
    if second:
        break
    for nights in (2, 1, 3):
        st, held = call(guest, "/api/bookings", {
            "listingId": booking["listingId"], "checkIn": future(at),
            "checkOut": future(at + nights),
            "guests": 1, "adults": 1, "children": 0, "infants": 0, "pets": 0,
            "guestName": "Khách Demo", "guestEmail": "guest@staylio.vn",
            "agreedToRules": True, "paymentMethod": "card"})
        if st == 201:
            second = held
            break

if second is None:
    check("Đặt được đơn thứ hai cùng chủ nhà", False, "hết ngày trống")
else:
    st, paid2 = gateway.pay(call, guest, second["id"])
    check("Đơn thứ hai đã thanh toán", (paid2 or {}).get("status") == "Confirmed",
          str((paid2 or {}).get("status")))

    sql('update payments set "PayoutDueOn" = current_date - 1 where "BookingId" = %d' % second["id"])

    ref2 = ""
    for _ in range(36):
        time.sleep(5)
        ref2 = sql('select coalesce("PayoutReference", \'\') from payments '
                   'where "BookingId"=%d' % second["id"])
        if ref2:
            break

    check("Lô thứ hai được tạo", bool(ref2), ref2 or "(không có — xem log, có thể trùng mã)")
    check("Mã lô thứ hai khác mã lô đầu", ref2 != batch_ref, "%s vs %s" % (batch_ref, ref2))
    check("Sổ vẫn cân sau lô thứ hai", ledger_total() == 0, str(ledger_total()))

# --- 6: the bank statement, read against what the platform says it sent -------
# docs/07 §15.4 — until this existed, "the bank paid the host" rested entirely on
# a person pressing a button, and that button is the only thing that posts the
# payout. A statement is the bank's own word, and §7 asks for exactly this on the
# incoming side.
print("\n6. Đối chiếu sao kê với lệnh đã chuyển")

# Downloading exports whatever is still pending, which is what makes a batch
# eligible to be confirmed by a statement at all.
call(admin, "/api/admin/finance/payout-batches/file", raw=True)

waiting_ref = sql("""select "Reference" from payout_batches where "Status"=1 order by "Id" desc limit 1""")

if not waiting_ref:
    print("     (bỏ qua: không còn lệnh nào đang chờ ngân hàng)")
else:
    waiting_amount = float(sql("""select "Amount" from payout_batches where "Reference"='%s'""" % waiting_ref))
    before = int(sql("""select count(*) from ledger_entries where "TransactionKind"='host-payout';""") or 0)

    # Wrong amount first, then right: the same reference must be refused once and
    # accepted once, in that order, or the test proves nothing about either.
    st, res = call(admin, "/api/admin/finance/payout-batches/reconcile", {
        "note": "Đối chiếu sao kê VIB ngày hôm nay",
        "lines": [
            {"bankReference": "FT0001", "amount": waiting_amount - 1000,
             "description": "CK Staylio %s" % waiting_ref},
            {"bankReference": "FT0002", "amount": 3000000,
             "description": "THANH TOAN TIEN DIEN THANG 8"},
            {"bankReference": "FT0003", "amount": waiting_amount,
             "description": "CK Staylio %s tra chu nha" % waiting_ref},
        ]})

    check("Đối chiếu chạy được", st == 200, "HTTP %s: %s" % (st, res))

    rows = (res or {}).get("rows", [])
    verdicts = [r["verdict"] for r in rows]

    check("Sai số tiền thì không ghi sổ", verdicts[:1] == ["WrongAmount"], str(verdicts))
    check("Khoản chi khác của công ty không bị nhận vơ",
          len(verdicts) > 1 and verdicts[1] == "Unidentified", str(verdicts))
    check("Đúng mã đúng tiền thì xác nhận", "Transferred" in verdicts, str(verdicts))
    check("Đếm đúng số lệnh đã chốt", (res or {}).get("settled") == 1, str((res or {}).get("settled")))

    status_now = sql("""select "Status" from payout_batches where "Reference"='%s'""" % waiting_ref)
    check("Lệnh chuyển sang 'ngân hàng đã chuyển'", status_now == "2", "Status=%s" % status_now)

    after = int(sql("""select count(*) from ledger_entries where "TransactionKind"='host-payout';""") or 0)
    check("Bút toán trả chủ nhà được ghi ở đây", after > before, "%d → %d" % (before, after))
    check("Sổ vẫn cân sau đối chiếu", ledger_total() == 0, str(ledger_total()))

    # Pasting the same day twice is what a tired person does at 6pm.
    st, again = call(admin, "/api/admin/finance/payout-batches/reconcile", {
        "note": "Dán lại đúng sao kê đó",
        "lines": [{"bankReference": "FT0003", "amount": waiting_amount,
                   "description": "CK Staylio %s tra chu nha" % waiting_ref}]})

    twice = int(sql("""select count(*) from ledger_entries where "TransactionKind"='host-payout';""") or 0)
    check("Dán lại sao kê không trả tiền hai lần", twice == after, "%d → %d" % (after, twice))
    check("Dòng cũ được nhận ra là đã xác nhận trước đó",
          [r["verdict"] for r in (again or {}).get("rows", [])] == ["AlreadySeen"],
          str((again or {}).get("rows")))

    # The half a statement cannot show: what is missing from it.
    check("Có danh sách lệnh ngân hàng chưa xác nhận",
          "stillAwaitingBank" in (again or {}), str(list((again or {}).keys())))

print("\n%d đạt · %d hỏng" % (len(passed), len(failed)))
for f in failed:
    print("  hỏng: " + f)
sys.exit(1 if failed else 0)
