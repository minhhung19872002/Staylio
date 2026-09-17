# docs/07 §2.5 — booking without an account, and paying the host at the door.
#
# Two features that answer the same person: the guest who will not hand a card to
# a website. Both change what the money does, so every scenario drives the real
# server and then reads the database — in particular the ledger, which must stay
# untouched by a booking whose money never reaches Staylio.
#
#   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web
#   python scripts/guestcheckout_acceptance.py
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
    return datetime.date.fromisoformat(sql("select (now() at time zone 'utc')::date"))


def ok(name, passed, detail=""):
    results.append((name, passed, detail))
    print(("PASS " if passed else "FAIL ") + name + (" - " + detail if detail else ""))


def ledger_off():
    return sql('select coalesce(sum(case when "Direction"=1 then "Amount" '
               'else -"Amount" end),0) from ledger_entries;')


def sign_in(email):
    op = opener()
    st, res = call(op, "/api/account/login", {"email": email, "password": PW})
    if res and res.get("challenge"):
        call(op, "/api/account/two-factor",
             {"challenge": res["challenge"], "code": res["devCode"]})
    return op


def plain_listing():
    """A published, instant-bookable place with no ĐP-10 preconditions on it."""
    return int(sql('select "Id" from listings where "IsPublished"=true and "Type"<>7 '
                   'and "InstantBook"=true and "RequireGuestPhoto"=false '
                   'and "RequireVerifiedToBook"=false and "ReviewStatus"=0 '
                   'order by "Id" limit 1'))


# Every run books on its own week. Two runs against the same database used to
# ask for the same nights, and the second one was refused by the GiST overlap
# guard — a working rule reading as a broken scenario.
SHIFT = 7 * (int(RUN) % 30)


def window(offset):
    today = utc_today()
    start = offset + SHIFT
    return ((today + datetime.timedelta(days=start)).isoformat(),
            (today + datetime.timedelta(days=start + 2)).isoformat())


def book(op, lid, offset, **extra):
    # The listing is shared with the other suites, which book it too; a week
    # another suite already took is not what these scenarios are about, so the
    # booking moves on a week rather than reporting a rule as broken.
    for attempt in range(8):
        ci, co = window(offset + 7 * attempt)
        body = {"listingId": lid, "checkIn": ci, "checkOut": co,
                "adults": 2, "children": 0, "infants": 0, "pets": 0, "agreedToRules": True}
        body.update(extra)
        st, res = call(op, "/api/bookings", body)
        if not (st == 409 and isinstance(res, dict) and res.get("reason") in ("DatesTaken", "TurnoverTime")):
            return st, res
    return st, res


def booked(name, st, res):
    """A booking that did not happen is reported, never indexed into. The first
    version of this suite did `res["id"]` and died with a KeyError on a re-run,
    which says nothing about what the server refused."""
    if st in (200, 201) and isinstance(res, dict) and "id" in res:
        return res["id"]
    ok(name, False, "book %s %s" % (st, res))
    return None


# ------------------------------------------------------------------ §2.5 guest
def s1_a_stranger_can_book_and_pay():
    """No account, no sign-in: a name, an email and a phone, and a card."""
    op = opener()
    lid = plain_listing()

    st, res = book(op, lid, 40,
                   guestName="Trần Khách Lạ", guestEmail="lakhach%s@vidu.vn" % RUN,
                   guestPhone="0901%s" % RUN)
    if st not in (200, 201):
        return ok("Khach khong tai khoan dat duoc", False, "book %s %s" % (st, res))

    bid = res["id"]
    owner = sql('select coalesce("GuestUserId"::text, \'null\') || \'|\' || '
                '(case when "SessionId" = \'\' then \'no-session\' else \'session\' end) '
                'from bookings where "Id"=%d' % bid)

    st2, pay = gateway.pay(call, op, bid, {"paymentMethod": "card", "cardLast4": "4242"})
    status = sql('select "Status" from bookings where "Id"=%d' % bid)

    # docs/07 §2.5 — the confirmation email is the whole promise of guest
    # checkout: there is no in-app inbox for somebody with no account, and the
    # reference inside it is the only way back to this booking. Asserted, not
    # printed: the first version of this scenario printed the count and passed
    # while it was zero, because PaymentCompletion looked up user id 0, found
    # nobody, and sent nothing.
    mail = int(sql("select count(*) from email_messages "
                   "where \"ToEmail\"='lakhach%s@vidu.vn'" % RUN))
    carries_reference = int(sql(
        "select count(*) from email_messages e "
        "where e.\"ToEmail\"='lakhach%s@vidu.vn' "
        "and e.\"Body\" like '%%' || (select \"Reference\" from bookings where \"Id\"=%d) || '%%'"
        % (RUN, bid)))

    ok("Khach khong tai khoan dat va tra duoc",
       st in (200, 201) and owner == "null|session" and st2 in (200, 201) and status == "2"
       and mail >= 1 and carries_reference >= 1,
       "chu don=%s, pay=%s, Status=%s, thu=%d (co ma don=%d)"
       % (owner, st2, status, mail, carries_reference))
    return bid


