import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useStore } from '../../lib/useStore.js';
import {
  set, state as store, activeFilterCount, resetFilters, totalGuests, guestLabel, loadSuggestions,
  toggleAmenity, toggleHostLanguage, bumpCount, bumpGuest, applyDatePreset, clearDates, setStayShape,
  applyCurrency, applyLanguage, closeOverlay, settleDates, toast,
  shareViaDevice, copyShareLink, saveCurrentSearch, requireAuth
} from '../../lib/store.js';
import { api } from '../../lib/api.js';
import { applySearch, go } from '../../lib/nav.js';
import { money, longDate, nightsBetween, dateRangeLabel } from '../../lib/format.js';
import { TimeZonePicker } from '../TimeZonePicker.jsx';
import { recentSearches, clearSearchHistory } from '../../lib/history.js';
import { t } from '../../lib/i18n.js';
import { Icon, AmenityIcon, CATEGORY_ICON } from '../Icon.jsx';
import { Calendar } from '../Calendar.jsx';
import { Modal, CountRow } from './Modal.jsx';

const SORTS = [
  ['reco', 'Đề xuất cho bạn'], ['low', 'Giá thấp đến cao'], ['high', 'Giá cao đến thấp'],
  ['rating', 'Đánh giá cao nhất'], ['reviews', 'Nhiều đánh giá nhất'], ['distance', 'Gần trung tâm nhất']
];

const RATINGS = [[0, 'Bất kỳ'], [4.5, 'Tuyệt hảo 4,5+'], [4, 'Rất tốt 4+'], [3.5, 'Tốt 3,5+']];
const CENTRE_KM = [[0, 'Bất kỳ'], [1, 'Dưới 1 km'], [3, 'Dưới 3 km'], [5, 'Dưới 5 km']];

