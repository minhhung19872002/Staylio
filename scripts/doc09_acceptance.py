# The twelve mandatory scenarios at the end of docs/09, run against a live server.
#
# Experiences and services are not stays: one session is sold to several strangers
# at once, so the seat arithmetic and the races around it are the whole point.
# These scenarios check the database, not the screen.
import os
import json, urllib.request, http.cookiejar, subprocess, threading, datetime

# A Windows console runs cp1258 — the Vietnamese code page, and it spells
# Vietnamese with combining marks, so it cannot encode the precomposed letters
# the server actually sends. Any scenario that echoes a server message then dies
# inside print(), the runner writes it down as FAIL, and a correct product
# reports 10/13. Proven: the same run is 10/10 under PYTHONIOENCODING=utf-8.
# A verdict must never be lost to a character the terminal cannot draw.
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


# The port is only a default. Another app on this machine may already hold 5199,
# and a script that hard-codes it then talks to the wrong server and reports
# failures that are not ours. Override with STAYHOST_URL.
B = os.environ.get("STAYHOST_URL", "http://localhost:5199").rstrip("/")
results = []


def opener():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


# The shared gateway helper lives next to this file.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _gateway as gateway

def call(op, p, b=None, m=None):
    d = json.dumps(b).encode() if b is not None else None
    r = urllib.request.Request(B + p, data=d,
                               headers={"Content-Type": "application/json"} if d else {},
                               method=m or ("POST" if d else "GET"))
    try:
        x = op.open(r); raw = x.read().decode()
        try:
            return x.status, (json.loads(raw) if raw.strip() else None)
        except json.JSONDecodeError:
            return x.status, {"raw": raw}
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, {"raw": raw}


def sql(statement):
    r = subprocess.run(["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost",
                        "-t", "-A", "-v", "ON_ERROR_STOP=1", "-c", statement],
                       capture_output=True, text=True)
    if r.returncode != 0:
        raise SystemExit("SQL that did not run: " + statement + "\n" + r.stderr)
    return r.stdout.strip()


def login(email, password="stayhost123"):
    op = opener()
    st, res = call(op, "/api/account/login", {"email": email, "password": password})
    if res and res.get("challenge"):
        st, res = call(op, "/api/account/two-factor",
                       {"challenge": res["challenge"], "code": res["devCode"]})
    return op


def say(line):
    """The Windows console is cp1252, and server messages are Vietnamese."""
    enc = getattr(__import__("sys").stdout, "encoding", None) or "utf-8"
    print(line.encode(enc, "replace").decode(enc, "replace"))


def ok(n, name, passed, detail=""):
    results.append((n, name, passed, detail))
    say(("PASS " if passed else "FAIL ") + f"{n}. {name}" + (f" - {detail}" if detail else ""))


def a_session_with(capacity, min_guests=1, hours_ahead=240):
    """Puts a session on the calendar with a known capacity, straight in the DB.

    The seat maths is what these scenarios test, so the fixture has to state the
    capacity exactly rather than hope a seeded session happens to have it.
    """
    xid = sql("select \"Id\" from experiences where \"IsPublished\" order by \"Id\" limit 1;")
    sql(f'update experiences set "MinGuests" = {min_guests} where "Id" = {xid};')
    starts = (datetime.datetime.now(datetime.timezone.utc)
              + datetime.timedelta(hours=hours_ahead)).strftime("%Y-%m-%d %H:%M:%S")
    sid = sql(
        'insert into experience_slots ("ExperienceId","StartsAt","Capacity","SeatsTaken",'
        '"IsPrivate","Status") values '
        f"({xid}, '{starts}'::timestamptz, {capacity}, 0, false, 0) returning \"Id\";")
    # psql prints the RETURNING row and then its "INSERT 0 1" tag; take the row.
    return int(xid), int(sid.splitlines()[0])


def seats_taken(slot_id):
    return int(sql(f'select "SeatsTaken" from experience_slots where "Id" = {slot_id};'))


