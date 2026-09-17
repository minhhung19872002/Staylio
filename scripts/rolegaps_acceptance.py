# The gaps the guest/host role audit of 28/08/2026 found: rules that were
# written, tested and reachable by no screen at all, plus the two places where
# the words on the page did not match the rules underneath.
#
# Every scenario drives the real server and then reads the database, because
# "there is an endpoint" is exactly what was wrong before — each of these had
# one, and none of them had a caller.
#
#   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web
#   python scripts/rolegaps_acceptance.py
import os
import json
import datetime
import http.cookiejar
import subprocess
import time
import urllib.error
import urllib.request

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
OFFSET = 200 + int(RUN) % 150
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
        ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost",
         "-t", "-A", "-c", q],
        capture_output=True, text=True, encoding="utf-8")
    if out.returncode != 0:
        raise SystemExit("psql failed: " + out.stderr)
    return out.stdout.strip()


def utc_today():
    # docs/09 note — psql in the container runs Asia/Ho_Chi_Minh and the server
    # judges by DateTime.UtcNow. Seven hours a day of false failures otherwise.
    return datetime.date.fromisoformat(sql("select (now() at time zone 'utc')::date"))


def ok(name, passed, detail=""):
    results.append((name, passed, detail))
    print(("PASS " if passed else "FAIL ") + name + (" - " + detail if detail else ""))


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
        raise SystemExit("register %s: %s %s" % (email, st, res))
    return op, int(sql("select \"Id\" from users where \"Email\"='%s'" % email))


def make_admin(slug, scope=31):
    email = "%s%s@staylio.vn" % (slug, RUN)
    _, uid = register(email, "Kiem tra " + slug)
    sql('update users set "Role"=2, "AdminScope"=%d, "TwoFactorEnabled"=true where "Id"=%d'
        % (scope, uid))
    return sign_in(email), uid


def ledger_off():
    return sql('select coalesce(sum(case when "Direction"=1 then "Amount" '
               'else -"Amount" end),0) from ledger_entries;')


def esc(s):
    return s.replace("'", "''")


# ----------------------------------------------------------------- TK-06
def upload_identity_photo(op, label):
    """docs/08 §4 — the real uploader, which stores the file outside the web root
    and answers with the /api/identity-files/ address that serves it.

    This suite used to hand /api/account/identity three made-up /uploads/ URLs
    because that is what the validator wanted. It passed, and the product was
    broken: the uploader has never answered that shape, so every real submission
    was refused with a complaint about the guest's own photo. A fixture that
    invents its input can only test the validator against itself."""
    # A one-pixel PNG is a real image as far as the content-type check goes.
    png = bytes([
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82])

    boundary = "----staylio%s%s" % (RUN, label)
    body = (
        ("--%s\r\n" % boundary).encode()
        + ('Content-Disposition: form-data; name="files"; filename="%s.png"\r\n' % label).encode()
        + b"Content-Type: image/png\r\n\r\n" + png + b"\r\n"
        + ("--%s--\r\n" % boundary).encode())

    r = urllib.request.Request(
        B + "/api/uploads/identity", data=body,
        headers={"Content-Type": "multipart/form-data; boundary=" + boundary},
        method="POST")
    try:
        return json.loads(op.open(r).read().decode())["urls"][0]
    except urllib.error.HTTPError as e:
        raise SystemExit("upload identity: %s %s" % (e.code, e.read().decode()[:200]))


def s1_identity_queue():
    """A guest uploads papers; before this nothing on any screen could approve
    them, so IsIdentityVerified could never turn true and docs/01 ĐP-03 / ĐP-10
    both read a flag that no path set."""
    op, uid = register("kyc%s@staylio.vn" % RUN, "Khach xac minh")

    front = upload_identity_photo(op, "front")
    back = upload_identity_photo(op, "back")
    selfie = upload_identity_photo(op, "selfie")

    # docs/08 §4 — the papers are stored outside the web root, so the uploader
    # answers with the guarded route rather than the public /uploads/ folder.
    stored_privately = all(u.startswith("/api/identity-files/") for u in (front, back, selfie))

    st, res = call(op, "/api/account/identity", {
        "document": "NationalId", "documentNumber": "079090001234",
        "frontImageUrl": front, "backImageUrl": back, "selfieImageUrl": selfie})
    if st not in (200, 201, 204):
        return ok("TK-06 hang cho xac minh danh tinh", False, "submit %s %s" % (st, res))

    admin, _ = make_admin("kycadmin")
    st, queue = call(admin, "/api/admin/identity")
    mine = [r for r in (queue or []) if r["userId"] == uid]
    if st != 200 or not mine:
        return ok("TK-06 hang cho xac minh danh tinh", False,
                  "queue %s, %d dong cua minh" % (st, len(mine)))

    st, _ = call(admin, "/api/admin/identity/%d/decide" % mine[0]["id"],
                 {"approve": True, "note": "Giay to ro rang"})

    verified = sql('select "IsIdentityVerified" from users where "Id"=%d' % uid)
    status = sql('select "Status" from identity_checks where "UserId"=%d' % uid)
    ok("TK-06 nop giay to that roi duoc duyet",
       stored_privately and st in (200, 204) and verified == "t" and status == "1",
       "luu ngoai wwwroot=%s, decide %s, IsIdentityVerified=%s, Status=%s"
       % (stored_privately, st, verified, status))


