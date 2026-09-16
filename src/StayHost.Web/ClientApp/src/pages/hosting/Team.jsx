import { useEffect, useState } from 'react';
import { api } from '../../lib/api.js';
import { state as store, toast } from '../../lib/store.js';
import { longDate, money, number } from '../../lib/format.js';
import { t } from '../../lib/i18n.js';

/*
 * The server sends the granted scopes as one joined line — "Lịch trống, Giá,
 * Tin nhắn" (CoHostScopes.Describe). Each part is a dictionary entry on its own,
 * so the line is split, translated part by part and put back together; a single
 * value like "Chưa có quyền nào" goes through the same path unchanged.
 */
const scopeText = label => (label ?? '').split(', ').map(part => t(part)).join(', ');

/*
 * docs/02 G8 — the agreed share, said in the reader's language.
 *
 * Built here rather than sent ready-made because the sentence carries a number
 * inside it, and a dictionary cannot key on "20% mỗi đơn". One whole key per
 * shape with a {} in it keeps the audit able to see the literal and lets each
 * language put the number where its own grammar wants it.
 */
const payoutText = (kind, percent, amount) => {
  switch (kind) {
    case 'cleaning':
      return t('Toàn bộ phí dọn dẹp');
    case 'cleaning-plus-percent':
      return t('Phí dọn dẹp + {}% phần còn lại').replace('{}', number(percent));
    case 'percent':
      return t('{}% mỗi đơn, không gồm phí dọn dẹp').replace('{}', number(percent));
    case 'percent-with-cleaning':
      return t('{}% mỗi đơn, gồm cả phí dọn dẹp').replace('{}', number(percent));
    case 'fixed':
      return t('{} mỗi đơn').replace('{}', money(amount));
    default:
      return t('Không chia thu nhập');
  }
};

/**
 * docs/01 QL-19 — invite someone to help run a listing, choose how much they
 * may touch, and take it back. docs/02 G8 adds the other half: an optional cut
 * of what the owner earns, offered by the owner and confirmed by the person
 * being paid. Both sides live here: the people this host invited, and the
 * invitations and offers waiting for this host.
 */
export function Team() {
  const [board, setBoard] = useState(null);
  const [error, setError] = useState(null);

  const reload = () => api.coHosts().then(setBoard).catch(e => setError(t(e.message)));
  useEffect(() => { reload(); }, []);

  if (error) return <div className="empty-state" style={{ marginTop: 24 }}><h3>{error}</h3></div>;
  if (!board) return <div className="stat skeleton" style={{ height: 200, border: 0, marginTop: 24 }} />;

  return (
    <div style={{ marginTop: 24, display: 'grid', gap: 34 }}>
      <Invites invites={board.helping} onDone={reload} />
      <PayoutOffers invites={board.helping} onDone={reload} />
      <InviteForm scopes={board.scopes} onDone={reload} />
      <Granted rows={board.invited} kinds={board.payoutKinds}
               overcommitted={board.overcommittedPercent} onDone={reload} />
      <MyShares rows={board.earnings} total={board.earnedToDate} />
    </div>
  );
}

function Invites({ invites, onDone }) {
  const pending = invites.filter(i => i.status === 'invited');
  if (!pending.length) return null;

  const answer = async (id, decision) => {
    try {
      await api.respondCoHost(id, decision);
      toast(decision === 'accept' ? t('Bạn đã nhận lời mời đồng quản lý.') : t('Đã từ chối lời mời.'));
      onDone();
    } catch (err) { toast(err.message); }
  };

  return (
    <section>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Lời mời dành cho bạn')}</h2>
      {pending.map(i => (
        <div className="team-row" key={i.id}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <b>{i.ownerName}</b>
            <div className="team-sub">{i.listingTitle ?? t('Tất cả chỗ nghỉ')} · {scopeText(i.scopeLabel)}</div>
          </div>
          <button className="btn btn-primary btn-sm" onClick={() => answer(i.id, 'accept')}>{t('Nhận lời')}</button>
          <button className="btn btn-outline btn-sm" onClick={() => answer(i.id, 'decline')}>{t('Từ chối')}</button>
        </div>
      ))}
    </section>
  );
}

/**
 * docs/02 G8 — an owner has offered this person a share of what they earn.
 *
 * The offer names its own deadline, because it has one: fourteen days and it
 * lapses. And it says out loud whether there is anywhere to send the money —
 * accepting without a payout account is how a share sits held for months with
 * nobody told why.
 */
