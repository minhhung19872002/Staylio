using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;
using StayHost.Web.Services.Gateways;

var builder = WebApplication.CreateBuilder(args);

// docs/03 §1 fixes the fee rates; the admin console (QT-06) needs to change them
// without a deploy, so they are bound from configuration with the spec as default.
var pricing = builder.Configuration.GetSection("Pricing").Get<PricingSettings>();
if (pricing is not null) PricingSettings.Current = pricing;

// docs/01 TC-07 — how long promotional balance lasts. docs/07 §16 settled the
// numbers on 11/08/2026 (twelve months for what the platform gave away, no expiry
// on a gift card) and appsettings.json carries them; an unset value still means
// that kind never lapses.
var credits = builder.Configuration.GetSection("Credits").Get<CreditSettings>();
if (credits is not null) CreditSettings.Current = credits;

// docs/01 AT-01 — pre-publish review of new listings. Off by default (a host
// publishes and the place is live at once), which is how the platform shipped;
// the queue and search gate are built and wait only on this switch.
var moderation = builder.Configuration.GetSection("Moderation").Get<ModerationSettings>();
if (moderation is not null) ModerationSettings.Current = moderation;

// docs/01 TĐ-03, TN-06 — machine translation. Both compose files run a
// LibreTranslate container and set Translation__Provider, so a deployment has this
// on without buying anything; a bare `dotnet run` has no provider named and the
// "Dịch" button never shows, like the social-login buttons. A paid provider is
// still an option — its key arrives as Translation__ApiKey, never appsettings.json.
var translation = builder.Configuration.GetSection("Translation").Get<TranslationSettings>() ?? new();
TranslationSettings.Current = translation;
var translationKey = builder.Configuration["Translation:ApiKey"];
builder.Services.AddHttpClient("translation");
if (translation.IsConfigured)
{
    if (string.Equals(translation.Provider, "google", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(translationKey))
        builder.Services.AddScoped<ITranslator>(sp => new GoogleTranslator(
            sp.GetRequiredService<IHttpClientFactory>(), translationKey!,
            sp.GetRequiredService<ILogger<GoogleTranslator>>()));
    else if (string.Equals(translation.Provider, "libretranslate", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(translation.Url))
        builder.Services.AddScoped<ITranslator>(sp => new LibreTranslator(
            sp.GetRequiredService<IHttpClientFactory>(), translation.Url!, translationKey,
            sp.GetRequiredService<ILogger<LibreTranslator>>()));
    else
        builder.Services.AddScoped<ITranslator, StubTranslator>();
}
// Factory so ITranslator can be absent (feature off) without DI failing to build.
builder.Services.AddScoped(sp => new TranslationService(
    sp.GetRequiredService<StayHostDbContext>(), sp.GetService<ITranslator>()));

// docs/01 TK-02 — the provider client ids live in configuration so one build can
// run against a test project on a laptop and the real one on the server. Nothing
// here is a secret except the Facebook app secret, which comes from the
// environment file rather than appsettings.json.
// docs/07 §2.3 — the company account a VietQR credits. Off until an account
// number exists, so the method never shows up leading nowhere.
var bankTransfer = builder.Configuration.GetSection("BankTransfer").Get<BankTransferSettings>() ?? new();
builder.Services.AddSingleton(bankTransfer);

var externalLogin = builder.Configuration.GetSection("ExternalLogin").Get<ExternalLoginSettings>() ?? new();
builder.Services.AddSingleton(externalLogin);
builder.Services.AddHttpClient("external-login");
builder.Services.AddScoped<ExternalTokenVerifier>();

// Outgoing mail. Everything queues rows in EmailMessages; EmailWorker drains
// them through whichever sender is registered here. With no Email:Host the
// queue simply holds the mail — nothing pretends to send. The SMTP password
// arrives as Email__Password from the environment, never appsettings.json.
var email = builder.Configuration.GetSection("Email").Get<EmailSettings>() ?? new();
builder.Services.AddSingleton(email);
if (email.IsConfigured) builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
else builder.Services.AddScoped<IEmailSender, UnconfiguredEmailSender>();
builder.Services.AddScoped<EmailDispatcher>();
builder.Services.AddHostedService<EmailWorker>();

// The address a link in an email has to carry, because the reader is outside the
// browser tab that would have made a relative path work. Any deployment taking
// real money already had to tell the gateways the same address, so Site:PublicUrl
// falls back to Psp:PublicUrl rather than becoming a second value that must agree
// with the first — two settings for one address is how they drift apart.
var site = builder.Configuration.GetSection("Site").Get<SiteSettings>() ?? new();
if (site.PublicUrl.Length == 0)
    site.PublicUrl = (builder.Configuration["Psp:PublicUrl"] ?? "").Trim();
builder.Services.AddSingleton(site);

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Port=5432;Database=stayhost;Username=stayhost;Password=stayhost";

builder.Services.AddDbContext<StayHostDbContext>(o => o
    .UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)));