export function FiltersModal() {
  const state = useStore();
  const meta = state.meta;

  // docs/01 TM-18 — the spoken-language set hosts choose from, for the filter chips.
  // Declared before the early return so the hook order never changes.
  const [spokenLangs, setSpokenLangs] = useState([]);
  useEffect(() => { api.profileOptions().then(setSpokenLangs).catch(() => setSpokenLangs([])); }, []);

  if (!meta) return <Modal title={t('Bộ lọc')}><p>{t('Đang tải…')}</p></Modal>;

  const span = Math.max(1, meta.maxPrice - meta.minPrice);
  const lowPct = ((state.minPrice - meta.minPrice) / span) * 100;
  const highPct = ((state.maxPrice - meta.minPrice) / span) * 100;
  const peak = Math.max(...meta.priceHistogram, 1);

  const groups = meta.amenities.reduce((acc, a) => {
    (acc[a.group] ||= []).push(a);
    return acc;
  }, {});

  // Dragging only repaints the slider; the search runs once the handle is dropped.
  const dragMin = v => set({ minPrice: Math.min(Number(v), state.maxPrice - 100000) });
  const dragMax = v => set({ maxPrice: Math.max(Number(v), state.minPrice + 100000) });

  return (
    <Modal title={t('Bộ lọc')} foot={<>
      <button className="text-btn" onClick={() => { resetFilters(); set({ q: '' }); applySearch(); }}>{t('Xoá tất cả')}</button>
      {/* docs/01 TM-23 — save this search to be alerted about new matches. */}
      {state.user && (
        <button className="btn btn-outline btn-sm" onClick={saveCurrentSearch}>{t('Lưu tìm kiếm')}</button>
      )}
      <button className="btn btn-dark btn-sm" onClick={closeOverlay}>
        {t(`Hiện ${state.results.total} chỗ nghỉ`)}{activeFilterCount() ? ` ${t(`(${activeFilterCount()} bộ lọc)`)}` : ''}
      </button>
    </>}>
      {/*
        * The same switch the header carries on wide screens. Below 720px the
        * header row has no space for a 198px pill that will not shrink — it was
        * squeezing the chip scroller down to 36px — so on a phone it lives here,
        * one tap away, instead of taking a band off every screen of results.
        * Two buttons, one `showTotalPrice`: nothing to keep in sync.
        */}
      <button className={`total-toggle is-phone-only ${state.showTotalPrice ? 'is-on' : ''}`}
              aria-pressed={state.showTotalPrice}
              onClick={() => set({ showTotalPrice: !state.showTotalPrice })}>
        <span className="switch" aria-hidden="true" /> {t('Giá đã gồm thuế và phí')}
      </button>

      {/*
        * docs/01 TM-24. This used to be a permanent button on the map, in the
        * bar the client asked us to take off it. The tool still draws on the
        * map — it has to — so the sheet closes itself and hands over.
        */}
      <button className="btn btn-outline btn-sm btn-block" style={{ marginBottom: 4 }}
              onClick={() => { set({ drawRequest: store.drawRequest + 1, hideMap: false }); closeOverlay(); }}>
        <Icon name="map" size={16} /> {t('Vẽ vùng trên bản đồ')}
      </button>

      <section className="modal-section">
        <h3>{t('Khoảng giá')}</h3>
        <span className="hint">{t('Giá mỗi đêm, đã gồm phí và thuế')}</span>
        <div className="histogram">
          {meta.priceHistogram.map((h, i) => {
            const at = meta.minPrice + (span * i) / (meta.priceHistogram.length - 1);
            return <i key={i} className={at >= state.minPrice && at <= state.maxPrice ? 'in' : ''}
                      style={{ height: `${Math.max(6, (h / peak) * 100)}%` }} />;
          })}
        </div>
        <div className="range-wrap">
          <span className="range-track" />
          <span className="range-fill" style={{ left: `${lowPct}%`, right: `${100 - highPct}%` }} />
          <input type="range" min={meta.minPrice} max={meta.maxPrice} step={100000}
                 value={state.minPrice} aria-label={t('Giá tối thiểu')}
                 onChange={e => dragMin(e.target.value)} onMouseUp={() => applySearch()} onTouchEnd={() => applySearch()} />
          <input type="range" min={meta.minPrice} max={meta.maxPrice} step={100000}
                 value={state.maxPrice} aria-label={t('Giá tối đa')}
                 onChange={e => dragMax(e.target.value)} onMouseUp={() => applySearch()} onTouchEnd={() => applySearch()} />
        </div>
        <div className="range-vals">
          <label><span className="cap">{t('Tối thiểu')}</span><div className="amt">{money(state.minPrice)}</div></label>
          <label><span className="cap">{t('Tối đa', 'range')}</span>
            <div className="amt">{money(state.maxPrice)}{state.maxPrice >= meta.maxPrice ? '+' : ''}</div></label>
        </div>
      </section>

      <section className="modal-section">
        <h3>{t('Loại nơi ở')}</h3>
        <span className="hint">{t('Bạn muốn ở trọn chỗ nghỉ hay chia sẻ với người khác?')}</span>
        <div className="opt-grid">
          {meta.roomTypes.map(r => (
            <button key={r.key} className={`opt ${state.roomType === r.key ? 'is-on' : ''}`}
                    onClick={() => { set({ roomType: r.key }); applySearch(); }}>
              <b>{t(r.label)}</b><span>{t(r.hint)}</span>
            </button>
          ))}
        </div>
      </section>

      <section className="modal-section">
        <h3>{t('Phòng và giường')}</h3>
        {[['Phòng ngủ', 'bedrooms'], ['Giường', 'beds'], ['Phòng tắm', 'bathrooms']].map(([label, key]) => (
          <CountRow key={key} label={t(label)} value={state[key]}
                    display={state[key] ? `${state[key]}+` : t('Bất kỳ')}
                    decDisabled={state[key] <= 0}
                    onDec={() => { bumpCount(key, -1); applySearch(); }}
                    onInc={() => { bumpCount(key, 1); applySearch(); }} />
        ))}
      </section>

      <section className="modal-section">
        <h3>{t('Loại chỗ ở')}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          {meta.categories.map(c => (
            <button key={c.key} className={`pill ${state.category === c.key ? 'is-on' : ''}`}
                    onClick={() => { set({ category: c.key }); applySearch(); }}>
              <Icon name={CATEGORY_ICON[c.key] ?? 'all'} size={17} /> {t(c.label)} ({c.count})
            </button>
          ))}
        </div>
      </section>

      {Object.entries(groups).map(([group, items]) => (
        <section className="modal-section" key={group}>
          <h3>{t(group)}</h3>
          <div className="pill-row" style={{ marginTop: 14 }}>
            {items.map(a => (
              <button key={a.key} className={`pill ${state.amenities.includes(a.key) ? 'is-on' : ''}`}
                      aria-pressed={state.amenities.includes(a.key)}
                      onClick={() => { toggleAmenity(a.key); applySearch(); }}>
                <AmenityIcon name={a.key} size={17} /> {t(a.label)}
              </button>
            ))}
          </div>
        </section>
      ))}

      {/*
        * docs/01 TM-15 — the four booking options as one group. Two are flags on
        * the search, two are amenities; a guest filtering for "tự nhận phòng"
        * should not have to know which is which.
        */}
      <section className="modal-section">
        <h3>{t('Tuỳ chọn đặt')}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          <button className={`pill ${state.instantBookOnly ? 'is-on' : ''}`}
                  aria-pressed={state.instantBookOnly}
                  onClick={() => { set({ instantBookOnly: !state.instantBookOnly }); applySearch(); }}>
            <Icon name="ev" size={17} /> {t('Đặt ngay')}
          </button>
          <button className={`pill ${state.amenities.includes('selfcheckin') ? 'is-on' : ''}`}
                  aria-pressed={state.amenities.includes('selfcheckin')}
                  onClick={() => { toggleAmenity('selfcheckin'); applySearch(); }}>
            <AmenityIcon name="selfcheckin" size={17} /> {t('Tự nhận phòng')}
          </button>
          <button className={`pill ${state.amenities.includes('pet') ? 'is-on' : ''}`}
                  aria-pressed={state.amenities.includes('pet')}
                  onClick={() => { toggleAmenity('pet'); applySearch(); }}>
            <AmenityIcon name="pet" size={17} /> {t('Cho thú cưng')}
          </button>
          <button className={`pill ${state.freeCancellationOnly ? 'is-on' : ''}`}
                  aria-pressed={state.freeCancellationOnly}
                  onClick={() => { set({ freeCancellationOnly: !state.freeCancellationOnly }); applySearch(); }}>
            <Icon name="heart" size={17} /> {t('Huỷ miễn phí')}
          </button>
          <button className={`pill ${state.payAtPropertyOnly ? 'is-on' : ''}`}
                  aria-pressed={state.payAtPropertyOnly}
                  onClick={() => { set({ payAtPropertyOnly: !state.payAtPropertyOnly }); applySearch(); }}>
            {t('Không cần trả trước')}
          </button>
        </div>
      </section>

      <section className="modal-section">
        <h3>{t('Điểm đánh giá')}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          {RATINGS.map(([value, label]) => (
            <button key={value} className={`pill ${state.minRating === value ? 'is-on' : ''}`}
                    aria-pressed={state.minRating === value}
                    onClick={() => { set({ minRating: value }); applySearch(); }}>{t(label)}</button>
          ))}
        </div>
      </section>

      <section className="modal-section">
        <h3>{t('Khoảng cách tới trung tâm')}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          {CENTRE_KM.map(([value, label]) => (
            <button key={value} className={`pill ${state.maxCentreKm === value ? 'is-on' : ''}`}
                    aria-pressed={state.maxCentreKm === value}
                    onClick={() => { set({ maxCentreKm: value }); applySearch(); }}>{t(label)}</button>
          ))}
        </div>
      </section>

      <section className="modal-section">
        <h3>{t('Lựa chọn nổi bật')}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          <button className={`pill ${state.superhostOnly ? 'is-on' : ''}`}
                  onClick={() => { set({ superhostOnly: !state.superhostOnly }); applySearch(); }}>◈ {t('Siêu chủ nhà')}</button>
          <button className={`pill ${state.guestFavoriteOnly ? 'is-on' : ''}`}
                  onClick={() => { set({ guestFavoriteOnly: !state.guestFavoriteOnly }); applySearch(); }}>♥ {t('Khách yêu thích')}</button>
        </div>
      </section>

      {/* docs/01 TM-18 — lọc theo ngôn ngữ chủ nhà nói được. */}
      {spokenLangs.length > 0 && (
        <section className="modal-section">
          <h3>{t('Ngôn ngữ chủ nhà')}</h3>
          <div className="pill-row" style={{ marginTop: 14 }}>
            {spokenLangs.map(l => (
              <button key={l.code}
                      className={`pill ${state.hostLanguages.includes(l.code) ? 'is-on' : ''}`}
                      aria-pressed={state.hostLanguages.includes(l.code)}
                      onClick={() => { toggleHostLanguage(l.code); applySearch(); }}>{t(l.label)}</button>
            ))}
          </div>
        </section>
      )}

      <section className="modal-section">
        <h3>{t('Sắp xếp kết quả')}</h3>
        <select className="field" style={{ marginTop: 14 }} value={state.sort}
                onChange={e => { set({ sort: e.target.value }); applySearch(); }}>
          {SORTS.map(([v, l]) => <option key={v} value={v}>{t(l)}</option>)}
        </select>
      </section>
    </Modal>
  );
}