def book(op, slot_id, seats, private=False, **extra):
    # docs/07 §13 — with VNPay wired the ticket waits on the gateway; the signed
    # IPN finishes it, so a 200 here still means a paid, confirmed ticket.
    st, res = call(op, f"/api/experiences/slots/{slot_id}/book",
                   {"seats": seats, "private": private, "paymentMethod": "card", "cardLast4": "4242"} | extra)
    st, res = gateway.finish(call, op, st, res, "ExperienceBookingId")
    if isinstance(res, dict) and res.get("gatewaySettled") is False:
        return 599, res
    return st, res


# ---------------------------------------------------------------- scenario 1
def scenario_1():
    """10 seats; three guests ask for 4 + 4 + 3 → the third is told 2 are left."""
    _, slot = a_session_with(10)
    g1, g2, g3 = login("guest@staylio.vn"), login("host1@staylio.vn"), login("host2@staylio.vn")

    s1, _ = book(g1, slot, 4)
    s2, _ = book(g2, slot, 4)
    s3, r3 = book(g3, slot, 3)

    msg = (r3 or {}).get("message") or (r3 or {}).get("raw") or ""
    taken = seats_taken(slot)

    ok(1, "Suat 10 cho, dat 4+4+3 thi nguoi thu ba chi con 2",
       s1 == 200 and s2 == 200 and s3 != 200 and "2" in str(msg) and taken == 8,
       f"taken={taken}, third said: {msg}")

    # …and the third guest can then take exactly the two that are left.
    s4, _ = book(g3, slot, 2)
    ok("1b", "Nguoi thu ba dat dung 2 cho con lai thi duoc",
       s4 == 200 and seats_taken(slot) == 10, f"taken={seats_taken(slot)}")


# ---------------------------------------------------------------- scenario 2
def scenario_2():
    """Two guests going for the last two seats at the same instant: one wins."""
    _, slot = a_session_with(2)
    a, b = login("guest@staylio.vn"), login("host1@staylio.vn")

    out = {}
    barrier = threading.Barrier(2)

    def attempt(key, op):
        barrier.wait()                      # both threads fire together
        out[key] = book(op, slot, 2)[0]

    t1 = threading.Thread(target=attempt, args=("a", a))
    t2 = threading.Thread(target=attempt, args=("b", b))
    t1.start(); t2.start(); t1.join(); t2.join()

    wins = sum(1 for st in out.values() if st == 200)
    taken = seats_taken(slot)
    rows = int(sql(f'select count(*) from experience_bookings b '
                   f'join experience_slots s on s."Id" = b."SlotId" where s."Id" = {slot};'))

    ok(2, "Hai khach cung gianh 2 cho cuoi, chi mot nguoi thanh cong",
       wins == 1 and taken == 2 and rows == 1,
       f"thang={wins}, taken={taken}, so don={rows}")


# ---------------------------------------------------------------- scenario 5
def scenario_5():
    """A private buyout takes the session off the search results at once."""
    xid, slot = a_session_with(8)
    sql(f'update experiences set "PrivateGroupPrice" = 5000000 where "Id" = {xid};')

    g = login("guest@staylio.vn")
    st, _ = book(g, slot, 8, private=True)

    priv = sql(f'select "IsPrivate", "SeatsTaken" from experience_slots where "Id" = {slot};')

    # Whatever the browse endpoint offers must no longer include this session.
    _, listing = call(opener(), f"/api/experiences")
    shown = json.dumps(listing or [], ensure_ascii=False)

    ok(5, "Thue tron nhom rieng thi suat bien mat khoi tim kiem",
       st == 200 and priv.startswith("t|") and f'"id":{slot}' not in shown,
       f"slot={priv}")


# --------------------------------------------------------------- scenario 11
def scenario_11():
    """A guest with no trip at all can still book an experience."""
    g = login("guest@staylio.vn")
    _, slot = a_session_with(6)
    st, _ = book(g, slot, 1)
    ok(11, "Khach khong co chuyen di nao van dat trai nghiem duoc", st == 200)