# ----------------------------------------------------------------- ĐG-07
def s2_host_reply(host_op, host_uid, guest_op):
    """docs/01 ĐG-07. HostReply rendered on the listing page from the first day
    and there was nowhere to write one, so it never rendered anything.

    The review is written straight into the table on a listing this run's host
    owns, rather than picked out of whatever the seed or another suite left
    behind: the newest unanswered review on a shared database belongs to
    somebody else's scenario as often as not."""
    lid = sql('''select l."Id" from listings l join hosts h on h."Id"=l."HostId"
                 where h."UserId"=%d and l."IsPublished"=true order by l."Id" limit 1''' % host_uid)
    if not lid:
        return ok("DG-07 chu nha tra loi danh gia", False, "chu nha nay khong co tin dang nao")
    lid = int(lid)

    sql('''insert into reviews
           ("ListingId","AuthorName","AuthorInitials","When","Text","Rating",
            "Cleanliness","Accuracy","CheckIn","Communication","Location","Value",
            "PublishedAt","CreatedAt","Language")
           values (%d,'Khach soat DG07','KS','Thang 8, 2026',
                   'Chuyen di rat vui, chu nha ho tro nhiet tinh.',5,
                   5,5,5,5,5,5, now(), now(), 'vi')''' % lid)
    rid = int(sql('select max("Id") from reviews where "ListingId"=%d' % lid))

    st, rows = call(host_op, "/api/host/reviews")
    if st != 200:
        return ok("DG-07 chu nha tra loi danh gia", False, "list %s" % st)

    mine = [r for r in rows if r["id"] == rid]
    if not mine or not mine[0]["canReply"]:
        return ok("DG-07 chu nha tra loi danh gia", False,
                  "danh gia vua tao khong nam trong danh sach tra loi duoc")

    st, _ = call(host_op, "/api/host/reviews/%d/reply" % rid,
                 {"text": "Cam on ban da o lai, hen gap lai!"})
    stored = sql('select coalesce("HostReply",\'\') from reviews where "Id"=%d' % rid)

    # docs/03 §7 — one reply only. A second attempt must be refused, not
    # allowed to quietly overwrite the answer guests have already read.
    st2, _ = call(host_op, "/api/host/reviews/%d/reply" % rid, {"text": "Mot lan nua thi sao"})

    # And it must now read as answered, so no screen offers the box again.
    st3, again = call(host_op, "/api/host/reviews")
    row = next((r for r in (again or []) if r["id"] == rid), None)

    ok("DG-07 chu nha tra loi danh gia",
       st in (200, 204) and stored.startswith("Cam on") and st2 == 409
       and row and row["canReply"] is False and row["hostReply"],
       "reply %s, luu=%r, lan hai=%s, canReply sau=%s"
       % (st, stored[:20], st2, row and row["canReply"]))