/**
 * Everything inside the date picker, without the box around it: the header bar
 * drops it under the search field, a narrow screen gets it in a modal. Keeping
 * one copy is what stops the two going their separate ways.
 */
export function DateFields() {
  const state = useStore();
  const nights = nightsBetween(state.checkIn, state.checkOut);
  const flexible = state.stay !== 'exact' || state.flexDays > 0;

  const tab = state.stay === 'months' ? 'months' : flexible ? 'flex' : 'exact';
  const pick = next => {
    if (next === 'exact') setStayShape({ stay: 'exact', flexDays: 0 });
    else if (next === 'flex') setStayShape({ stay: 'exact', flexDays: 3 });
    else setStayShape({ stay: 'months', flexDays: 0, stayMonths: 1 });
    applySearch();
  };

  return <>
    <div className="date-tabs">
      {[['exact', 'Ngày cụ thể'], ['flex', 'Linh hoạt'], ['months', 'Theo tháng']].map(([key, label]) => (
        <button key={key} className={`date-tab ${tab === key ? 'is-on' : ''}`} onClick={() => pick(key)}>{t(label)}</button>
      ))}
    </div>

    {tab === 'months' ? <MonthPicker state={state} /> : <>
      <div style={{ margin: '18px 0' }}>
        {state.pickingFrom ? <>
          <h3 style={{ margin: 0, fontSize: 20, fontWeight: 800 }}>{t('Chọn ngày trả phòng')}</h3>
          <p style={{ margin: '4px 0 0', fontSize: 14, color: 'var(--ink-muted)' }}>
            {t('Nhận phòng')} {longDate(state.pickingFrom)}
          </p>
        </> : <>
          <h3 style={{ margin: 0, fontSize: 20, fontWeight: 800 }}>{t(`${nights} đêm`)}</h3>
          <p style={{ margin: '4px 0 0', fontSize: 14, color: 'var(--ink-muted)' }}>
            {longDate(state.checkIn)} – {longDate(state.checkOut)}
          </p>
        </>}
      </div>
      <Calendar months={2} />

      {tab === 'flex' && <FlexRow state={state} />}

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 20 }}>
        {[['Cuối tuần này', 'weekend'], ['1 tuần', 'week'], ['2 tuần', 'fortnight'], ['1 tháng', 'month']].map(([label, key]) => (
          <button className="pill" key={key} onClick={() => { applyDatePreset(key); applySearch(); }}>{t(label)}</button>
        ))}
      </div>
    </>}
  </>;
}

