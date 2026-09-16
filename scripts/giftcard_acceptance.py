# docs/01 TC-08 — a gift card is only worth something once somebody paid for it.
#
# On 05/09/2026 this was not true. POST /api/wallet/gift-cards created the card
# Active, posted Ledger.SellGiftCard — the entry that says money reached escrow —
# and emailed the code, with no Payment, no PaymentSession and no gateway call
# anywhere in the path. Anyone signed in could mint the 20,000,000d ceiling,
# redeem it and spend it on a real stay with a real host at the other end.
#
# Nothing alarmed, and that is the part worth keeping a suite for: the two ledger
# legs balance each other, so the daily check of docs/07 §5 read zero, and a gift
# card raises no GatewayCharge for the reconciliation of §7 to find missing. The
# only way to see it was to ask "who took the money?" and follow the path.
#
# Every scenario below drives the real server and then reads the database back.
#
#   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StayHost.Web
#   python scripts/giftcard_acceptance.py
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
import time
import http.cookiejar

# The card row may or may not be wired to a real gateway; _gateway.py is how
# the other suites stay true either way (docs/07 §13).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _gateway as gateway

# A Windows console runs cp1258 — the Vietnamese code page, and it spells
# Vietnamese with combining marks, so it cannot encode the precomposed letters
# the server sends. A verdict must never be lost to a character the terminal
# cannot draw.
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

B = os.environ.get("STAYHOST_URL", "http://localhost:5199").rstrip("/")
PW = "stayhost123"
GUEST = "guest@staylio.vn"
results = []


def ok(name, passed, detail=""):
    # bool(), so the tally can tell a real verdict from skip()'s None.
    results.append((name, bool(passed), detail))
    print(("PASS " if passed else "FAIL ") + name + (f" - {detail}" if detail else ""))


def opener():
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(o, path, body=None, method=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(
        B + path, data=data,
        headers={"Content-Type": "application/json"} if data else {},
        method=method or ("POST" if data else "GET"))
    try:
        with o.open(req, timeout=40) as r:
            text = r.read().decode("utf-8", "replace")
            return r.status, (json.loads(text) if text.strip().startswith(("{", "[")) else text)
    except urllib.error.HTTPError as e:
        text = e.read().decode("utf-8", "replace")
        return e.code, (json.loads(text) if text.strip().startswith(("{", "[")) else text)


def sql(query):
    out = subprocess.run(
        ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost",
         "-t", "-A", "-c", query],
        capture_output=True, text=True, encoding="utf-8")
    return (out.stdout or "").strip()


def sold_entries():
    """How many gift-card sales have been posted.

    One leg, not both: every posting in this ledger is written twice by
    construction (docs/00 §6.1), so counting rows counts each sale as two and
    makes an off-by-one look like an off-by-two.
    """
    return int(sql("select count(*) from ledger_entries "
                   "where \"TransactionKind\"='gift-card-sold' and \"Direction\"=1;") or 0)


def ledger_off():
    return sql('select coalesce(sum(case when "Direction"=1 then "Amount" else -"Amount" end),0) '
               'from ledger_entries;')


def sign_in():
    o = opener()
    st, res = call(o, "/api/account/login", {"email": GUEST, "password": PW})
    if isinstance(res, dict) and res.get("challenge"):
        if not res.get("devCode"):
            raise SystemExit("Chay server voi ASPNETCORE_ENVIRONMENT=Development.")
        call(o, "/api/account/two-factor",
             {"challenge": res["challenge"], "code": res["devCode"]})
    return o


def buy(o, amount, method, card_last4=None):
    return call(o, "/api/wallet/gift-cards", {
        "amount": amount, "recipientEmail": GUEST,
        "recipientName": "Khach", "method": method, "cardLast4": card_last4})


# ---------------------------------------------------------------- scenarios

def skip(name, why):
    """A scenario the gateway would not let us reach.

    Scenarios 1-3 need a wallet gateway to actually open an order, and MoMo's
    sandbox intermittently answers resultCode 98 ("QR Code tao khong thanh
    cong") for a minute at a time. Reporting that as a broken business rule
    blames Staylio for someone else's bad afternoon - and it already cost two
    wrong diagnoses in one session. A scenario that could not run is named and
    counted apart, never quietly folded into the total.
    """
    results.append((name, None, why))
    print("SKIP " + name + " - " + why)


def buy_via_wallet(o, amount, tries=3):
    """Open a gift-card order at whichever wallet gateway will take it.

    Returns (card, res, note). `note` is set only when no live wallet gateway
    would open an order at all, which is the caller's cue to skip rather than
    fail: the rule under test is "an unpaid card is worth nothing", not "MoMo is
    up".
    """
    last = None
    for attempt in range(tries):
        for method in ("momo", "zalopay"):
            st, res = buy(o, amount, method)
            card = (res or {}).get("card") if isinstance(res, dict) else None
            if card is not None:
                return card, res, None
            last = f"{method} {st}: {res.get('message') if isinstance(res, dict) else res}"
        if attempt < tries - 1:
            time.sleep(4)
    return None, None, last or "khong co cong vi nao mo duoc don"


