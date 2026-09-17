namespace StayHost.Domain;

/// <summary>
/// What a search engine is allowed to see.
///
/// The interesting half of this is not what to index — it is what must never be
/// indexed. Several routes carry a secret in the address itself: a bill-split
/// invitation, a shared wishlist, an appeal form, a bank-transfer reference. A
/// crawler that reaches one of those has not found a page, it has found somebody
/// else's link, and once it is in an index it is public forever. So the private
/// list is the one that gets a test, and the sitemap is checked against it rather
/// than trusted.
///
/// Second rule, less obvious and more damaging when broken: a single-page app
/// serves the same HTML for every address. Putting one fixed <c>canonical</c> in
/// that HTML tells Google every listing is a duplicate of the home page, and the
/// listings drop out of the index entirely. Canonical has to be per-address or
/// absent — never a constant.
/// </summary>
public static class Seo
{
    /// <summary>
    /// Address prefixes a crawler must stay out of. Grouped by why, because the
    /// reasons are not the same and a later reader will want to know which ones
    /// are merely useless to index and which ones leak something.
    /// </summary>
    public static readonly string[] Disallow =
    [
        // Carries a secret in the address. Indexing one of these publishes it.
        "/split/",
        "/wishlist/",
        "/appeal",
        "/chuyen-khoan/",

        // Somebody's own account. Nothing here belongs in a public index, and
        // /users/ in particular is a real person's profile.
        "/trips",
        "/danh-gia",
        // docs/07 §2.5 — a form whose answer is somebody's booking.
        "/dat-cho",
        "/hosting",
        "/messages",
        "/wishlists",
        "/wallet",
        "/resolutions",
        "/account",
        // docs/02 F1 — the settings hub. Somebody's own preferences and devices.
        "/cai-dat",
        "/neighbors",
        "/friends",
        "/trip-plans",
        "/users/",
        "/admin",
        "/shield",

        // Mid-purchase. A crawler walking a checkout is at best noise, and these
        // pages are meaningless without the session that opened them.
        "/*/thanh-toan",
        "/thanh-toan/",
        "/experiences/bookings",
        "/services/bookings",

        // Data, not pages.
        "/api/",
    ];

    /// <summary>
    /// Narrower rules that win over the blanket ones above. Google applies the
    /// most specific match, so <c>/shield</c> can be closed while the terms page
    /// underneath it stays open — and that page is worth having indexed, since
    /// docs/06 §11 makes claims about it the platform has to stand behind.
    /// </summary>
    public static readonly string[] Allow =
    [
        "/shield/terms",
    ];

    /// <summary>
    /// True when <paramref name="path"/> falls under a Disallow rule and no
    /// narrower Allow rule rescues it. Used to keep the sitemap honest: anything
    /// this returns true for must never appear in it.
    /// </summary>
    public static bool IsPrivate(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return false;

        var blocked = Disallow.Where(rule => Matches(rule, p)).ToList();
        if (blocked.Count == 0) return false;

        // Longest rule wins, the way Google resolves a conflict.
        var longestBlock = blocked.Max(r => r.Length);
        var longestAllow = Allow.Where(rule => Matches(rule, p))
                                .Select(r => r.Length)
                                .DefaultIfEmpty(0)
                                .Max();

        return longestAllow < longestBlock;
    }

    /// <summary>
    /// robots.txt prefix matching, with the one wildcard Google supports. A rule
    /// matches when the path starts with it; <c>*</c> stands for any run of
    /// characters, which is how "/*/thanh-toan" covers a checkout under any slug.
    /// </summary>
    private static bool Matches(string rule, string path)
    {
        if (!rule.Contains('*')) return path.StartsWith(rule, StringComparison.OrdinalIgnoreCase);

        var parts = rule.Split('*');
        var at = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;

            if (i == 0)
            {
                if (!path.StartsWith(parts[i], StringComparison.OrdinalIgnoreCase)) return false;
                at = parts[i].Length;
                continue;
            }

            var found = path.IndexOf(parts[i], at, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            at = found + parts[i].Length;
        }
        return true;
    }