def s2_the_email_and_phone_are_not_optional():
    op = opener()
    lid = plain_listing()

    st1, r1 = book(op, lid, 44, guestName="Khong Co Email", guestPhone="0901234567")
    st2, r2 = book(op, lid, 46, guestName="Khong Co Dien Thoai",
                   guestEmail="thieusdt%s@vidu.vn" % RUN)
    st3, r3 = book(op, lid, 48, guestEmail="thieuten%s@vidu.vn" % RUN, guestPhone="0901234567")

    ok("Thieu ten, email hoac dien thoai thi khong dat duoc",
       st1 == 400 and r1.get("reason") == "MissingEmail"
       and st2 == 400 and r2.get("reason") == "MissingPhone"
       and st3 == 400 and r3.get("reason") == "MissingName",
       "email=%s/%s, sdt=%s/%s, ten=%s/%s"
       % (st1, r1.get("reason"), st2, r2.get("reason"), st3, r3.get("reason")))


def s3_account_only_money_is_refused_by_name():
    op = opener()
    lid = plain_listing()
    contact = {"guestName": "Khach Lạ", "guestEmail": "lam%s@vidu.vn" % RUN,
               "guestPhone": "0901234567"}

    st1, r1 = book(op, lid, 50, useCredit=True, **contact)
    st2, r2 = book(op, lid, 52, couponCode="CHAOMUNG10", **contact)

    ok("So du va ma giam gia bi tu choi co neu ten",
       st1 == 400 and r1.get("reason") == "NeedsAccountForMoney"
       and st2 == 400 and r2.get("reason") == "NeedsAccountForMoney",
       "so du=%s, ma giam gia=%s" % (r1.get("reason"), r2.get("reason")))


def s4_a_host_who_wants_a_profile_does_not_get_a_stranger():
    lid = plain_listing()
    sql('update listings set "RequireVerifiedToBook"=true where "Id"=%d' % lid)
    try:
        op = opener()
        st, res = book(op, lid, 54, guestName="Khach Lạ",
                       guestEmail="chan%s@vidu.vn" % RUN, guestPhone="0901234567")
        refused = st == 400 and res.get("reason") == "HostRequiresAccount"

        # The same booking made by an account goes straight through, so it is the
        # precondition doing the work and not the dates.
        member = sign_in("guest@staylio.vn")
        st2, res2 = book(member, lid, 54)
    finally:
        sql('update listings set "RequireVerifiedToBook"=false where "Id"=%d' % lid)

    ok("Tin dang doi xac minh thi khach la khong dat duoc",
       refused and st2 in (200, 201),
       "khach la=%s/%s, tai khoan=%s" % (st, res.get("reason"), st2))


def s5_lookup_by_reference_and_email(bid):
    """The cookie is not the answer: a different device has none of it."""
    reference = sql('select "Reference" from bookings where "Id"=%d' % bid)
    email = sql('select "GuestEmail" from bookings where "Id"=%d' % bid)

    fresh = opener()                       # a browser that has never seen this booking
    st, found = call(fresh, "/api/bookings/lookup", {"reference": reference, "email": email})

    st2, wrong = call(opener(), "/api/bookings/lookup",
                      {"reference": reference, "email": "nguoikhac@vidu.vn"})
    st3, nosuch = call(opener(), "/api/bookings/lookup",
                       {"reference": "SH00000000", "email": email})

    # A match adopts the booking into the new session, so the trip page works.
    st4, trip = call(fresh, "/api/bookings/%d" % bid)

    ok("Tra cuu bang ma don + email",
       st == 200 and found["id"] == bid and st2 == 404 and st3 == 404 and st4 == 200,
       "tim thay=%s, sai email=%s, ma bia=%s, doc lai duoc=%s" % (st, st2, st3, st4))