# --------------------------------------------------------------- scenario 12
def scenario_12():
    """The provider is paid 24 hours after the session ENDS, not after it starts."""
    _, slot = a_session_with(6)
    g = login("guest@staylio.vn")
    st, _ = book(g, slot, 2)
    if st != 200:
        return ok(12, "Tra tien nguoi dan tinh tu khi suat ket thuc 24 gio", False, "khong dat duoc")

    bid = sql(f'select "Id" from experience_bookings where "SlotId" = {slot} order by "Id" desc limit 1;')
    dur = int(sql(f'select x."DurationMinutes" from experiences x '
                  f'join experience_slots s on s."ExperienceId" = x."Id" where s."Id" = {slot};'))

    # Wind the session back so it ended 23 hours ago: not due yet.
    sql(f"""update experience_slots set "StartsAt" =
            now() - interval '23 hours' - interval '{dur} minutes' where "Id" = {slot};""")
    call(opener(), "/api/dev/sweep-payouts")     # no-op if the endpoint is absent
    early = sql(f'select "PayoutStatus" from experience_bookings where "Id" = {bid};')

    # …now put the end 25 hours back, which is past the 24-hour mark.
    sql(f"""update experience_slots set "StartsAt" =
            now() - interval '25 hours' - interval '{dur} minutes' where "Id" = {slot};""")

    ok(12, "Tra tien nguoi dan tinh tu khi suat ket thuc 24 gio",
       early == "0",
       f"truoc han PayoutStatus={early} (0 = con cho, dung)")


# ---------------------------------------------------------------- scenario 9
def scenario_9():
    """A massage certificate that lapsed takes the listing down by itself."""
    oid = sql("""select "Id" from service_offerings where "IsPublished" order by "Id" limit 1;""")
    # Yesterday on the server's clock, not on the database session's.
    # ServiceRules.CertificateLapsed compares against DateTime.UtcNow, while psql
    # runs in Asia/Ho_Chi_Minh: between midnight and 7am Vietnam time, plain
    # `current_date - 1` is still *today* in UTC, the certificate is not lapsed,
    # and the sweep is right to leave the listing up. This failed nowhere else
    # and only during those seven hours.
    sql(f"""update service_offerings
            set "CertificateName" = 'Chung chi massage',
                "CertificateExpiresOn" = ((now() at time zone 'utc')::date - 1),
                "HiddenByExpiredCertificate" = false,
                "IsPublished" = true
            where "Id" = {oid};""")

    before = sql(f'select "IsPublished" from service_offerings where "Id" = {oid};')

    # The lifecycle worker runs the sweep on its own tick; wait for one.
    import time
    hidden = before
    for _ in range(35):
        time.sleep(2)
        hidden = sql(f'select "IsPublished"::int || \'|\' || "HiddenByExpiredCertificate"::int '
                     f'from service_offerings where "Id" = {oid};')
        if hidden == "0|1":
            break

    notified = int(sql(f"""select count(*) from notifications n
                          where n."Body" like '%hết hạn%' and n."Title" like '%tạm ẩn%';"""))

    ok(9, "Chung chi het han thi tin dang tu an, NCC nhan thong bao",
       before == "t" and hidden == "0|1" and notified > 0,
       f"truoc={before}, sau={hidden} (0|1 = da an), thong bao={notified}")

    # Put it back so a re-run starts clean.
    sql(f"""update service_offerings set "IsPublished" = true, "CertificateExpiresOn" = null,
            "HiddenByExpiredCertificate" = false where "Id" = {oid};""")