export function DatesModal() {
  return (
    <Modal title={t('Chọn ngày')} foot={<>
      <button className="text-btn" onClick={() => { clearDates(); applySearch(); }}>{t('Xoá ngày')}</button>
      <button className="btn btn-dark btn-sm" onClick={() => { settleDates(); closeOverlay(); }}>{t('Xong')}</button>
    </>}>
      <DateFields />
    </Modal>
  );
}

/** docs/01 TM-06 — the same trip, a few days either side. */
function FlexRow({ state }) {
  return (
    <div style={{ marginTop: 18 }}>
      <p style={{ margin: '0 0 10px', fontSize: 14, fontWeight: 700 }}>{t('Xê dịch được bao nhiêu ngày?')}</p>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {[0, 1, 2, 3, 5, 7].map(d => (
          <button key={d} className={`pill ${state.flexDays === d ? 'is-on' : ''}`}
                  onClick={() => { setStayShape({ stay: 'exact', flexDays: d }); applySearch(); }}>
            {d === 0 ? t('Đúng ngày') : t(`± ${d} ngày`)}
          </button>
        ))}
      </div>
      <p style={{ margin: '10px 0 0', fontSize: 13, color: 'var(--ink-muted)' }}>
        {t('Chúng tôi giữ nguyên số đêm và tìm chỗ còn trống ở những ngày gần nhất.')}
      </p>
    </div>
  );
}

