# -*- coding: utf-8 -*-
"""Finishing a payment that left for a licensed gateway (docs/07 §13).

The acceptance suites drive the platform's own rules — cancellation, refunds,
suspensions, the ledger — and every one of them needs a booking that is actually
paid for. Until 17/08/2026 a POST to /pay did that: the stand-in gateway said yes
and the booking came back Confirmed. With VNPay wired to the two card rows it no
longer does, and it should not — the money moves on VNPay's page, and no script
can type a card number there.

So this stands in for the gateway's own server, the same way `dotnet run` stands
in for a deployment: it signs the IPN the way VNPay signs it, posts it, and the
booking is confirmed through the production path — signature check, amount check,
ledger, notifications, all of it. What it does not do is bypass anything.

The three suites that need a paid booking call `pay()` instead of hitting /pay
directly, so they behave the same whether or not a gateway is configured. With
none configured this is a single request and nothing below the first branch runs.

The HashSecret is read from `dotnet user-secrets` (or VNPAY_HASH_SECRET) because
that is where a developer's own VNPay key belongs; a key published by a vendor,
like MoMo's and ZaloPay's sandbox pair, lives in appsettings.Development.json
instead. Nothing here can read a production key, and nothing should.
"""
import hashlib
import hmac
import io
import json
import os
import subprocess
import urllib.parse

USER_SECRETS = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~/.microsoft/usersecrets")),
    "Microsoft", "UserSecrets", "stayhost-web-psp", "secrets.json")


def _secrets():
    try:
        with io.open(USER_SECRETS, encoding="utf-8-sig") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def vnpay_keys():
    """(TmnCode, HashSecret) for this machine, or (None, None)."""
    store = _secrets()
    return (os.environ.get("VNPAY_TMN_CODE") or store.get("Psp:Vnpay:TmnCode"),
            os.environ.get("VNPAY_HASH_SECRET") or store.get("Psp:Vnpay:HashSecret"))


def vnpay_sign(fields, secret):
    """Sorted, URL-encoded, HMAC-SHA512 — VNPay's rule, written out once."""
    query = "&".join(
        "%s=%s" % (urllib.parse.quote_plus(k), urllib.parse.quote_plus(v))
        for k, v in sorted(fields.items()) if v != "")
    return hmac.new(secret.encode(), query.encode(), hashlib.sha512).hexdigest()


def settle(call, op, order_ref, amount, response_code="00"):
    """Deliver the IPN for an order already handed over to VNPay.

    Split out of `pay()` because a booking is not the only thing that leaves for
    a gateway: a gift card does too (docs/01 TC-08), and it is settled by the
    same signed callback on the same route. Returns (ok, note).

    `response_code` is VNPay's own table. "00" is money that arrived; anything
    else is the gateway refusing, which - once a real gateway owns the card form
    - is the only way a script can still exercise a refusal at all. The card
    ending 0000 only ever meant something to the stand-in.
    """
    tmn, secret = vnpay_keys()
    if not secret or not order_ref:
        return False, "Không có khoá VNPay để ký thay cổng."

    fields = {
        "vnp_Amount": str(int(round(float(amount))) * 100),
        "vnp_BankCode": "NCB",
        "vnp_BankTranNo": "VNP" + str(order_ref)[-8:],
        "vnp_CardType": "ATM",
        "vnp_OrderInfo": "Staylio",
        "vnp_PayDate": str(order_ref)[:12] + "00",
        "vnp_ResponseCode": response_code,
        "vnp_TmnCode": tmn,
        "vnp_TransactionNo": "14" + str(order_ref)[-6:],
        "vnp_TransactionStatus": response_code,
        "vnp_TxnRef": str(order_ref),
    }
    fields["vnp_SecureHash"] = vnpay_sign(fields, secret)

    _, ipn = call(op, "/api/payments/vnpay/ipn?" + urllib.parse.urlencode(fields))
    code = (ipn or {}).get("RspCode") if isinstance(ipn, dict) else None

    # 00 recorded it, 02 says it was already recorded. A refusal is delivered
    # just as successfully as a payment - the platform accepting the news that
    # the money did not arrive is the whole point of the scenario.
    if code in ("00", "02"):
        return True, code
    return False, "IPN trả %s: %s" % (code, ipn)


