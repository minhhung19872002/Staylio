import { useEffect, useState } from 'react';
import { api } from '../../lib/api.js';
import { toast } from '../../lib/store.js';
import { money, longDate, dateTime } from '../../lib/format.js';
import { t } from '../../lib/i18n.js';

/* docs/08 §5.2 — each block names what it leaves alone. */
const RESTRICTIONS = [
  ['NoNewBookings', 'Không được đặt đơn mới'],
  ['NoNewListings', 'Không được đăng tin mới'],
  ['ListingsHiddenFromSearch', 'Tin đăng bị ẩn khỏi tìm kiếm'],
  ['NoReviews', 'Không được viết đánh giá'],
  ['NoNewConversations', 'Không được nhắn tin cho người mới'],
  ['PayoutsHeld', 'Khoản chuyển tiền bị giữ lại']
];

const SEVERE = [
  'Đe doạ hoặc bạo lực',
  'Nội dung liên quan tới trẻ em',
  'Gian lận thanh toán có bằng chứng',
  'Giả mạo giấy tờ',
  'Chiếm đoạt tài khoản người khác',
  'Lừa đảo có tổ chức'
];

const BAN_GROUNDS = [
  'Gian lận tiền',
  'Giả mạo danh tính',
  'Đe doạ an toàn người khác',
  'Tái phạm nhiều lần sau khi đã cảnh cáo và tạm khoá'
];

/**
 * docs/08 §4 — find somebody, then everything the console may show about them.
 *
 * The buttons offered come from the server's own answer to "what may this admin
 * do", so a role never sees an action it would be refused.
 */
