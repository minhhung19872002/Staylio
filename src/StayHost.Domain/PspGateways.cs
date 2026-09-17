using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace StayHost.Domain;

/// <summary>
/// docs/07 §13, phương án A — the arithmetic of talking to a licensed Vietnamese
/// payment gateway: what gets signed, in what order, with which key.
///
/// It lives in the domain rather than beside the HTTP client because it is the
/// part that can be wrong silently. A mis-ordered signature string does not
/// throw; the gateway simply answers "invalid signature" for every payment, or
/// worse, accepts a callback nobody signed. So the ordering is written down once,
/// here, with tests against the vendors' own published examples.
///
/// Nothing in this file makes a network call or reads configuration. It turns
/// (fields, secret) into a string and back.
/// </summary>
public static class Psp
{
    /// <summary>The three gateways docs/07 §13 names as licensed collection agents.</summary>
    public const string VnPay = "vnpay";
    public const string MoMo = "momo";
    public const string ZaloPay = "zalopay";

    /// <summary>
    /// OnePay, added after VNPay's sandbox turned out to have no international
    /// test card: their domestic NCB card is the only one published, so the
    /// Visa/Mastercard row could be opened but never completed. OnePay is the
    /// Vietnamese arm of Mastercard's own gateway (MiGS/MPGS) and its public
    /// test merchant does authorise a Visa card end to end.
    /// </summary>
    public const string OnePay = "onepay";

    /* ------------------------------------------------------------- the order ref */

    /// <summary>
    /// What the gateway will know this payment by, and the only thing it hands
    /// back. It has to survive a round trip through someone else's system, so it
    /// is short, has no punctuation any of the three dislike, and is unique per
    /// attempt rather than per booking — a guest who fails on MoMo and retries on
    /// VNPay must not reuse a reference the first gateway has already recorded.
    /// </summary>
    public static string OrderRef(int bookingId, DateTime nowUtc, int sequence) =>
        $"{Vn(nowUtc):yyMMddHHmmss}{bookingId:D6}{sequence % 100:D2}";

    /// <summary>
    /// ZaloPay refuses any <c>app_trans_id</c> that does not begin with today's
    /// date in Ho Chi Minh City, and it means today on <em>their</em> clock. This
    /// build stores and reasons in UTC, so a payment made between 00:00 and 07:00
    /// Vietnam time would carry yesterday's prefix and be rejected — the same
    /// seven-hour trap the acceptance scripts fell into.
    /// </summary>
    public static string ZaloTransId(string orderRef, DateTime nowUtc) =>
        $"{Vn(nowUtc):yyMMdd}_{orderRef}";

    /// <summary>The booking id back out of an order reference.</summary>
    public static int? BookingOf(string? orderRef)
    {
        var raw = (orderRef ?? "").Trim();

        // ZaloPay hands back what it was given, prefix and all.
        var underscore = raw.IndexOf('_');
        if (underscore >= 0) raw = raw[(underscore + 1)..];

        if (raw.Length != 20 || !raw.All(char.IsAsciiDigit)) return null;
        return int.TryParse(raw.AsSpan(12, 6), out var id) ? id : null;
    }

    /// <summary>Vietnam is what every one of these gateways timestamps in.</summary>
    public static DateTime Vn(DateTime utc) => utc.AddHours(7);

    /// <summary>
    /// The guest's address in a shape VNPay will accept: their spec says
    /// <c>vnp_IpAddr</c> is alphanumeric of length 7 to 45.
    ///
    /// Kestrel hands back <c>::1</c> for anything on the same machine, which is
    /// three characters and full of colons — VNPay answers a blank error page
    /// with no reason given, and the natural conclusion is that the signature is
    /// wrong. It is not; the address is. An IPv4 address behind an IPv6 socket
    /// arrives as <c>::ffff:203.0.113.5</c> and has to be unwrapped for the same
    /// reason.
    /// </summary>
    public static string ClientIp(string? raw)
    {
        var ip = (raw ?? "").Trim();

        if (ip.Length == 0 || ip is "::1" or "0.0.0.0" or "::") return "127.0.0.1";

        // ::ffff:203.0.113.5 — an IPv4 address wearing an IPv6 coat.
        const string mapped = "::ffff:";
        if (ip.StartsWith(mapped, StringComparison.OrdinalIgnoreCase))
            ip = ip[mapped.Length..];

        // A real IPv6 address is longer than 45 characters only in exotic cases,
        // but a truncated one would be a lie; anything unusable becomes the
        // loopback rather than a value the gateway will silently choke on.
        return ip.Length is >= 7 and <= 45 ? ip : "127.0.0.1";
    }

