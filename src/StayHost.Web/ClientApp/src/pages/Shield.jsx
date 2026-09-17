import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { set, toast } from '../lib/store.js';
import { api } from '../lib/api.js';
import { money, longDate, dateTime } from '../lib/format.js';
import { t } from '../lib/i18n.js';

/** Who did a thing, in words rather than in the actor codes the server stores. */
const ACTOR = { guest: 'Khách', host: 'Chủ nhà', admin: 'Staylio', system: 'Tự động' };

/**
 * docs/06 — Staylio Shield. The programme is always described as a platform support
 * policy: §11 forbids insurance language anywhere a user can read it, and the
 * server owns the wording so every screen says the same thing.
 */
export function Shield() {
  const { id } = useParams();
  return id ? <Claim id={Number(id)} /> : <Overview />;
}

/* ----------------------------------------------------- AT-06-01: the terms */

export function ShieldTerms() {
  const [side, setSide] = useState('guest');
  const [terms, setTerms] = useState(null);
  const navigate = useNavigate();

  useEffect(() => {
    api.shieldTerms(side).then(setTerms).catch(e => toast(e.message));
  }, [side]);

  return (
    <div className="shell shell-narrow" style={{ paddingBlock: '34px 90px' }}>
      <span className="shield-mark">Staylio Shield</span>
      <h1 className="section-title" style={{ marginTop: 10 }}>{terms?.title ?? 'Staylio Shield'}</h1>

      <div className="seg-tabs" style={{ marginTop: 16 }}>
        {[['guest', 'Dành cho khách'], ['host', 'Dành cho chủ nhà']].map(([key, label]) => (
          <button key={key} className={`seg-tab ${side === key ? 'is-active' : ''}`}
                  onClick={() => setSide(key)}>{t(label)}</button>
        ))}
      </div>

      {!terms ? <div className="stat skeleton" style={{ height: 300, border: 0, marginTop: 24 }} /> : <>
        <p className="shield-intro">{terms.intro}</p>

        {terms.sections.map(s => (
          <section className="detail-section" key={s.heading}>
            <h2>{s.heading}</h2>
            <ul className="shield-list">{s.points.map((p, i) => <li key={i}>{p}</li>)}</ul>
          </section>
        ))}

        <section className="detail-section">
          <h2>{t('Không áp dụng khi')}</h2>
          <ul className="shield-list is-muted">{terms.exclusions.map((p, i) => <li key={i}>{p}</li>)}</ul>
        </section>

        <p className="shield-disclaimer">{terms.disclaimer}</p>

        <div style={{ display: 'flex', gap: 10, marginTop: 20, flexWrap: 'wrap' }}>
          <button className="btn btn-primary btn-sm" onClick={() => navigate('/shield')}>{t('Hồ sơ của tôi')}</button>
          <button className="btn btn-outline btn-sm" onClick={() => navigate('/help')}>{t('Trung tâm trợ giúp')}</button>
        </div>
      </>}
    </div>
  );
}

/* ------------------------------------------------------ AT-06-06: tracking */

function Overview() {
  const state = useStore();
  const navigate = useNavigate();
  const [rows, setRows] = useState(null);

  useEffect(() => {
    if (state.user) api.shieldClaims().then(setRows).catch(e => toast(e.message));
  }, [state.user]);

  if (!state.user) {
    return <div className="shell" style={{ paddingBlock: '60px 90px' }}>
      <div className="empty-state"><h3>{t('Đăng nhập để xem hồ sơ Staylio Shield')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>{t('Đăng nhập')}</button>
      </div></div>;
  }

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <span className="shield-mark">Staylio Shield</span>
      <h1 className="section-title" style={{ marginTop: 10 }}>{t('Hồ sơ Staylio Shield')}</h1>
      <p className="section-sub">
        {t('Chính sách hỗ trợ của Staylio cho cả khách và chủ nhà.')}
        {' '}<button className="text-btn" onClick={() => navigate('/shield/terms')}>{t('Xem phạm vi và hạn mức')}</button>
      </p>

      {!rows ? <div className="stat skeleton" style={{ height: 200, border: 0, marginTop: 24 }} />
        : rows.length ? (
          <div style={{ marginTop: 20, display: 'grid', gap: 12 }}>
            {rows.map(c => <ClaimRow key={c.id} claim={c} onOpen={() => navigate(`/shield/${c.id}`)} />)}
          </div>
        ) : (
          <div className="empty-state" style={{ marginTop: 24 }}>
            <h3>{t('Chưa có hồ sơ nào')}</h3>
            <p>{t('Khi chuyến đi có vấn đề, mở hồ sơ ngay trong trang chuyến đi.')}</p>
            <button className="btn btn-primary" style={{ marginTop: 18 }}
                    onClick={() => navigate('/trips')}>{t('Chuyến đi của tôi')}</button>
          </div>
        )}
    </div>
  );
}