export function UserAdminPanel() {
  const [q, setQ] = useState('');
  const [rows, setRows] = useState([]);
  const [open, setOpen] = useState(null);

  const search = async term => {
    try { setRows(await api.adminSearchUsers(term)); }
    catch (err) { toast(err.message); }
  };

  // Mở trang là thấy người dùng ngay: bảng trống bắt admin phải đoán một từ khoá
  // mới có dòng đầu tiên, mà thứ họ muốn xem thường chỉ là "ai đang có trên sàn".
  useEffect(() => { search(''); }, []);

  const load = async id => {
    try { setOpen(await api.adminUser(id)); }
    catch (err) { toast(err.message); }
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Quản trị người dùng')}</h2>
      <p className="section-sub">
        {t('Tìm theo email, số điện thoại, tên, mã đơn, mã tin đăng hoặc mã giao dịch. Để trống ô tìm là danh sách tài khoản mới nhất.')}
      </p>

      <div style={{ display: 'flex', gap: 10, marginTop: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <label className="form-field" style={{ margin: 0, maxWidth: 340, flex: 1 }}>
          <span className="cap">{t('Từ khoá')}</span>
          <input value={q} onChange={e => setQ(e.target.value)}
                 onKeyDown={e => e.key === 'Enter' && search(q)}
                 placeholder="guest@staylio.vn" />
        </label>
        <button className="btn btn-outline btn-sm" onClick={() => search(q)}>{t('Tìm')}</button>
      </div>

      {!!rows.length && (
        <div className="table-wrap" style={{ marginTop: 16 }}>
          <table className="admin-table">
            <thead>
              <tr><th>{t('Người dùng')}</th><th>{t('Vai trò')}</th><th>{t('Trạng thái')}</th><th>{t('Tham gia')}</th><th /></tr>
            </thead>
            <tbody>
              {rows.map(u => (
                <tr key={u.id}>
                  <td><b>{u.fullName}</b><span>{u.email}{u.phone ? ` · ${u.phone}` : ''}</span></td>
                  <td>{u.role}</td>
                  <td>
                    <span className={`badge ${u.statusLabel === 'Bình thường' ? 'confirmed' : 'cancelled'}`}>
                      {u.statusLabel}
                    </span>
                  </td>
                  <td>{longDate(u.joinedAt)}</td>
                  <td><button className="link-btn" onClick={() => load(u.id)}>{t('Mở hồ sơ')}</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {!!open && (
        <AdminModal title={`${t('Hồ sơ')} · ${open.fullName}`} onClose={() => setOpen(null)}>
          <UserProfilePanel d={open} reload={() => load(open.id)} />
        </AdminModal>
      )}
    </section>
  );
}

/**
 * Cùng lớp giao diện với các hộp thoại khác của sàn, nhưng tự đóng bằng state của
 * trang quản trị chứ không đi qua overlay chung — trang này không nằm trong luồng
 * overlay của ứng dụng.
 */
function AdminModal({ title, onClose, children }) {
  useEffect(() => {
    const onKey = e => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = '';
    };
  }, [onClose]);

  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal wide" role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-head">
          <button className="modal-close" onClick={onClose} aria-label={t('Đóng')}>✕</button>
          <h2>{title}</h2>
          <span style={{ width: 32 }} />
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  );
}

function UserProfilePanel({ d, reload }) {
  const [preview, setPreview] = useState(null);
  // docs/08 §6 — "hoàn theo chính sách hoặc hoàn 100% tuỳ mức độ vi phạm —
  // admin chọn". One choice, read by the preview and by the lock itself, so
  // what is confirmed is what happens.
  const [fullRefund, setFullRefund] = useState(true);
  const [busy, setBusy] = useState(false);
  const [identity, setIdentity] = useState(null);
  const [thread, setThread] = useState(null);
  const [resetLink, setResetLink] = useState(null);

  const may = action => d.allowed.includes(action);

  const run = async (fn, done) => {
    setBusy(true);
    try { await fn(); toast(done); await reload(); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const ask = (label) => {
    const reason = prompt(`${label}\n\nLý do (bắt buộc, ít nhất 10 ký tự):`);
    if (!reason || reason.trim().length < 10) {
      if (reason !== null) toast('Cần ghi lý do ít nhất 10 ký tự.');
      return null;
    }
    return reason.trim();
  };

  const warn = () => {
    const reason = ask('Gửi cảnh cáo');
    if (!reason) return;
    const policy = prompt('Dẫn chiếu chính sách (ví dụ: docs/03 §9)') ?? '';
    run(() => api.adminSanction(d.id, { level: 'Warning', reason, policy }), 'Đã gửi cảnh cáo.');
  };

  const restrict = () => {
    const kind = prompt(`Hạn chế nào?\n\n${RESTRICTIONS.map((r, i) => `${i + 1}. ${r[1]}`).join('\n')}`);
    const picked = RESTRICTIONS[Number(kind) - 1];
    if (!picked) return;
    const reason = ask(`Hạn chế: ${picked[1]}`);
    if (!reason) return;
    const liftedWhen = prompt('Điều kiện để được gỡ hạn chế') ?? '';
    run(() => api.adminSanction(d.id, {
      level: 'Restriction', restriction: picked[0], reason, liftedWhen
    }), 'Đã áp hạn chế.');
  };

  // docs/08 §6 — the cost is shown before anything happens, never after.
  const showPreview = async (full = fullRefund) => {
    try { setPreview(await api.adminLockPreview(d.id, full)); }
    catch (err) { toast(err.message); }
  };

  const suspend = () => {
    const reason = ask('Tạm khoá tài khoản');
    if (!reason) return;
    const severe = prompt(
      `Có phải vi phạm nghiêm trọng theo §5.6 không? Để trống nếu không.\n\n${
        SEVERE.map((g, i) => `${i + 1}. ${g}`).join('\n')}`);
    const days = prompt('Tạm khoá bao nhiêu ngày? Để trống = tới khi xử lý xong.');
    run(() => api.adminSanction(d.id, {
      level: 'Suspension', reason,
      severeGround: SEVERE[Number(severe) - 1] ?? null,
      days: Number(days) || null,
      refundInFull: fullRefund
    }), 'Đã tạm khoá tài khoản.');
  };

  const ban = () => {
    const ground = prompt(
      `Khoá vĩnh viễn chỉ dùng cho:\n\n${BAN_GROUNDS.map((g, i) => `${i + 1}. ${g}`).join('\n')}`);
    const picked = BAN_GROUNDS[Number(ground) - 1];
    if (!picked) return;
    const reason = ask(`Khoá vĩnh viễn: ${picked}`);
    if (!reason) return;
    run(() => api.adminSanction(d.id, { level: 'Ban', reason, severeGround: picked, refundInFull: fullRefund }),
        'Đã khoá vĩnh viễn.');
  };

  const restore = () => {
    const reason = ask('Khôi phục tài khoản');
    if (!reason) return;
    run(() => api.adminRestore(d.id, { reason }), 'Đã khôi phục tài khoản.');
  };

  const viewIdentity = async () => {
    const reason = ask('Xem ảnh giấy tờ tuỳ thân');
    if (!reason) return;
    try { setIdentity(await api.adminIdentity(d.id, { reason })); }
    catch (err) { toast(err.message); }
  };

  /*
   * Máy chủ chưa cấu hình email thì thư đặt lại mật khẩu không tới đâu cả, nên
   * liên kết được trả về cho chính admin để chuyển tay cho người dùng. Admin
   * không biết mật khẩu mới — người dùng tự đặt ở đầu bên kia liên kết.
   */
  const resetPassword = async () => {
    const reason = ask('Đặt lại mật khẩu và huỷ mọi phiên đăng nhập');
    if (!reason) return;
    setBusy(true);
    try {
      const res = await api.adminForcePasswordReset(d.id, { reason });
      setResetLink(res.resetLink ?? null);
      toast('Đã huỷ mọi phiên và tạo liên kết đặt lại mật khẩu.');
      await reload();
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  // docs/08 §2 — tin nhắn của đúng một đơn, có lý do và nhật ký riêng.
  const viewThread = async bookingId => {
    const reason = ask('Xem tin nhắn của đơn này');
    if (!reason) return;
    try { setThread(await api.adminBookingThread(bookingId, { reason })); }
    catch (err) { toast(err.message); }
  };

  const editProfile = () => {
    const reason = ask('Sửa thông tin hồ sơ người dùng');
    if (!reason) return;
    const fullName = prompt('Họ tên (để trống = giữ nguyên)', d.fullName) ?? '';
    const phone = prompt('Số điện thoại (để trống = xoá)', d.phone ?? '') ?? '';
    run(() => api.adminEditProfile(d.id, {
      reason,
      fullName: fullName.trim() || null,
      phone: phone.trim()
    }), 'Đã sửa hồ sơ người dùng.');
  };

  // docs/08 §7 — chỉ mở được từ một hồ sơ hỗ trợ đang mở của chính người này.
  const impersonate = () => {
    const ticket = prompt('Mã hồ sơ hỗ trợ đang mở (bắt buộc theo §7.1):');
    if (!ticket || !Number(ticket)) return;
    const reason = ask('Đăng nhập thay mặt người dùng');
    if (!reason) return;
    run(() => api.adminImpersonate({ userId: d.id, ticketId: Number(ticket), reason }),
        'Đã vào chế độ thay mặt. Dải cảnh báo hiện ở đầu trang.');
  };

  return (
    <div>
      <div>
        <b style={{ fontSize: 17 }}>{d.fullName}</b>
        <div style={{ fontSize: 13, color: 'var(--ink-muted)' }}>
          {d.email}{d.phone ? ` · ${d.phone}` : ''} · {t('tham gia')} {longDate(d.joinedAt)}
        </div>
      </div>

      <div style={{ display: 'flex', gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
        <span className={`badge ${d.isLocked ? 'cancelled' : 'confirmed'}`}>{d.statusLabel}</span>
        {d.identityVerified && <span className="badge confirmed">{t('Đã xác minh danh tính')}</span>}
        {d.emailConfirmed && <span className="badge confirmed">{t('Email đã xác thực')}</span>}
        {d.phoneConfirmed && <span className="badge confirmed">{t('SĐT đã xác thực')}</span>}
        {d.isHost && <span className="badge">{d.isSuperhost ? t('Siêu chủ nhà') : t('Chủ nhà')}</span>}
        {d.isGuestFavoriteHost && <span className="badge">{t('Khách yêu thích')}</span>}
        {!!d.coHostOf.length && <span className="badge">Co-host ({d.coHostOf.length})</span>}
        {!!d.suspendedUntil && <span className="badge pending">{t('Tới')} {dateTime(d.suspendedUntil)}</span>}
      </div>

      <div className="stat-grid" style={{ marginTop: 16 }}>
        <Cell label={t('Đơn đặt')} value={String(d.bookings)} note={`${d.cancellations} ${t('huỷ')} · ${d.cancellationRate}%`} />
        <Cell label={t('Đánh giá')} value={`${d.reviewsWritten} / ${d.reviewsReceived}`}
              note={`${t('đã viết / đã nhận ·')} ${d.reportsAgainst} ${t('báo cáo bị nhận')}`} />
        <Cell label={t('Số dư')} value={money(d.balance)}
              note={d.giftCards ? `${d.cards.length} ${t('thẻ')} · ${d.giftCards} ${t('thẻ quà')} (${money(d.giftCardRemaining)})`
                                : (d.cards.join(' · ') || t('Chưa lưu thẻ nào'))} />
        <Cell label={t('Tranh chấp')} value={String(d.totalDisputes)}
              note={d.openDisputes ? `${d.openDisputes} ${t('đang mở')}` : t('không có hồ sơ nào đang mở')} />
        <Cell label={t('Tài khoản nhận tiền')}
              value={d.payoutAccountLast4 ? `•••• ${d.payoutAccountLast4}` : '—'}
              note={d.payoutBankName ?? t('Chỉ vai Tài chính xem được')} />
        <Cell label={t('Hoạt động gần nhất')}
              value={d.lastSeenAt ? dateTime(d.lastSeenAt).slice(0, 10) : '—'}
              note={`${d.listings.length} ${t('tin đăng')}`} />
      </div>

      {!!d.coHostOf.length && (
        <p className="field-note" style={{ marginTop: 12 }}>
          <b>Co-host cho:</b> {d.coHostOf.join(' · ')}
        </p>
      )}

      {/* docs/08 §4 and QT-U-03 — same signal that catches fraud. */}
      {!!d.relatedAccounts.length && (
        <div style={{ marginTop: 18 }}>
          <span className="cap">{t('Tài khoản có liên quan')}</span>
          <div style={{ display: 'grid', gap: 6, marginTop: 8 }}>
            {d.relatedAccounts.map(r => (
              <div key={r.id} style={{ fontSize: 13 }}>
                <b>{r.fullName}</b> · {r.email} · <span style={{ color: 'var(--ink-muted)' }}>{r.statusLabel}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* docs/08 §4 — tin đăng và tình trạng từng cái */}
      {!!d.listings.length && (
        <div style={{ marginTop: 18 }}>
          <span className="cap">{t('Tin đăng')}</span>
          <div style={{ display: 'grid', gap: 5, marginTop: 8, fontSize: 13 }}>
            {d.listings.map(l => (
              <div key={l.id}>
                <b>{l.title}</b> · {l.city} ·{' '}
                <span className={`badge ${l.published ? 'confirmed' : 'pending'}`}>
                  {l.published ? t('Đang hiển thị') : t('Đã ẩn')}
                </span>
                {' '}· {l.rating.toFixed(2)}★ ({l.reviewCount})
              </div>
            ))}
          </div>
        </div>
      )}

      {/* docs/08 §4 — lịch sử đơn đặt hai chiều, kèm cửa đọc tin nhắn của đúng một đơn */}
      {!!d.recentBookings.length && (
        <div style={{ marginTop: 18 }}>
          <span className="cap">{t('Đơn đặt gần đây')}</span>
          <div className="table-wrap" style={{ marginTop: 8 }}>
            <table className="admin-table">
              <thead><tr><th>{t('Mã')}</th><th>{t('Vai')}</th><th>{t('Chỗ nghỉ')}</th><th>{t('Ngày')}</th><th>{t('Trạng thái')}</th><th /></tr></thead>
              <tbody>
                {d.recentBookings.map(b => (
                  <tr key={b.id}>
                    <td><b>{b.reference}</b><span>{money(b.total)}</span></td>
                    <td>{b.side}</td>
                    <td>{b.listing}</td>
                    <td>{longDate(b.checkIn)} – {longDate(b.checkOut)}</td>
                    <td>{b.statusLabel}</td>
                    <td>
                      {may('ViewBookingThread') && (
                        <button className="link-btn" onClick={() => viewThread(b.id)}>{t('Tin nhắn')}</button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {!!thread && (
        <div style={{ marginTop: 16 }}>
          <span className="cap">{t('Tin nhắn đơn')} {thread.reference}</span>
          <p className="field-note" style={{ margin: '4px 0 8px' }}>
            {thread.guestName} ↔ {thread.hostName} · {thread.listingTitle}. {t('Lượt đọc này đã được ghi nhật ký riêng.')}
          </p>
          <div style={{ display: 'grid', gap: 6, maxHeight: 260, overflowY: 'auto', fontSize: 13 }}>
            {thread.messages.length === 0 && <span className="field-note">{t('Đơn này chưa có tin nhắn nào.')}</span>}
            {thread.messages.map((m, i) => (
              <div key={i}>
                <b>{m.isSystem ? 'Staylio' : m.sender}</b>{' '}
                <span style={{ color: 'var(--ink-muted)' }}>{dateTime(m.sentAt)}</span>
                <div>{m.body}</div>
              </div>
            ))}
          </div>
          <button className="link-btn" onClick={() => setThread(null)}>{t('Đóng tin nhắn')}</button>
        </div>
      )}

      {/* docs/08 §4 — thiết bị và địa chỉ mạng đăng nhập gần đây */}
      {!!d.sessions.length && (
        <div style={{ marginTop: 18 }}>
          <span className="cap">{t('Thiết bị và địa chỉ mạng gần đây')}</span>
          <div style={{ display: 'grid', gap: 4, marginTop: 8, fontSize: 12.5 }}>
            {d.sessions.map((s, i) => (
              <div key={i} style={{ color: s.active ? 'inherit' : 'var(--ink-muted)' }}>
                {dateTime(s.at)} · {s.ip ?? t('IP không rõ')} · {(s.device || '—').slice(0, 80)}
                {s.active ? '' : ` · ${t('đã kết thúc')}`}
              </div>
            ))}
          </div>
        </div>
      )}

      {!!d.sanctions.length && (
        <div style={{ marginTop: 18 }}>
          <span className="cap">{t('Hồ sơ vi phạm')}</span>
          <div className="table-wrap" style={{ marginTop: 8 }}>
            <table className="admin-table">
              <thead><tr><th>{t('Mức')}</th><th>{t('Lý do')}</th><th>{t('Người quyết định')}</th><th>{t('Khi nào')}</th></tr></thead>
              <tbody>
                {d.sanctions.map(s => (
                  <tr key={s.id}>
                    <td>
                      <b>{s.levelLabel}</b>
                      {!!s.restrictionLabel && <span>{s.restrictionLabel}</span>}
                      {s.overturnedOnAppeal && <span>{t('Đã gỡ theo khiếu nại')}</span>}
                      {!!s.liftedAt && !s.overturnedOnAppeal && <span>{t('Đã gỡ')}</span>}
                    </td>
                    <td>{s.reason}<span>{s.policy}</span></td>
                    <td>{s.decidedBy}</td>
                    <td>{dateTime(s.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* docs/08 §6 and QT-U-07 — what a lock would cost, before it happens. */}
      {!!preview && (
        <div className={`book-alert ${preview.guestsStaying ? '' : 'is-error'}`} style={{ marginTop: 16 }}>
          <label className="check-row">
            <input type="checkbox" checked={fullRefund}
                   onChange={e => { setFullRefund(e.target.checked); showPreview(e.target.checked); }} />
            <span>{t('Hoàn 100% cho các chuyến người này đã đặt (bỏ chọn để hoàn theo chính sách huỷ)')}</span>
          </label>
          <b>{t(preview.warning)}</b>
          {!!preview.openDisputeNotice && <span>{preview.openDisputeNotice}</span>}
          {!!preview.safetyNotice && <span>{preview.safetyNotice}</span>}
          {/* docs/08 §6 — the row about promotional balance, which the admin
              pressing this button will be asked about first. */}
          {!!preview.balanceNotice && <span>{t(preview.balanceNotice)}</span>}
          <div style={{ marginTop: 8, display: 'grid', gap: 4, fontSize: 12.5 }}>
            {preview.lines.map(l => (
              <div key={l.bookingId}>
                <b>{l.reference}</b> · {l.counterparty} · {l.note}
                {l.money > 0 ? ` · ${money(l.money)}` : ''}
              </div>
            ))}
          </div>
        </div>
      )}

      {!!resetLink && (
        <div className="book-alert" style={{ marginTop: 16 }}>
          <b>{t('Liên kết đặt lại mật khẩu — gửi cho người dùng')}</b>
          <span>{t('Sống 2 giờ, chỉ dùng được một lần. Mọi phiên đăng nhập của họ đã bị huỷ.')}</span>
          <code style={{ display: 'block', wordBreak: 'break-all', marginTop: 6, fontSize: 12 }}>
            {window.location.origin}{resetLink}
          </code>
          <button className="btn btn-outline btn-sm" style={{ marginTop: 8 }}
                  onClick={() => {
                    navigator.clipboard?.writeText(`${window.location.origin}${resetLink}`);
                    toast('Đã chép liên kết.');
                  }}>{t('Chép liên kết')}</button>
        </div>
      )}

      {!!identity && (
        <div style={{ marginTop: 16 }}>
          <span className="cap">{identity.documentLabel} •••• {identity.documentLast4}</span>
          <p style={{ fontSize: 12.5, color: 'var(--ink-muted)', margin: '6px 0' }}>
            {t('Ảnh có đóng dấu mờ:')} <b>{identity.watermark}</b>. {t('Lượt xem này đã được ghi nhật ký riêng.')}
          </p>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
            {[identity.frontImageUrl, identity.backImageUrl, identity.selfieImageUrl]
              .filter(Boolean)
              .map(url => (
                <div key={url} className="id-shot">
                  <img src={url} alt="" />
                  <span>{identity.watermark}</span>
                </div>
              ))}
          </div>
        </div>
      )}

      <div style={{ display: 'flex', gap: 10, marginTop: 18, flexWrap: 'wrap' }}>
        <button className="btn btn-outline btn-sm" onClick={() => showPreview()}>{t('Xem trước hậu quả khoá')}</button>
        {may('Warn') && <button className="btn btn-outline btn-sm" disabled={busy} onClick={warn}>{t('Cảnh cáo')}</button>}
        {may('Restrict') && <button className="btn btn-outline btn-sm" disabled={busy} onClick={restrict}>{t('Hạn chế')}</button>}
        {may('Suspend') && !d.isLocked &&
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={suspend}>{t('Tạm khoá')}</button>}
        {may('Ban') && !d.isLocked &&
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={ban}>{t('Khoá vĩnh viễn')}</button>}
        {may('Restore') && d.isLocked &&
          <button className="btn btn-primary btn-sm" disabled={busy} onClick={restore}>{t('Khôi phục')}</button>}
        {may('ViewIdentityDocuments') &&
          <button className="btn btn-outline btn-sm" onClick={viewIdentity}>{t('Xem giấy tờ')}</button>}
        {may('EditProfile') &&
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={editProfile}>{t('Sửa hồ sơ')}</button>}
        {may('Impersonate') && !d.isLocked &&
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={impersonate}>{t('Thay mặt người dùng')}</button>}
        {may('ForcePasswordReset') && (
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={resetPassword}>
            {t('Đặt lại mật khẩu')}
          </button>
        )}
        {may('ForceIdentityRecheck') && (
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={() => {
            const reason = ask('Buộc xác minh lại danh tính');
            if (reason) run(() => api.adminForceIdentityRecheck(d.id, { reason }), 'Đã yêu cầu xác minh lại.');
          }}>{t('Xác minh lại danh tính')}</button>
        )}
      </div>
    </div>
  );
}

function Cell({ label, value, note }) {
  return (
    <div className="stat">
      <span className="cap">{label}</span>
      <b style={{ display: 'block', fontSize: 20, margin: '6px 0 2px' }}>{value}</b>
      <span style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>{note}</span>
    </div>
  );
}

/** docs/08 §8 — the appeal queue, and who may read each one. */
export function AppealsPanel() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    try { setRows(await api.adminAppeals()); }
    catch { /* an admin without the role simply does not see this */ }
  };
  useEffect(() => { load(); }, []);

  if (!rows.length) return null;

  const decide = async (a, result) => {
    const outcome = prompt(
      'Kết quả phải nêu lý do, ít nhất 40 ký tự — trả lời cụt lủn không phải là trả lời:');
    if (!outcome || outcome.trim().length < 40) {
      if (outcome !== null) toast('Kết quả quá ngắn.');
      return;
    }
    setBusy(true);
    try {
      setRows(await api.adminDecideAppeal(a.id, { result, outcome }));
      toast('Đã trả lời khiếu nại.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Khiếu nại quyết định')}</h2>
      <p className="section-sub">
        {t('Mỗi quyết định được khiếu nại một lần. Người xét lại phải khác người ra quyết định.')}
      </p>

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="admin-table">
          <thead>
            <tr><th>{t('Người khiếu nại')}</th><th>{t('Quyết định')}</th><th>{t('Lý lẽ')}</th><th>{t('Hạn trả lời')}</th><th /></tr>
          </thead>
          <tbody>
            {rows.map(a => (
              <tr key={a.id}>
                <td><b>{a.userName}</b></td>
                <td>{a.sanctionLevel}<span>{a.sanctionReason}</span></td>
                <td style={{ maxWidth: 320 }}>{a.argument}</td>
                <td>
                  {longDate(a.dueBy)}
                  {a.overdue && <span>{t('Đã quá hạn')}</span>}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {a.status !== 'Open'
                    ? <span className="badge">{a.statusLabel}</span>
                    : a.mayReview
                      ? <>
                          <button className="link-btn" disabled={busy}
                                  onClick={() => decide(a, 'Upheld')}>{t('Giữ nguyên')}</button>
                          <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                                  onClick={() => decide(a, 'Reduced')}>{t('Giảm mức')}</button>
                          <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                                  onClick={() => decide(a, 'Overturned')}>{t('Gỡ bỏ')}</button>
                        </>
                      : <span style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>
                          {t('Bạn đã ra quyết định này')}
                        </span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** docs/08 §10 — the scoreboard, the alarms, and what is waiting on a second name. */
export function OversightPanel() {
  const [d, setD] = useState(null);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    try { setD(await api.adminOversight()); }
    catch { setD(null); }
  };
  useEffect(() => { load(); }, []);

  if (!d) return null;

  const decide = async (m, approve) => {
    const reason = prompt(approve ? 'Ghi chú khi duyệt' : 'Lý do từ chối') ?? '';
    if (reason.trim().length < 10) { toast('Cần ghi lý do ít nhất 10 ký tự.'); return; }
    setBusy(true);
    try { await api.adminDecideApproval(m.id, { approve, reason }); await load(); toast('Đã xử lý.'); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const act = async (fn, done) => {
    setBusy(true);
    try { await fn(); await load(); toast(done); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const withReason = label => {
    const reason = prompt(`${label}\n\nLý do (bắt buộc, ít nhất 10 ký tự):`);
    if (!reason || reason.trim().length < 10) {
      if (reason !== null) toast('Cần ghi lý do ít nhất 10 ký tự.');
      return null;
    }
    return reason.trim();
  };

  // docs/08 §5.6 — Quản trị tối cao ký xác nhận đã đọc hồ sơ nghiêm trọng.
  const signOffSevere = id => {
    const reason = withReason('Xác nhận đã xem lại hồ sơ nghiêm trọng này');
    if (reason) act(() => api.adminSevereReview(id, { reason }), 'Đã ghi nhận xem lại.');
  };

  // docs/08 §3 — rà soát quyền định kỳ, và cấp/thu hồi quyền admin.
  const markReviewed = id => {
    const reason = withReason('Đánh dấu đã rà soát quyền của quản trị viên này');
    if (reason) act(() => api.adminMarkReviewed(id, { reason }), 'Đã ghi nhận rà soát.');
  };

  const grantScopes = adminUserId => {
    const scopes = prompt(
      'Nhập các vai, cách nhau bằng dấu phẩy (support, moderation, finance, arbitration, super).\n' +
      'Để trống = thu hồi toàn bộ quyền và huỷ mọi phiên đăng nhập.');
    if (scopes === null) return;
    const reason = withReason('Cấp hoặc thu hồi quyền quản trị');
    if (!reason) return;
    act(() => api.adminGrantScopes({
      userId: adminUserId,
      scopes: scopes.split(',').map(s => s.trim()).filter(Boolean),
      reason
    }), 'Đã cập nhật quyền quản trị.');
  };

  const mergeUsers = () => {
    const from = prompt('ID tài khoản trùng (sẽ được đóng lại):');
    if (!from || !Number(from)) return;
    const into = prompt('ID tài khoản giữ lại:');
    if (!into || !Number(into)) return;
    const reason = withReason(`Hợp nhất #${from} vào #${into}`);
    if (!reason) return;
    act(() => api.adminMergeUsers({ fromUserId: Number(from), intoUserId: Number(into), reason }),
        'Đã hợp nhất hai tài khoản.');
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Giám sát quản trị viên')}</h2>
      <p className="section-sub">
        {t('Khoản tiền từ')} {money(d.twoPersonThreshold)} {t('trở lên cần hai người duyệt.')}
        {' '}{t('Mỗi tháng')} {d.randomReviewPercent}{t('% quyết định được đọc lại.')}
      </p>

      {!!d.flags.length && (
        <div className="book-alert is-error" style={{ marginTop: 14 }}>
          <b>{d.flags.length} {t('cảnh báo cần xem')}</b>
          {d.flags.map((f, i) => <span key={i}>{f.adminName}: {f.label} — {f.detail}</span>)}
        </div>
      )}

      {/* docs/08 §5.6 — hồ sơ nghiêm trọng phải được Tối cao xem lại trong 24 giờ */}
      {!!d.severeQueue?.length && (
        <div style={{ marginTop: 16 }}>
          <span className="cap">{t('Hồ sơ nghiêm trọng chờ Quản trị tối cao xem lại')}</span>
          <div className="table-wrap" style={{ marginTop: 8 }}>
            <table className="admin-table">
              <thead><tr><th>{t('Người dùng')}</th><th>{t('Mức')}</th><th>{t('Lý do')}</th><th>{t('Hạn xem lại')}</th><th /></tr></thead>
              <tbody>
                {d.severeQueue.map(s => (
                  <tr key={s.sanctionId}>
                    <td><b>{s.userName}</b><span>{s.decidedBy} · {dateTime(s.decidedAt)}</span></td>
                    <td>{s.level}</td>
                    <td>{s.reason}<span>{s.ground}</span></td>
                    <td className={s.overdue ? 'is-error' : ''}>
                      {dateTime(s.dueBy)}{s.overdue ? ` · ${t('QUÁ HẠN')}` : ''}
                    </td>
                    <td>
                      <button className="link-btn" disabled={busy}
                              onClick={() => signOffSevere(s.sanctionId)}>{t('Đã xem lại')}</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {!!d.pendingApprovals.length && (
        <div style={{ marginTop: 16 }}>
          <span className="cap">{t('Chờ người thứ hai duyệt')}</span>
          <div className="table-wrap" style={{ marginTop: 8 }}>
            <table className="admin-table">
              <thead><tr><th>{t('Việc')}</th><th>{t('Số tiền')}</th><th>{t('Người đề nghị')}</th><th /></tr></thead>
              <tbody>
                {d.pendingApprovals.map(m => (
                  <tr key={m.id}>
                    <td><b>{m.target}</b><span>{m.reason}</span></td>
                    <td>{money(m.amount)}</td>
                    <td>{m.requestedBy}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      {m.mayApprove
                        ? <>
                            <button className="link-btn" disabled={busy}
                                    onClick={() => decide(m, true)}>{t('Duyệt')}</button>
                            <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                                    onClick={() => decide(m, false)}>{t('Từ chối')}</button>
                          </>
                        : <span style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>
                            {t('Bạn là người đề nghị')}
                          </span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 18 }}>
        <span className="cap">{t('Quản trị viên và quyền')}</span>
        <button className="btn btn-outline btn-sm" disabled={busy} onClick={mergeUsers}>
          {t('Hợp nhất tài khoản trùng')}
        </button>
      </div>

      <div className="table-wrap" style={{ marginTop: 8 }}>
        <table className="admin-table">
          <thead>
            <tr><th>{t('Quản trị viên')}</th><th>{t('Quyền')}</th><th>{t('Đã xem')}</th><th>{t('Quyết định')}</th><th>{t('Bị khiếu nại')}</th><th>{t('Rà soát')}</th><th /></tr>
          </thead>
          <tbody>
            {d.admins.map(a => (
              <tr key={a.adminUserId}>
                <td>
                  <b>{a.name}</b>
                  {!a.twoFactorEnabled && <span>{t('Chưa bật bảo mật 2 lớp')}</span>}
                </td>
                <td>{a.scopes}</td>
                <td>{a.profilesViewed}</td>
                <td>{a.decisions}</td>
                <td>
                  {a.appealsUpheld}/{a.appealsAgainst}
                  {a.looksUnreliable && <span>{t('Tỉ lệ')} {a.overturnRatePercent}{t('% bất thường')}</span>}
                </td>
                <td>
                  {a.accessReviewDue ? t('Tới hạn') : t('Đã rà soát')}
                  {a.scopeLooksUnused && <span>{t('Chưa dùng quyền quá 90 ngày')}</span>}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <button className="link-btn" disabled={busy}
                          onClick={() => grantScopes(a.adminUserId)}>{t('Sửa quyền')}</button>
                  <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                          onClick={() => markReviewed(a.adminUserId)}>{t('Đã rà soát')}</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {!!d.randomSample.length && (
        <div style={{ marginTop: 18 }}>
          <span className="cap">{t('Mẫu rà soát ngẫu nhiên tháng này')}</span>
          <div style={{ display: 'grid', gap: 6, marginTop: 8, fontSize: 13 }}>
            {d.randomSample.map(x => (
              <div key={x.id}>
                <b>{x.level}</b> · {x.userName} · {x.decidedBy} · {longDate(x.at)}
                <span style={{ color: 'var(--ink-muted)' }}> — {x.reason}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

/** docs/08 §9 — export and erasure requests, with what is standing in the way. */
export function DataRequestsPanel() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    try { setRows(await api.adminDataRequests()); }
    catch { /* not this admin's job */ }
  };
  useEffect(() => { load(); }, []);

  if (!rows.length) return null;

  const erase = async r => {
    const reason = prompt('Lý do xử lý yêu cầu xoá (bắt buộc, ít nhất 10 ký tự):');
    if (!reason || reason.trim().length < 10) return;
    setBusy(true);
    try { setRows(await api.adminErase(r.id, { reason })); toast('Đã ẩn danh tài khoản.'); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  // docs/08 §9 — cấp đường dẫn tải có hạn rồi báo cho người yêu cầu.
  const fulfilExport = async r => {
    const reason = prompt('Lý do cấp liên kết tải dữ liệu (bắt buộc, ít nhất 10 ký tự):');
    if (!reason || reason.trim().length < 10) return;
    setBusy(true);
    try { setRows(await api.adminFulfilExport(r.id, { reason })); toast('Đã gửi liên kết tải cho người dùng.'); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Yêu cầu dữ liệu cá nhân')}</h2>
      <p className="section-sub">
        {t('Xoá tài khoản là ẩn danh hoá: tên, ảnh, email, giấy tờ bị xoá; đơn đặt và sổ ghi tiền giữ nguyên.')}
      </p>

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="admin-table">
          <thead><tr><th>{t('Người dùng')}</th><th>{t('Yêu cầu')}</th><th>{t('Hạn')}</th><th>{t('Trạng thái')}</th><th /></tr></thead>
          <tbody>
            {rows.map(r => (
              <tr key={r.id}>
                <td><b>{r.userName}</b><span>{r.email}</span></td>
                <td>{r.kindLabel}</td>
                <td>{longDate(r.dueBy)}{r.overdue && <span>{t('Đã quá hạn')}</span>}</td>
                <td>
                  {r.statusLabel}
                  {!!r.blockers.length && <span>{t('Vướng:')} {r.blockers.join(', ')}</span>}
                </td>
                <td>
                  {r.kind === 'Erase' && r.status === 'Open' && r.mayErase && (
                    <button className="link-btn" disabled={busy} onClick={() => erase(r)}>{t('Xoá')}</button>
                  )}
                  {r.kind === 'Export' && r.status === 'Open' && (
                    <button className="link-btn" disabled={busy} onClick={() => fulfilExport(r)}>
                      {t('Cấp liên kết tải')}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