    /* -------------------------------------------------------------------- VNPay */

    /// <summary>
    /// VNPay signs the whole parameter set: names sorted ascending, values
    /// URL-encoded, joined with <c>&amp;</c>, HMAC-SHA512 in lowercase hex.
    ///
    /// The encoder has to be the same one that builds the query the browser is
    /// sent to, or the string VNPay rebuilds on its side differs from the one
    /// signed here by exactly one space. <see cref="WebUtility.UrlEncode"/> is
    /// what their own sample uses, so it is what both sides of this file use.
    /// </summary>
    public static string VnPayQuery(IReadOnlyDictionary<string, string> fields)
    {
        var sb = new StringBuilder();

        foreach (var (k, v) in Sorted(fields))
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(WebUtility.UrlEncode(k)).Append('=').Append(WebUtility.UrlEncode(v));
        }

        return sb.ToString();
    }

    public static string VnPaySign(IReadOnlyDictionary<string, string> fields, string secret) =>
        HmacHex(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(VnPayQuery(fields))));

    /// <summary>
    /// Checks a return or an IPN. The two hash fields are dropped before the
    /// rebuild — they were not part of what was signed — and so is anything
    /// outside the <c>vnp_</c> family, because a caller who appends
    /// <c>?anything=1</c> to the return URL must not be able to break the check.
    ///
    /// Both spellings of the hash field are accepted because VNPay uses both:
    /// the payment API sends <c>vnp_SecureHash</c> and the token API
    /// <c>vnp_secure_hash</c>. The rule for building the string is the same for
    /// both — established by asking their sandbox, which accepted the sorted
    /// query and answered <c>error.html</c> to every pipe-joined variant.
    /// </summary>
    public static bool VnPayVerify(IReadOnlyDictionary<string, string> query, string secret)
    {
        var given = query.GetValueOrDefault("vnp_SecureHash")
                    ?? query.GetValueOrDefault("vnp_secure_hash");

        if (string.IsNullOrWhiteSpace(given)) return false;

        var signed = query
            .Where(p => p.Key.StartsWith("vnp_", StringComparison.Ordinal)
                        && !IsHashField(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);

        return Same(VnPaySign(signed, secret), given);
    }

    private static bool IsHashField(string key) =>
        key is "vnp_SecureHash" or "vnp_SecureHashType" or "vnp_secure_hash" or "vnp_secure_hash_type";

    /// <summary>VNPay counts in đồng × 100 and refuses a decimal point.</summary>
    public static long VnPayAmount(decimal dong) => (long)Math.Round(dong, MidpointRounding.AwayFromZero) * 100;

    /// <summary>
    /// The querydr / refund checksum, which is a different shape from the payment
    /// one: a pipe-joined list in a fixed order rather than a sorted query.
    /// </summary>
    public static string VnPayApiSign(string secret, params string[] parts) =>
        HmacHex(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Join('|', parts))));

    /// <summary>
    /// docs/07 §8 — what to tell the guest. VNPay's own code never reaches the
    /// screen; it is turned into one of the reasons the message table knows.
    /// </summary>
    public static DeclineReason VnPayDecline(string? code) => code switch
    {
        "00" => DeclineReason.Unknown,           // not a decline at all
        "07" => DeclineReason.SuspectedFraud,
        "09" => DeclineReason.OnlinePaymentsOff, // card not registered for internet banking
        "10" => DeclineReason.IncorrectDetails,  // authentication failed 3 times
        "11" => DeclineReason.GatewayError,      // payment window expired
        "12" => DeclineReason.BankRefused,       // card locked
        "13" => DeclineReason.IncorrectDetails,  // wrong OTP
        "51" => DeclineReason.InsufficientFunds,
        "65" => DeclineReason.LimitExceeded,
        "75" => DeclineReason.GatewayError,      // issuing bank under maintenance
        "79" => DeclineReason.IncorrectDetails,  // wrong password too many times
        "24" => DeclineReason.Unknown,           // guest cancelled
        _ => DeclineReason.Unknown
    };

    /// <summary>A guest who pressed "huỷ" on VNPay's page did not fail; they left.</summary>
    public static bool VnPayCancelled(string? code) => code == "24";

    /* ------------------------------------------------------ §10, sending it back */

    /// <summary>
    /// docs/07 §10 — the checksum for a refund, which is a pipe-joined list in a
    /// fixed order and <em>not</em> the sorted query the payment uses.
    ///
    /// The order is also not the order the parameters are documented in:
    /// <c>vnp_TransactionNo</c> sits in the middle and <c>vnp_OrderInfo</c> comes
    /// last. Their documentation writes it out, so it is copied here verbatim
    /// rather than inferred:
    ///
    /// <code>
    /// data = vnp_RequestId | vnp_Version | vnp_Command | vnp_TmnCode
    ///      | vnp_TransactionType | vnp_TxnRef | vnp_Amount | vnp_TransactionNo
    ///      | vnp_TransactionDate | vnp_CreateBy | vnp_CreateDate | vnp_IpAddr
    ///      | vnp_OrderInfo
    /// </code>
    /// </summary>
    public static string VnPayRefundSign(
        string secret, string requestId, string version, string command, string tmnCode,
        string transactionType, string txnRef, string amount, string transactionNo,
        string transactionDate, string createBy, string createDate, string ipAddr, string orderInfo) =>
        VnPayApiSign(secret, requestId, version, command, tmnCode, transactionType, txnRef,
            amount, transactionNo, transactionDate, createBy, createDate, ipAddr, orderInfo);

    /// <summary>Full or partial, in VNPay's own numbering.</summary>
    public static string VnPayRefundType(decimal refunded, decimal original) =>
        refunded >= original ? "02" : "03";

    /// <summary>
    /// What a gateway's answer to a refund actually means.
    ///
    /// The distinction that matters is not success versus failure but
    /// <em>permanent</em> refusal versus <em>not knowing yet</em>. A permanent
    /// refusal is docs/07 §10's case — the card is closed, so the money becomes
    /// balance. Not knowing is a reason to ask again, and turning it into balance
    /// would risk paying the guest twice.
    /// </summary>
    public enum RefundOutcome
    {
        /// <summary>The gateway took the request. The money is on its way back.</summary>
        Accepted,
        /// <summary>It will never happen: closed card, unknown transaction, rejected.</summary>
        Refused,
        /// <summary>No answer worth acting on. Ask again.</summary>
        Unknown
    }

    /// <summary>
    /// docs/07 §10 — reading VNPay's refund reply.
    ///
    /// <c>vnp_ResponseCode</c> answers "did you accept my request", not "did the
    /// money move": 00 with a transaction status of 05 or 06 means they are still
    /// working on it, and 09 means the bank said no. Treating 00 alone as done
    /// would report a refund that was refused a second later.
    /// </summary>
    public static RefundOutcome VnPayRefundOutcome(string? responseCode, string? transactionStatus)
    {
        // 09 is the bank refusing, whatever the response code said.
        if (transactionStatus == "09") return RefundOutcome.Refused;

        return responseCode switch
        {
            // 00 accepted; 05 processing, 06 sent to the bank — all in flight.
            "00" => transactionStatus is null or "00" or "05" or "06"
                ? RefundOutcome.Accepted
                : RefundOutcome.Unknown,

            // Already being processed, from a retry of the same request id.
            "94" => RefundOutcome.Accepted,

            // Nothing to refund, or the original never succeeded. Neither will
            // improve by asking again.
            "91" or "95" => RefundOutcome.Refused,

            // 02 bad TmnCode, 03 bad format, 97 bad checksum, 99 unexplained —
            // all faults on this side or theirs, none of them the guest's card.
            _ => RefundOutcome.Unknown
        };
    }

    /// <summary>
    /// The id a refund is known by at VNPay, derived rather than random so a
    /// retry after a lost reply is recognised as the same request instead of
    /// refunding a second time. Their rule is one per day, so the day is in it.
    /// </summary>
    public static string RefundRequestId(string orderRef, decimal amount, DateTime nowUtc) =>
        $"r{Vn(nowUtc):yyMMdd}{orderRef[^10..]}{(long)Math.Round(amount) % 100000:D5}";

    /// <summary>MoMo signs a refund over its own short list, alphabetical again.</summary>
    public static string MoMoRefundSign(
        string accessKey, string secretKey, long amount, string description,
        string orderId, string partnerCode, string requestId, string transId) =>
        Sha256Hex(secretKey,
            $"accessKey={accessKey}&amount={amount}&description={description}&orderId={orderId}" +
            $"&partnerCode={partnerCode}&requestId={requestId}&transId={transId}");

    /// <summary>ZaloPay's refund mac: five fields, pipe-joined, signed with key1.</summary>
    public static string ZaloRefundMac(
        string key1, string appId, string zpTransId, long amount, string description, long timestampMs) =>
        Sha256Hex(key1, $"{appId}|{zpTransId}|{amount}|{description}|{timestampMs}");

    /// <summary>ZaloPay wants <c>m_refund_id</c> prefixed with today's date and the app id.</summary>
    public static string ZaloRefundId(string appId, string orderRef, DateTime nowUtc) =>
        $"{Vn(nowUtc):yyMMdd}_{appId}_{orderRef[^10..]}";

    /* -------------------------------------------------- §14.2, cards we never see */

    /// <summary>
    /// docs/07 §4 — VNPay's token API, which is how the platform gets a card's
    /// last four digits back after §14.2 took the card form away.
    ///
    /// Two things about it differ from the payment API and both are easy to miss:
    /// every parameter name is lower-case with underscores (<c>vnp_command</c>,
    /// not <c>vnp_Command</c>), and it lives on its own host path. The checksum
    /// rule is the same sorted query — not documented anywhere, established by
    /// sending their sandbox one of each and seeing which reached a payment page.
    /// </summary>
    public const string VnPayCreateTokenCommand = "pay_and_create";

    /// <summary>Charging a card the guest already saved. A redirect, not a server call.</summary>
    public const string VnPayTokenPayCommand = "token_pay";

    /// <summary>docs/07 §15.5 — telling VNPay to stop holding a card.</summary>
    public const string VnPayRemoveTokenCommand = "token_remove";

    /// <summary>
    /// Who the gateway's token store knows this guest as.
    ///
    /// It is written here rather than at the two call sites because a token is
    /// created under this name and removed under it, and the two must agree
    /// exactly. They did not: creation used "stayhost-4" while removal sent "4",
    /// and VNPay answers a mismatch with <c>11 — The token is not found</c>,
    /// which is the same thing it says about a card that really has gone. The
    /// removal would have looked successful for ever while the card stayed on
    /// their side.
    /// </summary>
    public static string AppUserRef(int userId) => $"stayhost-{userId}";

    /// <summary>VNPay's own words for the two card families, on the token API only.</summary>
    public static string VnPayCardType(string method) => method == "napas" ? "01" : "02";

    /// <summary>
    /// The last four digits out of the masked number VNPay hands back
    /// (<c>vnp_card_number</c>, e.g. <c>970436xxxxxx1234</c>).
    ///
    /// This is the whole point of the exercise: with a real gateway the guest
    /// types the card on VNPay's page, so nothing else in this platform ever
    /// learns those four digits — and docs/07 §4's saved cards, the expiring-card
    /// reminder and §10's closed-card refund branch all read exactly that field.
    /// </summary>
    public static string? Last4Of(string? maskedCardNumber)
    {
        var digits = new string((maskedCardNumber ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : null;
    }

    /// <summary>
    /// The card family VNPay says it was, mapped onto the brands docs/07 §4
    /// stores. Their token API answers only "domestic" or "international", so a
    /// Visa and a Mastercard are indistinguishable here — which is honest: the
    /// platform genuinely does not know, and guessing a brand from a masked
    /// number's first digit would be a guess printed as fact.
    /// </summary>
    public static bool VnPayIsDomesticCard(string? cardType) => cardType == "01";

    /* --------------------------------------------------------------------- MoMo */

    /// <summary>
    /// MoMo signs a fixed alphabetical list of named fields, not whatever it was
    /// sent. Anything missing is signed as an empty value, which is why every
    /// caller passes <c>extraData</c> even when it has none.
    /// </summary>
    public static string MoMoCreateSign(
        string accessKey, string secretKey, long amount, string extraData, string ipnUrl,
        string orderId, string orderInfo, string partnerCode, string redirectUrl,
        string requestId, string requestType)
    {
        var raw = $"accessKey={accessKey}&amount={amount}&extraData={extraData}&ipnUrl={ipnUrl}" +
                  $"&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}" +
                  $"&redirectUrl={redirectUrl}&requestId={requestId}&requestType={requestType}";

        return Sha256Hex(secretKey, raw);
    }

    /// <summary>
    /// The signature on a redirect or an IPN. A different field list from the
    /// create call, and the reason a callback can be trusted at all: MoMo is the
    /// only party who can produce it.
    /// </summary>
    public static string MoMoResultSign(
        string accessKey, string secretKey, long amount, string extraData, string message,
        string orderId, string orderInfo, string orderType, string partnerCode, string payType,
        string requestId, string responseTime, string resultCode, string transId)
    {
        var raw = $"accessKey={accessKey}&amount={amount}&extraData={extraData}&message={message}" +
                  $"&orderId={orderId}&orderInfo={orderInfo}&orderType={orderType}" +
                  $"&partnerCode={partnerCode}&payType={payType}&requestId={requestId}" +
                  $"&responseTime={responseTime}&resultCode={resultCode}&transId={transId}";

        return Sha256Hex(secretKey, raw);
    }

    public static string MoMoQuerySign(
        string accessKey, string secretKey, string orderId, string partnerCode, string requestId) =>
        Sha256Hex(secretKey,
            $"accessKey={accessKey}&orderId={orderId}&partnerCode={partnerCode}&requestId={requestId}");

    /// <summary>MoMo says 0 for taken, 1000 for "the guest has not decided yet".</summary>
    public static bool MoMoPaid(int resultCode) => resultCode == 0;

    public static bool MoMoPending(int resultCode) => resultCode is 1000 or 7000 or 7002 or 9000;

    public static DeclineReason MoMoDecline(int resultCode) => resultCode switch
    {
        1001 => DeclineReason.InsufficientFunds,
        1004 => DeclineReason.LimitExceeded,
        1005 or 1006 => DeclineReason.GatewayError,   // expired or cancelled by guest
        1007 or 1017 => DeclineReason.BankRefused,
        2001 or 2007 => DeclineReason.IncorrectDetails,
        4001 or 4100 => DeclineReason.SuspectedFraud,
        _ => DeclineReason.Unknown
    };

    /// <summary>1006 is the guest pressing cancel, not a refusal to act on.</summary>
    public static bool MoMoCancelled(int resultCode) => resultCode is 1006 or 1003;

    /* ------------------------------------------------------------------ ZaloPay */

    /// <summary>
    /// ZaloPay's create mac: seven fields, pipe-joined, in this order and no
    /// other. Signed with key1.
    /// </summary>
    public static string ZaloCreateMac(
        string key1, string appId, string appTransId, string appUser, long amount,
        long appTimeMs, string embedData, string item) =>
        Sha256Hex(key1, $"{appId}|{appTransId}|{appUser}|{amount}|{appTimeMs}|{embedData}|{item}");

    /// <summary>The query mac, signed with key1 but shaped differently again.</summary>
    public static string ZaloQueryMac(string key1, string appId, string appTransId) =>
        Sha256Hex(key1, $"{appId}|{appTransId}|{key1}");

    /// <summary>
    /// The callback mac, signed with key2 — a different secret from the one that
    /// signs outgoing calls, so a leaked create key cannot forge a payment.
    /// </summary>
    public static bool ZaloCallbackValid(string key2, string data, string mac) =>
        Same(Sha256Hex(key2, data), mac);

    /// <summary>ZaloPay: 1 taken, 2 refused, 3 still deciding.</summary>
    public static bool ZaloPaid(int returnCode) => returnCode == 1;

    public static bool ZaloPending(int returnCode) => returnCode == 3;

    /// <summary>Milliseconds since the epoch, which is what <c>app_time</c> is in.</summary>
    public static long ZaloTime(DateTime nowUtc) => new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();

    /* -------------------------------------------------------------------- misc */

    /// <summary>
    /// A gateway that says a payment succeeded for a different amount than the
    /// one the booking is for is not a success — it is either a tampered return
    /// URL or a bug on their side, and confirming a stay on it would be the fault
    /// docs/07 §7 calls the worst one in the module.
    /// </summary>
    public static bool AmountMatches(decimal expected, decimal reported) =>
        Math.Abs(expected - reported) < 1m;


    /* -------------------------------------------------------------- OnePay */

    /// <summary>
    /// OnePay signs a sorted query, like VNPay, but with three differences that
    /// each fail silently if got wrong: the digest is SHA-256 rather than 512,
    /// the key is the secret read as <em>hexadecimal bytes</em> rather than as
    /// text, and only fields named <c>vpc_</c> or <c>user_</c> are signed —
    /// which is why <c>AgainLink</c>, a field OnePay itself sends, must be left
    /// out of the rebuild.
    ///
    /// Established against a real authorised transaction on their public test
    /// merchant rather than from the documentation: the captured response and
    /// its signature are the fixture in <c>PspOnePayTests</c>.
    /// </summary>
    public static string OnePaySign(IReadOnlyDictionary<string, string> fields, string secret) =>
        HmacHex(HMACSHA256.HashData(HexKey(secret),
            Encoding.UTF8.GetBytes(OnePayRaw(fields)))).ToUpperInvariant();

    /// <summary>
    /// What gets signed: sorted, joined <c>k=v</c>, and <b>not</b> URL-encoded.
    /// The browser query is encoded separately; encoding it here as well would
    /// sign a different string from the one OnePay rebuilds.
    /// </summary>
    private static string OnePayRaw(IReadOnlyDictionary<string, string> fields)
    {
        var sb = new StringBuilder();

        foreach (var (k, v) in Sorted(fields).Where(p => OnePaySigned(p.Key)))
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(k).Append('=').Append(v);
        }

        return sb.ToString();
    }

    private static bool OnePaySigned(string key) =>
        (key.StartsWith("vpc_", StringComparison.Ordinal)
         || key.StartsWith("user_", StringComparison.Ordinal))
        && key != "vpc_SecureHash"
        && key != "vpc_SecureHashType";

    /// <summary>
    /// The secret is 32 hex characters standing for 16 bytes, and it is those
    /// bytes that key the HMAC. Feeding the characters themselves produces a
    /// signature OnePay rejects on every single request — with no message
    /// saying why.
    /// </summary>
    private static byte[] HexKey(string secret)
    {
        var raw = (secret ?? "").Trim();

        try
        {
            return Convert.FromHexString(raw);
        }
        catch (FormatException)
        {
            // A secret that is not hexadecimal is a misconfiguration, not a
            // reason to throw inside a payment: sign with the text and let the
            // gateway refuse it loudly.
            return Encoding.UTF8.GetBytes(raw);
        }
    }

    /// <summary>Checks a return from OnePay. Same rule as signing, in reverse.</summary>
    public static bool OnePayVerify(IReadOnlyDictionary<string, string> query, string secret)
    {
        var given = query.GetValueOrDefault("vpc_SecureHash");
        if (string.IsNullOrWhiteSpace(given)) return false;

        return Same(OnePaySign(query, secret), given);
    }

    /// <summary>
    /// Which of OnePay's two gateways an order belongs on. They share a
    /// merchant, a secret and a signature; only the address differs — and it is
    /// the address that decides whether the guest meets a Visa form or the list
    /// of Vietnamese banks. Sending a napas order to the international URL puts
    /// an ATM cardholder in front of a form their card cannot fill, and nothing
    /// about the request looks wrong.
    /// </summary>
    public static bool OnePayIsDomestic(string? method) => method == "napas";

    /// <summary>OnePay counts in đồng × 100, exactly as VNPay does.</summary>
    public static long OnePayAmount(decimal dong) => VnPayAmount(dong);

    /// <summary>
    /// Their response code is a single character and <c>0</c> is the only one
    /// that means the money moved. Note it is <c>"0"</c>, not <c>"00"</c> —
    /// VNPay's spelling, and a plausible enough mistake to be worth a test.
    /// </summary>
    public static bool OnePayPaid(string? code) => code == "0";

    /// <summary>The guest pressed cancel on OnePay's page rather than being refused.</summary>
    public static bool OnePayCancelled(string? code) => code is "99" or "E";

    /// <summary>
    /// docs/07 §8 — OnePay's code turned into one of the reasons the guest-facing
    /// message table knows about. Their list is MiGS's, so it is not VNPay's.
    /// </summary>
    public static DeclineReason OnePayDecline(string? code) => code switch
    {
        "0" => DeclineReason.Unknown,             // not a decline at all
        "1" => DeclineReason.BankRefused,         // unspecified failure at the issuer
        "2" => DeclineReason.BankRefused,         // bank declined
        "3" => DeclineReason.GatewayError,        // no reply from the issuer
        "4" => DeclineReason.ExpiredCard,
        "5" => DeclineReason.InsufficientFunds,
        "6" => DeclineReason.GatewayError,
        "7" => DeclineReason.GatewayError,
        "8" => DeclineReason.IncorrectDetails,    // unknown card
        "9" => DeclineReason.IncorrectDetails,
        "A" => DeclineReason.IncorrectDetails,    // 3-D Secure failed
        "B" => DeclineReason.SuspectedFraud,
        "C" => DeclineReason.SuspectedFraud,
        "D" => DeclineReason.SuspectedFraud,
        "F" => DeclineReason.IncorrectDetails,    // 3-D Secure authentication failed
        "I" => DeclineReason.SuspectedFraud,
        "L" => DeclineReason.GatewayError,        // shopping transaction locked
        "N" => DeclineReason.LimitExceeded,
        "P" => DeclineReason.GatewayError,        // pending
        "R" => DeclineReason.LimitExceeded,       // retry limit exceeded
        "T" => DeclineReason.IncorrectDetails,    // address verification failed
        "U" => DeclineReason.IncorrectDetails,    // CSC failed
        _ => DeclineReason.Unknown
    };

    /// <summary>
    /// docs/07 §15.5 — whether VNPay has stopped holding a card.
    ///
    /// Two answers both mean the card is gone from their side and neither is an
    /// error worth showing a guest: <c>00</c> removed it, <c>11</c> says there
    /// was nothing there to remove. The second happens whenever a removal is
    /// retried, and treating it as a failure would make the retry look worse
    /// than the original problem.
    /// </summary>
    public static bool VnPayTokenForgotten(string? responseCode) =>
        responseCode is "00" or "11";

    /// <summary>
    /// OnePay's masked number arrives as <c>400555xxxxxx0001</c> — the last four
    /// are simply there, on an ordinary payment, with no token API involved.
    /// That is a real difference from VNPay, where docs/07 §14.2 leaves the
    /// platform knowing nothing about the card unless the guest saves it.
    /// </summary>
    public static string? OnePayLast4(string? maskedCardNumber) => Last4Of(maskedCardNumber);

    /// <summary>
    /// docs/07 §10 — what OnePay's refund reply means. Their <c>vpc_TxnResponseCode</c>
    /// carries the same alphabet as a payment, so 0 is done and anything they
    /// actually answered is a refusal; only a lost call is unknown.
    /// </summary>
    public static RefundOutcome OnePayRefundOutcome(string? code) => code switch
    {
        "0" => RefundOutcome.Accepted,
        null or "" => RefundOutcome.Unknown,
        _ => RefundOutcome.Refused
    };

    private static IEnumerable<KeyValuePair<string, string>> Sorted(IReadOnlyDictionary<string, string> fields) =>
        fields.Where(p => !string.IsNullOrEmpty(p.Value))
              .OrderBy(p => p.Key, StringComparer.Ordinal);

    private static string Sha256Hex(string key, string raw) =>
        HmacHex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(raw)));

    private static string HmacHex(byte[] hash)
    {
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>Signatures are compared without regard to case and without early exit.</summary>
    private static bool Same(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
}

/// <summary>Where one trip out to a gateway got to.</summary>
public enum PaymentSessionStatus
{
    /// <summary>Created, guest sent there, nothing heard back.</summary>
    Pending = 0,
    Paid = 1,
    Failed = 2,
    /// <summary>The guest pressed cancel on the gateway's own page.</summary>
    Cancelled = 3,
    /// <summary>Nobody came back and the gateway no longer knows the order.</summary>
    Expired = 4
}

/// <summary>
/// docs/07 §13 — one visit to a licensed gateway, from the moment the guest is
/// sent there to the moment the platform knows what happened.
///
/// It is a separate record from <see cref="PaymentAttempt"/> on purpose. The
/// attempt is the platform's promise not to charge twice; this is the gateway's
/// side of the same event, and docs/07 §7's daily reconciliation only means
/// something while the two are kept apart.
/// </summary>
public class PaymentSession
{
    public long Id { get; set; }

    /// <summary>Unique. What the gateway knows the payment by.</summary>
    public string OrderRef { get; set; } = "";

    /// <summary>The <see cref="PaymentAttempt.Key"/> this visit belongs to.</summary>
    public string AttemptKey { get; set; } = "";

    /// <summary>
    /// The stay being paid for, when that is what this is.
    ///
    /// Nullable since a gift card became payable: a card is bought by somebody
    /// who is not travelling, so there is no booking to hang the visit on. Same
    /// shape <c>ledger_entries</c> already uses for the experience and service
    /// lines — a column per subject rather than one column asked to mean several
    /// things, which is the arrangement that made passing the wrong id throw.
    /// Exactly one of the two is set.
    /// </summary>
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>docs/01 TC-08 — the gift card being bought, when that is the subject.</summary>
    public int? GiftCardId { get; set; }
    public GiftCard? GiftCard { get; set; }

    /// <summary>docs/09 — a ticket to an experience, when that is the subject.</summary>
    public int? ExperienceBookingId { get; set; }
    public ExperienceBooking? ExperienceBooking { get; set; }

    /// <summary>docs/09 — a booked service, when that is the subject.</summary>
    public int? ServiceBookingId { get; set; }
    public ServiceBooking? ServiceBooking { get; set; }

    /// <summary>
    /// docs/01 ĐP-07 — one person's share of a split bill. <see cref="BookingId"/>
    /// is set as well, because a refund of the stay has to find these visits too.
    /// </summary>
    public int? BillShareId { get; set; }
    public BillShare? BillShare { get; set; }

    /// <summary>
    /// docs/01 ĐP-06 — the second half of a part-paid stay. Set together with
    /// <see cref="BookingId"/>; the booking is already confirmed, so settling this
    /// visit collects the balance instead of confirming anything.
    /// </summary>
    public bool IsBalance { get; set; }

    /// <summary>vnpay / momo / zalopay.</summary>
    public string Provider { get; set; } = "";

    /// <summary>The method the guest picked: card, napas, momo, zalopay.</summary>
    public string Method { get; set; } = "card";

    public decimal Amount { get; set; }

    /// <summary>True when this was a deposit rather than the whole stay.</summary>
    public bool Partial { get; set; }

    public PaymentSessionStatus Status { get; set; } = PaymentSessionStatus.Pending;

    /// <summary>Where the guest was sent.</summary>
    public string? PayUrl { get; set; }

    /// <summary>The gateway's own transaction id, for a refund or a dispute later.</summary>
    public string? ProviderTxnId { get; set; }

    /// <summary>The gateway's raw code, kept for reconciliation. Never shown to a guest.</summary>
    public string? ResponseCode { get; set; }

    /// <summary>Which of the three told us: the browser coming back, the IPN, or our own query.</summary>
    public string? SettledBy { get; set; }

    /// <summary>
    /// When the gateway says the money moved, in the gateway's own format
    /// (VNPay: yyyyMMddHHmmss, GMT+7).
    ///
    /// Kept because docs/07 §10's refund asks for the original transaction's date
    /// back, and the platform's own clock is not the same thing — a refund sent
    /// with our timestamp is refused with a code that says nothing about why.
    /// </summary>
    public string? ProviderPaidAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /* ---------------------------------------------- docs/07 §10, the way back */

    /// <summary>How much of this payment the gateway has been asked to send back.</summary>
    public decimal RefundedAmount { get; set; }

    /// <summary>The gateway's own transaction number for the refund, which is not the payment's.</summary>
    public string? RefundTxnId { get; set; }

    /// <summary>Their raw answer code, kept for docs/07 §7's reconciliation. Never shown to a guest.</summary>
    public string? RefundCode { get; set; }

    public DateTime? RefundedAt { get; set; }

    /// <summary>How long a guest has on the gateway's page before the dates go back on sale.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
}