def s3_experience_slots():
    """docs/01 MR-02. AddSlots and CancelSlot were complete; no screen called
    them, so a host who listed an experience had nothing to sell."""
    xid = int(sql('select "Id" from experiences where "IsPublished"=true order by "Id" limit 1'))
    owner = int(sql('select u."Id" from experiences x join hosts h on h."Id"=x."HostId" '
                    'join users u on u."Id"=h."UserId" where x."Id"=%d' % xid))
    op = sign_in(sql('select "Email" from users where "Id"=%d' % owner))

    before = int(sql('select count(*) from experience_slots where "ExperienceId"=%d' % xid))

    # Both halves of this scenario are placed past every session this experience
    # already has, rather than at a fixed offset from today. docs/09 §2.5 makes
    # an exact repeat of an existing start idempotent, so a second run at the
    # same offset adds nothing — correct behaviour that a count-based check
    # reads as a failure. Three runs in a row did exactly that.
    def horizon():
        return datetime.date.fromisoformat(sql(
            'select coalesce(max("StartsAt")::date, (now() at time zone \'utc\')::date) '
            'from experience_slots where "ExperienceId"=%d' % xid))

    # One session by hand.
    at = datetime.datetime.combine(horizon() + datetime.timedelta(days=3), datetime.time(3, 0))
    st, _ = call(op, "/api/experiences/%d/slots" % xid,
                 {"startsAt": [at.isoformat() + "Z"], "capacity": 6})
    one = int(sql('select count(*) from experience_slots where "ExperienceId"=%d' % xid)) - before

    # A weekly pattern, read on the host's own clock (docs/09 §2.5).
    #
    # The window starts past every session this experience already has, rather
    # than at a fixed offset. An exact repeat of an existing start is idempotent
    # by design, so a run that lands on Mondays a previous run already created
    # gets fewer new rows and a count-based check reads a working rule as a
    # failure. Two runs in a row did exactly that.
    start = horizon() + datetime.timedelta(days=7)
    monday = start + datetime.timedelta(days=(0 - start.weekday()) % 7)
    pattern = {"capacity": 6, "repeatWeekdayMask": 1, "repeatAt": "09:00:00",
               "repeatFrom": monday.isoformat(), "repeatWeeks": 3}

    # Rows this call made, not rows in the window: earlier runs against the same
    # database leave their own Mondays behind, and counting the window reads
    # their sessions as duplicates of ours.
    high = int(sql('select coalesce(max("Id"),0) from experience_slots'))
    st2, _ = call(op, "/api/experiences/%d/slots" % xid, pattern)
    weekly = sql('select to_char("StartsAt" at time zone \'utc\', \'YYYY-MM-DD HH24\') '
                 'from experience_slots where "ExperienceId"=%d and "Id" > %d '
                 'order by "StartsAt"' % (xid, high))
    rows = [r for r in weekly.split("\n") if r]

    # Three Mondays, and 09:00 in Ho Chi Minh City is 02:00Z. Stamped as UTC it
    # would be 09:00Z and every session the host offered would sit at four in
    # the afternoon — the same seven hours that broke the services picker.
    mondays = all(datetime.date.fromisoformat(r[:10]).weekday() == 0 for r in rows)
    local_ok = rows and all(r.endswith(" 02") for r in rows)

    # docs/09 §2.5 — the same pattern again is one session, not two.
    st3, _ = call(op, "/api/experiences/%d/slots" % xid, pattern)
    after_repeat = sql('select count(*) from experience_slots where "ExperienceId"=%d '
                       'and "Id" > %d' % (xid, high))
    idempotent = int(after_repeat) == len(rows)

    sid = int(sql('select "Id" from experience_slots where "ExperienceId"=%d '
                  'order by "Id" desc limit 1' % xid))
    st4, _ = call(op, "/api/experiences/slots/%d?reason=Doi%%20lich" % sid, m="DELETE")
    gone = sql('select "Status" from experience_slots where "Id"=%d' % sid)

    ok("MR-02 chu nha tao va huy suat trai nghiem",
       st in (200, 201) and one == 1 and len(rows) == 3 and mondays and local_ok
       and idempotent and gone != "0",
       "them 1 => %d, lap 3 tuan => %d suat, deu thu Hai=%s, gio UTC=%s, lap lai khong nhan doi=%s, huy => Status=%s"
       % (one, len(rows), mondays, ",".join(r[-2:] for r in rows), idempotent, gone))