function PayoutOffers({ invites, onDone }) {
  const offers = invites.filter(i => i.payoutStatus === 'proposed');
  if (!offers.length) return null;

  const answer = async (id, decision) => {
    try {
      await api.respondCoHostPayout(id, decision);
      toast(decision === 'accept'
        ? t('Bạn đã nhận chia thu nhập từ chỗ nghỉ này.')
        : t('Đã từ chối đề nghị chia thu nhập.'));
      onDone();
    } catch (err) { toast(err.message); }
  };

  return (
    <section>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Đề nghị chia thu nhập')}</h2>
      <p className="section-sub">
        {t('Khoản này được trừ vào thu nhập của chủ nhà và chuyển thẳng vào tài khoản của bạn, sau khi khách nhận phòng.')}
      </p>

      {offers.map(o => (
        <div className="team-row" key={o.id}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <b>{o.ownerName}</b>
            <div className="team-sub">
              {o.listingTitle ?? t('Tất cả chỗ nghỉ')} ·{' '}
              <b>{payoutText(o.payoutKind, o.payoutPercent, o.payoutFixed)}</b>
            </div>
            {o.payoutConfirmBy && (
              <div className="team-sub">{t('Cần trả lời trước')} {longDate(o.payoutConfirmBy)}</div>
            )}
            {!o.hasPayoutAccount && (
              <div className="team-sub" style={{ color: 'var(--danger, #c13515)' }}>
                {t('Bạn chưa khai tài khoản nhận tiền — hãy khai trong phần Nhận tiền, nếu không khoản chia sẽ bị giữ lại.')}
              </div>
            )}
          </div>
          <button className="btn btn-primary btn-sm" onClick={() => answer(o.id, 'accept')}>{t('Đồng ý')}</button>
          <button className="btn btn-outline btn-sm" onClick={() => answer(o.id, 'decline')}>{t('Từ chối')}</button>
        </div>
      ))}
    </section>
  );
}

