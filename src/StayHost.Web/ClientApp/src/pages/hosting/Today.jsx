import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api.js';
import { set, respondBooking, toast } from '../../lib/store.js';
import { longDate } from '../../lib/format.js';
import { t } from '../../lib/i18n.js';

/**
 * docs/01 QL-01 — the board a host opens first: who is arriving, who is in the
 * house, who is leaving, and what still needs an answer.
 */
export function Today() {
  const [board, setBoard] = useState(null);
  const [error, setError] = useState(null);

  const load = () => api.hostToday().then(setBoard).catch(e => setError(t(e.message)));
  useEffect(() => { load(); }, []);

  if (error) return <div className="empty-state" style={{ marginTop: 24 }}><h3>{error}</h3></div>;

  if (!board) {
    return (
      <div className="stat-grid" style={{ marginTop: 24 }}>
        {Array.from({ length: 4 }, (_, i) => <div className="stat skeleton" key={i} style={{ height: 110, border: 0 }} />)}
      </div>
    );
  }

  const groups = [
    ['Cần trả lời', board.awaitingAnswer, 'Yêu cầu đặt tự hết hạn sau 24 giờ.'],
    ['Khách sắp đến', board.arriving, 'Trong 7 ngày tới.'],
    ['Khách đang ở', board.inHouse, 'Đang lưu trú tại chỗ nghỉ của bạn.'],
    ['Khách sắp đi', board.leaving, 'Nhớ sắp lịch dọn dẹp.']
  ];

  const empty = groups.every(([, items]) => !items.length);

  return (
    <div style={{ marginTop: 24 }}>
      {empty && (
        <div className="empty-state">
          <h3>{t('Hôm nay không có việc gì cần làm')}</h3>
          <p>{t('Khi có khách đến, đang ở hoặc sắp đi, họ sẽ hiện ở đây.')}</p>
          {/*
            * This is the tab a host lands on, and an empty one reads as an empty
            * account: a host came here looking for the listing they had just
            * published and concluded it was gone. It says where the listings are.
            */}
          <button className="btn btn-outline btn-sm" style={{ marginTop: 16 }}
                  onClick={() => set({ hostingTab: 'listings' })}>
            {t('Xem chỗ nghỉ của tôi')}
          </button>
        </div>
      )}

      {groups.filter(([, items]) => items.length).map(([title, items, hint]) => (
        <section key={title} style={{ marginBottom: 34 }}>
          <h2 className="section-title" style={{ fontSize: 20 }}>{t(title)} ({items.length})</h2>
          <p className="section-sub">{t(hint)}</p>
          {items.map(item => <TodayRow key={`${title}-${item.bookingId}`} item={item} onDone={load} />)}
        </section>
      ))}
    </div>
  );
}

function TodayRow({ item, onDone }) {
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);

  const answer = async decision => {
    setBusy(true);
    await respondBooking(item.bookingId, decision);
    setBusy(false);
    onDone();
  };

  return (
    <article className="host-booking">
      <div style={{ minWidth: 0 }}>
        <h3>{item.listingTitle}</h3>
        <div className="meta">{item.guestName} · {t('mã')} {item.reference}</div>
        <div className="meta">
          {longDate(item.checkIn)} → {longDate(item.checkOut)} · {item.nights} {t('đêm')} · {item.guests} {t('khách')}
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
          <span className={`badge ${item.statusBadge}`}>{t(item.statusLabel, 'status')}</span>
          <span className="badge pending">{t(item.what)}</span>
        </div>
      </div>
      <div className="host-booking-actions">
        {item.statusLabel === 'Chờ chủ nhà duyệt' && <>
          <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => answer('confirm')}>{t('Xác nhận')}</button>
          <button className="btn btn-outline btn-sm" disabled={busy} onClick={() => answer('decline')}>{t('Từ chối')}</button>
        </>}
        <button className="btn btn-outline btn-sm" onClick={() => navigate('/messages')}>{t('Nhắn khách')}</button>
        <button className="btn btn-outline btn-sm" onClick={() => {
          navigator.clipboard?.writeText(item.reference).then(
            () => toast(t('Đã sao chép mã đặt chỗ.')),
            () => toast(item.reference));
        }}>{t('Sao chép mã')}</button>
      </div>
    </article>
  );
}