def s4_price_match(guest_op):
    """docs/01 MR-10. Neither side had a screen — and approving one wrote the
    ledger and the booking but never a CreditEntry, so the notification promised
    the guest money their balance never saw."""
    # IsHotel is computed from Type in the entity, so the column does not exist;
    # PlaceType.Hotel is 7 (docs/01 MR-08).
    hotel = sql('select "Id" from listings where "Type"=7 and "IsPublished"=true limit 1')
    if not hotel:
        return ok("MR-10 cam ket gia tot", False, "khong co khach san nao")
    lid = int(hotel)

    today = utc_today()
    ci = (today + datetime.timedelta(days=OFFSET)).isoformat()
    co = (today + datetime.timedelta(days=OFFSET + 2)).isoformat()
    rt = sql('select "Id" from room_types where "ListingId"=%d order by "Id" limit 1' % lid)

    body = {"listingId": lid, "checkIn": ci, "checkOut": co,
            "adults": 2, "children": 0, "infants": 0, "pets": 0, "agreedToRules": True}
    if rt:
        body["roomTypeId"] = int(rt)

    st, res = call(guest_op, "/api/bookings", body)
    if st not in (200, 201):
        return ok("MR-10 cam ket gia tot", False, "book %s %s" % (st, res))
    bid = res["id"]
    st, pay = gateway.pay(call, guest_op, bid, {"paymentMethod": "card", "cardLast4": "4242"})
    if st not in (200, 201):
        return ok("MR-10 cam ket gia tot", False, "pay %s %s" % (st, pay))

    uid = int(sql('select "GuestUserId" from bookings where "Id"=%d' % bid))
    before = sql('select coalesce(sum("Amount"),0) from credit_entries where "UserId"=%d' % uid)

    # The trip page only offers the form when the server says it would take it.
    st, trip = call(guest_op, "/api/bookings/%d" % bid)
    offered = bool(trip and trip.get("canPriceMatch"))

    nightly = float(sql('select round("RoomBeforeDiscount"/"Nights") from bookings where "Id"=%d' % bid))
    st, claim = call(guest_op, "/api/bookings/%d/price-match" % bid, {
        "competitorUrl": "https://vi.example-booking.test/phong/%d" % lid,
        "competitorNightlyRate": max(1, int(nightly * 0.8))})
    if st not in (200, 201):
        return ok("MR-10 cam ket gia tot", False, "submit %s %s" % (st, claim))

    admin, _ = make_admin("pmadmin")
    st, queue = call(admin, "/api/admin/price-matches")
    mine = [r for r in (queue or []) if r["bookingId"] == bid]
    st2, _ = call(admin, "/api/admin/price-matches/%d/approve" % claim["id"],
                  {"resolution": "Da xac minh gia ben kia"})

    after = sql('select coalesce(sum("Amount"),0) from credit_entries where "UserId"=%d' % uid)
    granted = float(after) - float(before)
    expiry = sql('select count(*) from credit_entries where "UserId"=%d and "ExpiresAt" is not null'
                 % uid)

    ok("MR-10 cam ket gia tot, so du khach thuc su tang",
       offered and mine and st2 in (200, 204)
       and abs(granted - float(claim["difference"])) < 1 and int(expiry) > 0,
       "moi=%s, hang cho=%d, duyet=%s, so du +%.0f (bu %.0f), co han dung=%s"
       % (offered, len(mine), st2, granted, float(claim["difference"]), expiry))