builder.Services.AddHttpContextAccessor();

// The live visitor count. Singleton because the whole point is that it survives
// between requests; see PresenceTracker for what that does and does not promise.
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<BadgeService>();
builder.Services.AddScoped<PayoutService>();
builder.Services.AddScoped<CoHostPayoutService>();
builder.Services.AddScoped<PayoutStatementService>();
// docs/07 §14.3 — the key that seals hosts' bank account numbers. With none set
// the number is not stored and no transfer file can be produced; see DEPLOY.md.
builder.Services.AddScoped<DataSecrets>();
builder.Services.AddScoped<PayoutAccounts>();
// docs/07 §10 — sending a guest's money back through whichever gateway took it.
builder.Services.AddScoped<RefundGateway>();
builder.Services.AddScoped<PaymentCompletion>();
builder.Services.AddScoped<CardAuthSweeper>();
builder.Services.AddScoped<BankTransferService>();
builder.Services.AddScoped<SavedSearchSweeper>();
builder.Services.AddScoped<ScarcitySweeper>();
builder.Services.AddScoped<AdminGate>();
builder.Services.AddScoped<ThreadMessenger>();
builder.Services.AddScoped<AdminAudit>();
builder.Services.AddScoped<PaymentGateway>();
builder.Services.AddScoped<BalanceCollector>();
builder.Services.AddScoped<RiskWatch>();
builder.Services.AddScoped<SanctionExpiry>();
builder.Services.AddScoped<StayReminderSweeper>();
builder.Services.AddScoped<SplitBillService>();
builder.Services.AddScoped<ExperienceService>();
builder.Services.AddScoped<ServiceMarketService>();
builder.Services.AddScoped<WalletService>();
// docs/01 TC-08 — the one place a paid-for gift card is switched on, shared by
// the gateway settlement and the stand-in charge so they cannot disagree.
builder.Services.AddScoped<GiftCardService>();
// The sale itself, which starts a payment. Separate from the wallet because the
// wallet is what PaymentCompletion needs, and PspCheckout needs that.
builder.Services.AddScoped<GiftCardSales>();
builder.Services.AddScoped<CouponService>();
builder.Services.AddScoped<ShieldService>();
builder.Services.AddScoped<HostAccess>();
builder.Services.AddScoped<CalendarSyncService>();

// docs/07 §13 phương án A — the licensed gateways. Each stays dormant until its
// keys are filled in, so an unconfigured build behaves exactly as it did: the
// stand-in gateway keeps the demo checkout working (Psp:Methods decides which
// method belongs to which gateway).
builder.Services.Configure<PspSettings>(builder.Configuration.GetSection("Psp"));
builder.Services.AddScoped<IPspProvider, VnPayProvider>();
builder.Services.AddScoped<IPspProvider, OnePayProvider>();
builder.Services.AddScoped<IPspProvider, MoMoProvider>();
builder.Services.AddScoped<IPspProvider, ZaloPayProvider>();
builder.Services.AddScoped<PspRouter>();
builder.Services.AddScoped<PspCheckout>();
builder.Services.AddScoped<PspSweeper>();
// docs/07 §7 — the gateway's own half of the daily reconciliation.
builder.Services.AddScoped<GatewayStatement>();
builder.Services.AddHttpClient("psp", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);

    // VNPay's merchant API answers 403 with an HTML page to any request that
    // carries no User-Agent, and HttpClient sends none. That is not a signature
    // problem and does not look like one: it silently disabled both the refund
    // call and the querydr self-check of docs/07 §5 — the safety net for a guest
    // whose connection drops mid-payment — while every log line said only that
    // the reply could not be parsed as JSON.
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Staylio/1.0 (+https://staylio.vn)");
});

// A host-typed address: only the public internet, checked at connect time.
builder.Services.AddHttpClient("ical")
    .ConfigurePrimaryHttpMessageHandler(StayHost.Web.Infrastructure.PublicNetworkOnly.Handler);
builder.Services.AddHostedService<CalendarSyncWorker>();
builder.Services.AddHostedService<BookingLifecycleWorker>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddControllers();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddHealthChecks().AddDbContextCheck<StayHostDbContext>();

// docs/08 §3 — the admin two-factor gate. Configurable only so a server with no
// mail configured is not locked out of its own console; see AdminActions.
AdminActions.RequireTwoFactor = builder.Configuration.GetValue("Admin:RequireTwoFactor", true);

