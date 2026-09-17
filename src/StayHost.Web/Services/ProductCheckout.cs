using StayHost.Domain;
using StayHost.Web.Services.Gateways;

namespace StayHost.Web.Services;

/// <summary>
/// docs/07 §2 for the two product lines that are not a stay (docs/09): which
/// road a ticket or a service is paid by.
///
/// Both used to hand every method except VietQR to the stand-in, so on a site
/// whose card and wallet rows all went to licensed gateways a ticket was
/// confirmed with nothing paid, and "pay at the property" — a stay-only option
/// — booked a ticket that then waited for a bank transfer nobody would make.
/// </summary>
public static class ProductCheckout
{
    public enum Road { Gateway, StandIn, Transfer }

    public static string Normalise(string? method) => (method ?? "card").Trim().ToLowerInvariant();

    /// <summary>The road for <paramref name="method"/>, or an error to show the guest.</summary>
    public static (Road Road, string? Error) Choose(PspRouter router, BankTransferSettings bank, string? method)
    {
        var key = Normalise(method);

        if (key == "vietqr")
            return bank.Enabled
                ? (Road.Transfer, null)
                : (Road.Transfer, Payments.Message(DeclineReason.MethodUnavailable));

        if (router.IsLive(key)) return (Road.Gateway, null);
        if (router.StandInMay(key)) return (Road.StandIn, null);

        return (Road.StandIn, Payments.Message(DeclineReason.MethodUnavailable));
    }
}