# ----------------------------------------------------------------- ĐG-08
def s5_edit_review(guest_op):
    """docs/01 ĐG-08. PUT worked; nothing showed a guest what they had written,
    so nothing could offer to change it."""
    # A stay that is over and has not been reviewed. On a freshly reset database
    # there may be none, so one confirmed booking is moved past its check-out —
    # the same fixture-setting the other suites do.
    row = sql('select b."Id" from bookings b where b."Status"=4 and b."GuestUserId" is not null '
              'and not exists (select 1 from reviews r where r."BookingId"=b."Id") '
              # A host review already there publishes the guest's at once, and a
              # published review is past editing — another suite may have left one.
              'and not exists (select 1 from guest_reviews g where g."BookingId"=b."Id") '
              'order by b."Id" desc limit 1')
    if not row:
        row = sql('select "Id" from bookings where "Status"=2 and "GuestUserId" is not null '
                  'order by "Id" desc limit 1')
        if not row:
            return ok("DG-08 sua danh gia trong 48 gio", False, "khong co don nao de danh gia")
        sql('''update bookings set "CheckIn" = (now() at time zone 'utc')::date - 4,
               "CheckOut" = (now() at time zone 'utc')::date - 1, "Status" = 4
               where "Id"=%s''' % row)

    bid = int(row)
    uid = int(sql('select "GuestUserId" from bookings where "Id"=%d' % bid))
    op = sign_in(sql('select "Email" from users where "Id"=%d' % uid))

    st, res = call(op, "/api/bookings/%d/review" % bid, {
        "bookingId": bid, "rating": 5, "text": "Phong sach, chu nha de thuong, se quay lai.",
        "cleanliness": 5, "accuracy": 5, "checkIn": 5,
        "communication": 5, "location": 5, "value": 5, "language": "vi"})
    if st not in (200, 201):
        return ok("DG-08 sua danh gia trong 48 gio", False, "post %s %s" % (st, res))

    # docs/01 TĐ-11 — the language the writer was reading the site in is stored,
    # not guessed, when the client says so.
    lang_on_write = sql('select coalesce("Language",\'?\') from reviews where "BookingId"=%d' % bid)

    st, mine = call(op, "/api/bookings/%d/review" % bid)
    if st != 200:
        return ok("DG-08 sua danh gia trong 48 gio", False, "read %s" % st)

    st2, _ = call(op, "/api/bookings/%d/review" % bid, {
        "bookingId": bid, "rating": 4, "text": "Sua lai cho ro rang hon, phong rat sach se.",
        "cleanliness": 4, "accuracy": 4, "checkIn": 4,
        "communication": 4, "location": 4, "value": 4, "language": "vi"}, m="PUT")

    text = sql('select "Text" from reviews where "BookingId"=%d' % bid)

    # Past the window it must be refused rather than quietly accepted.
    sql('update reviews set "EditableUntil" = now() - interval \'1 hour\' where "BookingId"=%d' % bid)
    st3, _ = call(op, "/api/bookings/%d/review" % bid, {
        "bookingId": bid, "rating": 1, "text": "Doi y sau khi het han, khong duoc phep.",
        "cleanliness": 1, "accuracy": 1, "checkIn": 1,
        "communication": 1, "location": 1, "value": 1}, m="PUT")
    st4, closed = call(op, "/api/bookings/%d/review" % bid)
    unchanged = sql('select "Text" from reviews where "BookingId"=%d' % bid)

    ok("DG-08 sua danh gia trong 48 gio",
       lang_on_write == "vi" and mine.get("canEdit") and st2 in (200, 204)
       and text.startswith("Sua lai") and st3 == 400
       and closed.get("canEdit") is False and unchanged == text,
       "Language=%s, canEdit=%s, put=%s, qua han=%s, canEdit sau=%s"
       % (lang_on_write, mine.get("canEdit"), st2, st3, closed.get("canEdit")))


def s6_coupons():
    """docs/01 TC-09. The checkout has always had a "Mã giảm giá" box; with no
    screen to create a campaign there was never a code it could accept."""
    admin, _ = make_admin("couponadmin")
    code = "AUDIT" + RUN

    st, made = call(admin, "/api/admin/coupons", {
        "code": code, "campaign": "Soat vai tro 2026", "kind": "Percentage",
        "value": 10, "maxDiscount": 300000, "maxRedemptions": 50})
    st2, listed = call(admin, "/api/admin/coupons")
    present = any(c["code"] == code for c in (listed or []))

    # No body, so the method has to be said out loud: call() sends GET otherwise.
    st3, _ = call(admin, "/api/admin/coupons/%d/deactivate" % made["id"], m="POST")
    active = sql('select "IsActive" from coupons where "Code"=\'%s\'' % esc(code))

    ok("TC-09 quan tri tao va ngung ma giam gia",
       st in (200, 201) and present and st3 in (200, 204) and active == "f",
       "tao=%s, co trong danh sach=%s, ngung=%s, IsActive=%s" % (st, present, st3, active))


# ----------------------------------------------------------------- ĐP-15
def s7_calendar_file(guest_op):
    """docs/01 ĐP-15 — the one code of docs/01 with no implementation at all."""
    # A booking with an account behind it: guestcheckout_acceptance leaves
    # confirmed stays that nobody can sign in to.
    bid = int(sql('select "Id" from bookings where "Status"=2 and "GuestUserId" is not null '
                  'order by "Id" desc limit 1'))
    uid = int(sql('select "GuestUserId" from bookings where "Id"=%d' % bid))
    op = sign_in(sql('select "Email" from users where "Id"=%d' % uid))

    st, text = call(op, "/api/bookings/%d/calendar.ics" % bid)
    ref = sql('select "Reference" from bookings where "Id"=%d' % bid)

    body = text if isinstance(text, str) else ""
    ok("DP-15 them chuyen di vao lich ca nhan",
       st == 200 and "BEGIN:VCALENDAR" in body and "DTEND;VALUE=DATE:" in body and ref in body,
       "st=%s, %d byte, co ma don=%s" % (st, len(body), ref in body))