    /// <summary>
    /// How many places a city landing page shows before it needs a second page.
    ///
    /// The number matters to more than layout. A city page is where a crawler
    /// finds the individual places, so anything past this cut has no link
    /// pointing at it and is reachable only through the sitemap — which works,
    /// but is the weaker of the two paths. Today the busiest city holds seven,
    /// so nothing is cut; the paging exists so that stops being true quietly.
    /// </summary>
    public const int CityPageSize = 12;

    /// <summary>
    /// Pages needed for <paramref name="total"/> items. Always at least one, so
    /// an empty city still has a page 1 rather than a page range of nothing.
    /// </summary>
    public static int TotalPages(int total, int pageSize = CityPageSize)
    {
        if (pageSize <= 0) return 1;
        return Math.Max(1, (total + pageSize - 1) / pageSize);
    }

    /// <summary>
    /// <paramref name="page"/> clamped into the range that actually exists. A
    /// crawler will ask for ?trang=99 sooner or later — usually because it found
    /// the number in an old link — and answering with an empty page teaches it
    /// the site is full of thin content.
    /// </summary>
    public static int ClampPage(int page, int total, int pageSize = CityPageSize) =>
        Math.Clamp(page, 1, TotalPages(total, pageSize));

    /// <summary>How many items to skip to reach <paramref name="page"/>.</summary>
    public static int Skip(int page, int total, int pageSize = CityPageSize) =>
        (ClampPage(page, total, pageSize) - 1) * pageSize;

    /// <summary>
    /// The address of one page in a city series. Page 1 is the bare address, not
    /// "?trang=1" — two addresses for the same page is the duplicate the whole
    /// canonical arrangement exists to prevent.
    /// </summary>
    public static string CityPagePath(string slug, int page) =>
        page <= 1 ? $"/thanh-pho/{slug}" : $"/thanh-pho/{slug}?trang={page}";

    /// <summary>
    /// The body of robots.txt. <paramref name="sitemapUrl"/> is left out entirely
    /// when there is none rather than written as a relative address — the Sitemap
    /// directive is defined as absolute, and a relative one is silently ignored,
    /// which looks exactly like having no sitemap at all.
    /// </summary>
    public static string RobotsTxt(string? sitemapUrl)
    {
        var lines = new List<string> { "User-agent: *" };
        lines.AddRange(Allow.Select(a => $"Allow: {a}"));
        lines.AddRange(Disallow.Select(d => $"Disallow: {d}"));

        if (!string.IsNullOrWhiteSpace(sitemapUrl))
        {
            lines.Add("");
            lines.Add($"Sitemap: {sitemapUrl}");
        }

        return string.Join("\n", lines) + "\n";
    }
}

/// <summary>What kind of page an address names, once the SPA has finished with it.</summary>
public enum PageKind
{
    /// <summary>No route in the app answers this address. It is a 404.</summary>
    Unknown,

    /// <summary>
    /// A route that exists whether or not anything is behind it — somebody's own
    /// trips, an admin screen, a checkout. These are noindex anyway, and asking
    /// the database whether the row exists would only move the 404 to a place
    /// that cannot render it, so they are always answered 200.
    /// </summary>
    App,

    /// <summary>/rooms/{slug} — real only if that listing is published.</summary>
    Listing,

    /// <summary>/thanh-pho/{slug} — real only if that city has something to show.</summary>
    City,

    /// <summary>/experiences/{slug}</summary>
    Experience,

    /// <summary>/services/{slug}</summary>
    Service,

    /// <summary>/help/{slug}</summary>
    HelpArticle,
}

/// <summary>An address, resolved to the kind of page it names and the slug it carries.</summary>
public readonly record struct PageRoute(PageKind Kind, string Slug)
{
    /// <summary>True when the answer depends on a row existing in the database.</summary>
    public bool NeedsLookup => Kind is PageKind.Listing or PageKind.City
        or PageKind.Experience or PageKind.Service or PageKind.HelpArticle;
}

