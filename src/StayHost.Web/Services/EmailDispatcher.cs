using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// Drains the email queue. Every service that wants to write to somebody adds a
/// row to <see cref="EmailMessage"/> inside its own transaction; this is the one
/// place those rows are handed to a mail server. Which failures are retried and
/// when is decided in <see cref="EmailDelivery"/>, not here.
/// </summary>
public class EmailDispatcher(
    StayHostDbContext db, IEmailSender sender, TranslationService translation,
    ILogger<EmailDispatcher> log)
{
    public sealed record Result(int Sent, int Retrying, int GivenUp)
    {
        public bool Any => Sent + Retrying + GivenUp > 0;
        public override string ToString() => $"{Sent} gửi được, {Retrying} chờ thử lại, {GivenUp} bỏ cuộc";
    }

    public async Task<Result> SweepAsync(CancellationToken ct)
    {
        // docs/01 TK-09 — content is translated here, at dispatch, never at
        // queue time: the Queue* methods deliberately do not save, and
        // TranslationService saves its own cache rows. Placed BEFORE the
        // no-mail-server gate on purpose, so a deployment with no SMTP still
        // translates its queue and a person can verify the whole path.
        await TranslatePendingAsync(ct);

        // With no server configured an attempt would only burn through the retry
        // schedule and mark good mail as failed. The queue holds everything, and
        // it all goes out once Email:Host is set.
        if (!sender.CanSend) return new(0, 0, 0);

        var now = DateTime.UtcNow;

        var due = await db.EmailMessages
            // Attempts == 0 with no schedule is a fresh message; a scheduled one
            // is due when its time has come. NextAttemptAt null with attempts on
            // the clock means the message was given up on, and stays given up.
            .Where(m => m.SentAt == null && !m.Undeliverable
                && ((m.Attempts == 0 && m.NextAttemptAt == null) || m.NextAttemptAt <= now))
            .OrderBy(m => m.Id)
            .Take(50)
            .ToListAsync(ct);

        int sent = 0, retrying = 0, givenUp = 0;

        foreach (var mail in due)
        {
            var result = await sender.SendAsync(mail.ToEmail, mail.ToName, mail.Subject, mail.Body, ct);
            mail.Attempts++;

            if (result.Ok)
            {
                mail.SentAt = DateTime.UtcNow;
                mail.NextAttemptAt = null;
                mail.Error = null;
                sent++;
                continue;
            }

            mail.Error = result.Error;

            if (EmailDelivery.IsPermanent(result.StatusCode))
            {
                // The receiving server will say the same no every time; asking
                // again only costs the sending reputation the sign-in codes need.
                mail.Undeliverable = true;
                mail.NextAttemptAt = null;
                givenUp++;
            }
            else if (EmailDelivery.ShouldRetry(result.StatusCode, mail.Attempts))
            {
                mail.NextAttemptAt = EmailDelivery.NextAttemptAt(DateTime.UtcNow, mail.Attempts);
                retrying++;
            }
            else
            {
                mail.NextAttemptAt = null;
                mail.Error = EmailDelivery.GiveUpNotice(mail.Attempts);
                givenUp++;
            }
        }

        if (due.Count > 0) await db.SaveChangesAsync(ct);

        return new(sent, retrying, givenUp);
    }

    /// <summary>
    /// docs/01 TK-09 — turns a queued mail's CONTENT into the reader's language.
    /// The frame around it was already composed by hand at queue time.
    ///
    /// One pass per mail, success or failure: TranslatedAt is stamped either
    /// way, because the Vietnamese original is the designed fallback and a mail
    /// must never sit in the queue waiting for a translator to feel better.
    /// Secret-bearing mail never gets here — its RawTitle is null by design,
    /// since a machine that "improves" one digit of a sign-in code locks a
    /// person out with no error anywhere.
    /// </summary>
    private async Task TranslatePendingAsync(CancellationToken ct)
    {
        var pending = await db.EmailMessages
            .Where(m => m.SentAt == null && !m.Undeliverable && m.TranslatedAt == null
                        && m.Language != null && m.Language != "vi"
                        && m.RawTitle != null && m.RawBody != null)
            .OrderBy(m => m.Id)
            .Take(20)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var mail in pending)
        {
            if (!translation.Enabled)
            {
                mail.TranslatedAt = DateTime.UtcNow;
                continue;
            }

            // Stamped after the attempt, not before: TranslationService saves its
            // cache mid-way, which used to write the stamp while the subject was
            // still Vietnamese — a mail "translated" that was not, for as long as
            // the second call took.
            try
            {
                var title = await translation.TranslateAsync(mail.RawTitle, mail.Language, ct);
                var body = await translation.TranslateAsync(mail.RawBody, mail.Language, ct);

                if (title.Ok && body.Ok)
                {
                    // The columns are varchar(250)/varchar(4000); a translation
                    // that overflows them must cost characters, not the sweep.
                    var subject = title.Text!.Length > 250 ? title.Text[..250] : title.Text!;
                    var composed = Emails.Compose(
                        mail.Language, mail.ToName, title.Text!, body.Text!, mail.CtaUrl,
                        machineTranslated: true);
                    mail.Subject = subject;
                    mail.Body = composed.Length > 4000 ? composed[..4000] : composed;
                }
                else
                {
                    log.LogWarning("Không dịch được thư {Id} sang {Lang}; gửi bản tiếng Việt.",
                        mail.Id, mail.Language);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Dịch thư {Id} sang {Lang} lỗi; gửi bản tiếng Việt.",
                    mail.Id, mail.Language);
            }
            finally
            {
                mail.TranslatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Runs the sweep every fifteen seconds — not a minute, because the first thing
/// this queue carries is the six-digit sign-in code, and whoever asked for it is
/// sitting there watching their inbox.
/// </summary>
public class EmailWorker(IServiceProvider services, ILogger<EmailWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<EmailDispatcher>();
                var result = await dispatcher.SweepAsync(stoppingToken);
                if (result.Any) log.LogInformation("Gửi thư: {Result}.", result);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Email sweep failed; next tick will try again.");
            }
        }
    }
}