# ----------------------------------------------------------------- ĐP-07
def s8_split_view(guest_op):
    """docs/01 ĐP-07. A split could be opened and then never looked at again:
    every share link lived only in an email, on a deployment that has no SMTP."""
    lid = int(sql('select "Id" from listings where "IsPublished"=true and "Type"<>7 '
                  'and "InstantBook"=true order by "Id" limit 1'))
    # docs/01 QL-06 — a host only opens the calendar so far ahead (a year by
    # default), so this window is kept well inside it. OFFSET on its own reached
    # 390 days on some runs and the booking was refused for CalendarHorizon,
    # which reads exactly like a broken split.
    today = utc_today()
    near = 30 + int(RUN) % 60
    st, res = call(guest_op, "/api/bookings", {
        "listingId": lid,
        "checkIn": (today + datetime.timedelta(days=near)).isoformat(),
        "checkOut": (today + datetime.timedelta(days=near + 2)).isoformat(),
        "adults": 2, "children": 0, "infants": 0, "pets": 0, "agreedToRules": True})
    if st not in (200, 201):
        return ok("DP-07 xem va huy chia hoa don", False, "book %s %s" % (st, res))
    bid = res["id"]

    st, split = call(guest_op, "/api/bookings/%d/split" % bid,
                     {"emails": ["ban1%s@staylio.vn" % RUN, "ban2%s@staylio.vn" % RUN]})
    if st not in (200, 201):
        return ok("DP-07 xem va huy chia hoa don", False, "open %s %s" % (st, split))

    st2, read = call(guest_op, "/api/bookings/%d/split" % bid)
    links = [s for s in (read or {}).get("shares", []) if s.get("link")]

    st3, _ = call(guest_op, "/api/bookings/%d/split" % bid, m="DELETE")
    status = sql('select "Status" from bill_splits where "BookingId"=%d' % bid)

    ok("DP-07 xem va huy chia hoa don",
       st2 == 200 and len(links) == 3 and st3 in (200, 204) and status != "0",
       "doc lai=%s, %d lien ket, huy=%s, Status=%s" % (st2, len(links), st3, status))


# ----------------------------------------------------------------- TĐ-11/21
def s9_review_insights():
    """docs/01 TĐ-11 and TĐ-21 — the language filter and the theme summary.

    The seeded reviews are drawn from a rotating pool, so no subject reaches the
    three separate mentions a theme needs — which is the rule working, not a
    fault. Three reviews that do talk about the same thing are written straight
    into the table here, one of them in English with no stored language, so the
    read path is exercised over both a recorded language and a guessed one."""
    lid = int(sql('select "Id" from listings where "IsPublished"=true order by "Id" desc limit 1'))
    slug = sql('select "Slug" from listings where "Id"=%d' % lid)

    texts = [
        ("Vi tri qua tien, di bo ra bien mat nam phut.", "vi", 5.0),
        ("Vị trí đẹp, gần trung tâm và rất sạch sẽ.", "vi", 4.0),
        ("The location is perfect and the place was very clean.", None, 5.0),
    ]
    for i, (text, lang, rating) in enumerate(texts):
        sql('''insert into reviews
               ("ListingId","AuthorName","AuthorInitials","When","Text","Rating",
                "Cleanliness","Accuracy","CheckIn","Communication","Location","Value",
                "PublishedAt","CreatedAt","Language")
               values (%d,'Khach soat %d','KS','Thang 8, 2026','%s',%s,
                       5,5,5,5,5,5, now(), now(), %s)'''
            % (lid, i, esc(text), rating, ("'%s'" % lang) if lang else "null"))

    st, d = call(opener(), "/api/listings/%s" % slug)
    if st != 200:
        return ok("TD-11/TD-21 loc ngon ngu va tom tat chu de", False, "detail %s" % st)

    langs = [r.get("language") for r in d["reviews"]]
    themes = {t["key"]: t for t in (d.get("reviewThemes") or [])}

    # Every review carries a language: the two written through the site say so,
    # and the English one with nothing stored is read from its own characters.
    every_one_answered = all(langs)
    guessed_english = "en" in langs and "vi" in langs

    # "Vị trí" and "location" reach three separate reviews; the rule needs three.
    location = themes.get("location")

    ok("TD-11/TD-21 loc ngon ngu va tom tat chu de",
       every_one_answered and guessed_english and location and location["mentions"] >= 3,
       "%d danh gia, ngon ngu=%s, chu de=%s"
       % (len(d["reviews"]), sorted(set(x for x in langs if x)),
          ", ".join("%s(%d, %.1f)" % (k, v["mentions"], v["rating"]) for k, v in themes.items())))


