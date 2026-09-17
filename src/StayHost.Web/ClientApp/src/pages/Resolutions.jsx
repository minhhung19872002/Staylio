import { useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { set, toast } from '../lib/store.js';
import { api } from '../lib/api.js';
import { money, dateTime, longDate } from '../lib/format.js';
import { t } from '../lib/i18n.js';

const KINDS = [
  ['Damage', 'Bồi thường thiệt hại', 'Khách làm hỏng đồ đạc trong nhà.'],
  ['NotAsDescribed', 'Không đúng mô tả', 'Chỗ nghỉ khác xa với tin đăng.'],
  ['Refund', 'Yêu cầu hoàn tiền', 'Xin hoàn ngoài chính sách huỷ.'],
  ['Other', 'Vấn đề khác', 'Trường hợp không thuộc các nhóm trên.']
];

/**
 * docs/01 AT-04 — the resolution centre. One side claims, the other has 24
 * hours to answer, and Staylio decides if they object.
 */
export function Resolutions() {
  const state = useStore();
  const [cases, setCases] = useState(null);
  const [opening, setOpening] = useState(false);
  // docs/01 CĐ-12 — arriving from a trip's "Cần trợ giúp" opens the form on that booking.
  const preBooking = useLocation().state?.bookingId ?? null;

  const load = () => api.resolutions().then(setCases).catch(e => toast(e.message));
  useEffect(() => { if (state.user) load(); }, [state.user]);
  useEffect(() => { if (preBooking) setOpening(true); }, [preBooking]);

  if (!state.user) {
    return (
      <div className="shell" style={{ paddingBlock: '60px 90px' }}>
        <div className="empty-state">
          <h3>{t('Đăng nhập để xem hồ sơ')}</h3>
          <p>{t('Trung tâm giải quyết dành cho khách và chủ nhà có đơn đặt trên Staylio.')}</p>
          <button className="btn btn-primary" style={{ marginTop: 18 }}
                  onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>{t('Đăng nhập')}</button>
        </div>
      </div>
    );
  }

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <div className="page-head">
        <div>
          <h1 className="section-title">{t('Trung tâm giải quyết')}</h1>
          <p className="section-sub">
            {t('Mở hồ sơ khi có thiệt hại hoặc tranh chấp. Bên còn lại có 24 giờ để trả lời; nếu phản đối, Staylio sẽ phân xử.')}
          </p>
        </div>
        <button className="btn btn-primary btn-sm" onClick={() => setOpening(true)}>{t('+ Mở hồ sơ')}</button>
      </div>

      {opening && <OpenCase preBooking={preBooking} onClose={() => setOpening(false)} onDone={() => { setOpening(false); load(); }} />}

      {cases === null && <div className="stat skeleton" style={{ height: 160, border: 0, marginTop: 24 }} />}

      {cases?.length === 0 && (
        <div className="empty-state" style={{ marginTop: 24 }}>
          <h3>{t('Chưa có hồ sơ nào')}</h3>
          <p>{t('Mong là bạn không bao giờ cần tới trang này.')}</p>
        </div>
      )}

      {cases?.map(c => <CaseCard key={c.id} kase={c} onDone={load} />)}
    </div>
  );
}

function CaseCard({ kase: c, onDone }) {
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState('');

  const respond = async accept => {
    setBusy(true);
    try {
      await api.respondResolution(c.id, { accept, note });
      toast(accept ? t('Đã ghi nhận bạn đồng ý.') : t('Đã chuyển Staylio phân xử.'));
      onDone();
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  const withdraw = async () => {
    if (!confirm(t('Rút lại hồ sơ này?'))) return;
    setBusy(true);
    try { await api.withdrawResolution(c.id); toast(t('Đã rút hồ sơ.')); onDone(); }
    catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  return (
    <article className="host-booking" style={{ alignItems: 'flex-start' }}>
      <div style={{ minWidth: 0, flex: 1 }}>
        <h3>{t(c.kindLabel)} · {c.listingTitle}</h3>
        <div className="meta">
          {t('Hồ sơ')} {c.reference} · {t('đơn')} {c.bookingReference} · {t('mở bởi')} {c.openedByName} ({c.openedByHost ? t('chủ nhà') : t('khách')})
        </div>
        <div className="meta">
          {t('Yêu cầu')} <b style={{ color: 'var(--ink)' }}>{money(c.amountClaimed)}</b>
          {c.amountAwarded > 0 && <> · {t('đã chuyển')} <b style={{ color: 'var(--brand)' }}>{money(c.amountAwarded)}</b></>}
        </div>

        <p style={{ margin: '10px 0 0', fontSize: 14, lineHeight: 1.6, color: 'var(--ink-body)' }}>{c.description}</p>

        {!!c.evidenceUrls.length && (
          <div className="thumb-grid" style={{ marginTop: 12 }}>
            {c.evidenceUrls.map((url, i) => (
              <figure className="thumb" key={i}><img src={url} alt={`${t('Bằng chứng')} ${i + 1}`} loading="lazy" /></figure>
            ))}
          </div>
        )}

        {c.response && (
          <div className="review-reply" style={{ marginLeft: 0 }}>
            <b>{t('Phản hồi của bên kia')}{c.respondedAt ? ` · ${dateTime(c.respondedAt)}` : ''}</b>
            <p>{c.response}</p>
          </div>
        )}

        {c.decision && (
          <div className="book-alert" style={{ marginTop: 12 }}>
            <b>{t('Staylio phân xử')}{c.decidedByName ? ` · ${c.decidedByName}` : ''}</b>
            <span>{c.decision}</span>
          </div>
        )}

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 10 }}>
          <span className={`badge ${c.statusBadge}`}>{t(c.statusLabel)}</span>
          {c.status === 'AwaitingResponse' && (
            <span className="badge pending">{t('Hạn trả lời')} {dateTime(c.responseDueAt)}</span>
          )}
        </div>

        {c.needsMyResponse && (
          <div style={{ marginTop: 14, maxWidth: 560 }}>
            <label className="form-field">
              <span className="cap">{t('Ý kiến của bạn')}</span>
              <textarea rows={3} value={note} onChange={e => setNote(e.target.value)}
                        placeholder={t('Giải thích ngắn gọn nếu bạn không đồng ý.')}
                        style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
            </label>
            <div style={{ display: 'flex', gap: 10 }}>
              <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => respond(true)}>{t('Đồng ý')}</button>
              <button className="btn btn-outline btn-sm" disabled={busy} onClick={() => respond(false)}>{t('Phản đối')}</button>
            </div>
          </div>
        )}

        <History events={c.history} />
      </div>

      <div className="host-booking-actions">
        {c.canWithdraw && <button className="btn btn-outline btn-sm" disabled={busy} onClick={withdraw}>{t('Rút hồ sơ')}</button>}
      </div>
    </article>
  );
}

