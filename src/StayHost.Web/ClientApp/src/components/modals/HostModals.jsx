import { useEffect, useState } from 'react';
import { useStore } from '../../lib/useStore.js';
import { set, loadHostCalendar, loadHosting, confirmHostCancel, toast } from '../../lib/store.js';
import { api } from '../../lib/api.js';
import { money, shortMoney, longDate, isoOf } from '../../lib/format.js';
import { Modal } from './Modal.jsx';
import { t } from '../../lib/i18n.js';

/* --------------------------------------------------------- host calendar */

const HOST_MONTHS = ['Tháng 1', 'Tháng 2', 'Tháng 3', 'Tháng 4', 'Tháng 5', 'Tháng 6',
                     'Tháng 7', 'Tháng 8', 'Tháng 9', 'Tháng 10', 'Tháng 11', 'Tháng 12'];

export function HostCalendarModal() {
  const state = useStore();
  const cal = state.hostCalendar;
  if (!cal) return null;

  const reload = () => loadHostCalendar(cal.listingId);

  const addBlock = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    try {
      await api.addBlock({
        listingId: cal.listingId,
        from: f.from.value, to: f.to.value,
        note: f.note.value.trim() || null
      });
      await reload();
      toast('Đã khoá lịch.');
      f.reset();
    } catch (err) { toast(err.message); }
  };

  const addRule = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    try {
      await api.addPriceRule({
        listingId: cal.listingId,
        name: f.name.value.trim(),
        from: f.from.value, to: f.to.value,
        nightlyRate: Number(f.rate.value)
      });
      await reload();
      toast('Đã thêm quy tắc giá.');
      f.reset();
    } catch (err) { toast(err.message); }
  };

  return (
    <Modal title={t('Lịch & giá')}>
      <HostMonths cal={cal} offset={state.hostMonthOffset} />

      <BulkDays listingId={cal.listingId} basePrice={cal.basePrice ?? 0} onDone={reload} />
      <CalendarRules listingId={cal.listingId} />

      <section className="modal-section">
        <h3>{t('Khoá lịch')}</h3>
        <span className="hint">{t('Chặn khoảng ngày bạn không muốn nhận khách (bảo trì, gia đình dùng…).')}</span>
        <form onSubmit={addBlock} style={{ marginTop: 14 }}>
          <div className="field-grid">
            <label className="form-field"><span className="cap">{t('Từ ngày')}</span><input type="date" name="from" required /></label>
            <label className="form-field"><span className="cap">{t('Đến ngày')}</span><input type="date" name="to" required /></label>
          </div>
          <label className="form-field"><span className="cap">{t('Ghi chú')}</span><input name="note" placeholder={t('Bảo trì hồ bơi')} /></label>
          <button type="submit" className="btn btn-dark btn-block">{t('Khoá lịch')}</button>
        </form>
      </section>

      <section className="modal-section">
        <h3>{t('Giá theo mùa')}</h3>
        <span className="hint">
          {t('Giá cơ bản hiện tại:')} <b>{money(cal.basePrice ?? 0)}</b> / {t('đêm')}.
          {' '}{t('Quy tắc mùa sẽ thay thế giá này trong khoảng ngày đã chọn.')}
        </span>
        <form onSubmit={addRule} style={{ marginTop: 14 }}>
          <div className="field-grid">
            <label className="form-field" style={{ gridColumn: '1/-1' }}><span className="cap">{t('Tên đợt')}</span>
              <input name="name" placeholder={t('Tết Nguyên đán')} required /></label>
            <label className="form-field"><span className="cap">{t('Từ ngày')}</span><input type="date" name="from" required /></label>
            <label className="form-field"><span className="cap">{t('Đến ngày')}</span><input type="date" name="to" required /></label>
            <label className="form-field"><span className="cap">{t('Giá mỗi đêm (₫)')}</span>
              <input type="number" name="rate" min={50000} step={10000}
                     defaultValue={Math.round((cal.basePrice ?? 800000) * 1.5)} required /></label>
          </div>
          <button type="submit" className="btn btn-outline btn-block">{t('Thêm quy tắc giá')}</button>
        </form>

        {!!cal.priceRules?.length && (
          <div style={{ display: 'grid', gap: 8, marginTop: 14 }}>
            {cal.priceRules.map(r => (
              <div className="cal-row" key={r.id}>
                <span className="badge pending">{r.name}</span>
                <div style={{ flex: 1, minWidth: 0, fontSize: 13.5 }}>
                  {r.from} → {r.to} · <b>{money(r.nightlyRate)}</b>/{t('đêm')}
                </div>
                <button className="text-btn" onClick={async () => {
                  try { await api.removePriceRule(r.id); await reload(); toast('Đã bỏ quy tắc giá.'); }
                  catch (err) { toast(err.message); }
                }}>{t('Bỏ')}</button>
              </div>
            ))}
          </div>
        )}
      </section>

      {cal.bookings?.length ? (
        <div style={{ marginTop: 24 }}>
          <b style={{ fontSize: 13 }}>{t('Lịch khách đã đặt')}</b>
          <div style={{ display: 'grid', gap: 8, marginTop: 10 }}>
            {cal.bookings.map(b => (
              <div className="cal-row" key={b.reference}>
                <span className="badge confirmed">{t('Đã đặt')}</span>
                <div style={{ flex: 1, minWidth: 0, fontSize: 13.5 }}>
                  {b.checkIn} → {b.checkOut}
                  <span style={{ color: 'var(--ink-muted)' }}> · {b.guests} {t('khách')} · {b.reference}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : <p style={{ marginTop: 24, fontSize: 13, color: 'var(--ink-muted)' }}>{t('Chưa có lượt đặt nào sắp tới.')}</p>}

      {!!cal.blocks?.length && (
        <div style={{ marginTop: 22 }}>
          <b style={{ fontSize: 13 }}>{t('Bạn đang khoá')}</b>
          <div style={{ display: 'grid', gap: 8, marginTop: 10 }}>
            {cal.blocks.map(b => (
              <div className="cal-row" key={b.id}>
                <span className="badge cancelled">{t('Khoá')}</span>
                <div style={{ flex: 1, minWidth: 0, fontSize: 13.5 }}>
                  {b.from} → {b.to}
                  {b.note && <span style={{ color: 'var(--ink-muted)' }}> · {b.note}</span>}
                </div>
                <button className="text-btn" onClick={async () => {
                  try { await api.removeBlock(b.id); await reload(); toast('Đã bỏ khoá lịch.'); }
                  catch (err) { toast(err.message); }
                }}>{t('Bỏ')}</button>
              </div>
            ))}
          </div>
        </div>
      )}
    </Modal>
  );
}

/**
 * docs/01 QL-05 — one action over a run of days: set the rate, block or unblock
 * them, or change the minimum stay that may start on them.
 */
function BulkDays({ listingId, basePrice, onDone }) {
  const [busy, setBusy] = useState(false);

  const submit = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    const rate = Number(f.rate.value);
    const minNights = Number(f.minNights.value);
    const blocked = f.blocked.value;

    setBusy(true);
    try {
      await api.editDays(listingId, {
        from: f.from.value,
        to: f.to.value,
        nightlyRate: rate > 0 ? rate : null,
        minNights: minNights > 0 ? minNights : null,
        blocked: blocked === 'keep' ? null : blocked === 'block',
        label: f.label.value.trim() || null
      });
      await onDone();
      toast('Đã áp dụng cho khoảng ngày đã chọn.');
      f.reset();
    } catch (err) {
      toast(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="modal-section">
      <h3>{t('Sửa nhiều ngày cùng lúc')}</h3>
      <span className="hint">
        {t('Giá theo ngày được ưu tiên cao nhất, đè lên giá mùa và giá cuối tuần.')}
        {' '}{t('Giá cơ bản hiện tại:')} <b>{money(basePrice)}</b>/{t('đêm')}.
      </span>
      <form onSubmit={submit} style={{ marginTop: 14 }}>
        <div className="field-grid">
          <label className="form-field"><span className="cap">{t('Từ ngày')}</span>
            <input type="date" name="from" required /></label>
          <label className="form-field"><span className="cap">{t('Đến ngày')}</span>
            <input type="date" name="to" required /></label>
          <label className="form-field"><span className="cap">{t('Giá mỗi đêm (₫)')}</span>
            <input type="number" name="rate" min={0} step={10000} placeholder={t('Để trống nếu không đổi')} /></label>
          <label className="form-field"><span className="cap">{t('Số đêm tối thiểu')}</span>
            <input type="number" name="minNights" min={0} max={90} placeholder={t('Để trống nếu không đổi')} /></label>
          <label className="form-field"><span className="cap">{t('Trạng thái')}</span>
            <select name="blocked" defaultValue="keep">
              <option value="keep">{t('Giữ nguyên')}</option>
              <option value="block">{t('Khoá các ngày này')}</option>
              <option value="open">{t('Mở lại các ngày này')}</option>
            </select></label>
          <label className="form-field"><span className="cap">{t('Ghi chú')}</span>
            <input name="label" placeholder={t('Cao điểm hè')} /></label>
        </div>
        <button type="submit" className="btn btn-dark btn-block" disabled={busy}>
          {busy ? t('Đang áp dụng…') : t('Áp dụng cho khoảng ngày')}
        </button>
      </form>
    </section>
  );
}

const WEEKDAYS = [
  [1, 'T2'], [2, 'T3'], [3, 'T4'], [4, 'T5'], [5, 'T6'], [6, 'T7'], [0, 'CN']
];

/**
 * docs/01 QL-06 and QL-07 — the rules the nine availability checks read: night
 * limits, notice, turnover, calendar horizon, and closed weekdays.
 */
function CalendarRules({ listingId }) {
  const [rules, setRules] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.hostRules(listingId).then(setRules).catch(() => setRules(null)); }, [listingId]);
  if (!rules) return null;

  const field = (key, value) => setRules(r => ({ ...r, [key]: value }));
  const num = (key, value) => field(key, Number(value) || 0);

  const toggleDay = (key, day) => setRules(r => ({ ...r, [key]: r[key] ^ (1 << day) }));
  const isOn = (mask, day) => (mask & (1 << day)) !== 0;

  const save = async () => {
    setBusy(true);
    try {
      setRules(await api.saveHostRules(listingId, rules));
      toast('Đã lưu quy tắc lịch.');
    } catch (err) {
      toast(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="modal-section">
      <h3>{t('Quy tắc lịch')}</h3>
      <span className="hint">{t('Những quy tắc này quyết định khách có đặt được hay không.')}</span>

      <div className="field-grid" style={{ marginTop: 14 }}>
        <label className="form-field"><span className="cap">{t('Số đêm tối thiểu')}</span>
          <input type="number" min={1} max={365} value={rules.minNights}
                 onChange={e => num('minNights', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Số đêm tối đa (0 = không giới hạn)')}</span>
          <input type="number" min={0} max={365} value={rules.maxNights}
                 onChange={e => num('maxNights', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Báo trước (giờ)')}</span>
          <input type="number" min={0} max={720} value={rules.advanceNoticeHours}
                 onChange={e => num('advanceNoticeHours', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Giờ cắt đặt trong ngày')}</span>
          <input type="number" min={0} max={23} value={rules.sameDayCutoffHour ?? ''}
                 placeholder={t('Không giới hạn')}
                 onChange={e => field('sameDayCutoffHour', e.target.value === '' ? null : Number(e.target.value))} /></label>
        <label className="form-field"><span className="cap">{t('Mở lịch trước (tháng, 0 = vô hạn)')}</span>
          <input type="number" min={0} max={24} value={rules.calendarVisibilityMonths}
                 onChange={e => num('calendarVisibilityMonths', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Ngày dọn dẹp giữa hai khách')}</span>
          <input type="number" min={0} max={14} value={rules.turnoverDays}
                 onChange={e => num('turnoverDays', e.target.value)} /></label>
      </div>

      <div style={{ marginTop: 16 }}>
        <span className="cap">{t('Không nhận phòng vào')}</span>
        <div className="pill-row" style={{ marginTop: 8 }}>
          {WEEKDAYS.map(([day, label]) => (
            <button type="button" key={`in-${day}`}
                    className={`pill ${isOn(rules.blockedCheckInDays, day) ? 'is-on' : ''}`}
                    onClick={() => toggleDay('blockedCheckInDays', day)}>{t(label)}</button>
          ))}
        </div>
      </div>

      <div style={{ marginTop: 14 }}>
        <span className="cap">{t('Không trả phòng vào')}</span>
        <div className="pill-row" style={{ marginTop: 8 }}>
          {WEEKDAYS.map(([day, label]) => (
            <button type="button" key={`out-${day}`}
                    className={`pill ${isOn(rules.blockedCheckOutDays, day) ? 'is-on' : ''}`}
                    onClick={() => toggleDay('blockedCheckOutDays', day)}>{t(label)}</button>
          ))}
        </div>
      </div>

      <button className="btn btn-outline btn-block" style={{ marginTop: 18 }} disabled={busy} onClick={save}>
        {busy ? t('Đang lưu…') : t('Lưu quy tắc lịch')}
      </button>
    </section>
  );
}

/** Two-month availability grid: bookings, blocks and seasonal rates at a glance. */
function HostMonths({ cal, offset }) {
  const anchor = new Date();
  anchor.setDate(1);
  anchor.setMonth(anchor.getMonth() + offset);

  const booked = new Set();
  for (const b of cal.bookings ?? []) expandRange(b.checkIn, b.checkOut, false).forEach(d => booked.add(d));

  const blocked = new Set();
  for (const b of cal.blocks ?? []) expandRange(b.from, b.to, true).forEach(d => blocked.add(d));

  const seasonal = new Map();
  for (const r of cal.priceRules ?? []) {
    expandRange(r.from, r.to, true).forEach(d => seasonal.set(d, r.nightlyRate));
  }

  return (
    <section className="modal-section">
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, marginBottom: 12 }}>
        <h3 style={{ margin: 0 }}>{t('Lịch trống')}</h3>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="round-btn" aria-label={t('Tháng trước')}
                  onClick={() => set({ hostMonthOffset: Math.max(0, offset - 1) })}>‹</button>
          <button className="round-btn" aria-label={t('Tháng sau')}
                  onClick={() => set({ hostMonthOffset: offset + 1 })}>›</button>
        </div>
      </div>
      <div className="host-cal">
        {[0, 1].map(i => {
          const m = new Date(anchor.getFullYear(), anchor.getMonth() + i, 1);
          return <MonthPanel key={`${m.getFullYear()}-${m.getMonth()}`} monthStart={m}
                             booked={booked} blocked={blocked} seasonal={seasonal} basePrice={cal.basePrice ?? 0} />;
        })}
      </div>
      <div className="host-cal-legend">
        <span><i className="sw booked" /> {t('Đã có khách')}</span>
        <span><i className="sw blocked" /> {t('Bạn khoá')}</span>
        <span><i className="sw seasonal" /> {t('Giá mùa')}</span>
      </div>
    </section>
  );
}

function MonthPanel({ monthStart, booked, blocked, seasonal, basePrice }) {
  const year = monthStart.getFullYear();
  const month = monthStart.getMonth();
  const days = new Date(year, month + 1, 0).getDate();
  const lead = (new Date(year, month, 1).getDay() + 6) % 7;

  const cells = Array.from({ length: lead }, (_, i) => <span key={`lead${i}`} />);

  for (let d = 1; d <= days; d++) {
    const iso = `${year}-${String(month + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
    const cls = booked.has(iso) ? 'is-booked' : blocked.has(iso) ? 'is-blocked' : seasonal.has(iso) ? 'is-seasonal' : '';
    const rate = seasonal.get(iso) ?? basePrice;

    cells.push(
      <span className={`host-day ${cls}`} key={iso} title={`${iso} · ${money(rate)}`}>
        <b>{d}</b><i>{shortMoney(rate)}</i>
      </span>
    );
  }

  return (
    <div className="host-cal-month">
      <div className="host-cal-head">{t(HOST_MONTHS[month])} {year}</div>
      <div className="host-cal-grid">
        {['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'].map(d => <span className="host-dow" key={d}>{t(d)}</span>)}
        {cells}
      </div>
    </div>
  );
}

function expandRange(fromIso, toIso, inclusive) {
  const out = [];
  const to = new Date(`${toIso}T12:00:00`);
  for (const d = new Date(`${fromIso}T12:00:00`); inclusive ? d <= to : d < to; d.setDate(d.getDate() + 1)) {
    out.push(isoOf(d));
  }
  return out;
}

/* ------------------------------------------------------- host reviews guest */

export function GuestReviewModal() {
  const state = useStore();
  const b = state.guestReviewBooking;
  // docs/03 §7 — "sạch sẽ, giao tiếp, tuân thủ nội quy"; the server averages them.
  const [draft, setDraft] = useState({ cleanliness: 5, communication: 5, houseRules: 5, wouldHostAgain: true });
  if (!b) return null;

  const submit = async e => {
    e.preventDefault();
    try {
      const rating = Math.round((draft.cleanliness + draft.communication + draft.houseRules) / 3 * 100) / 100;
      await api.reviewGuest(b.id, { ...draft, rating, text: e.currentTarget.text.value.trim() });
      set({ overlay: null, guestReviewBooking: null });
      toast('Đã gửi đánh giá khách.');
      await loadHosting();
    } catch (err) { toast(err.message); }
  };

  return (
    <Modal title={t('Đánh giá khách')} size="narrow">
      <div style={{ paddingBottom: 16, borderBottom: '1px solid var(--divider)' }}>
        <div style={{ fontSize: 15, fontWeight: 700 }}>{b.guestName}</div>
        <div style={{ fontSize: 13, color: 'var(--ink-muted)' }}>
          {b.listingTitle} · {longDate(b.checkIn)} – {longDate(b.checkOut)}
        </div>
      </div>

      <form onSubmit={submit}>
        <div style={{ padding: '18px 0', borderBottom: '1px solid var(--divider)' }}>
          <b style={{ fontSize: 15 }}>{t('Khách này thế nào?')}</b>
          {[['cleanliness', 'Sạch sẽ'], ['communication', 'Giao tiếp'], ['houseRules', 'Tuân thủ nội quy']].map(([key, label]) => (
            <div key={key} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 10 }}>
              <span style={{ fontSize: 14 }}>{t(label)}</span>
              <div className="star-row">
                {[1, 2, 3, 4, 5].map(n => (
                  <button type="button" key={n} aria-label={`${t(label)} ${n} ${t('sao')}`}
                          className={`star ${n <= draft[key] ? 'is-on' : ''}`}
                          onClick={() => setDraft(d => ({ ...d, [key]: n }))}>★</button>
                ))}
              </div>
            </div>
          ))}
        </div>

        <div className="count-row">
          <div className="tx"><b>{t('Bạn sẽ đón lại khách này?')}</b></div>
          <button type="button" className={`pill ${draft.wouldHostAgain ? 'is-on' : ''}`}
                  onClick={() => setDraft(d => ({ ...d, wouldHostAgain: !d.wouldHostAgain }))}>
            {draft.wouldHostAgain ? t('Có') : t('Không')}
          </button>
        </div>

        <label className="form-field" style={{ marginTop: 18 }}>
          <span className="cap">{t('Nhận xét')}</span>
          <textarea name="text" rows={5} required minLength={10}
                    placeholder={t('Khách giữ gìn nhà cửa, trao đổi rõ ràng…')}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>

        <button type="submit" className="btn btn-primary btn-block">{t('Gửi đánh giá')}</button>
      </form>
    </Modal>
  );
}

/**
 * docs/01 QL-13 — everything that follows a host cancellation, before it
 * happens. The consequences come from the server, computed with the same rules
 * that will run a second later, so this is a preview and not a description of
 * one.
 */
export function HostCancelModal() {
  const state = useStore();
  const p = state.hostCancel;
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  if (!p) return null;

  const go = async () => {
    setBusy(true);
    await confirmHostCancel(p.id, reason.trim() || null);
    setBusy(false);
  };

  return (
    <Modal title={t('Huỷ đơn của khách')}>
      <div className="book-alert">
        <b>{t('Việc này không thể hoàn tác')}</b>
        <span>{p.guestName} {t('đã đặt')} {p.nights} {t('đêm')} {t('từ')} {longDate(p.checkIn)} · {t('mã')} {p.reference}</span>
      </div>

      <ul className="consequences">
        {p.consequences.map(c => <li key={c}>{c}</li>)}
      </ul>

      <div className="kv-grid" style={{ marginTop: 16 }}>
        <div className="kv"><span className="kv-label">{t('Khách được hoàn')}</span><b>{money(p.guestRefund)}</b></div>
        <div className="kv"><span className="kv-label">{t('Bạn mất khoản nhận')}</span><b>{money(p.hostPayoutLost)}</b></div>
      </div>

      <label className="form-field" style={{ marginTop: 16 }}>
        <span className="cap">{t('Lý do huỷ')} <span style={{ fontWeight: 400 }}>{t('(khách sẽ đọc được)')}</span></span>
        <input value={reason} onChange={e => setReason(e.target.value)}
               placeholder={t('Ví dụ: nhà đang sửa chữa đột xuất')} />
      </label>

      <button className="btn btn-block" style={{ marginTop: 6 }} disabled={busy} onClick={go}>
        {busy ? t('Đang huỷ…') : t('Tôi hiểu hậu quả, vẫn huỷ đơn')}
      </button>
      <button className="btn btn-primary btn-block" style={{ marginTop: 8 }}
              onClick={() => set({ overlay: null, hostCancel: null })}>{t('Giữ đơn lại')}</button>
    </Modal>
  );
}