def order_ref_of(card_id):
    return sql(f'select "OrderRef" from payment_sessions where "GiftCardId"={card_id};')


def card_row(card_id):
    return sql(f'select "Status", "Remaining" from gift_cards where "Id"={card_id};')


def buy_by_card(o, amount, response_code="00", last4="4242"):
    """Buy through the card row and finish the payment however that row is wired.

    With no gateway configured the stand-in settles inside /gift-cards and the
    card comes back Active — that is the shape this suite was written against,
    and `cardLast4` picking success or refusal only ever meant something to it.
    With VNPay wired, which is the production shape and the one a developer with
    a key in user-secrets actually runs, the buyer is sent to VNPay's page
    instead and nothing is settled by the request at all. Reporting that as a
    broken business rule is the trap CLAUDE.md §4 already names: "Bật một cổng
    thật là bẻ gãy mọi script nghiệm thu trả tiền bằng thẻ."

    So the IPN is signed here the way VNPay signs it and delivered to the same
    route production uses. `response_code` "00" is money that arrived; anything
    else is VNPay reporting a refusal, which is the only way to exercise one
    once a real gateway owns the card form.

    Returns (http_status, card_dict, handed_over, note).
    """
    st, res = buy(o, amount, "card", card_last4=last4)
    card = (res or {}).get("card") if isinstance(res, dict) else None

    if card is None or not (res or {}).get("gatewayRedirectUrl"):
        return st, card, False, None

    settled, note = gateway.settle(call, o, order_ref_of(card["id"]), amount, response_code)
    if not settled:
        return st, card, True, note

    _, wallet = call(o, "/api/wallet")
    fresh = next((g for g in (wallet.get("giftCards") or []) if g["id"] == card["id"]), None)
    return st, (fresh or card), True, None

def scenario_live_gateway_leaves_the_card_unpaid():
    """The card must be worth nothing until the gateway says the money arrived.

    A wallet gateway is live in Development, so this is the production shape:
    the buyer is sent away, and the platform writes nothing to the ledger while
    they are gone. Whichever of MoMo or ZaloPay will open an order does; the
    rule under test is about the card, not about which wallet took it.
    """
    o = sign_in()
    before = sold_entries()
    card, res, note = buy_via_wallet(o, 300_000)
    after = sold_entries()

    if card is None:
        return skip("1. Cong that: the chua tra tien thi chua co gia tri", note)

    row = sql(f'select "Status", "Remaining" from gift_cards where "Id"={card["id"]};')
    sessions = int(sql(f'select count(*) from payment_sessions where "GiftCardId"={card["id"]};') or 0)

    ok("1. Cong that: the chua tra tien thi chua co gia tri",
       card["status"] == "AwaitingPayment" and row == "3|0.00"
       and before == after and sessions == 1
       and bool(res.get("gatewayRedirectUrl")),
       f"trang thai={card['status']}, DB={row}, phien={sessions}, "
       f"but toan {before}->{after}, chuyen huong={'co' if res.get('gatewayRedirectUrl') else 'khong'}")


def scenario_the_code_is_withheld_until_paid():
    """The code is the bearer instrument. Handing it over before the money
    arrives puts the hole straight back, whichever screen does it."""
    o = sign_in()
    card, res, note = buy_via_wallet(o, 300_000)
    if card is None:
        return skip("2. Ma the bi giu lai cho toi khi tra tien", note)

    _, wallet = call(o, "/api/wallet")
    listed = next((g for g in (wallet.get("giftCards") or []) if g["id"] == card["id"]), None)
    real = sql(f'select "Code" from gift_cards where "Id"={card["id"]};')

    ok("2. Ma the bi giu lai cho toi khi tra tien",
       card.get("code") == "" and listed is not None and listed.get("code") == "" and real.startswith("GC-"),
       f"mua tra ve='{card.get('code')}', danh sach vi='{listed.get('code') if listed else '?'}', "
       f"trong DB='{real[:6]}…'")


def scenario_an_unpaid_card_cannot_be_redeemed():
    """Even with the code read straight out of the database, an unpaid card is
    not money. CreditRules.CanRedeem has always insisted on Active; what was
    missing was an unpaid state for it to refuse."""
    o = sign_in()
    card, res, note = buy_via_wallet(o, 300_000)
    if card is None:
        return skip("3. The chua tra tien thi khong doi duoc", note)

    code = sql(f'select "Code" from gift_cards where "Id"={card["id"]};')
    before = float(sql(f"select coalesce(sum(\"Amount\"),0) from credit_entries "
                       f"where \"Memo\" like '%{code}%';") or 0)
    st2, res2 = call(o, "/api/wallet/redeem", {"code": code})
    after = float(sql(f"select coalesce(sum(\"Amount\"),0) from credit_entries "
                      f"where \"Memo\" like '%{code}%';") or 0)

    ok("3. The chua tra tien thi khong doi duoc",
       st2 == 400 and before == after == 0.0,
       f"{st2}: {res2.get('message') if isinstance(res2, dict) else res2}; so du cong them={after}")