const ACTOR = { system: 'Hệ thống', guest: 'Khách', host: 'Chủ nhà', admin: 'Staylio' };

function History({ events }) {
  if (!events?.length) return null;

  return (
    <details style={{ marginTop: 14 }}>
      <summary style={{ fontSize: 13, fontWeight: 600, cursor: 'pointer' }}>{t('Lịch sử hồ sơ')}</summary>
      <div style={{ display: 'grid', gap: 8, marginTop: 10 }}>
        {events.map((e, i) => (
          <div className="cal-row" key={i}>
            <span className="badge pending">{dateTime(e.at)}</span>
            <div style={{ flex: 1, minWidth: 0, fontSize: 13.5 }}>
              <b>{e.fromLabel ? `${e.fromLabel} → ${e.toLabel}` : e.toLabel}</b>
              {e.note && <span style={{ color: 'var(--ink-muted)' }}> · {e.note}</span>}
            </div>
            <span style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>
              {t(ACTOR[e.actor.split(':')[0]] ?? e.actor)}
            </span>
          </div>
        ))}
      </div>
    </details>
  );
}

function OpenCase({ onClose, onDone, preBooking }) {
  const state = useStore();
  const [busy, setBusy] = useState(false);
  const [kind, setKind] = useState('Damage');

  // Only stays that actually happened can be claimed about.
  const eligible = state.bookings.filter(b =>
    ['Completed', 'InProgress', 'CancelledByGuest', 'CancelledByHost'].includes(b.status));

  const submit = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    setBusy(true);
    try {
      await api.openResolution({
        bookingId: Number(f.bookingId.value),
        kind,
        amountClaimed: Number(f.amount.value),
        description: f.description.value.trim(),
        evidenceUrls: f.evidence.value.split('\n').map(s => s.trim()).filter(Boolean)
      });
      toast(t('Đã mở hồ sơ. Bên kia có 24 giờ để trả lời.'));
      onDone();
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  return (
    <section className="modal-section" style={{ border: '1px solid var(--line)', borderRadius: 16, padding: 20, marginTop: 20 }}>
      <h3>{t('Mở hồ sơ mới')}</h3>

      {!eligible.length ? (
        <p className="section-sub">{t('Bạn chưa có chuyến đi nào đủ điều kiện mở hồ sơ.')}</p>
      ) : (
        <form onSubmit={submit} style={{ marginTop: 14 }}>
          <label className="form-field">
            <span className="cap">{t('Chuyến đi')}</span>
            <select name="bookingId" required defaultValue={preBooking ?? ''}>
              {eligible.map(b => (
                <option key={b.id} value={b.id}>
                  {b.reference} · {b.listingTitle} · {longDate(b.checkIn)}
                </option>
              ))}
            </select>
          </label>

          <div className="opt-grid">
            {KINDS.map(([key, label, hint]) => (
              <button type="button" key={key} className={`opt ${kind === key ? 'is-on' : ''}`}
                      onClick={() => setKind(key)}>
                <b>{t(label)}</b><span>{t(hint)}</span>
              </button>
            ))}
          </div>

          <label className="form-field" style={{ marginTop: 14 }}>
            <span className="cap">{t('Số tiền yêu cầu (₫)')}</span>
            <input type="number" name="amount" min={1} step={10000} required />
          </label>

          <label className="form-field">
            <span className="cap">{t('Mô tả sự việc')} <span style={{ fontWeight: 400 }}>{t('(tối thiểu 20 ký tự)')}</span></span>
            <textarea name="description" rows={4} required minLength={20}
                      placeholder={t('Chuyện gì đã xảy ra, khi nào, thiệt hại ra sao.')}
                      style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
          </label>

          <label className="form-field">
            <span className="cap">{t('Ảnh bằng chứng')} <span style={{ fontWeight: 400 }}>{t('(mỗi dòng một liên kết)')}</span></span>
            <textarea name="evidence" rows={3} placeholder="https://…"
                      style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 13, fontFamily: 'ui-monospace,monospace' }} />
          </label>

          <div style={{ display: 'flex', gap: 10 }}>
            <button type="submit" className="btn btn-primary btn-sm" disabled={busy}>
              {busy ? t('Đang gửi…') : t('Gửi hồ sơ')}
            </button>
            <button type="button" className="btn btn-outline btn-sm" onClick={onClose}>{t('Huỷ')}</button>
          </div>
        </form>
      )}
    </section>
  );
}
