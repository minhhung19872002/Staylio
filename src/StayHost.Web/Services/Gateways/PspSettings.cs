namespace StayHost.Web.Services.Gateways;

/// <summary>
/// docs/07 §13 phương án A — which licensed gateway takes the money, and with
/// whose keys.
///
/// Every one of these starts empty, and an empty one means the gateway is not
/// wired: the stand-in <see cref="StayHost.Web.Services.PaymentGateway"/> keeps
/// the checkout working exactly as it did. That is the same rule the bank
/// transfer follows — a payment method with no credentials behind it is not
/// offered rather than offered and broken.
///
/// Secrets belong in environment variables (<c>Psp__Vnpay__HashSecret</c>), not
/// in appsettings.json. docs/07 §14.4.
/// </summary>
public class PspSettings
{
    /// <summary>
    /// Where the gateway sends the guest back to, and where it posts its IPN.
    /// It has to be an address the gateway can reach, so on a laptop it is the
    /// local one for the return trip and the IPN simply never arrives — which is
    /// exactly why docs/07 §5 insists the platform ask the gateway itself.
    /// </summary>
    public string PublicUrl { get; set; } = "";

    /// <summary>
    /// Whether the stand-in <see cref="StayHost.Web.Services.PaymentGateway"/> may
    /// take a payment for a method no gateway serves.
    ///
    /// Null means "not in production": a demo build keeps its checkout, while a
    /// live site whose MoMo keys went missing refuses MoMo instead of confirming
    /// every MoMo booking for free — which is what the stand-in does, since it
    /// says yes to any card but the declining test one.
    /// </summary>
    public bool? AllowStandIn { get; set; }

    public VnPayOptions Vnpay { get; set; } = new();
    public OnePayOptions Onepay { get; set; } = new();
    public MoMoOptions Momo { get; set; } = new();
    public ZaloPayOptions Zalopay { get; set; } = new();

    /// <summary>
    /// Which gateway serves which of the methods on the checkout. Left empty a
    /// method falls back to the stand-in, so the four rows of docs/07 §2.1 can be
    /// switched on one at a time.
    /// </summary>
    public Dictionary<string, string> Methods { get; set; } = new()
    {
        ["card"] = Domain.Psp.VnPay,
        ["napas"] = Domain.Psp.VnPay,
        ["momo"] = Domain.Psp.MoMo,
        ["zalopay"] = Domain.Psp.ZaloPay
    };

    public class VnPayOptions
    {
        public string TmnCode { get; set; } = "";
        public string HashSecret { get; set; } = "";
        public string PayUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        public string ApiUrl { get; set; } = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";

        /// <summary>
        /// docs/07 §4 — the token API, which is a different path and a different
        /// parameter spelling from the payment one. Paying and creating a token
        /// in one go; and paying with a token the guest saved earlier.
        /// </summary>
        public string CreateTokenUrl { get; set; } = "https://sandbox.vnpayment.vn/token_ui/pay-create-token.html";

        public string TokenPayUrl { get; set; } = "https://sandbox.vnpayment.vn/token_ui/payment-token.html";

        /// <summary>
        /// docs/07 §15.5 — telling VNPay to forget a card the guest deleted here.
        ///
        /// Unlike the other two token URLs this one is not a page the guest is
        /// sent to: it answers a query string server-to-server, so it is called
        /// rather than redirected to.
        /// </summary>
        public string RemoveTokenUrl { get; set; } = "https://sandbox.vnpayment.vn/token_ui/remove-token.html";

        /// <summary>
        /// Whether to offer keeping a card at all. Off by default: token creation
        /// is a feature VNPay enables per merchant, and offering a tick box that
        /// their gateway then refuses is worse than not offering it.
        /// </summary>
        public bool Tokens { get; set; }

        public bool IsConfigured => TmnCode.Length > 0 && HashSecret.Length > 0;
    }

    /// <summary>
    /// OnePay, for the international card row. Their gateway is the Vietnamese
    /// deployment of MiGS/MPGS, so the vocabulary is <c>vpc_</c> rather than
    /// <c>vnp_</c> and the merchant is identified by a code plus an access code
    /// rather than one terminal id.
    ///
    /// The refund and query API is a different endpoint again, and it wants a
    /// user name and password that are not the same thing as the hash secret —
    /// OnePay issues them separately, so both are optional here: without them
    /// the gateway still takes money, and only docs/07 §5's self-check and §10's
    /// refunds go quiet. They say so in the log rather than pretending.
    /// </summary>
    public class OnePayOptions
    {
        public string Merchant { get; set; } = "";
        public string AccessCode { get; set; } = "";
        public string HashSecret { get; set; } = "";
        /// <summary>Where an international card goes.</summary>
        public string PayUrl { get; set; } = "https://mtf.onepay.vn/vpcpay/vpcpay.op";

        /// <summary>
        /// Where a domestic ATM card or bank account goes. Same merchant, same
        /// secret, same signature — a different address is the whole difference,
        /// and it is what decides whether the guest is shown a Visa form or the
        /// bank list. Sending a napas order to the international URL would put
        /// an ATM cardholder in front of a form their card cannot fill.
        /// </summary>
        public string DomesticPayUrl { get; set; } = "https://mtf.onepay.vn/onecomm-pay/vpc.op";
        /// <summary>
        /// The operator API. Note <c>vpcpay/</c> and not <c>onecomm-pay/</c>:
        /// both answer <c>queryDR</c>, but the domestic one reports
        /// <c>vpc_TransactionNo=0</c> for an international payment, and that
        /// number is the only handle docs/07 §7's reconciliation has.
        /// </summary>
        public string ApiUrl { get; set; } = "https://mtf.onepay.vn/vpcpay/Vpcdps.op";

        /// <summary>
        /// The same query API on the domestic side. Both answer for either kind
        /// of transaction, but each is fuller about its own, so a query that
        /// draws a blank on one is asked again here rather than reported as
        /// "never heard of it" — which docs/07 §5 would read as unpaid.
        /// </summary>
        public string DomesticApiUrl { get; set; } = "https://mtf.onepay.vn/onecomm-pay/Vpcdps.op";

        /// <summary>The operator account for the query/refund API, when OnePay has issued one.</summary>
        public string ApiUser { get; set; } = "";
        public string ApiPassword { get; set; } = "";

        public bool IsConfigured => Merchant.Length > 0 && AccessCode.Length > 0 && HashSecret.Length > 0;

        /// <summary>Whether §5's self-check and §10's refunds can be attempted at all.</summary>
        public bool HasApiUser => ApiUser.Length > 0 && ApiPassword.Length > 0;
    }

    public class MoMoOptions
    {
        public string PartnerCode { get; set; } = "";
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public string Endpoint { get; set; } = "https://test-payment.momo.vn/v2/gateway/api";
        public bool IsConfigured => PartnerCode.Length > 0 && AccessKey.Length > 0 && SecretKey.Length > 0;
    }

    public class ZaloPayOptions
    {
        public string AppId { get; set; } = "";
        public string Key1 { get; set; } = "";
        public string Key2 { get; set; } = "";
        public string Endpoint { get; set; } = "https://sb-openapi.zalopay.vn/v2";
        public bool IsConfigured => AppId.Length > 0 && Key1.Length > 0 && Key2.Length > 0;
    }
}