/** docs/01 TM-07 — how many months, starting from which month. */
function MonthPicker({ state }) {
  const now = new Date();
  const months = Array.from({ length: 12 }, (_, i) => {
    const d = new Date(now.getFullYear(), now.getMonth() + 1 + i, 1);
    return { key: `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`, d };
  });

  const toggle = key => {
    const on = state.startMonths.includes(key);
    setStayShape({ startMonths: on ? state.startMonths.filter(m => m !== key) : [...state.startMonths, key] });
    applySearch();
  };

  return (
    <div style={{ marginTop: 18 }}>
      <p style={{ margin: '0 0 10px', fontSize: 14, fontWeight: 700 }}>{t('Ở bao nhiêu tháng?')}</p>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {[1, 2, 3, 6, 12].map(m => (
          <button key={m} className={`pill ${state.stayMonths === m ? 'is-on' : ''}`}
                  onClick={() => { setStayShape({ stayMonths: m }); applySearch(); }}>{t(`${m} tháng`)}</button>
        ))}
      </div>

      <p style={{ margin: '18px 0 10px', fontSize: 14, fontWeight: 700 }}>{t('Bắt đầu từ tháng nào?')}</p>
      <div className="month-grid">
        {months.map(({ key, d }) => (
          <button key={key} className={`month-cell ${state.startMonths.includes(key) ? 'is-on' : ''}`}
                  onClick={() => toggle(key)}>
            <b>{t(`Tháng ${d.getMonth() + 1}`)}</b>
            <span>{d.getFullYear()}</span>
          </button>
        ))}
      </div>
      <p style={{ margin: '12px 0 0', fontSize: 13, color: 'var(--ink-muted)' }}>
        {t('Chưa chọn tháng nào thì chúng tôi tìm trong ba tháng tới.')}
      </p>
    </div>
  );
}

const GUEST_ROWS = [
  ['adults', 'Người lớn', 'Từ 13 tuổi trở lên'],
  ['children', 'Trẻ em', 'Độ tuổi 2 – 12'],
  ['infants', 'Em bé', 'Dưới 2 tuổi'],
  ['pets', 'Thú cưng', 'Bạn mang theo thú hỗ trợ?']
];

/** The guest counters on their own, for the header dropdown and the modal alike. */
export function GuestFields() {
  const state = useStore();

  return GUEST_ROWS.map(([key, label, hint]) => (
    <CountRow key={key} label={t(label)} hint={t(hint)} value={state.guests[key]}
              decDisabled={state.guests[key] <= (key === 'adults' ? 1 : 0)}
              onDec={() => { bumpGuest(key, -1); applySearch(); }}
              onInc={() => { bumpGuest(key, 1); applySearch(); }} />
  ));
}

export function GuestsModal() {
  return (
    <Modal title={t('Khách')} size="narrow" foot={<>
      <span style={{ fontSize: 13, color: 'var(--ink-muted)' }}>{t(`Tổng ${totalGuests()} khách`)}</span>
      <button className="btn btn-dark btn-sm" onClick={closeOverlay}>{t('Xong')}</button>
    </>}>
      <GuestFields />
    </Modal>
  );
}

/*
 * The whole search, on a screen that has no room for three fields side by side.
 * The header bar below 720px is a single summary pill; this is what it opens.
 *
 * One section is expanded at a time and the other two are one-line summaries,
 * which is the only way the destination field, two months of calendar and four
 * guest counters all fit without the sheet turning into a scroll marathon. The
 * pieces are the same DateFields / GuestFields / Suggestions the desktop
 * dropdown uses — a second copy of any of them would drift within the week.
 */
export function SearchModal() {
  const state = useStore();
  const [step, setStep] = useState(state.q.trim() ? 'when' : 'where');

  // The destination field owns the suggestion list, so opening any other step
  // has to take the list off the screen with it.
  useEffect(() => {
    set({ suggestOpen: step === 'where' });
    if (step === 'where') loadSuggestions();
    return () => set({ suggestOpen: false });
  }, [step]);

  const submit = () => { settleDates(); set({ suggestOpen: false }); closeOverlay(); applySearch({ replace: false }); };

  const where = state.q.trim() || t('Mọi nơi');
  const rows = [
    ['where', t('Địa điểm'), where],
    ['when', t('Ngày'), dateRangeLabel(state.checkIn, state.checkOut)],
    ['who', t('Khách'), guestLabel()]
  ];

  return (
    <Modal title={t('Tìm kiếm')} foot={<>
      <button className="text-btn" onClick={() => { set({ q: '' }); clearDates(); applySearch(); }}>
        {t('Xoá tất cả')}
      </button>
      <button className="btn btn-dark btn-sm" onClick={submit}>{t('Tìm kiếm')}</button>
    </>}>
      <div className="ssheet">
        {rows.map(([key, cap, value]) => (
          <section className={`ssheet-card ${step === key ? 'is-open' : ''}`} key={key}>
            {step === key ? <>
              <h3>{cap}</h3>
              {key === 'where' && <>
                <input className="ssheet-input" type="text" value={state.q} autoFocus
                       placeholder={t('Tìm điểm đến')} autoComplete="off"
                       onChange={e => { set({ q: e.target.value, suggestOpen: true }); loadSuggestions(); }} />
                <Suggestions onDone={closeOverlay} />
              </>}
              {key === 'when' && <DateFields />}
              {key === 'who' && <GuestFields />}
            </> : (
              <button type="button" className="ssheet-row" onClick={() => setStep(key)}>
                <span>{cap}</span><b>{value}</b>
              </button>
            )}
          </section>
        ))}
      </div>
    </Modal>
  );
}

