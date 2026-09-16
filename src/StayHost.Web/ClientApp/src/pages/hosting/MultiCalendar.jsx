import { useEffect, useState } from 'react';
import { api } from '../../lib/api.js';
import { set, loadHostCalendar, toast } from '../../lib/store.js';
import { shortMoney, isoOf, parseIso, longDate } from '../../lib/format.js';
import { t } from '../../lib/i18n.js';

const DOW = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'];

/**
 * docs/01 QL-04 — one row per listing, one column per day, scrolling sideways.
 * A host with several places sees the whole month without opening each one.
 */
export function MultiCalendar() {
  const [data, setData] = useState(null);
  const [from, setFrom] = useState(() => isoOf(new Date()));
  const [error, setError] = useState(null);

  useEffect(() => {
    api.multiCalendar({ from, days: 45 }).then(setData).catch(e => setError(t(e.message)));
  }, [from]);

  const shift = days => {
    const d = parseIso(from);
    d.setDate(d.getDate() + days);
    setFrom(isoOf(d));
  };

  if (error) return <div className="empty-state" style={{ marginTop: 24 }}><h3>{error}</h3></div>;
  if (!data) return <div className="stat skeleton" style={{ height: 240, border: 0, marginTop: 24 }} />;

  if (!data.rows.length) {
    return (
      <div className="empty-state" style={{ marginTop: 24 }}>
        <h3>{t('Chưa có chỗ nghỉ nào để xem lịch')}</h3>
      </div>
    );
  }

  return (
    <div style={{ marginTop: 24 }}>
      <div className="page-head" style={{ marginBottom: 12 }}>
        <p className="section-sub" style={{ margin: 0 }}>
          {data.rows.length} {t('chỗ nghỉ')} · {data.days} {t('ngày từ')} {from}
        </p>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="round-btn" onClick={() => shift(-30)} aria-label={t('Lùi 30 ngày')}>‹</button>
          <button className="btn btn-outline btn-sm" onClick={() => setFrom(isoOf(new Date()))}>{t('Hôm nay')}</button>
          <button className="round-btn" onClick={() => shift(30)} aria-label={t('Tiến 30 ngày')}>›</button>
        </div>
      </div>

      <div className="multi-cal">
        <div className="multi-cal-scroll">
          <div className="multi-cal-head">
            <div className="multi-cal-name" />
            {data.rows[0].days.map(d => {
              const date = parseIso(d.date);
              return (
                <div className="multi-cal-day" key={d.date}
                     data-weekend={date.getDay() === 0 || date.getDay() === 6}>
                  <b>{date.getDate()}</b>
                  <i>{DOW[date.getDay()]}</i>
                </div>
              );
            })}
          </div>

          {data.rows.map(row => (
            <div className="multi-cal-row" key={row.listingId}>
              <button className="multi-cal-name" onClick={async () => {
                await loadHostCalendar(row.listingId);
                set({ overlay: 'host-block', hostMonthOffset: 0 });
              }}>
                <b>{row.title}</b>
                <span>{row.isPublished ? t('Đang hiển thị') : t('Bản nháp')}</span>
              </button>

              {row.days.map(d => (
                <div className={`multi-cal-cell is-${d.state}`} key={d.date}
                     title={`${d.date} · ${d.state === 'booked' ? d.bookingReference : d.state === 'blocked' ? t('đã khoá') : t('còn trống')}`}
                     onClick={() => d.state === 'booked' && toast(`${t('Đơn')} ${d.bookingReference}`)}>
                  {d.state === 'open' ? shortMoney(d.rate) : d.state === 'booked' ? '●' : '×'}
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>

      <div className="host-cal-legend" style={{ marginTop: 12 }}>
        <span><i className="sw booked" /> {t('Đã có khách')}</span>
        <span><i className="sw blocked" /> {t('Bạn khoá')}</span>
        <span><i className="sw seasonal" /> {t('Còn trống (hiện giá)')}</span>
      </div>

      <CalendarSync rows={data.rows} />
    </div>
  );
}

/**
 * docs/01 QL-10 — the calendars this listing keeps somewhere else. Anything
 * they bring in becomes a block, never a booking, and only this feed's blocks
 * are ever replaced when it syncs again.
 */
function CalendarSync({ rows }) {
  const [listingId, setListingId] = useState(rows[0].listingId);
  const [board, setBoard] = useState(null);
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setBoard(null);
    api.calendarFeeds(listingId).then(setBoard).catch(e => toast(e.message));
  }, [listingId]);

  const run = async work => {
    setBusy(true);
    try { setBoard(await work()); } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  const add = async e => {
    e.preventDefault();
    await run(async () => {
      const next = await api.addCalendarFeed(listingId, { url: url.trim(), label: label.trim() || null });
      setUrl(''); setLabel('');
      return next;
    });
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Đồng bộ lịch với nền tảng khác')}</h2>
      <p className="section-sub">{t('Lịch nhập về chỉ khoá ngày, không tạo đơn và không sinh tiền.')}</p>

      <label className="form-field" style={{ maxWidth: 420, marginTop: 14 }}>
        <span className="cap">{t('Chỗ nghỉ')}</span>
        <select value={listingId} onChange={e => setListingId(Number(e.target.value))}>
          {rows.map(r => <option key={r.listingId} value={r.listingId}>{r.title}</option>)}
        </select>
      </label>

      {!board ? <div className="stat skeleton" style={{ height: 120, border: 0, marginTop: 16 }} /> : <>
        <div className="feed-export">
          <span className="cap">{t('Địa chỉ để nền tảng khác đọc lịch của bạn')}</span>
          <div className="feed-url">
            <code>{board.exportUrl}</code>
            <button className="btn btn-outline btn-sm" onClick={() => {
              navigator.clipboard?.writeText(board.exportUrl);
              toast(t('Đã chép địa chỉ lịch.'));
            }}>{t('Chép')}</button>
          </div>
        </div>

        {board.feeds.map(f => (
          <div className="team-row" key={f.id}>
            <div style={{ minWidth: 0, flex: 1 }}>
              <b>{f.label}</b>
              <div className="team-sub">
                {f.lastError
                  ? t(f.lastError)
                  : `${f.eventCount} ${t('khoảng ngày')} · ${t('lần cuối')} ${f.lastSyncedAt ? longDate(f.lastSyncedAt) : t('chưa chạy')}`}
              </div>
              {/* docs/01 QL-11 — a clash with a confirmed Staylio booking. */}
              {f.overlapWarning && <div className="notice notice-warn" style={{ marginTop: 6 }}>{t(f.overlapWarning)}</div>}
            </div>
            <span className={`badge ${f.lastError ? 'cancelled' : 'confirmed'}`}>
              {f.lastError ? t('Lỗi') : t('Đang chạy')}
            </span>
            <button className="btn btn-outline btn-sm" disabled={busy}
                    onClick={() => run(() => api.syncCalendarFeed(listingId, f.id))}>{t('Đồng bộ ngay')}</button>
            <button className="btn btn-outline btn-sm" disabled={busy}
                    onClick={() => confirm(`${t('Bỏ lịch')} "${f.label}" ${t('và mở lại những ngày nó khoá?')}`)
                      && run(() => api.removeCalendarFeed(listingId, f.id))}>{t('Bỏ')}</button>
          </div>
        ))}

        <form onSubmit={add} className="field-grid" style={{ maxWidth: 620, marginTop: 16 }}>
          <label className="form-field" style={{ gridColumn: '1/-1' }}>
            <span className="cap">{t('Địa chỉ lịch (.ics)')}</span>
            <input type="url" required value={url} placeholder="https://..."
                   onChange={e => setUrl(e.target.value)} />
          </label>
          <label className="form-field"><span className="cap">{t('Tên gọi')}</span>
            <input value={label} placeholder={t('Nền tảng khác')} onChange={e => setLabel(e.target.value)} /></label>
          <div style={{ display: 'flex', alignItems: 'end' }}>
            <button type="submit" className="btn btn-primary btn-sm" disabled={busy}>{t('Nối lịch')}</button>
          </div>
        </form>
      </>}
    </section>
  );
}