function InviteForm({ scopes, onDone }) {
  const listings = store.hosting?.listings ?? [];
  const [email, setEmail] = useState('');
  const [listingId, setListingId] = useState('');
  const [picked, setPicked] = useState(['calendar', 'messages']);
  const [busy, setBusy] = useState(false);

  const toggle = key => setPicked(p => (p.includes(key) ? p.filter(k => k !== key) : [...p, key]));

  const send = async e => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.inviteCoHost({
        email: email.trim(),
        listingId: listingId ? Number(listingId) : null,
        scopes: picked
      });
      setEmail('');
      toast(t('Đã gửi lời mời đồng quản lý.'));
      onDone();
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  return (
    <section>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Mời người đồng quản lý')}</h2>
      <p className="section-sub">{t('Dù được cấp quyền nào, người đồng quản lý cũng không thấy tài khoản nhận tiền của bạn.')}</p>

      <form onSubmit={send} style={{ maxWidth: 560, marginTop: 16 }}>
        <div className="field-grid">
          <label className="form-field"><span className="cap">{t('Email người được mời')}</span>
            <input type="email" required value={email} placeholder={t('ban@vidu.vn')}
                   onChange={e => setEmail(e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Áp dụng cho')}</span>
            <select value={listingId} onChange={e => setListingId(e.target.value)}>
              <option value="">{t('Tất cả chỗ nghỉ của tôi')}</option>
              {listings.map(l => <option key={l.id} value={l.id}>{l.title}</option>)}
            </select>
          </label>
        </div>

        <p className="cap" style={{ margin: '14px 0 8px' }}>{t('Được làm gì')}</p>
        <div className="pill-row">
          {scopes.map(s => (
            <button type="button" key={s.key} className={`pill ${picked.includes(s.key) ? 'is-on' : ''}`}
                    onClick={() => toggle(s.key)}>{t(s.label)}</button>
          ))}
        </div>

        <button type="submit" className="btn btn-primary" style={{ marginTop: 18 }}
                disabled={busy || !picked.length}>{t('Gửi lời mời')}</button>
      </form>
    </section>
  );
}

function Granted({ rows, kinds, overcommitted, onDone }) {
  const [editing, setEditing] = useState(null);

  const revoke = async row => {
    if (!confirm(`${t('Thu hồi quyền của')} ${row.name ?? row.email}?`)) return;
    try {
      await api.revokeCoHost(row.id);
      toast(t('Đã thu hồi quyền đồng quản lý.'));
      onDone();
    } catch (err) { toast(err.message); }
  };

  return (
    <section>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Người đang đồng quản lý')}</h2>
      {overcommitted > 0 && (
        <p className="section-sub" style={{ color: 'var(--danger, #c13515)' }}>
          {t('Bạn đã chia tổng cộng {}% thu nhập, nhiều hơn số bạn nhận được. Người được thêm sau cùng sẽ nhận thiếu.')
            .replace('{}', number(overcommitted))}
        </p>
      )}

      {rows.length ? rows.map(r => (
        <div key={r.id}>
          <div className="team-row">
            <div style={{ minWidth: 0, flex: 1 }}>
              <b>{r.name ?? r.email}</b>
              <div className="team-sub">
                {r.listingTitle ?? t('Tất cả chỗ nghỉ')} · {scopeText(r.scopeLabel)} · {t('mời ngày')} {longDate(r.invitedAt)}
              </div>
              <div className="team-sub">
                {t('Chia thu nhập:')}{' '}
                <b>{payoutText(r.payoutKind, r.payoutPercent, r.payoutFixed)}</b>
                {r.payoutStatus !== 'none' && <> · {t(r.payoutStatusLabel)}</>}
                {r.paidToDate > 0 && <> · {t('đã trả')} {money(r.paidToDate)}</>}
              </div>
            </div>
            <span className={`badge ${r.status === 'active' ? 'confirmed' : 'pending'}`}>{t(r.statusLabel)}</span>
            {r.status === 'active' && (
              <button className="btn btn-outline btn-sm"
                      onClick={() => setEditing(editing === r.id ? null : r.id)}>
                {t('Chia thu nhập')}
              </button>
            )}
            <button className="btn btn-outline btn-sm" onClick={() => revoke(r)}>{t('Thu hồi')}</button>
          </div>

          {editing === r.id && (
            <PayoutForm row={r} kinds={kinds}
                        onDone={() => { setEditing(null); onDone(); }} />
          )}
        </div>
      )) : <p className="section-sub">{t('Chưa mời ai.')}</p>}
    </section>
  );
}

/**
 * docs/02 G8 — what the owner is offering, and on what.
 *
 * Only the box the chosen shape actually uses is shown. A percentage field
 * sitting next to "toàn bộ phí dọn dẹp" reads as though it does something, and
 * a number typed into it would be silently ignored.
 */
function PayoutForm({ row, kinds, onDone }) {
  const [kind, setKind] = useState(row.payoutKind ?? 'none');
  const [percent, setPercent] = useState(row.payoutPercent || 20);
  const [amount, setAmount] = useState(row.payoutFixed || 300000);
  const [busy, setBusy] = useState(false);

  const chosen = kinds.find(k => k.key === kind);

  const save = async e => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.setCoHostPayout(row.id, {
        kind,
        percent: chosen?.needsPercent ? Number(percent) : 0,
        amount: chosen?.needsAmount ? Number(amount) : 0
      });
      toast(kind === 'none'
        ? t('Đã dừng chia thu nhập.')
        : t('Đã gửi đề nghị. Khoản chia chỉ bắt đầu khi người kia xác nhận.'));
      onDone();
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  return (
    <form onSubmit={save} className="team-row" style={{ display: 'block', maxWidth: 560 }}>
      <p className="section-sub" style={{ marginTop: 0 }}>
        {t('Phần chia được tính trên thu nhập của bạn sau phí dịch vụ và thuế, rồi chuyển thẳng cho họ.')}
      </p>

      <label className="form-field"><span className="cap">{t('Cách chia')}</span>
        <select value={kind} onChange={e => setKind(e.target.value)}>
          <option value="none">{t('Không chia thu nhập')}</option>
          {kinds.map(k => <option key={k.key} value={k.key}>{t(k.label)}</option>)}
        </select>
      </label>

      {chosen?.needsPercent && (
        <label className="form-field" style={{ marginTop: 12 }}><span className="cap">{t('Phần trăm')}</span>
          <input type="number" min="0.01" max="100" step="0.01" value={percent}
                 onChange={e => setPercent(e.target.value)} /></label>
      )}

      {chosen?.needsAmount && (
        <label className="form-field" style={{ marginTop: 12 }}><span className="cap">{t('Số tiền mỗi đơn')}</span>
          <input type="number" min="1000" step="1000" value={amount}
                 onChange={e => setAmount(e.target.value)} /></label>
      )}

      <button type="submit" className="btn btn-primary btn-sm" style={{ marginTop: 16 }} disabled={busy}>
        {kind === 'none' ? t('Dừng chia thu nhập') : t('Gửi đề nghị')}
      </button>
    </form>
  );
}

/**
 * What this user has been paid as somebody else's co-host.
 *
 * docs/09 §3.5 taught this repo the other half of the lesson: money that is
 * recorded and then never shown to the person it concerns may as well not have
 * been recorded at all.
 */
function MyShares({ rows, total }) {
  if (!rows?.length) return null;

  return (
    <section>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Phần chia bạn nhận được')}</h2>
      <p className="section-sub">{t('Tổng đã nhận:')} <b>{money(total)}</b></p>

      {rows.map(r => (
        <div className="team-row" key={r.id}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <b>{r.listingTitle}</b>
            <div className="team-sub">
              {t('Đơn')} {r.bookingReference} · {t('nhận phòng')} {longDate(r.checkIn)}
              {' · '}{payoutText(r.kind, r.percent, r.fixed)}
            </div>
            {r.clawedBack > 0 && (
              <div className="team-sub">
                {t('Đã trừ lại do hoàn tiền:')} {money(r.clawedBack)}
              </div>
            )}
          </div>
          <b>{money(r.amount)}</b>
          <span className={`badge ${r.status === 'paid' ? 'confirmed' : 'pending'}`}>{t(r.statusLabel)}</span>
        </div>
      ))}
    </section>
  );
}