var app = builder.Build();

// The web container starts alongside Postgres; retry until the database answers, then migrate + seed.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StayHostDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    for (var attempt = 1; attempt <= 15; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);

            // docs/08 §3 — "Bắt buộc bảo mật 2 lớp. Không bật thì không đăng nhập
            // được, không có ngoại lệ." An admin row saved before that rule
            // existed has the flag off, and the rule then locks the console
            // against everyone: turning two-factor on requires signing in, and
            // signing in requires two-factor. Nobody can climb out of that from
            // the UI, so the invalid state is repaired here instead.
            //
            // Admin:RequireTwoFactor=false stands the whole gate down for a
            // deployment that cannot send the code yet. The flag on the rows
            // follows the setting, because a live TwoFactorEnabled would still
            // send a code nobody can read.
            if (AdminActions.RequireTwoFactor)
            {
                var repaired = await db.Users
                    .Where(u => u.Role == UserRole.Admin && !u.TwoFactorEnabled)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, true));

                if (repaired > 0)
                    log.LogWarning("Đã bật bảo mật 2 lớp cho {Count} tài khoản quản trị (docs/08 §3).", repaired);
            }
            else
            {
                var stoodDown = await db.Users
                    .Where(u => u.Role == UserRole.Admin && u.TwoFactorEnabled)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, false));

                log.LogWarning(
                    "BẢO MẬT 2 LỚP CHO QUẢN TRỊ ĐANG TẮT (Admin:RequireTwoFactor=false) — {Count} tài khoản. " +
                    "Mật khẩu là thứ duy nhất chặn người lạ vào trang quản trị. " +
                    "Bật lại ngay khi gửi được email.", stoodDown);
            }

            // The seeded console account is admin@staylio.vn. Owning the domain
            // is not the same as having that mailbox, so unless someone created
            // it the six-digit code of §3 is posted to an address nobody reads.
            // Setting Admin:Email (ADMIN_EMAIL in the deploy env file) moves the
            // account to a real inbox, which is also the address it is then
            // signed in with. Left unset, nothing changes.
            var adminEmail = (builder.Configuration["Admin:Email"] ?? "").Trim().ToLowerInvariant();
            if (adminEmail.Length > 0)
            {
                var console = await db.Users
                    .Where(u => u.Role == UserRole.Admin)
                    .OrderBy(u => u.Id)
                    .FirstOrDefaultAsync();

                if (console is null)
                    log.LogWarning("Admin:Email được đặt nhưng chưa có tài khoản quản trị nào.");
                else if (console.Email == adminEmail)
                    log.LogInformation("Tài khoản quản trị đã dùng {Email}.", adminEmail);
                else if (await db.Users.AnyAsync(u => u.Email == adminEmail && u.Id != console.Id))
                    log.LogWarning("Không đổi được email quản trị: {Email} đã thuộc tài khoản khác.", adminEmail);
                else
                {
                    var was = console.Email;
                    console.Email = adminEmail;
                    // The address is where the sign-in code goes, so control of
                    // it is proved by the next sign-in rather than assumed here.
                    console.EmailConfirmed = false;
                    await db.SaveChangesAsync();

                    log.LogWarning("Đã chuyển tài khoản quản trị từ {Was} sang {Now}.", was, adminEmail);
                }
            }

            // docs/01 AT-07 — help articles seed on their own, so adding one
            // later does not need the whole catalogue rebuilt.
            await HelpSeeder.SeedAsync(db);
            await ExperienceSeeder.SeedAsync(db);
            await ServiceSeeder.SeedAsync(db);
            // Last of the three: it books the sessions and jobs the other two
            // created, so both have to exist before it runs.
            await ReviewSeeder.SeedAsync(db);
            await HotelSeeder.SeedAsync(db);
            await FeatureFlagSeeder.SeedAsync(db);
            log.LogInformation("Database ready.");
            break;
        }
        catch (Exception ex) when (attempt < 15)
        {
            log.LogWarning("Database not ready ({Attempt}/15): {Message}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

// TLS ends at the shared Caddy container, so every request reaches Kestrel as
// plain HTTP — and until this line the app believed it. UseHsts below never
// fired (it only answers HTTPS requests), the sign-in cookie went out without
// Secure, and links built from Request.Scheme (the iCal export a host pastes
// into Airbnb, the split-bill invitation) said http://. Kestrel is published on
// host loopback and the compose network only, so the proxy is the one thing
// that can set these headers; Caddy also replaces any X-Forwarded-For a client
// sends rather than appending to it.
var forwarded = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
};
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Nothing here is meant to be framed by another site: the booking and payment
// buttons are exactly what a clickjacking page would overlay. nosniff stops an
// uploaded photo from being run as a script by an old browser.
app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

var presence = app.Services.GetRequiredService<PresenceTracker>();

app.UseResponseCompression();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers.CacheControl =
            app.Environment.IsDevelopment() || path.EndsWith(".html")
                ? "no-cache"
                : "public,max-age=3600";
    }
});