def s6_signing_in_adopts_what_the_session_held():
    op = opener()
    lid = plain_listing()
    email = "nhannuoi%s@vidu.vn" % RUN

    st, res = book(op, lid, 56, guestName="Khach Se Dang Ky",
                   guestEmail=email, guestPhone="0902%s" % RUN)
    if st not in (200, 201):
        return ok("Dang ky xong thi don ve tai khoan", False, "book %s %s" % (st, res))

    before = sql('select coalesce("GuestUserId"::text, \'null\') from bookings where "Id"=%d' % res["id"])

    # The same browser registers; AuthService adopts the session's bookings.
    st2, _ = call(op, "/api/account/register",
                  {"email": email, "password": PW, "fullName": "Khach Se Dang Ky",
                   "dateOfBirth": "1990-01-01"})
    after = sql('select coalesce("GuestUserId"::text, \'null\') from bookings where "Id"=%d' % res["id"])

    ok("Dang ky xong thi don ve tai khoan",
       before == "null" and st2 in (200, 201) and after != "null",
       "truoc=%s, dang ky=%s, sau=%s" % (before, st2, after))


# ------------------------------------------------------- §2.5 pay at property
def property_listing():
    lid = plain_listing()
    sql('update listings set "AcceptsPayAtProperty"=true where "Id"=%d' % lid)
    return lid


def s7_pay_at_property_moves_no_money():
    lid = property_listing()
    before = ledger_off()
    rows_before = int(sql('select count(*) from ledger_entries'))

    op = opener()
    st, res = book(op, lid, 60, guestName="Khach Tra Tai Cho",
                   guestEmail="taicho%s@vidu.vn" % RUN, guestPhone="0903%s" % RUN)
    bid = booked("Tra tai noi o: xac nhan ngay va khong ghi but toan nao", st, res)
    if bid is None:
        return None
    st2, paid = call(op, "/api/bookings/%d/pay" % bid, {"paymentMethod": "property"})

    row = sql('select "Status" || \'|\' || (case when "PaidAtProperty" then \'yes\' else \'no\' end) '
              'from bookings where "Id"=%d' % bid)
    pay_status = sql('select p."Status" from payments p where p."BookingId"=%d' % bid)
    rows_after = int(sql('select count(*) from ledger_entries'))

    ok("Tra tai noi o: xac nhan ngay va khong ghi but toan nao",
       st2 == 200 and row == "2|yes" and pay_status == "0"
       and rows_after == rows_before and ledger_off() == before,
       "pay=%s, don=%s, Payment.Status=%s, but toan them=%d"
       % (st2, row, pay_status, rows_after - rows_before))
    return bid


def s8_it_is_refused_where_the_host_did_not_offer_it():
    lid = plain_listing()
    sql('update listings set "AcceptsPayAtProperty"=false where "Id"=%d' % lid)

    op = opener()
    st, res = book(op, lid, 64, guestName="Khach Thu",
                   guestEmail="tuchoi%s@vidu.vn" % RUN, guestPhone="0904%s" % RUN)
    bid = booked("Chi hien va chi nhan o tin dang co bat", st, res)
    if bid is None:
        return

    st2, refused = call(op, "/api/bookings/%d/pay" % bid, {"paymentMethod": "property"})
    status = sql('select "Status" from bookings where "Id"=%d' % bid)

    # And the catalogue does not offer it there either, so the guest never sees
    # a method the pay endpoint would refuse.
    st3, cat = call(opener(), "/api/payment-methods/catalogue?listingId=%d" % lid)
    offered = any(m["key"] == "property" for m in (cat or {}).get("methods", []))

    ok("Chi hien va chi nhan o tin dang co bat",
       st2 == 400 and refused.get("reason") == "NotOfferedHere"
       and status == "1" and not offered,
       "pay=%s/%s, don van cho thanh toan=%s, catalogue co=%s"
       % (st2, refused.get("reason"), status == "1", offered))