/*
 * The language and currency grids, shared by the header modal and by
 * /cai-dat/tuy-chinh. One component per grid, because the settings page
 * offering a second hand-rolled copy of this list is exactly how the "Dịch"
 * button once grew its own stale label set (PLAN.md §9.0).
 */
export function LanguageChoices() {
  const state = useStore();
  return (
    <div className="lang-grid" style={{ marginTop: 14 }}>
      {(state.meta?.languages ?? []).map(l => (
        <button key={l.code} className={`lang ${state.language.code === l.code ? 'is-on' : ''}`}
                onClick={() => applyLanguage(l.code)}>
          <b>{l.label}</b><span>{l.region}</span>
        </button>
      ))}
    </div>
  );
}

export function CurrencyChoices() {
  const state = useStore();
  return (
    <div className="lang-grid" style={{ marginTop: 14 }}>
      {(state.meta?.currencies ?? []).map(c => (
        <button key={c.code} className={`lang ${state.currency.code === c.code ? 'is-on' : ''}`}
                onClick={() => applyCurrency(c.code)}>
          <b>{c.label}</b><span>{c.code} — {c.symbol}</span>
        </button>
      ))}
    </div>
  );
}

export function LanguageModal() {
  const state = useStore();
  const meta = state.meta;
  if (!meta) return <Modal title={t('Ngôn ngữ')}><p>{t('Đang tải…')}</p></Modal>;

  return (
    <Modal title={t('Ngôn ngữ & tiền tệ')}>
      <section className="modal-section">
        <h3>{t('Ngôn ngữ đề xuất')}</h3>
        <LanguageChoices />
      </section>
      <section className="modal-section">
        <h3>{t('Chọn loại tiền tệ')}</h3>
        <CurrencyChoices />
      </section>
      <section className="modal-section">
        {/* docs/01 TK-09 — the third setting of the row. Deadlines and message
            timestamps follow it; check-in DATES stay on the device clock, since
            a date shifted westward moves the stay by a day. */}
        <h3>{t('Múi giờ hiển thị')}</h3>
        <p className="section-sub" style={{ marginTop: 4 }}>
          {t('Áp cho giờ và hạn chót. Ngày nhận và trả phòng luôn là ngày tại chỗ nghỉ.')}
        </p>
        <TimeZonePicker />
      </section>
    </Modal>
  );
}

/**
 * docs/01 AT-02 — reports a listing, a person, a message or a review. Which one
 * comes from store.report, set by openReport() at the button that opened this.
 * The reasons come from the server so the four lists cannot drift from the ones
 * the domain offers.
 */
