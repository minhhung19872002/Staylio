using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// docs/07 §13 — the trip out to a licensed gateway and back, for a stay.
///
/// Three things can tell the platform a payment happened: the guest's browser
/// coming back, the gateway's IPN, and the platform asking. All three land here,
/// and the first one to arrive with a real signature wins — the other two find
/// the session already settled and do nothing. That is what makes it safe for
/// them to race, and docs/07 §7 says a double charge is the worst fault in the
/// module.
/// </summary>
public class PspCheckout(
    StayHostDbContext db, PspRouter router, PaymentGateway gateway,
    PaymentCompletion completion, DataSecrets secrets, GiftCardService giftCards,
    ExperienceService experiences, ServiceMarketService market,
    SplitBillService splits, BalanceCollector balances,
    ILogger<PspCheckout> log)
{
    public sealed record Started(bool Ok, string? PayUrl = null, string? OrderRef = null, string? Error = null);

    /// <summary>
    /// Opens an order at the gateway and hands back the address to send the
    /// guest to. Nothing is charged here and nothing is written to the ledger:
    /// the money moves on somebody else's page.
    /// </summary>
    public async Task<Started> StartAsync(
        Booking booking, string method, decimal amount, bool partial,
        string attemptKey, string clientIp, CancellationToken ct,
        bool saveCard = false, string? cardToken = null)
    {
        var provider = router.For(method);
        if (provider is null) return new Started(false, Error: "Cách thanh toán này chưa nối cổng nào.");

        var now = DateTime.UtcNow;

        // docs/07 §7 — a guest who double-clicks, or whose browser retried the
        // request, must go back to the order that already exists rather than open
        // a second one at the gateway.
        var open = await db.PaymentSessions
            .Where(s => s.AttemptKey == attemptKey && s.Status == PaymentSessionStatus.Pending)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (open is { PayUrl.Length: > 0 } && open.CreatedAt.Add(PaymentSession.Window) > now)
            return new Started(true, open.PayUrl, open.OrderRef);

        var sequence = await db.PaymentSessions.CountAsync(s => s.BookingId == booking.Id, ct);
        var orderRef = await NextOrderRefAsync(booking.Id, now, sequence, ct);

        var session = new PaymentSession
        {
            OrderRef = orderRef,
            AttemptKey = attemptKey,
            BookingId = booking.Id,
            Provider = provider.Key,
            Method = method,
            Amount = amount,
            Partial = partial
        };

        db.PaymentSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var start = await provider.StartAsync(
            new PspOrder(orderRef, amount, $"Staylio {booking.Reference}", method, clientIp,
                SaveCard: saveCard,
                // The gateway keeps its tokens per user of ours, so it needs a
                // handle on the guest that survives them saving a second card.
                UserRef: booking.GuestUserId is { } id ? Psp.AppUserRef(id) : null,
                Token: cardToken), ct);

        if (!start.Ok || start.PayUrl is null)
        {
            session.Status = PaymentSessionStatus.Failed;
            session.ResponseCode = "start";
            session.CompletedAt = now;
            session.SettledBy = "start";
            await db.SaveChangesAsync(ct);
            return new Started(false, Error: start.Error ?? Payments.Message(DeclineReason.GatewayError));
        }

        session.PayUrl = start.PayUrl;

        // The dates stay off the market for as long as the gateway's own window,
        // the same way docs/07 §2.3 holds them while a transfer is in flight.
        // Without this the 15-minute checkout hold, most of which the guest has
        // already spent reading the page, could lapse while they are typing a
        // card number somewhere else.
        booking.HoldExpiresAt = now.Add(PaymentSession.Window);

        if (booking.Payment is not null)
        {
            booking.Payment.Method = method;
            booking.Payment.Status = PaymentStatus.Pending;
        }

        db.BookingEvents.Add(BookingLifecycle.Note(
            booking, "system", $"Chuyển sang cổng {provider.Key.ToUpperInvariant()} · mã {orderRef}."));

        await db.SaveChangesAsync(ct);

        return new Started(true, start.PayUrl, orderRef);
    }

    /// <summary>
    /// docs/01 TC-08 — the same trip out to a gateway, for a gift card.
    ///
    /// Deliberately a second method rather than a nullable booking threaded
    /// through the first: half of <see cref="StartAsync"/> is about a stay — the
    /// dates it holds off the market, the event written to its history, the
    /// payment row it updates — and none of that has any meaning for a card.
    /// What the two share is the part that matters, which is that the money is
    /// taken on somebody else's page and nothing is written to the ledger here.
    /// </summary>
    public async Task<Started> StartForGiftCardAsync(
        GiftCard card, string method, string attemptKey, string clientIp, CancellationToken ct)
    {
        var provider = router.For(method);
        if (provider is null) return new Started(false, Error: "Cách thanh toán này chưa nối cổng nào.");

        var now = DateTime.UtcNow;

        // Same double-click guard as a stay: a buyer whose browser retried goes
        // back to the order already open at the gateway rather than opening a
        // second one and paying twice.
        var open = await db.PaymentSessions
            .Where(s => s.AttemptKey == attemptKey && s.Status == PaymentSessionStatus.Pending)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (open is { PayUrl.Length: > 0 } && open.CreatedAt.Add(PaymentSession.Window) > now)
            return new Started(true, open.PayUrl, open.OrderRef);

        var sequence = await db.PaymentSessions.CountAsync(s => s.GiftCardId == card.Id, ct);
        var orderRef = await NextOrderRefAsync(card.Id, now, sequence, ct);

        var session = new PaymentSession
        {
            OrderRef = orderRef,
            AttemptKey = attemptKey,
            GiftCardId = card.Id,
            Provider = provider.Key,
            Method = method,
            Amount = card.Amount
        };

        db.PaymentSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var start = await provider.StartAsync(
            new PspOrder(orderRef, card.Amount, $"Staylio {card.Code}", method, clientIp,
                UserRef: card.PurchasedByUserId is { } id ? Psp.AppUserRef(id) : null), ct);

        if (!start.Ok || start.PayUrl is null)
        {
            session.Status = PaymentSessionStatus.Failed;
            session.ResponseCode = "start";
            session.CompletedAt = now;
            session.SettledBy = "start";
            await db.SaveChangesAsync(ct);
            await giftCards.CancelUnpaidAsync(card.Id, ct);
            return new Started(false, Error: start.Error ?? Payments.Message(DeclineReason.GatewayError));
        }

        session.PayUrl = start.PayUrl;
        await db.SaveChangesAsync(ct);

        return new Started(true, start.PayUrl, orderRef);
    }

    /// <summary>
    /// The same trip out to a gateway for everything that is not a whole stay or
    /// a gift card: an experience ticket, a service, the second half of a
    /// part-paid stay, one person's share of a split bill (docs/07 §13).
    ///
    /// Until this existed all four were charged by the stand-in even on a site
    /// whose every checkout row went to a licensed gateway — a ticket, a
    /// service, a balance or a share confirmed with nobody having paid.
    /// </summary>
    /// <param name="draft">
    /// The session to open: subject id(s), method, amount and attempt key. The
    /// order reference and provider are filled in here.
    /// </param>
    public async Task<Started> StartForAsync(
        PaymentSession draft, int subjectId, string description, int? userId,
        string clientIp, CancellationToken ct)
    {
        var provider = router.For(draft.Method);
        if (provider is null) return new Started(false, Error: "Cách thanh toán này chưa nối cổng nào.");
        if (draft.Amount <= 0) return new Started(false, Error: "Không có khoản nào cần trả.");

        var now = DateTime.UtcNow;

        // Double-click guard, as for a stay — but only onto an order for the same
        // method and amount, or a guest who switches from MoMo to a card would be
        // sent back to MoMo.
        var open = await db.PaymentSessions
            .Where(s => s.AttemptKey == draft.AttemptKey && s.Status == PaymentSessionStatus.Pending)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (open is { PayUrl.Length: > 0 } && open.Method == draft.Method
            && open.Amount == draft.Amount && open.CreatedAt.Add(PaymentSession.Window) > now)
            return new Started(true, open.PayUrl, open.OrderRef);

        var sequence = await db.PaymentSessions.CountAsync(s => s.AttemptKey == draft.AttemptKey, ct);
        draft.OrderRef = await NextOrderRefAsync(subjectId, now, sequence, ct);
        draft.Provider = provider.Key;
        draft.Status = PaymentSessionStatus.Pending;
        draft.CreatedAt = now;

        db.PaymentSessions.Add(draft);
        await db.SaveChangesAsync(ct);

        var start = await provider.StartAsync(
            new PspOrder(draft.OrderRef, draft.Amount, description, draft.Method, clientIp,
                UserRef: userId is { } id ? Psp.AppUserRef(id) : null), ct);

        if (!start.Ok || start.PayUrl is null)
        {
            draft.Status = PaymentSessionStatus.Failed;
            draft.ResponseCode = "start";
            draft.CompletedAt = now;
            draft.SettledBy = "start";
            await db.SaveChangesAsync(ct);
            return new Started(false, Error: start.Error ?? Payments.Message(DeclineReason.GatewayError));
        }

        draft.PayUrl = start.PayUrl;
        await db.SaveChangesAsync(ct);
        return new Started(true, start.PayUrl, draft.OrderRef);
    }

    /// <summary>
    /// An order reference nobody has used. The reference embeds the subject id,
    /// and ids of different subjects overlap — gift card 5 and booking 5 opened
    /// in the same second used to collide on the unique index.
    /// </summary>
    private async Task<string> NextOrderRefAsync(int subjectId, DateTime now, int sequence, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            var candidate = Psp.OrderRef(subjectId, now, sequence + i);
            if (!await db.PaymentSessions.AnyAsync(s => s.OrderRef == candidate, ct)) return candidate;
        }

        return Psp.OrderRef(subjectId, now.AddSeconds(1), sequence);
    }

    public Task<PaymentSession?> FindAsync(string? orderRef, CancellationToken ct)
    {
        var raw = (orderRef ?? "").Trim();
        if (raw.Length == 0) return Task.FromResult<PaymentSession?>(null);

        // ZaloPay hands back the id it was given, date prefix and all.
        var underscore = raw.IndexOf('_');
        if (underscore >= 0) raw = raw[(underscore + 1)..];

        return db.PaymentSessions
            .Include(s => s.Booking!).ThenInclude(b => b.Payment)
            .Include(s => s.Booking!).ThenInclude(b => b.Listing)
            .Include(s => s.Booking!).ThenInclude(b => b.Events)
            .Include(s => s.BillShare)
            .FirstOrDefaultAsync(s => s.OrderRef == raw, ct);
    }

    /// <summary>
    /// Records what a gateway said and, if it said the money moved, confirms the
    /// booking through the same path an ordinary payment takes.
    ///
    /// Safe to call more than once for the same session: the first settlement
    /// wins and the rest are told what it decided.
    /// </summary>
    public async Task<PaymentSessionStatus> SettleAsync(
        PaymentSession session, PspVerdict verdict, string settledBy, CancellationToken ct)
    {
        if (session.Status != PaymentSessionStatus.Pending) return session.Status;

        // An unsigned payload is not news about the payment, whichever way it
        // leans. Anyone can post to the callback routes, so this is the line that
        // stops a stranger writing off somebody else's booking.
        if (verdict.Code == PspVerdict.Signature)
        {
            log.LogWarning("Bỏ qua callback không đúng chữ ký cho đơn {Ref}.", session.OrderRef);
            return PaymentSessionStatus.Pending;
        }

        if (verdict.Status == PaymentSessionStatus.Pending) return PaymentSessionStatus.Pending;

        var now = DateTime.UtcNow;
        session.SettledBy = settledBy;
        session.ResponseCode = verdict.Code;
        session.CompletedAt = now;
        if (!string.IsNullOrWhiteSpace(verdict.TxnId)) session.ProviderTxnId = verdict.TxnId;
        if (!string.IsNullOrWhiteSpace(verdict.PaidAt)) session.ProviderPaidAt = verdict.PaidAt;

        if (verdict.Status != PaymentSessionStatus.Paid)
        {
            session.Status = verdict.Status;
            await FailAttemptAsync(session, verdict.Decline, ct);
            await db.SaveChangesAsync(ct);

            log.LogInformation("Cổng {Provider} trả lời {Status} cho đơn {Ref} (mã {Code}).",
                session.Provider, verdict.Status, session.OrderRef, verdict.Code);

            return session.Status;
        }

        // docs/07 §7 — a gateway reporting a different amount than the booking is
        // for is not a payment to act on. Confirming a stay on it would be the
        // exact fault the module is built around, so it is refused and shouted
        // about rather than quietly accepted.
        if (verdict.Amount > 0 && !Psp.AmountMatches(session.Amount, verdict.Amount))
        {
            session.Status = PaymentSessionStatus.Failed;
            session.ResponseCode = "amount";
            await FailAttemptAsync(session, DeclineReason.GatewayError, ct);
            await db.SaveChangesAsync(ct);

            log.LogError(
                "Cổng {Provider} báo đã thu {Reported} cho đơn {Ref} nhưng đơn là {Expected}. Không xác nhận.",
                session.Provider, verdict.Amount, session.OrderRef, session.Amount);

            return PaymentSessionStatus.Failed;
        }

        session.Status = PaymentSessionStatus.Paid;

        // The gateway's own book, which is one half of the daily reconciliation of
        // docs/07 §7. Written through PaymentGateway so business code still never
        // touches that table directly.
        gateway.RecordExternalCharge(session.AttemptKey, session.Amount, session.Method);

        // docs/01 TC-08 — a gift card was what this visit paid for. The card is
        // worth nothing until here: it was created AwaitingPayment, with no
        // ledger entry and no code sent, so this is the first moment the sale is
        // real. No attempt row, because that exists to stop one stay being
        // charged twice, and the guard at the top of this method already refuses
        // to settle a session that is not Pending.
        if (session.GiftCardId is { } giftCardId)
        {
            await db.SaveChangesAsync(ct);
            await giftCards.ActivateAsync(giftCardId, session.Amount, ct);
            log.LogInformation("Thẻ quà tặng {Id} đã thu đủ tiền qua {Provider}.",
                giftCardId, session.Provider);
            return PaymentSessionStatus.Paid;
        }

        // docs/09 and docs/01 ĐP-06/ĐP-07 — the subjects that are not a whole
        // stay. Each confirms through the same code its other paths run, and each
        // answers false when the subject stopped waiting for this money while the
        // guest was away (the seats lapsed, the split expired, the stay was
        // cancelled). Money that arrived for nothing is sent straight back.
        bool? applied = null;

        if (session.ExperienceBookingId is { } xpId)
        {
            await db.SaveChangesAsync(ct);
            applied = await experiences.ConfirmPaidAsync(xpId, ct);
        }
        else if (session.ServiceBookingId is { } svcId)
        {
            await db.SaveChangesAsync(ct);
            applied = await market.ConfirmPaidAsync(svcId, ct);
        }
        else if (session.BillShareId is { } shareId)
        {
            await db.SaveChangesAsync(ct);
            applied = await splits.SharePaidAsync(shareId, session.Amount, ct);
        }
        else if (session.IsBalance && session.BookingId is { } balanceOf)
        {
            await db.SaveChangesAsync(ct);
            applied = await balances.CollectedAsync(balanceOf, session.Amount, $"psp:{settledBy}", ct);
        }

        if (applied is { } done)
        {
            if (!done) await ReturnOrphanAsync(session, ct);
            log.LogInformation("Phiên {Ref} qua {Provider} đã thu {Amount} ({By}); áp vào chủ thể: {Done}.",
                session.OrderRef, session.Provider, session.Amount, settledBy, done);
            return PaymentSessionStatus.Paid;
        }

        var claim = await db.PaymentAttempts.FirstOrDefaultAsync(a => a.Key == session.AttemptKey, ct);
        if (claim is null)
        {
            claim = new PaymentAttempt
            {
                Key = session.AttemptKey, BookingId = session.BookingId ?? 0,
                Amount = session.Amount, Method = session.Method
            };
            db.PaymentAttempts.Add(claim);
        }
        claim.Status = PaymentAttemptStatus.Succeeded;
        claim.CompletedAt = now;
        claim.Message = null;

        await db.SaveChangesAsync(ct);

        var booking = session.Booking ?? await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events).Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == session.BookingId, ct);

        if (booking is null) return PaymentSessionStatus.Paid;

        // Already confirmed — by the IPN, or by the sweep, or by the guest coming
        // back before either. Nothing left to do, and doing it twice would post
        // the ledger twice.
        if (booking.Status != BookingStatus.PendingPayment) return PaymentSessionStatus.Paid;

        var price = await completion.QuoteFromRecordAsync(booking, ct);
        if (price is null)
        {
            log.LogError("Đơn {Ref} đã thu tiền qua {Provider} nhưng không dựng lại được giá.",
                booking.Reference, session.Provider);
            return PaymentSessionStatus.Paid;
        }

        // docs/07 §4 — VNPay's token API is the only thing that ever tells this
        // platform four digits of a card, so when it does, they are kept: §10's
        // closed-card refund branch and §4's expiry reminder both read that
        // column and have had nothing to read since the card form went away.
        await RememberCardAsync(session, verdict, booking, ct);

        await completion.ConfirmAsync(
            booking, price, session.Amount, session.Partial, DateOnly.FromDateTime(now),
            // docs/07 §2.5 — null, not zero. A booking made without an account has
            // no user to look up, and `?? 0` sent the confirmation to a search for
            // user id 0: it found nobody and returned quietly, so a guest who paid
            // through the gateway was never sent the reference they need.
            booking.GuestUserId, session.Method, verdict.CardLast4, ct);

        log.LogInformation("Đơn {Ref} đã xác nhận sau khi {Provider} thu {Amount} ({By}).",
            booking.Reference, session.Provider, session.Amount, settledBy);

        return PaymentSessionStatus.Paid;
    }

    /// <summary>
    /// Money that arrived for something no longer waiting for it goes straight
    /// back. Keeping it would hold a guest's money for a ticket they do not have;
    /// the error line is what support reconciles by if the gateway refuses.
    /// </summary>
    private async Task ReturnOrphanAsync(PaymentSession session, CancellationToken ct)
    {
        if (router.ByKey(session.Provider) is not { } provider) return;

        var result = await provider.RefundAsync(new PspRefund(
            session.OrderRef, session.Amount, session.Amount, session.ProviderTxnId,
            session.ProviderPaidAt, session.CreatedAt, $"Hoan tien {session.OrderRef}", "system"), ct);

        session.RefundedAmount = session.Amount;
        session.RefundTxnId = result.TxnId;
        session.RefundCode = result.Code;
        session.RefundedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (result.Outcome == Psp.RefundOutcome.Accepted)
            log.LogWarning("Phiên {Ref} đã thu tiền cho một chủ thể không còn chờ; đã hoàn {Amount}.",
                session.OrderRef, session.Amount);
        else
            log.LogError(
                "Phiên {Ref} đã thu {Amount} cho một chủ thể không còn chờ và KHÔNG hoàn được ({Code}). Cần xử lý tay.",
                session.OrderRef, session.Amount, result.Code);
    }

    /// <summary>
    /// docs/07 §4 — keeps the card the guest asked to keep.
    ///
    /// The number is never here: what arrives is four digits VNPay chose to show
    /// and a token only they can use. Saving the same card twice is a no-op, so a
    /// guest who ticks the box on every booking ends up with one row, not ten.
    /// </summary>
    private async Task RememberCardAsync(
        PaymentSession session, PspVerdict verdict, Booking booking, CancellationToken ct)
    {
        if (booking.GuestUserId is not { } userId) return;
        if (verdict.CardToken is not { Length: > 0 } || verdict.CardLast4 is not { Length: 4 }) return;

        var sealedToken = secrets.Seal(DataSecrets.CardToken, verdict.CardToken);

        // No key, no storing it — the same rule the payout account follows. The
        // last four digits are not a secret and are kept either way.
        if (sealedToken is null)
        {
            log.LogWarning("Chưa có khoá mã hoá nên không lưu được thẻ của người dùng {UserId}.", userId);
            return;
        }

        var brand = Psp.VnPayIsDomesticCard(verdict.CardType) ? CardBrand.Napas : CardBrand.Unknown;

        var already = await db.SavedCards.FirstOrDefaultAsync(
            c => c.UserId == userId && c.Last4 == verdict.CardLast4
                 && c.Provider == session.Provider, ct);

        if (already is not null)
        {
            // The token can be reissued for the same card; the newest one wins.
            already.GatewayTokenSealed = sealedToken;
            already.Brand = brand;
            await db.SaveChangesAsync(ct);
            return;
        }

        var cards = await db.SavedCards.Where(c => c.UserId == userId).ToListAsync(ct);

        var card = new SavedCard
        {
            UserId = userId,
            Brand = brand,
            Last4 = verdict.CardLast4,
            // VNPay's token API returns no expiry date at all — the card's
            // expiry is theirs to know, and SavedCards.ExpiryKnown says so
            // rather than this pretending to a month it was never told.
            ExpiryMonth = 0,
            ExpiryYear = 0,
            Provider = session.Provider,
            GatewayTokenSealed = sealedToken
        };

        db.SavedCards.Add(card);
        await db.SaveChangesAsync(ct);

        // The first card saved is the default, exactly as the typed-in path does.
        cards.Add(card);
        SavedCards.Reseat(cards);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Đã lưu thẻ •••• {Last4} của người dùng {UserId} tại {Provider}.",
            verdict.CardLast4, userId, session.Provider);
    }

    /// <summary>
    /// A refusal is written down, because docs/07 §8 counts refusals: five in an
    /// hour on one booking and the guest is stopped. A guest pressing "huỷ" on
    /// the gateway's own page is not one of those — they did not fail to pay,
    /// they decided not to — so nothing is recorded and the dates stay held for
    /// the second try.
    /// </summary>
    private async Task FailAttemptAsync(PaymentSession session, DeclineReason reason, CancellationToken ct)
    {
        if (session.Status == PaymentSessionStatus.Cancelled) return;

        // An attempt row counts refusals against one booking, so a gift card has
        // nothing to write here. Its protection is different and simpler: the
        // card stays AwaitingPayment, which CreditRules.CanRedeem refuses.
        if (session.BookingId is not { } bookingId || session.IsBalance || session.BillShareId is not null) return;

        var claim = await db.PaymentAttempts.FirstOrDefaultAsync(a => a.Key == session.AttemptKey, ct);

        if (claim is null)
        {
            claim = new PaymentAttempt
            {
                Key = session.AttemptKey, BookingId = bookingId,
                Amount = session.Amount, Method = session.Method
            };
            db.PaymentAttempts.Add(claim);
        }
        else if (claim.Status == PaymentAttemptStatus.Succeeded) return;

        claim.Status = PaymentAttemptStatus.Failed;
        claim.Reason = reason;
        claim.Message = Payments.Message(reason);
        claim.CompletedAt = DateTime.UtcNow;
    }
}