def s10_feature_flags():
    """docs/01 QT-08. The rollout was computed server-side from the first day and
    nothing ever asked for it, so every switch an admin set moved nothing."""
    st, flags = call(opener(), "/api/features")
    admin, _ = make_admin("flagadmin")

    key = "price-match"
    st2, _ = call(admin, "/api/admin/feature-flags",
                  {"key": key, "description": "Cam ket gia tot", "enabled": False,
                   "rolloutPercent": 0})
    st3, off = call(opener(), "/api/features")
    call(admin, "/api/admin/feature-flags",
         {"key": key, "description": "Cam ket gia tot", "enabled": True, "rolloutPercent": 100})
    st4, back = call(opener(), "/api/features")

    dead = sql("select count(*) from feature_flags where \"Key\" in ('new-map-search','ai-trip-ideas')")

    ok("QT-08 co tinh nang co nguoi doc",
       st == 200 and isinstance(flags, dict) and key in flags
       and off.get(key) is False and back.get(key) is True and dead == "0",
       "bat=%s, tat=%s, bat lai=%s, co chet con lai=%s"
       % (flags.get(key), off.get(key), back.get(key), dead))


# ----------------------------------------------------------------- TK-12
def s11_pause_account():
    """docs/01 TK-12 — "tạm vô hiệu hoá hoặc xoá tài khoản". The erase half has
    existed for months; this one had no column, no endpoint and no button, and
    the code counted as done because one clause of an "hoặc" was there."""
    email = "pause%s@staylio.vn" % RUN
    op, uid = register(email, "Khach tam dung")

    # A host with a place on sale, so the effect is visible in the database
    # rather than only in a flag.
    call(op, "/api/account/become-host")
    st, made = call(op, "/api/host/listings", {
        "title": "Nha thu nghiem tam dung %s" % RUN,
        "city": "Đà Nẵng", "description": "Mo ta du dai cho qua kiem tra toi thieu bon muoi ky tu.",
        "pricePerNight": 900000, "maxGuests": 4, "bedrooms": 1, "beds": 2, "bathrooms": 1,
        "minNights": 1, "typeKey": "villa", "roomTypeKey": "entire",
        "cancellationTier": "Moderate",
        "images": ["/uploads/a.jpg", "/uploads/b.jpg", "/uploads/c.jpg",
                   "/uploads/d.jpg", "/uploads/e.jpg"],
        "amenityKeys": [], "isPublished": True})
    if st not in (200, 201):
        return ok("TK-12 tam dung tai khoan", False, "listing %s %s" % (st, made))
    lid = made["id"]
    sql('update listings set "IsPublished"=true, "ReviewStatus"=0 where "Id"=%d' % lid)

    st, before = call(op, "/api/account/pause")
    st2, paused = call(op, "/api/account/pause", {})

    hidden = sql('select "IsPublished" || \'|\' || (case when "HiddenByPauseAt" is null '
                 'then \'no\' else \'yes\' end) from listings where "Id"=%d' % lid)
    flag = sql('select case when "PausedAt" is null then \'no\' else \'yes\' end '
               'from users where "Id"=%d' % uid)

    # docs/01 TK-12 — the public profile is not somewhere a paused person is
    # still on the platform.
    st3, _ = call(opener(), "/api/users/%d" % uid)

    # Signing in is the whole gesture that ends it (AccountPause.ResumesOnSignIn).
    back = sign_in(email)
    st4, state = call(back, "/api/account/pause")
    restored = sql('select "IsPublished" || \'|\' || (case when "HiddenByPauseAt" is null '
                   'then \'no\' else \'yes\' end) from listings where "Id"=%d' % lid)

    ok("TK-12 tam dung tai khoan roi quay lai",
       before.get("canPause") and st2 == 200 and paused.get("isPaused")
       and hidden == "false|yes" and flag == "yes" and st3 == 404
       and state.get("isPaused") is False and restored == "true|no",
       "cho phep=%s, tin dang khi dung=%s, ho so cong khai=%s, sau khi dang nhap lai=%s"
       % (before.get("canPause"), hidden, st3, restored))