export function ReportModal() {
  const state = useStore();
  const subject = state.report ?? (store.detail?.card
    // The listing page opened this dialog directly for years; keep that working
    // rather than leave a dead button behind on a page nobody remembered to change.
    ? { target: 'listing', subjectId: store.detail.card.id, title: store.detail.card.title }
    : null);

  const [config, setConfig] = useState(null);
  const [reason, setReason] = useState(null);
  const [detail, setDetail] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!subject) return;
    api.reportReasons(subject.target).then(setConfig).catch(err => toast(err.message));
    // Only the kind of subject decides the reason list. Depending on the whole
    // subject would refetch on every render, since it is rebuilt each time.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [subject?.target]);

  if (!subject) return null;

  const send = async () => {
    setBusy(true);
    try {
      const res = await api.report({
        target: subject.target, subjectId: subject.subjectId, reason, detail: detail.trim() || null
      });
      closeOverlay();
      toast(res.message);
    } catch (err) {
      toast(err.message);
    } finally { setBusy(false); }
  };

  const label = config?.targetLabel ?? '';

  return (
    <Modal title={`${t('Báo cáo', 'action')} ${t(label, 'report').toLowerCase()}`} size="narrow">
      {subject.title && (
        <p style={{ margin: '0 0 6px', fontSize: 14, fontWeight: 700 }}>{subject.title}</p>
      )}
      <p style={{ margin: '0 0 16px', fontSize: 13.5, color: 'var(--ink-muted)', lineHeight: 1.6 }}>
        {t('Đội an toàn Staylio sẽ xem xét báo cáo của bạn. Người bị báo cáo không biết ai đã gửi.')}
      </p>

      {!config ? (
        <p style={{ fontSize: 13.5, color: 'var(--ink-muted)' }}>{t('Đang tải…')}</p>
      ) : (
        <>
          <div style={{ display: 'grid', gap: 8 }}>
            {config.reasons.map(r => (
              <button className="opt" key={r} aria-pressed={reason === r}
                      style={reason === r ? { borderColor: 'var(--ink)', borderWidth: 2 } : undefined}
                      onClick={() => setReason(r)}><b>{t(r)}</b></button>
            ))}
          </div>

          <label className="form-field" style={{ marginTop: 14 }}>
            <span className="cap">{t('Mô tả thêm (không bắt buộc)')}</span>
            <textarea rows={3} value={detail} maxLength={2000}
                      onChange={e => setDetail(e.target.value)}
                      placeholder={t('Kể thêm chuyện gì đã xảy ra, càng cụ thể càng dễ xử lý.')}
                      style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)',
                               borderRadius: 12, fontSize: 14 }} />
          </label>

          <button className="btn btn-primary" style={{ width: '100%', marginTop: 8 }}
                  disabled={!reason || busy} onClick={send}>
            {busy ? t('Đang gửi…') : t('Gửi báo cáo')}
          </button>
        </>
      )}
    </Modal>
  );
}

/**
 * docs/01 TĐ-18 — "qua link, mạng xã hội, email".
 *
 * Every destination is an ordinary link the browser opens. No provider script is
 * embedded: a share button that loads Facebook's SDK reports every visitor who
 * merely looked at the listing page, whether or not they ever shared anything.
 */
const SHARE_TARGETS = [
  ['Facebook', u => `https://www.facebook.com/sharer/sharer.php?u=${u}`],
  ['X (Twitter)', (u, t) => `https://twitter.com/intent/tweet?url=${u}&text=${t}`],
  ['WhatsApp', (u, t) => `https://wa.me/?text=${t}%20${u}`],
  ['Telegram', (u, t) => `https://t.me/share/url?url=${u}&text=${t}`],
  ['Messenger', u => `https://www.facebook.com/dialog/send?link=${u}&app_id=0&redirect_uri=${u}`]
];

export function ShareModal() {
  const state = useStore();
  const share = state.share;
  if (!share) return null;

  const u = encodeURIComponent(share.url);
  // Not named `t`: that is the translator, and shadowing it here would silently
  // turn every t('…') in this component into encodeURIComponent.
  const title = encodeURIComponent(share.title);

  const email = `mailto:?subject=${title}&body=${encodeURIComponent(`${share.title}

${share.url}`)}`;

  return (
    <Modal title={t('Chia sẻ chỗ nghỉ này')} size="narrow">
      <p style={{ margin: '0 0 16px', fontSize: 14, fontWeight: 600 }}>{share.title}</p>

      <div style={{ display: 'grid', gap: 8 }}>
        <button className="opt" onClick={() => copyShareLink(share.url)}>
          <b>{t('Sao chép liên kết')}</b>
        </button>

        <a className="opt" href={email}><b>{t('Gửi qua email')}</b></a>

        {SHARE_TARGETS.map(([label, build]) => (
          <a className="opt" key={label} target="_blank" rel="noreferrer noopener"
             href={build(u, title)}><b>{label}</b></a>
        ))}

        {/* The device's own sheet reaches apps a link never will, but only some
            browsers have it, so it is offered rather than relied on. */}
        {typeof navigator !== 'undefined' && navigator.share && (
          <button className="opt" onClick={() => shareViaDevice(share)}>
            <b>{t('Ứng dụng khác trên máy…')}</b>
          </button>
        )}
      </div>
    </Modal>
  );
}

/**
 * docs/01 TN-01 — ask the host a question before booking.
 *
 * This box used to throw the guest's message away and toast "bản demo" at them,
 * while the button beside it sent a hard-coded sentence nobody typed. Both are
 * the same act, so both now land here: the guest writes their own words, the
 * message opens a real thread, and the page moves to it.
 *
 * A seeded host with no account (h.userId null) has no inbox to receive
 * anything. That is said plainly rather than dressed up as a send that fails.
 */