def pay(call, op, booking_id, body=None, amount=None):
    """Pay a held booking, going through the gateway if one is wired.

    `call` is the suite's own request helper, so cookies and the base URL stay
    that suite's business. Returns (status, booking) exactly as /pay does, but
    for a gateway handover the booking returned is the one read back *after* the
    payment landed — which is what every caller actually wanted.
    """
    st, paid = call(op, "/api/bookings/%s/pay" % booking_id, body if body is not None else {})

    redirect = (paid or {}).get("gatewayRedirectUrl") if isinstance(paid, dict) else None
    if st not in (200, 201) or not redirect:
        return st, paid

    order_ref = paid.get("gatewayOrderRef")
    _, secret = vnpay_keys()

    if "vnpayment.vn" not in redirect or not secret or not order_ref:
        # Some other gateway, or no key to sign with. Say so rather than let the
        # caller report a business rule broken when the truth is a missing key.
        return st, dict(paid, gatewaySettled=False,
                        gatewayNote="Không ký thay được cổng này: %s" % redirect[:60])

    # The amount the session is for. A deposit charges half, so a caller taking
    # one has to say what it paid.
    due = amount if amount is not None else paid.get("total")

    settled, note = settle(call, op, order_ref, due)
    if not settled:
        return st, dict(paid, gatewaySettled=False, gatewayNote=note)

    # The IPN above was signed here, not by VNPay: they never saw this payment
    # and have no transaction under that reference. Leaving the session row would
    # send a later refund to a gateway that answers 91 — "no such transaction" —
    # and the guest's money would divert to balance for a reason that is entirely
    # this fixture's doing. Removing it puts refunds back on the stand-in, which
    # is what these suites are actually testing.
    _forget_session(booking_id)

    st2, after = call(op, "/api/bookings/%s" % booking_id)
    return (st2, after) if st2 == 200 else (st, paid)


def finish(call, op, st, dto, column, subject_id=None, amount=None):
    """Carry a ticket, a service or a split share through VNPay the way `pay()`
    carries a stay (docs/07 §13).

    Until 17/09/2026 those three were charged by the stand-in even with VNPay
    wired, so a suite got a confirmed ticket back from one request. Now the
    response says where the guest would be sent, and this signs the IPN for it.
    `column` names the payment_sessions column the subject hangs on
    (ExperienceBookingId, ServiceBookingId, BillShareId). Returns (st, dto) with
    `gatewaySettled` set to False when it could not finish.
    """
    if st not in (200, 201) or not isinstance(dto, dict) or not dto.get("gatewayRedirectUrl"):
        return st, dto

    redirect = dto["gatewayRedirectUrl"]
    order_ref = dto.get("gatewayOrderRef")
    _, secret = vnpay_keys()
    if "vnpayment.vn" not in redirect or not secret or not order_ref:
        return st, dict(dto, gatewaySettled=False,
                        gatewayNote="Không ký thay được cổng này: %s" % redirect[:60])

    due = amount if amount is not None else dto.get("total") or dto.get("amount")
    settled, note = settle(call, op, order_ref, due)
    if not settled:
        return st, dict(dto, gatewaySettled=False, gatewayNote=note)

    # Same reason as in pay(): VNPay never saw this order, so a later refund must
    # not be sent to them.
    sid = subject_id if subject_id is not None else dto.get("id")
    _forget_by(column, sid)
    return st, dict(dto, gatewaySettled=True, status="Confirmed")


def _forget_by(column, subject_id):
    try:
        subprocess.run(
            ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost", "-c",
             'delete from payment_sessions where "%s" = %s' % (column, int(subject_id))],
            capture_output=True, text=True, timeout=30)
    except Exception:
        pass


def _forget_session(booking_id):
    try:
        subprocess.run(
            ["docker", "exec", "stayhost-db", "psql", "-U", "stayhost", "-d", "stayhost", "-c",
             'delete from payment_sessions where "BookingId" = %s' % int(booking_id)],
            capture_output=True, text=True, timeout=30)
    except Exception:
        pass
