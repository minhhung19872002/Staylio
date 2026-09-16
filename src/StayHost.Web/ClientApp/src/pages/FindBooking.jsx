import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../lib/api.js';
import { money, longDate } from '../lib/format.js';
import { t } from '../lib/i18n.js';

/**
 * docs/07 §2.5, docs/01 ĐP-13 — finding a booking again without an account.
 *
 * The session cookie carries ownership while it lasts, which is not long enough
 * to be the answer: somebody books on a phone and looks it up on a laptop, or
 * clears their browser, and all they have left is the reference from the email
 * and the address it went to. Both are asked for — a reference travels alone in
 * a forwarded subject line.
 *
 * A match hands the booking back to this session, so the trip page and the
 * cancel button work from here on without a second lookup.
 */
export function FindBooking() {
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [found, setFound] = useState(null);

  const submit = async e => {
    e.preventDefault();
    const f = e.target;
    setBusy(true);
    setError(null);
    try {
      setFound(await api.lookupBooking(f.reference.value, f.email.value));
    } catch (err) {
      setError(t(err.message));
      setFound(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="shell" style={{ paddingBlock: '34px 90px', maxWidth: 620 }}>
      <h1 className="section-title">{t('Tra cứu đặt chỗ')}</h1>
      <p className="section-sub">
        {t('Nhập mã đặt chỗ trong email xác nhận và chính email đó. Không cần tài khoản.')}
      </p>

      <form onSubmit={submit} style={{ display: 'grid', gap: 14, marginTop: 22 }}>
        <label className="form-field"><span className="cap">{t('Mã đặt chỗ')}</span>
          <input name="reference" className="field" required placeholder="SH1A2B3C4D"
                 autoComplete="off" spellCheck="false" /></label>
        <label className="form-field"><span className="cap">{t('Email đã dùng khi đặt')}</span>
          <input name="email" type="email" className="field" required placeholder="ban@email.com" /></label>
        <div>
          <button className="btn btn-primary" disabled={busy}>
            {busy ? t('Đang tìm…') : t('Tìm đặt chỗ')}
          </button>
        </div>
      </form>

      {error && <p className="notice notice-warn" style={{ marginTop: 18 }}>{error}</p>}

      {found && (
        <article className="trip" style={{ marginTop: 24 }}>
          {found.listingImage && <img src={found.listingImage} alt={found.listingTitle} loading="lazy" />}
          <div style={{ minWidth: 0 }}>
            <h3>{found.listingTitle}</h3>
            <div className="meta">{found.listingCity} · {t('Mã đặt chỗ')} {found.reference}</div>
            <div className="meta">
              {longDate(found.checkIn)} → {longDate(found.checkOut)} · {found.nights} {t('đêm')} · {found.guests} {t('khách')}
            </div>
            <div className="meta">
              <b style={{ color: 'var(--ink)' }}>{money(found.total)}</b> {t('tổng cộng')}
            </div>
            <span className={`badge ${found.statusBadge}`} style={{ marginTop: 8 }}>
              {t(found.statusLabel, 'status')}
            </span>
          </div>
          <div style={{ display: 'grid', gap: 8 }}>
            <button className="btn btn-dark btn-sm" onClick={() => navigate(`/trips/${found.id}`)}>
              {t('Xem chi tiết')}
            </button>
          </div>
        </article>
      )}
    </div>
  );
}
