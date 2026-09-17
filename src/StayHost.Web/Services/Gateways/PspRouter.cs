using Microsoft.Extensions.Options;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// Which gateway, if any, serves the method the guest picked.
///
/// A method with no gateway behind it is not an error — it is the stand-in
/// gateway keeping the demo checkout working. The one thing that must never
/// happen is the opposite: a method wired to a real gateway being charged by the
/// stand-in, which would confirm a stay nobody paid for. That is why the pay
/// endpoint asks this router before it charges anything.
/// </summary>
public class PspRouter(
    IOptions<PspSettings> options, IEnumerable<IPspProvider> providers, IWebHostEnvironment env)
{
    private readonly PspSettings _psp = options.Value;

    /// <summary>The methods the stand-in knows how to play. Nothing else is money.</summary>
    private static readonly HashSet<string> StandInMethods = ["card", "napas", "momo", "zalopay"];

    /// <summary>
    /// Whether the stand-in may take money for this method, here and now.
    ///
    /// Never for a method a real gateway owns — confirming that stay in place
    /// would skip the gateway entirely — and never for a name that is not one of
    /// the four checkout rows. /pay used to accept any string at all, so
    /// "applepay" or "xyz" with card 4242 confirmed a stay nobody paid for, on a
    /// site where every real row went out to VNPay, OnePay, MoMo or ZaloPay.
    /// </summary>
    public bool StandInMay(string? method)
    {
        var key = (method ?? "").Trim().ToLowerInvariant();
        if (!StandInMethods.Contains(key) || IsLive(key)) return false;
        return StandInEnabled;
    }

    /// <summary>Whether this deployment runs the stand-in at all (never, by default, in production).</summary>
    public bool StandInEnabled => _psp.AllowStandIn ?? !env.IsProduction();

    /// <summary>A method a guest can actually pay with right now, by either road.</summary>
    public bool CanTake(string? method) => IsLive(method) || StandInMay(method);

    /// <summary>The gateway for a method, or null when the stand-in still owns it.</summary>
    public IPspProvider? For(string? method)
    {
        var key = (method ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) return null;
        if (!_psp.Methods.TryGetValue(key, out var provider)) return null;

        return providers.FirstOrDefault(
            p => p.Key.Equals(provider, StringComparison.OrdinalIgnoreCase) && p.IsConfigured);
    }

    public IPspProvider? ByKey(string? providerKey) =>
        providers.FirstOrDefault(
            p => p.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase) && p.IsConfigured);

    /// <summary>True when this method leaves the site instead of being charged in place.</summary>
    public bool IsLive(string? method) => For(method) is not null;

    /// <summary>
    /// Whose page the guest is about to land on — "vnpay", "onepay", "momo",
    /// "zalopay" — or null while the stand-in still owns the method.
    ///
    /// The checkout has to say this out loud before it redirects, and it cannot
    /// work it out for itself: which gateway serves a method is configuration,
    /// and a client that guesses names the wrong company the day it changes.
    /// </summary>
    public string? ProviderOf(string? method) => For(method)?.Key;

    /// <summary>
    /// docs/07 §4 — whether the gateway behind this method can keep a card.
    ///
    /// Only VNPay does, and only when their token feature is switched on for the
    /// merchant. A checkout that offered "lưu thẻ này" and then met a refusal
    /// would be worse than one that never offered it.
    /// </summary>
    public bool KeepsCards(string? method) => For(method) is VnPayProvider { TokensEnabled: true };

    /// <summary>
    /// Whether the public URL is one a gateway could reach. On a laptop it is
    /// not, so the IPN never arrives and the self-check of docs/07 §5 is the only
    /// thing that settles a payment. Worth saying out loud in the log rather than
    /// leaving as a mystery.
    /// </summary>
    public bool PublicUrlIsReachable =>
        _psp.PublicUrl.Length > 0
        && !_psp.PublicUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
        && !_psp.PublicUrl.Contains("127.0.0.1", StringComparison.Ordinal);

    public IReadOnlyList<string> LiveMethods =>
        _psp.Methods.Keys.Where(IsLive).ToList();
}