public static class SpaRoutes
{
    /// <summary>
    /// Addresses the app answers with no parameter of their own.
    ///
    /// This list is a copy of the route table in App.jsx, and a copy is a thing
    /// that goes stale — but the alternative is worse. Without it every address
    /// on the site answers 200, which is how a typo, an old link and a crawler
    /// guessing all end up indexed as blank pages carrying the home page title.
    /// A route added to the app and forgotten here shows up as a 404 on a page
    /// that works, which is loud; the reverse, an address that quietly returns
    /// 200 forever, is what this exists to stop.
    /// </summary>
    public static readonly string[] Fixed =
    [
        "/",
        "/wishlists", "/trips", "/danh-gia", "/dat-cho", "/host", "/hosting", "/messages", "/resolutions",
        "/help", "/experiences", "/experiences/bookings", "/services",
        "/services/bookings", "/wallet", "/shield", "/shield/terms",
        "/thanh-toan/ket-qua", "/account/sanctions", "/appeal", "/neighbors",
        "/friends", "/trip-plans", "/admin",

        // Payout and review emails linked here before the host page read its tab
        // from the query; those emails are still in inboxes.
        "/hosting/earnings", "/hosting/reviews",

        // docs/02 F1 — the settings hub and its nine groups, each a literal
        // entry on purpose. The tempting shortcut — "cai-dat" in Resolve's
        // two-segment fallback arm — would answer 200 for /cai-dat/anything,
        // which is the exact soft-404 hole MapFallbackToFile used to leave. As
        // literals, the nine real groups answer 200 and an invented tenth still
        // falls through to Unknown and an honest 404.
        "/cai-dat",
        "/cai-dat/ho-so", "/cai-dat/bao-mat", "/cai-dat/thanh-toan",
        "/cai-dat/nhan-tien", "/cai-dat/thong-bao", "/cai-dat/quyen-rieng-tu",
        "/cai-dat/tuy-chinh", "/cai-dat/cong-tac", "/cai-dat/gioi-thieu",
    ];

    /// <summary>
    /// What page <paramref name="path"/> names.
    ///
    /// Order matters in one place: "/experiences/bookings" and
    /// "/services/bookings" are fixed pages that sit exactly where a slug would,
    /// so the fixed list is consulted first. Reversed, the app would go looking
    /// for an experience whose slug is "bookings", find none, and answer 404 on
    /// a page that works.
    /// </summary>
    public static PageRoute Resolve(string? rawPath)
    {
        var path = (rawPath ?? "").Split('?')[0].Split('#')[0].Trim();
        if (path.Length == 0) return new(PageKind.Unknown, "");

        // "/rooms/x/" and "/rooms/x" are the same page.
        if (path.Length > 1) path = path.TrimEnd('/');
        if (path.Length == 0) path = "/";

        if (Fixed.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            return new(PageKind.App, "");

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return new(PageKind.App, "");

        var slug = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";

        // Two segments: /section/slug
        if (parts.Length == 2)
        {
            return parts[0].ToLowerInvariant() switch
            {
                "rooms" => new(PageKind.Listing, slug),
                "thanh-pho" => new(PageKind.City, slug),
                "experiences" => new(PageKind.Experience, slug),
                "services" => new(PageKind.Service, slug),
                "help" => new(PageKind.HelpArticle, slug),

                // Behind a login or a token, and noindex either way.
                "wishlist" or "trips" or "messages" or "shield" or "split"
                    or "chuyen-khoan" or "users" => new(PageKind.App, slug),

                _ => new(PageKind.Unknown, slug),
            };
        }

        // Three segments: only the two checkout addresses.
        if (parts.Length == 3
            && parts[2].Equals("thanh-toan", StringComparison.OrdinalIgnoreCase)
            && (parts[0].Equals("experiences", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("services", StringComparison.OrdinalIgnoreCase)))
            return new(PageKind.App, slug);

        return new(PageKind.Unknown, slug);
    }

    /// <summary>
    /// True for an address that asks for a file rather than a page — a stale
    /// bundle name, a missing image. These must never be answered with the app
    /// shell: a script tag that receives HTML fails in a way that reads as a
    /// syntax error somewhere in the app rather than as a missing file.
    /// </summary>
    public static bool LooksLikeAsset(string? rawPath)
    {
        var path = (rawPath ?? "").Split('?')[0];
        var last = path.Split('/').LastOrDefault() ?? "";
        var dot = last.LastIndexOf('.');
        if (dot <= 0 || dot == last.Length - 1) return false;

        var ext = last[(dot + 1)..];
        return ext.Length is >= 2 and <= 5 && ext.All(char.IsLetterOrDigit);
    }
}