// Every request gets a wishlist identity, including the first HTML hit.
app.Use(async (ctx, next) =>
{
    var sid = ctx.SessionId();
    var counts = Presence.CountsAsVisit(
        ctx.Request.Path.Value,
        ctx.Request.Headers.UserAgent.ToString(),
        broughtCookie: !ctx.SessionIsNew());

    await next();

    // Counted AFTER the request, so the signed-in user is known: AuthService
    // caches whoever it resolved in HttpContext.Items, and most endpoints have
    // asked by the time they finish. Before next(), every visitor would look
    // like a guest.
    if (!counts) return;

    var user = ctx.Items.TryGetValue("__sh_user", out var u) ? u as StayHost.Domain.User : null;
    presence.Touch(
        sid, user?.Id,
        hasAuthCookie: ctx.Request.Cookies.ContainsKey(StayHost.Web.Services.AuthService.CookieName),
        DateTime.UtcNow);
});

// docs/08 §7.6 — the nine things a support session inside somebody's account
// must never do, refused before any controller sees the request.
app.UseMiddleware<StayHost.Web.Infrastructure.ImpersonationGuard>();

app.MapControllers();
app.MapHealthChecks("/health");
app.Map("/error", () => Results.Problem("Đã có lỗi xảy ra."));

// Client-side routes (/rooms/..., /wishlists, /host, /trips) fall back to the SPA
// shell — but the shell is not an answer to every address.
//
// MapFallbackToFile answered 200 for anything at all, so /rooms/khong-co-that
// came back as a successful page carrying the home page's title and an empty
// body. Google calls that a soft 404 and it is invisible from this side: a
// person sees "không tìm thấy" and goes back, while a crawler files a blank
// page under a real-looking address. The shell still renders — the guest gets
// the same screen either way — but the status line now says what happened.
app.MapFallback(async (HttpContext ctx, StayHostDbContext db, SiteSettings site) =>
{
    var path = ctx.Request.Path.Value ?? "/";

    // Nothing under /api/ is a page. An unmatched API address reaching this far
    // means a wrong verb or a wrong route, and answering it with the app shell
    // is how a caller reads a 405 as a success: three acceptance scenarios sent
    // a GET to a POST endpoint for months and passed, because the shell came
    // back 200 and only the printed detail line ("? chỗ đã lưu") ever said so.
    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    // A missing file must never be answered with HTML. A <script> tag that
    // receives the app shell fails as a syntax error somewhere inside the app,
    // which is a long way from "that bundle name is stale".
    if (SpaRoutes.LooksLikeAsset(path))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var route = SpaRoutes.Resolve(path);
    var exists = await PageExistence.ExistsAsync(db, route, ctx.RequestAborted);

    ctx.Response.StatusCode = exists
        ? StatusCodes.Status200OK
        : StatusCodes.Status404NotFound;

    // Belt and braces for the 404: the shell is rendered by JavaScript, and a
    // crawler that gave up before running it would otherwise have only the
    // status line to go on.
    if (!exists) ctx.Response.Headers["X-Robots-Tag"] = "noindex";

    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";

    // The address this site admits to living at. Configured first, because that
    // is the one canonical answer; otherwise the host the request arrived on,
    // minus any "www." - both hosts answer, and a canonical that points at
    // whichever one was asked hands Google two complete copies of the catalogue,
    // each claiming to be the original.
    var host = site.PublicUrl.Length > 0
        ? site.PublicUrl.TrimEnd('/')
        : $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
    var origin = host.Replace("://www.", "://", StringComparison.OrdinalIgnoreCase);

    // "/rooms/x/" and "/rooms/x" are one page, and only one of them may be the
    // canonical address of it.
    var tidy = path.Length > 1 ? path.TrimEnd('/') : "/";
    if (tidy.Length == 0) tidy = "/";

    _ = int.TryParse(ctx.Request.Query["trang"], out var wantedPage);

    var html = await ShellSeo.RenderAsync(
        app.Environment.WebRootFileProvider, db, route, origin, tidy, wantedPage,
        ctx.RequestAborted);

    if (html is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return;
    }

    await ctx.Response.WriteAsync(html, ctx.RequestAborted);
});

app.Run();