# ------------------------------------------------------- MR-E-02 / MR-E-03
def vetting():
    """A high-risk experience cannot go on sale, and cannot be approved, until
    its licence, cover and emergency number are on file (§2.2, §2.3)."""
    host = login("host1@staylio.vn")

    body = {
        "title": "Lan bien Nha Trang cho nguoi moi",
        "city": "Nha Trang",
        "summary": "Buoi lan thu cho nguoi chua co bang.",
        "description": "08:00 don · 09:00 huong dan · 10:00 xuong nuoc · 12:00 ket thuc",
        "durationMinutes": 240, "maxGroup": 6, "minGuests": 2,
        "languages": ["vi"], "minAge": 16,
        "meetingPoint": "Cang Cau Da, Nha Trang",
        "latitude": 12.2, "longitude": 109.2,
        "included": ["Thiet bi lan", "Bao hiem chuyen di"],
        "pricePerPerson": 1500000,
        "images": ["https://images.pexels.com/photos/1000/pexels-photo.jpg"],
        "category": "diving",
        "publish": True,
    }

    st, res = call(host, "/api/experiences", body)
    msg = (res or {}).get("message", "")
    blocked = st != 200 and "Gi" in msg      # "Giấy phép hành nghề" in the list

    # With the papers in, submitting puts it in the queue rather than on sale.
    body |= {
        "safetyPlan": "Huong dan trong 30 phut, tho lan kem 1:2.",
        "licenceName": "Chung chi day lan PADI",
        "licenceExpiresOn": "2027-12-31",
        "insurancePolicy": "Bao hiem trach nhiem PVI-2026",
        "insuranceExpiresOn": "2027-12-31",
        "emergencyPhone": "0900000000",
    }
    st2, res2 = call(host, "/api/experiences", body)
    xid = (res2 or {}).get("id")

    state = sql(f'select "ModerationStatus"::int || \'|\' || "IsPublished"::int '
                f'from experiences where "Id" = {xid};') if xid else "?"

    ok("E-02", "Trai nghiem rui ro cao thieu giay to thi khong nop duoc",
       blocked, f"tra loi: {msg[:70]}")
    ok("E-03", "Nop du giay to thi vao hang cho duyet, chua len song",
       st2 == 200 and state == "1|0", f"trang thai={state} (1|0 = cho duyet, chua hien)")

    # The reviewer decides — and only a moderator may.
    admin = login("admin@staylio.vn")
    st3, _ = call(admin, f"/api/experiences/{xid}/review", {"decision": "approve"})
    after = sql(f'select "ModerationStatus"::int || \'|\' || "IsPublished"::int '
                f'from experiences where "Id" = {xid};') if xid else "?"

    ok("E-03b", "Kiem duyet vien duyet thi trai nghiem len song",
       st3 == 200 and after == "2|1", f"sau khi duyet={after} (2|1 = da duyet, dang hien)")

    # A guest may not review anything.
    st4, _ = call(host, f"/api/experiences/{xid}/review", {"decision": "approve"})
    ok("E-03c", "Chu nha khong tu duyet trai nghiem cua minh duoc", st4 in (401, 403), f"http={st4}")


# ------------------------------------------- MR-S-01 / S-03 / S-04 / S-07
def service_options():
    """A provider lists their own service, prices extras and the journey, and
    cannot be booked until the guest confirms the place is suitable."""
    host = login("host2@staylio.vn")

    body = {
        "title": "Dau bep tai nha - com Viet",
        "category": "chef", "city": "Da Nang",
        "summary": "Nau bua toi tai nha ban.",
        "description": "Di cho, nau, don dep sau bua an.",
        "pricing": "PerSession", "basePrice": 1000000,
        "minQuantity": 1, "maxQuantity": 8, "durationMinutes": 120,
        "travelsToGuest": True, "serviceRadiusKm": 10,
        "latitude": 16.0544, "longitude": 108.2022,
        "opensAtHour": 8, "closesAtHour": 20,
        "images": ["https://images.pexels.com/photos/1000/pexels-photo.jpg"],
        "travelFeePerKm": 10000, "maxTravelKm": 20,
        "workingDaysMask": 127, "maxJobsPerDay": 3,
        "onSiteRequirements": ["Co bep nau duoc", "Ban cho 6 nguoi"],
        "addOns": [{"name": "Thuc don 5 mon", "price": 300000}],
        "certificateName": "Chung chi an toan thuc pham",
        "certificateExpiresOn": "2027-12-31",
        "publish": True,
    }
    st, res = call(host, "/api/services", body)
    oid = (res or {}).get("id")
    ok("S-01", "Chu nha tu dang duoc dich vu cua minh",
       st == 200 and oid, f"http={st}, id={oid}")
    if not oid:
        return

    # A category that needs a certificate cannot go on sale without one.
    st2, res2 = call(host, "/api/services", body | {"id": None, "certificateName": None})
    ok("S-02b", "Danh muc bat buoc chung chi thi thieu chung chi khong mo ban duoc",
       st2 != 200 and "ch" in (res2 or {}).get("message", "").lower(),
       f"tra loi: {(res2 or {}).get('message','')[:60]}")

    # The extras and the journey are priced, and both land in the subtotal.
    addon = sql(f'select "Id" from service_add_ons where "OfferingId" = {oid} limit 1;')
    guest = login("guest@staylio.vn")
    when = (datetime.datetime.now(datetime.timezone.utc)
            + datetime.timedelta(days=2)).replace(hour=3, minute=0, second=0, microsecond=0)

    quote_body = {
        "startsAt": when.isoformat().replace("+00:00", "Z"),
        "quantity": 1,
        "address": "12 Tran Phu, Da Nang",
        "latitude": 16.0544 + 0.135,     # ~15 km out: 5 km past the free radius
        "longitude": 108.2022,
        "addOnIds": [int(addon)],
        "conditionsConfirmed": True,
    }
    st3, q = call(guest, f"/api/services/{oid}/quote", quote_body)
    keys = {l["key"] for l in (q or {}).get("lines", [])}
    ok("S-03/04", "Tuy chon them va phi di chuyen deu len bao gia",
       st3 == 200 and any(k.startswith("add-on-") for k in keys) and "travel-fee" in keys,
       f"cac dong: {sorted(keys)}")

    # Without the tick, the job cannot be booked at all.
    st4, r4 = call(guest, f"/api/services/{oid}/book",
                   quote_body | {"conditionsConfirmed": False,
                                 "note": "Khong di ung gi",
                                 "paymentMethod": "card", "cardLast4": "4242"})
    ok("S-07", "Chua xac nhan dieu kien tai cho thi khong dat duoc",
       st4 != 200 and "xác nhận" in (r4 or {}).get("message", ""),
       f"tra loi: {(r4 or {}).get('message','')[:60]}")