def s12_pause_refused_while_a_stay_is_live(guest_op):
    """A pause that could strand a booked guest is not a pause, it is a
    disappearance. Refused with the count said out loud."""
    lid = int(sql('select "Id" from listings where "IsPublished"=true and "Type"<>7 '
                  'and "InstantBook"=true order by "Id" limit 1'))
    today = utc_today()
    near = 90 + int(RUN) % 60
    st, res = call(guest_op, "/api/bookings", {
        "listingId": lid,
        "checkIn": (today + datetime.timedelta(days=near)).isoformat(),
        "checkOut": (today + datetime.timedelta(days=near + 2)).isoformat(),
        "adults": 2, "children": 0, "infants": 0, "pets": 0, "agreedToRules": True})
    if st not in (200, 201):
        return ok("TK-12 khong tam dung khi con don hieu luc", False, "book %s %s" % (st, res))

    st2, pay = gateway.pay(call, guest_op, res["id"], {"paymentMethod": "card", "cardLast4": "4242"})
    if st2 not in (200, 201):
        return ok("TK-12 khong tam dung khi con don hieu luc", False, "pay %s %s" % (st2, pay))

    st3, state = call(guest_op, "/api/account/pause")
    st4, refused = call(guest_op, "/api/account/pause", {})

    still = sql('select count(*) from users u where u."PausedAt" is not null and u."Id"='
                '(select "GuestUserId" from bookings where "Id"=%d)' % res["id"])

    ok("TK-12 khong tam dung khi con don hieu luc",
       state.get("canPause") is False and st4 == 400
       and refused.get("reason") == "HasLiveBookings" and still == "0",
       "canPause=%s, POST=%s, ly do=%s, van chua bi dung=%s"
       % (state.get("canPause"), st4, refused.get("reason"), still == "0"))


# ----------------------------------------------------------------- docs/02 H1
def s13_my_reviews():
    """docs/02 H1 — the three groups. Every piece existed and none of it was
    gathered: a stay could be reviewed only from its own trip page, what you had
    written could not be read back without opening each trip, and what hosts said
    about you was visible only on your own public profile."""
    uid = int(sql('select "GuestUserId" from bookings b where "GuestUserId" is not null '
                  'and exists (select 1 from reviews r where r."BookingId"=b."Id" '
                  'and r."AuthorUserId"=b."GuestUserId") order by b."Id" desc limit 1'))
    op = sign_in(sql('select "Email" from users where "Id"=%d' % uid))

    st, d = call(op, "/api/account/reviews")
    if st != 200:
        return ok("H1 trang danh gia ba nhom", False, "st %s" % st)

    written = d.get("written") or []
    todo = d.get("toWrite") or []

    # docs/03 §7 — an unpublished review is the writer's to see and nobody
    # else's, so the "about me" group must never carry one.
    about_all_public = all(r["isPublic"] for r in (d.get("aboutMe") or []))
    # docs/01 ĐG-02 — the fourteen days are the point of the "cần viết" group.
    deadlines_sane = all(0 < r["daysLeft"] <= 14 for r in todo)
    mine_only = int(sql('select count(*) from reviews where "AuthorUserId"=%d' % uid)) >= len(
        [r for r in written if r["wouldHostAgain"] is None])

    ok("H1 trang danh gia ba nhom",
       written and about_all_public and deadlines_sane and mine_only,
       "can viet=%d, da viet=%d, ve toi=%d, ve toi deu da cong khai=%s, han hop le=%s"
       % (len(todo), len(written), len(d.get("aboutMe") or []),
          about_all_public, deadlines_sane))


def main():
    print("=" * 70)
    print("Soat vai tro khach & chu nha — cac quy tac tung khong co duong goi toi")
    print("=" * 70)

    before = ledger_off()

    guest_op, _ = register("soat%s@staylio.vn" % RUN, "Khach soat vai tro")
    host_op = sign_in("host1@staylio.vn")
    host_uid = int(sql("select \"Id\" from users where \"Email\"='host1@staylio.vn'"))

    s1_identity_queue()
    s2_host_reply(host_op, host_uid, guest_op)
    s3_experience_slots()
    s4_price_match(guest_op)
    s5_edit_review(guest_op)
    s6_coupons()
    s7_calendar_file(guest_op)
    s8_split_view(guest_op)
    s9_review_insights()
    s10_feature_flags()
    s11_pause_account()
    s12_pause_refused_while_a_stay_is_live(guest_op)
    s13_my_reviews()

    after = ledger_off()
    ok("So sach van can bang", abs(float(after)) < 0.01,
       "truoc=%s, sau=%s" % (before, after))

    passed = sum(1 for _, p, _ in results if p)
    print()
    print("=" * 70)
    print("KET QUA: %d/%d dat" % (passed, len(results)))
    raise SystemExit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