def scenario_a_refused_card_mints_nothing():
    """A refusal must leave no card and no ledger entry.

    Without a gateway the stand-in refuses the test card ending 0000 and the
    request itself fails. With VNPay wired the refusal happens on their page and
    reaches the platform as a signed IPN whose response code is not 00, so that
    is what is sent. The question is the same either way: did a payment that
    never arrived create anything worth money?
    """
    o = sign_in()
    before = sold_entries()
    cards_before = int(sql('select count(*) from gift_cards where "Status"=0;') or 0)

    # 24 is VNPay's "khách hàng huỷ giao dịch".
    st, card, handed_over, note = buy_by_card(o, 5_000_000, response_code="24", last4="0000")

    after = sold_entries()
    cards_after = int(sql('select count(*) from gift_cards where "Status"=0;') or 0)
    refused = st == 400 if not handed_over else (card or {}).get("status") != "Active"

    ok("4. The bi tu choi thi khong sinh ra gi",
       note is None and refused and before == after and cards_before == cards_after,
       f"{'qua cong that' if handed_over else 'qua ban gia lap'}: "
       f"trang thai={(card or {}).get('status', st)}, "
       f"but toan {before}->{after}, the con hieu luc {cards_before}->{cards_after}"
       + (f", {note}" if note else ""))


def scenario_a_paid_card_is_recorded_where_reconciliation_looks():
    """A card that really was paid for posts exactly one ledger entry, and the
    charge lands in gateway_charges — the half docs/07 §7 reads. The old sale
    posted the ledger entry and no charge, so the day balanced and the
    reconciliation had nothing to compare."""
    o = sign_in()
    before = sold_entries()
    charges_before = int(sql("select count(*) from gateway_charges;") or 0)

    st, card, handed_over, note = buy_by_card(o, 400_000)

    after = sold_entries()
    charges_after = int(sql("select count(*) from gateway_charges;") or 0)

    if card is None or note is not None:
        return ok("5. The da tra tien duoc ghi dung mot lan", False, f"{st} {note or card}")

    row = card_row(card["id"])
    ok("5. The da tra tien duoc ghi dung mot lan",
       after == before + 1 and charges_after == charges_before + 1
       and row == f"0|{float(card['amount']):.2f}" and card.get("code", "").startswith("GC-"),
       f"{'qua cong that' if handed_over else 'qua ban gia lap'}: "
       f"but toan {before}->{after}, gateway_charges {charges_before}->{charges_after}, DB={row}")


def scenario_balance_cannot_buy_balance():
    """Buying credit with credit is not a purchase, it is a rename."""
    o = sign_in()
    before = sold_entries()
    st, res = buy(o, 300_000, "balance")
    ok("6. Khong mua the bang so du", st == 400 and sold_entries() == before,
       f"{st}: {res.get('message') if isinstance(res, dict) else res}")


def scenario_a_paid_card_redeems_once():
    """The happy path still works end to end, and only once."""
    o = sign_in()
    st, card, handed_over, note = buy_by_card(o, 400_000)
    if card is None or note is not None:
        return ok("7. The da tra tien doi duoc, va chi mot lan", False, f"{st} {note or card}")

    st1, w1 = call(o, "/api/wallet/redeem", {"code": card["code"]})
    st2, w2 = call(o, "/api/wallet/redeem", {"code": card["code"]})
    row = card_row(card["id"])

    ok("7. The da tra tien doi duoc, va chi mot lan",
       st1 == 200 and st2 == 400 and row == "1|0.00",
       f"lan 1={st1}, lan 2={st2} ({w2.get('message') if isinstance(w2, dict) else ''}), DB={row}")


def main():
    print(f"\nTC-08 — the qua tang chi co gia tri khi da co nguoi tra tien\n{'=' * 70}\n")
    before = ledger_off()

    for fn in (scenario_live_gateway_leaves_the_card_unpaid,
               scenario_the_code_is_withheld_until_paid,
               scenario_an_unpaid_card_cannot_be_redeemed,
               scenario_a_refused_card_mints_nothing,
               scenario_a_paid_card_is_recorded_where_reconciliation_looks,
               scenario_balance_cannot_buy_balance,
               scenario_a_paid_card_redeems_once):
        try:
            fn()
        except Exception as e:  # a broken scenario must not hide the rest
            ok(fn.__name__, False, f"loi script: {e}")

    after = ledger_off()
    ok("8. So sach van can bang", float(after) == 0.0, f"truoc={before} sau={after}")

    passed = sum(1 for _, p, _ in results if p is True)
    failed = sum(1 for _, p, _ in results if p is False)
    skipped = sum(1 for _, p, _ in results if p is None)

    # Skips counted apart, never folded in: a suite whose total quietly shrinks
    # is worse than one that fails, because the number stays plausible.
    tail = f" - {skipped} bo qua" if skipped else ""
    print("\n" + "=" * 70 + f"\nKET QUA: {passed}/{passed + failed} dat{tail}")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