export function ContactHostModal() {
  const state = useStore();
  const h = state.detail?.host;
  const listingId = state.detail?.card?.id;
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  if (!h) return null;

  const reachable = !!h.userId;

  const send = async () => {
    if (!requireAuth()) return;
    const text = body.trim();
    if (!text) { toast(t('Viết vài dòng cho chủ nhà trước khi gửi.')); return; }

    setBusy(true);
    try {
      const thread = await api.sendMessage({ listingId, body: text });
      set({ activeThread: thread });
      closeOverlay();
      go('/messages');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <Modal title={`${t('Nhắn tin cho')} ${h.name}`} size="narrow">
      <p style={{ fontSize: 14, color: 'var(--ink-muted)', lineHeight: 1.6, margin: '0 0 16px' }}>
        {h.name} {t('thường phản hồi')} {h.responseTime} · {t('tỉ lệ phản hồi')} {h.responseRate}.
      </p>

      {reachable ? <>
        <label className="form-field">
          <span className="cap">{t('Tin nhắn', 'field')}</span>
          <textarea rows={5} value={body} onChange={e => setBody(e.target.value)}
                    placeholder={`${t('Chào')} ${h.name}, ${t('mình muốn hỏi về...')}`}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>
        <button className="btn btn-primary btn-block" onClick={send} disabled={busy}>
          {busy ? t('Đang gửi…') : t('Gửi tin nhắn')}
        </button>
      </> : (
        <p style={{ fontSize: 14, color: 'var(--ink-muted)', lineHeight: 1.6, margin: 0 }}>
          {t('Chủ nhà này chưa mở hộp thư trên Staylio. Bạn đặt chỗ trước, rồi nhắn qua trang chuyến đi.')}
        </p>
      )}
    </Modal>
  );
}

/**
 * Destination dropdown. With nothing typed it leads with the guest's recent
 * searches (docs/01 TM-04); cities jump to a search, listings open the room page.
 */
export function Suggestions({ onDone }) {
  const state = useStore();
  const navigate = useNavigate();
  const [recent, setRecent] = useState(() => recentSearches());

  // The list is re-read each time the panel opens, so a search made a moment
  // ago is already there.
  useEffect(() => { if (state.suggestOpen) setRecent(recentSearches()); }, [state.suggestOpen]);

  const showRecent = !state.q.trim() && recent.length > 0;
  if (!state.suggestOpen || (!state.suggestions?.length && !showRecent)) return null;

  const pick = s => {
    set({ suggestOpen: false });
    onDone?.();
    if (s.kind === 'listing') { navigate(`/rooms/${s.value}`); return; }
    set({ q: s.value });
    applySearch({ replace: false });
  };

  const replay = entry => {
    onDone?.();
    set({
      suggestOpen: false,
      q: entry.q,
      checkIn: entry.checkIn ?? state.checkIn,
      checkOut: entry.checkOut ?? state.checkOut,
      guests: entry.guests ?? state.guests
    });
    applySearch({ replace: false });
  };

  return (
    <div className="suggest-list" role="listbox">
      {showRecent && <>
        <div className="suggest-head">
          {t('Tìm kiếm gần đây')}
          <button className="link-btn" style={{ float: 'right', fontSize: 12 }}
                  onMouseDown={e => e.preventDefault()}
                  onClick={() => { clearSearchHistory(); setRecent([]); }}>{t('Xoá')}</button>
        </div>
        {recent.map(entry => (
          <button type="button" className="suggest-row" role="option" key={`recent:${entry.q}`}
                  onMouseDown={e => e.preventDefault()} onClick={() => replay(entry)}>
            <span className="suggest-ic"><Icon name="search" size={18} /></span>
            <span style={{ minWidth: 0 }}>
              <b>{entry.q}</b>
              <span>{entry.checkIn ? dateRangeLabel(entry.checkIn, entry.checkOut) : t('Mọi ngày')}</span>
            </span>
          </button>
        ))}
      </>}

      <div className="suggest-head">{state.q.trim() ? t('Kết quả gợi ý') : t('Điểm đến phổ biến')}</div>
      {(state.suggestions ?? []).map(s => (
        <button type="button" className="suggest-row" role="option" key={`${s.kind}:${s.value}`}
                onMouseDown={e => e.preventDefault()} onClick={() => pick(s)}>
          <span className="suggest-ic"><Icon name={s.kind === 'city' ? 'map' : 'house'} size={18} /></span>
          <span style={{ minWidth: 0 }}>
            <b>{s.label}</b>
            <span>{s.sub}</span>
          </span>
        </button>
      ))}
    </div>
  );
}