def s9_cancelling_promises_nothing_back(bid):
    """docs/01 CĐ-07 shows this figure before the guest confirms. Off a booking
    nobody paid for, the policy would have quoted a refund of imaginary money."""
    uid_email = sql('select "GuestEmail" from bookings where "Id"=%d' % bid)
    reference = sql('select "Reference" from bookings where "Id"=%d' % bid)

    op = opener()
    call(op, "/api/bookings/lookup", {"reference": reference, "email": uid_email})

    st, preview = call(op, "/api/bookings/%d/refund-preview" % bid)
    before = ledger_off()
    rows_before = int(sql('select count(*) from ledger_entries'))

    st2, _ = call(op, "/api/bookings/%d/cancel" % bid, {})
    refunded = sql('select "RefundedAmount" from bookings where "Id"=%d' % bid)
    rows_after = int(sql('select count(*) from ledger_entries'))

    ok("Huy don tra tai noi o: khong hua hoan dong nao",
       st == 200 and float(preview["refund"]) == 0 and float(preview["penalty"]) == 0
       and "chưa trả đồng nào" in preview["explanation"]
       and st2 in (200, 204) and float(refunded) == 0
       and rows_after == rows_before and ledger_off() == before,
       "hoan=%s, mat=%s, RefundedAmount=%s, but toan them=%d"
       % (preview["refund"], preview["penalty"], refunded, rows_after - rows_before))


def s10_the_host_bills_the_fee_when_the_cash_is_in_hand():
    lid = property_listing()
    host_email = sql('''select u."Email" from listings l join hosts h on h."Id"=l."HostId"
                        join users u on u."Id"=h."UserId" where l."Id"=%d''' % lid)
    host_id = int(sql('select "HostId" from listings where "Id"=%d' % lid))

    op = opener()
    st, res = book(op, lid, 70, guestName="Khach Dua Tien",
                   guestEmail="duatien%s@vidu.vn" % RUN, guestPhone="0905%s" % RUN)
    bid = booked("Chu nha ghi nhan tien: chi tinh phi, van khong co but toan", st, res)
    if bid is None:
        return
    call(op, "/api/bookings/%d/pay" % bid, {"paymentMethod": "property"})

    owed_before = float(sql('select "OwedToPlatform" from hosts where "Id"=%d' % host_id))
    fees = float(sql('select "ServiceFee" + "HostServiceFee" from bookings where "Id"=%d' % bid))
    rows_before = int(sql('select count(*) from ledger_entries'))

    host = sign_in(host_email)
    st2, done = call(host, "/api/host/bookings/%d/cash-collected" % bid, {})
    st3, again = call(host, "/api/host/bookings/%d/cash-collected" % bid, {})

    owed_after = float(sql('select "OwedToPlatform" from hosts where "Id"=%d' % host_id))
    collected = sql('select case when "CashCollectedAt" is null then \'no\' else \'yes\' end '
                    'from bookings where "Id"=%d' % bid)
    rows_after = int(sql('select count(*) from ledger_entries'))

    ok("Chu nha ghi nhan tien: chi tinh phi, van khong co but toan",
       st2 == 200 and collected == "yes"
       and abs((owed_after - owed_before) - fees) < 1
       and st3 == 200 and again.get("alreadyRecorded") is True
       and abs(owed_after - float(sql('select "OwedToPlatform" from hosts where "Id"=%d' % host_id))) < 1
       and rows_after == rows_before,
       "ghi nhan=%s, OwedToPlatform +%.0f (phi %.0f), bam lai=%s, but toan them=%d"
       % (st2, owed_after - owed_before, fees, again.get("alreadyRecorded"),
          rows_after - rows_before))


def s11_it_does_not_mix_with_platform_money():
    lid = property_listing()
    member = sign_in("guest@staylio.vn")

    st, res = book(member, lid, 74)
    bid = booked("Khong di cung dat coc", st, res)
    if bid is None:
        return

    st2, refused = call(member, "/api/bookings/%d/pay" % bid,
                        {"paymentMethod": "property", "payDeposit": True,
                         "depositAmount": 500000})

    ok("Khong di cung dat coc",
       st2 == 400 and refused.get("reason") == "NotWithDeposit",
       "pay=%s, ly do=%s" % (st2, refused.get("reason")))


def main():
    print("=" * 70)
    print("docs/07 §2.5 — dat khong can tai khoan & tra tien tai noi o")
    print("=" * 70)

    before = ledger_off()

    bid = s1_a_stranger_can_book_and_pay()
    s2_the_email_and_phone_are_not_optional()
    s3_account_only_money_is_refused_by_name()
    s4_a_host_who_wants_a_profile_does_not_get_a_stranger()
    if bid:
        s5_lookup_by_reference_and_email(bid)
    s6_signing_in_adopts_what_the_session_held()

    property_booking = s7_pay_at_property_moves_no_money()
    s8_it_is_refused_where_the_host_did_not_offer_it()
    if property_booking:
        s9_cancelling_promises_nothing_back(property_booking)
    s10_the_host_bills_the_fee_when_the_cash_is_in_hand()
    s11_it_does_not_mix_with_platform_money()

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