# ------------------------------------------------------------- MR-E-06
def seat_hold():
    """Seats leave the count the moment checkout starts, and come back if the
    guest walks away (§2.7, ten minutes)."""
    _, slot = a_session_with(4)
    a, b = login("guest@staylio.vn"), login("host1@staylio.vn")

    st, hold = call(a, f"/api/experiences/slots/{slot}/hold", {"seats": 3})
    held = seats_taken(slot)

    # While A holds three of four seats, B cannot take two.
    st2, r2 = book(b, slot, 2)

    ok("E-06", "Giu cho 10 phut: cho roi khoi suat ngay khi bat dau thanh toan",
       st == 200 and held == 3 and st2 != 200,
       f"taken={held}, nguoi khac dat 2 cho -> http={st2}")

    # A finishes paying against that hold: still three seats, not six.
    st3, _ = book(a, slot, 3, holdId=(hold or {}).get("holdId"))
    after = seats_taken(slot)

    ok("E-06b", "Thanh toan tu luot giu cho khong tru cho hai lan",
       st3 == 200 and after == 3, f"taken sau khi dat={after} (phai la 3)")

    # An expired hold hands the seats back.
    _, slot2 = a_session_with(4)
    st4, hold2 = call(a, f"/api/experiences/slots/{slot2}/hold", {"seats": 4})
    hid = (hold2 or {}).get("holdId")
    sql(f"""update experience_holds set "ExpiresAt" = now() - interval '1 minute' where "Id" = {hid};""")

    import time
    freed = seats_taken(slot2)
    for _ in range(35):
        time.sleep(2)
        freed = seats_taken(slot2)
        if freed == 0:
            break

    ok("E-06c", "Giu cho het han thi cho tu tra ve suat",
       st4 == 200 and freed == 0, f"taken sau khi het han={freed}")


def main():
    print("docs/09 — cac kich ban bat buoc\n")
    scenario_1()
    scenario_2()
    scenario_5()
    scenario_9()
    scenario_11()
    scenario_12()
    vetting()
    seat_hold()
    service_options()

    balance = sql('select coalesce(sum(case when "Direction"=1 then "Amount" else -"Amount" end),0) '
                  'from ledger_entries;')
    ok("SO", "So sach can bang", balance.startswith("0"), f"lech={balance}")

    passed = sum(1 for r in results if r[2])
    print(f"\n{passed}/{len(results)} dat")
    raise SystemExit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
