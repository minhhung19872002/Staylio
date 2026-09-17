import { useEffect, useState } from 'react';
import { useStore } from '../lib/useStore.js';
import { pickDate, shiftCalendar, calendarAnchor, set, normaliseDates } from '../lib/store.js';
import { api } from '../lib/api.js';
import { isoOf, parseIso, todayIso, shortMoney, longDate, dateFormat } from '../lib/format.js';
import { t } from '../lib/i18n.js';

/**
 * Month and weekday names come from Intl in the reader's language rather than a
 * hard-coded Vietnamese list: a Korean guest reading "Tháng 8" over the calendar
 * is being told the language switch did not take. Weeks start on Monday here, so
 * the run is anchored to a known Monday (5 Jan 1970) and walked forward.
 */
const MONTH_NAME = m => dateFormat({ month: 'long' }).format(new Date(2026, m, 1));

const DOW_NAMES = () => {
  const fmt = dateFormat({ weekday: 'short' });
  return Array.from({ length: 7 }, (_, i) => fmt.format(new Date(1970, 0, 5 + i)));
};

/**
 * Range picker. The first click after a complete range starts a new one; the
 * second closes it. When a listing is open, every cell also carries its own
 * nightly rate (docs/01 TM-05) and sold nights are disabled.
 */
export function Calendar({ months = 2 }) {
  const state = useStore();
  const listingId = state.detail?.card.id;
  const [calendar, setCalendar] = useState(null);

  // The view follows its own state, not the chosen dates: paging must be able to
  // reach a check-out months away without moving the check-in to get there.
  const anchor = calendarAnchor();
  const panels = Array.from({ length: months }, (_, i) =>
    new Date(anchor.getFullYear(), anchor.getMonth() + i, 1, 12));

  // One fetch covers the whole visible span plus a month of slack, so paging
  // back and forth does not re-request on every arrow click.
  useEffect(() => {
    if (!listingId) { setCalendar(null); return undefined; }

    let cancelled = false;
    const from = isoOf(new Date(anchor.getFullYear(), anchor.getMonth() - 1, 1, 12));
    const to = isoOf(new Date(anchor.getFullYear(), anchor.getMonth() + months + 1, 1, 12));

    api.listingCalendar(listingId, { from, to })
      .then(data => { if (!cancelled) setCalendar(data); })
      .catch(() => { if (!cancelled) setCalendar(null); });

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [listingId, anchor.getFullYear(), anchor.getMonth(), months]);

  const byDate = new Map((calendar?.nights ?? []).map(n => [n.date, n]));

  return <>
    <div className="cal-wrap" data-months={months}>
      {panels.map((m, i) =>
        <Month key={`${m.getFullYear()}-${m.getMonth()}`} monthStart={m}
               isFirst={i === 0} isLast={i === panels.length - 1}
               state={state} nights={byDate} />)}
    </div>

    <Openings openings={calendar?.nextOpenings} nights={byDate} />
  </>;
}

function Month({ monthStart, isFirst, isLast, state, nights }) {
  const year = monthStart.getFullYear();
  const month = monthStart.getMonth();
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const lead = (new Date(year, month, 1).getDay() + 6) % 7;   // Monday-first
  const today = todayIso();
  const blocked = new Set(state.detail?.unavailableDates ?? []);

  const cells = [];
  for (let i = 0; i < lead; i++) cells.push(<span key={`lead${i}`} />);

  for (let d = 1; d <= daysInMonth; d++) {
    const iso = isoOf(new Date(year, month, d, 12));
    const night = nights.get(iso);
    // The calendar endpoint is authoritative when we have it; the detail page's
    // unavailable list is the fallback while it loads.
    const disabled = night ? !night.available : iso < today || blocked.has(iso);
    // Mid-pick the calendar shows the one day chosen and nothing else — not the
    // range from before, which is still what the search is running on.
    const picking = state.pickingFrom;
    const edge = picking !== null
      ? iso === picking
      : iso === state.checkIn || iso === state.checkOut;
    const between = picking === null && iso > state.checkIn && iso < state.checkOut;

    cells.push(
      <button key={iso} type="button" disabled={disabled}
              className={`cal-day ${edge ? 'is-edge' : between ? 'is-between' : ''} ${night ? 'has-rate' : ''}`}
              onClick={() => pickDate(iso)}
              title={night ? `${longDate(iso)} · ${shortMoney(night.rate)}` : undefined}
              aria-label={`${d} ${MONTH_NAME(month)} ${year}`}>
        <b>{d}</b>
        {night && !disabled && <i className="cal-rate">{shortMoney(night.rate)}</i>}
      </button>
    );
  }

  return (
    <div className="cal-month">
      {/* Both arrows on every month; the stylesheet decides which ones show.
          Side by side, "previous" belongs to the left month and "next" to the
          right one. Stacked on a phone they both belong to the first, and the
          old markup had no button there to reveal — the "next" arrow ended up
          floating beside the second month's name, halfway down the sheet.
          `visibility` rather than `display` so the name stays centred. */}
      <div className="cal-head">
        <button type="button" className="round-btn cal-prev" onClick={() => shiftCalendar(-1)}
                aria-label={t('Tháng trước')} tabIndex={isFirst ? 0 : -1}>‹</button>
        <b>{MONTH_NAME(month)} {year}</b>
        <button type="button" className="round-btn cal-next" onClick={() => shiftCalendar(1)}
                aria-label={t('Tháng sau')} tabIndex={isLast ? 0 : -1}>›</button>
      </div>
      <div className="cal-grid" role="grid">
        {DOW_NAMES().map((d, i) => <span className="cal-dow" key={i}>{d}</span>)}
        {cells}
      </div>
    </div>
  );
}

/**
 * docs/01 TĐ-09 — when the dates the guest wanted are gone, offer the next runs
 * of free nights instead of leaving them to hunt through the grid.
 */
function Openings({ openings, nights }) {
  const state = useStore();
  if (!openings?.length) return null;

  const wanted = [];
  for (const d = parseIso(state.checkIn); isoOf(d) < state.checkOut; d.setDate(d.getDate() + 1)) {
    wanted.push(isoOf(d));
  }
  const chosenIsFree = wanted.every(iso => nights.get(iso)?.available !== false);
  if (chosenIsFree) return null;

  const take = opening => {
    const wantedNights = Math.max(1, wanted.length);
    const from = parseIso(opening.from);
    const to = new Date(from);
    to.setDate(to.getDate() + Math.min(wantedNights, opening.nights));

    // Taking a suggested opening is "show me that stay", so the view follows it
    // — unlike picking a date, where the months must stay where they are.
    set({ checkIn: isoOf(from), checkOut: isoOf(to), pickingFrom: null, calendarMonth: null });
    normaliseDates();
  };

  return (
    <div className="book-alert" style={{ marginTop: 16 }}>
      <b>Khoảng ngày bạn chọn đã có người đặt</b>
      <span>Ba khoảng trống gần nhất:</span>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 10 }}>
        {openings.map(o => (
          <button className="pill" key={o.from} onClick={() => take(o)}>
            {longDate(o.from)} – {longDate(o.to)} · {o.nights} đêm
          </button>
        ))}
      </div>
    </div>
  );
}