function ClaimRow({ claim, onOpen }) {
  return (
    <article className="host-booking">
      <div style={{ minWidth: 0 }}>
        <h3>{t(claim.kindLabel)}</h3>
        <div className="meta">{claim.listingTitle} · {t('đơn')} {claim.bookingReference} · {t('mã')} {claim.reference}</div>
        <div className="meta">{claim.description}</div>
        {claim.approved > 0 && <div className="meta">{t('Đã duyệt')} {money(claim.approved)}</div>}
        {claim.decision && <div className="meta">{t('Kết luận:')} {claim.decision}</div>}
        <span className={`badge ${claim.statusBadge}`} style={{ marginTop: 8 }}>{t(claim.statusLabel)}</span>
      </div>
      <div className="host-booking-actions">
        <button className="btn btn-outline btn-sm" onClick={onOpen}>{t('Xem hồ sơ')}</button>
      </div>
    </article>
  );
}

/* ---------------------------------------- AT-06-06 / AT-06-07: one case */

function Claim({ id }) {
  const navigate = useNavigate();
  const [c, setC] = useState(null);
  const [missing, setMissing] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = () => api.shieldClaim(id).then(setC).catch(() => setMissing(true));
  useEffect(() => { load(); }, [id]);

  if (missing) {
    return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
      <div className="empty-state"><h3>{t('Không tìm thấy hồ sơ này')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => navigate('/shield')}>{t('Về danh sách hồ sơ')}</button></div></div>;
  }

  if (!c) return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
    <div className="stat skeleton" style={{ height: 260, border: 0 }} /></div>;

  const respond = async (answer, agreed) => {
    setBusy(true);
    try {
      const note = prompt(answer === 'dispute' ? t('Vì sao bạn phản đối?') : `${t('Ghi chú')} ${t('(không bắt buộc)')}`) ?? '';
      setC(await api.respondShield(c.id, { answer, agreedAmount: agreed ?? null, note: note.trim() || null }));
      toast(t('Đã gửi phản hồi.'));
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  const appeal = async () => {
    const note = prompt(t('Bạn muốn chúng tôi xem lại điều gì?')) ?? '';
    if (!note.trim()) return;
    setBusy(true);
    try {
      setC(await api.appealShield(c.id, note.trim()));
      toast(t('Đã gửi khiếu nại. Một người khác sẽ xem lại.'));
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  const waiting = c.status === 'Open';
  const canAppeal = !c.appealed && (c.status === 'Settled' || c.status === 'Rejected');

  return (
    <div className="shell shell-narrow" style={{ paddingBlock: '26px 90px' }}>
      <button className="back-link" onClick={() => navigate('/shield')}>← Hồ sơ Staylio Shield</button>

      <span className="shield-mark" style={{ marginTop: 12 }}>Staylio Shield</span>
      <h1 className="section-title" style={{ marginTop: 8 }}>{t(c.kindLabel)}</h1>
      <p className="section-sub">
        {t('Mã')} {c.reference} · {t('đơn')} {c.bookingReference} · {c.listingTitle}
        {' '}· {t('mở ngày')} {longDate(c.createdAt.slice(0, 10))}
      </p>

      <div style={{ display: 'flex', gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
        <span className={`badge ${c.statusBadge}`}>{t(c.statusLabel)}</span>
        {c.needsManualReview && <span className="badge pending">{t('Cần người xem lại')}</span>}
        {c.appealed && <span className="badge pending">{t('Đã khiếu nại một lần')}</span>}
      </div>

      <section className="detail-section">
        <h2>{t('Nội dung')}</h2>
        <p style={{ fontSize: 15, lineHeight: 1.7, color: 'var(--ink-body)' }}>{c.description}</p>

        {!!c.items.length && (
          <div className="table-wrap" style={{ marginTop: 14 }}>
            <table className="admin-table">
              <thead><tr><th>{t('Món')}</th><th style={{ textAlign: 'right' }}>{t('Giá trị')}</th>
                <th style={{ textAlign: 'right' }}>{t('Được tính')}</th></tr></thead>
              <tbody>
                {c.items.map(i => (
                  <tr key={i.id}>
                    <td>{i.name}{i.declaredOnListing ? ` · ${t('đã khai báo')}` : ''}</td>
                    <td style={{ textAlign: 'right' }}>{money(i.value)}</td>
                    <td style={{ textAlign: 'right' }}>{money(i.allowed)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!!c.evidence.length && (
          <div className="bubble-photos" style={{ marginTop: 14 }}>
            {c.evidence.map(e => (
              <a href={e.url} target="_blank" rel="noreferrer" key={e.id}>
                <img src={e.url} alt={e.caption ?? t('Bằng chứng')} loading="lazy" />
              </a>
            ))}
          </div>
        )}
      </section>

      {(c.approved > 0 || c.creditGranted > 0) && (
        <section className="detail-section">
          <h2>{t('Kết quả')}</h2>
          <div className="kv-grid">
            <Kv label={t('Được duyệt')} value={money(c.approved)} />
            {c.deductible > 0 && <Kv label={t('Bạn tự chịu')} value={money(c.deductible)} />}
            {c.creditGranted > 0 && <Kv label={t('Số dư tặng thêm')} value={money(c.creditGranted)} />}
          </div>
          {c.decision && (
            <p style={{ margin: '14px 0 0', fontSize: 14.5, lineHeight: 1.7, color: 'var(--ink-body)' }}>
              {c.decision}
            </p>
          )}
        </section>
      )}

      {waiting && !c.openedByMe && (
        <section className="detail-section">
          <h2>{t('Bạn có 24 giờ để phản hồi')}</h2>
          <p className="section-sub">{t('Hạn phản hồi:')} {dateTime(c.respondBy)}</p>
          <div style={{ display: 'flex', gap: 10, marginTop: 14, flexWrap: 'wrap' }}>
            <button className="btn btn-primary btn-sm" disabled={busy}
                    onClick={() => respond('accept')}>{t('Đồng ý')}</button>
            {c.claimed > 0 && (
              <button className="btn btn-outline btn-sm" disabled={busy} onClick={() => {
                const raw = prompt(`${t('Bạn đồng ý bao nhiêu trong')} ${money(c.claimed)}?`);
                const amount = Number((raw ?? '').replace(/\D/g, ''));
                if (amount > 0) respond('partial', amount);
              }}>{t('Đồng ý một phần')}</button>
            )}
            <button className="btn btn-outline btn-sm" disabled={busy}
                    onClick={() => respond('dispute')}>{t('Phản đối')}</button>
          </div>
        </section>
      )}

      {waiting && c.openedByMe && (
        <div className="book-alert" style={{ marginTop: 20 }}>
          <b>{t('Đang chờ bên kia phản hồi')}</b>
          <span>{t('Hết')} {dateTime(c.respondBy)} {t('mà không có trả lời thì Staylio sẽ tự xem xét.')}</span>
        </div>
      )}

      {canAppeal && c.openedByMe && (
        <section className="detail-section">
          <h2>{t('Chưa đồng ý với quyết định?')}</h2>
          <p className="section-sub">{t('Bạn khiếu nại được một lần, trong 7 ngày. Người khác sẽ xem lại.')}</p>
          <button className="btn btn-outline btn-sm" style={{ marginTop: 12 }}
                  disabled={busy} onClick={appeal}>{t('Yêu cầu xem lại')}</button>
        </section>
      )}

      <section className="detail-section">
        <h2>{t('Diễn biến')}</h2>
        <div style={{ display: 'grid', gap: 10 }}>
          {c.events.map(e => (
            <div className="cal-row" key={e.id}>
              <span className="badge pending">{dateTime(e.createdAt)}</span>
              <div style={{ flex: 1, minWidth: 0, fontSize: 13.5 }}>
                <b>{e.toStatusLabel}</b>
                {e.note && <span style={{ color: 'var(--ink-muted)' }}> · {e.note}</span>}
              </div>
              <span style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>{t(ACTOR[e.actor.split(':')[0]] ?? e.actor)}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

function Kv({ label, value }) {
  return <div className="kv"><span className="kv-label">{label}</span><b>{value}</b></div>;
}
