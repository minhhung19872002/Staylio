import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { set, toast } from '../lib/store.js';
import { api } from '../lib/api.js';
import { money, longDate, dateFormat, number } from '../lib/format.js';
import { CardCarousel } from '../components/CardCarousel.jsx';
import { PhotoMosaic } from '../components/PhotoMosaic.jsx';
import { Avatar } from '../components/Avatar.jsx';
import { Icon } from '../components/Icon.jsx';
import { DetailMap } from '../components/Maps.jsx';
import { Sheet } from '../components/modals/Sheet.jsx';
import { PaymentMethods } from '../components/PaymentMethods.jsx';
import { t } from '../lib/i18n.js';
import { TranslatedText } from '../components/TranslatedText.jsx';

/**
 * docs/09 §2.10 (MR-E-11) — an experience is judged on four things of its own.
 * Deliberately not the stay's six: there is no cleanliness, no check-in and no
 * location here, and the order is the one the spec writes.
 */
const XP_CRITERIA = [
  ['host', 'Người dẫn'],
  ['asDescribed', 'Đúng như mô tả'],
  ['safety', 'Tổ chức và an toàn'],
  ['value', 'Đáng giá tiền']
];

/**
 * The host stores the languages they run in as codes. "vi, en" is not something
 * a guest reads, so they are shown the way CatalogService.Languages names them —
 * each in its own script, which needs no translating in either direction.
 */
const LANGUAGE_NAME = {
  vi: 'Tiếng Việt', en: 'English', ja: '日本語', ko: '한국어',
  zh: '中文 (简体)', fr: 'Français', de: 'Deutsch', es: 'Español'
};

const languagesOf = codes =>
  (codes ?? []).map(c => LANGUAGE_NAME[c] ?? c).join(', ');

/** Two letters for somebody with no photo, the way the server builds them. */
const initialsOf = name => (name || '?')
  .trim().split(/\s+/).slice(-2).map(w => w[0] ?? '').join('').toUpperCase() || '?';

// Dates follow the language the guest picked (see format.js LOCALE), so these
// are asked for per render rather than frozen at import.
const TIME = () => dateFormat({ hour: '2-digit', minute: '2-digit' });
const DAY = () => dateFormat({ weekday: 'short', day: '2-digit', month: '2-digit' });
const LONG_DAY = () => dateFormat({ weekday: 'long', day: 'numeric', month: 'long' });
const MONTH = () => dateFormat({ month: 'long', year: 'numeric' });

/** "Hôm nay · thứ tư, 12 tháng 8" — the day a session falls on, said the short way. */
function dayLabel(date) {
  const today = new Date();
  const tomorrow = new Date(today.getTime() + 86400000);
  const same = (a, b) => a.toDateString() === b.toDateString();
  const written = LONG_DAY().format(date);

  if (same(date, today)) return `${t('Hôm nay')} · ${written}`;
  if (same(date, tomorrow)) return `${t('Ngày mai')} · ${written}`;
  return written;
}

// Exported so the trip page's cross-sell cards (docs/09 §4) read a session's
// length exactly the way the experience cards here do.
export const duration = minutes =>
  minutes >= 60
    ? `${Math.floor(minutes / 60)} ${t('giờ')}${minutes % 60 ? ` ${minutes % 60} ${t('phút')}` : ''}`
    : `${minutes} ${t('phút')}`;

/** docs/01 MR-01 → MR-04 — things a local runs, sold by the seat. */
export function Experiences() {
  const { slug } = useParams();
  return slug ? <Detail slug={slug} /> : <Browse />;
}

function Browse() {
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  const [items, setItems] = useState(null);

  useEffect(() => {
    let live = true;
    const timer = setTimeout(() => {
      api.experiences({ q: q.trim() || undefined })
        .then(r => { if (live) setItems(r); })
        .catch(e => toast(e.message));
    }, 180);
    return () => { live = false; clearTimeout(timer); };
  }, [q]);

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <h1 className="section-title">{t('Trải nghiệm')}</h1>
      <p className="section-sub">{t('Hoạt động do người địa phương dẫn, tính theo vé chứ không theo đêm.')}</p>

      <div className="help-search" style={{ maxWidth: 520 }}>
        <input value={q} onChange={e => setQ(e.target.value)} autoComplete="off"
               placeholder={t('Nấu ăn, chèo SUP, cà phê…')} />
      </div>

      {!items ? (
        <div className="card-grid" style={{ marginTop: 24 }}>
          {Array.from({ length: 4 }, (_, i) => (
            <div className="card skeleton" key={i} style={{ height: 300, border: 0 }} />
          ))}
        </div>
      ) : items.length ? (
        <div className="card-grid" style={{ marginTop: 24 }}>
          {items.map(x => (
            <article className="card" key={x.id}>
              {/* The photos stay a click handler because the carousel arrows and
                  dots live inside them, and interactive controls inside an <a> is
                  invalid HTML and a keyboard trap. The body below is the real
                  link — same split Card.jsx makes for stays. */}
              <div onClick={() => navigate(`/experiences/${x.slug}`)} style={{ cursor: 'pointer' }}>
                <CardCarousel images={x.images} alt={x.title} />
              </div>
              <a className="card-body" href={`/experiences/${x.slug}`}
                 onClick={e => {
                   if (e.metaKey || e.ctrlKey || e.shiftKey || e.button === 1) return;
                   e.preventDefault();
                   navigate(`/experiences/${x.slug}`);
                 }}>
                <div className="card-row">
                  <h3 className="card-title"><TranslatedText as="span" text={x.title} notice={false} /></h3>
                  <div className="card-rating">
                    {x.reviewCount ? `★ ${x.rating.toFixed(2)} (${x.reviewCount})` : `★ ${t('Mới')}`}
                  </div>
                </div>
                <div className="card-sub card-line">{x.city} · {duration(x.durationMinutes)} · {t('tối đa')} {x.maxGroup} {t('người')}</div>
                <div className="card-price">
                  <b>{money(x.pricePerPerson)}</b> <span>/ {t('người')}</span>
                </div>
                <div className="card-perks card-line">
                  {x.openSlots ? `${t('Còn')} ${x.openSlots} ${t('suất')}` : t('Tạm hết suất')} · {x.hostName}
                </div>
              </a>
            </article>
          ))}
        </div>
      ) : (
        <div className="empty-state" style={{ marginTop: 28 }}>
          <h3>{t('Chưa có trải nghiệm nào khớp')}</h3>
          <p>{t('Thử một từ khoá khác.')}</p>
        </div>
      )}
    </div>
  );
}

function Detail({ slug }) {
  const navigate = useNavigate();
  const [x, setX] = useState(null);
  const [missing, setMissing] = useState(false);
  const [slotId, setSlotId] = useState(null);
  const [seats, setSeats] = useState(2);
  const [priv, setPriv] = useState(false);
  const [picking, setPicking] = useState(false);

  const load = () => api.experience(slug).then(d => {
    setX(d);
    setSlotId(id => id ?? d.slots.find(s => s.status === 'Open' && s.seatsLeft > 0)?.id ?? null);
  }).catch(() => setMissing(true));

  useEffect(() => { load(); }, [slug]);

  if (missing) {
    return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
      <div className="empty-state"><h3>{t('Không tìm thấy trải nghiệm này')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => navigate('/experiences')}>{t('Xem tất cả')}</button></div>
    </div>;
  }

  if (!x) return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
    <div className="stat skeleton" style={{ height: 320, border: 0 }} /></div>;

  /*
   * Choosing a session ends the dialog on the checkout page, the same way a
   * service does. The choice travels in the address bar so a reload or a back
   * button lands on the same session rather than an empty page.
   */
  const toCheckout = () => {
    const q = new URLSearchParams({ slot: String(slotId), seats: String(seats) });
    if (priv) q.set('private', '1');
    navigate(`/experiences/${slug}/thanh-toan?${q}`);
  };

  const open = x.slots.filter(s => s.status === 'Open');

  return (
    <div className="shell" style={{ paddingBlock: '26px 90px' }}>
      <button className="back-link" onClick={() => navigate('/experiences')}>← {t('Trải nghiệm')}</button>

      {/*
        * Photographs beside the facts rather than above them. A session is sold on
        * what it looks like and what it costs, and stacking those meant a screen of
        * pictures before the first number — the reader had to scroll to learn the
        * price of the thing they were looking at.
        */}
      <div className="xp-hero">
        <div className="xp-hero-photos">
          {!!x.images.length && <PhotoMosaic images={x.images} alt={x.title} />}
        </div>

        <div className="xp-hero-info">
          <h1><TranslatedText as="span" text={x.title} /></h1>
          <p className="xp-hero-sub"><TranslatedText as="span" text={x.summary} notice={false} /></p>

          <p className="xp-hero-meta">
            {x.reviewCount ? <><b>★ {x.rating.toFixed(2)}</b> · {x.reviewCount} {t('đánh giá')} · </> : null}
            {x.city} · {duration(x.durationMinutes)}
          </p>

          <div className="xp-hero-facts">
            <div className="xp-fact">
              <Avatar initials={x.hostInitials} />
              {/* Name first, role under it. "Do X dẫn" is a Vietnamese sentence
                  frame, and splitting it into two keys around the name gives
                  "By X led" in English — a label and a proper noun survive
                  every language, a half-sentence does not. */}
              <div>
                <b>{x.hostName}</b>
                <span>{t('Người dẫn')} · {t('tối đa')} {x.maxGroup} {t('người')}</span>
              </div>
            </div>
            <div className="xp-fact">
              <span className="xp-fact-ic"><Icon name="pin" size={18} /></span>
              <div>
                <b>{t('Điểm hẹn')}</b>
                <span><TranslatedText as="span" text={x.meetingPoint} notice={false} /></span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="trip-layout" style={{ marginTop: 24 }}>
        <div style={{ minWidth: 0 }}>
          <section className="detail-section" style={{ paddingTop: 0 }}>
            <h2>{t('Buổi này có gì')}</h2>
            <TranslatedText as="p" style={{ fontSize: 15.5, lineHeight: 1.75, color: 'var(--ink-body)' }} text={x.description} />
          </section>

          {/* docs/01 MR-01 — what actually happens, in order. Left out entirely
              when the host has not written one, rather than shown empty. */}
          {!!x.itinerary?.length && (
            <section className="detail-section">
              <h2>{t('Hành trình')}</h2>
              <ol className="xp-steps">
                {x.itinerary.map((step, i) => (
                  <li className="xp-step" key={i}>
                    <span className="xp-step-dot" aria-hidden="true">
                      {step.imageUrl
                        ? <img src={step.imageUrl} alt="" loading="lazy" decoding="async" />
                        : <i>{i + 1}</i>}
                    </span>
                    <div className="xp-step-body">
                      <b><TranslatedText as="span" text={step.title} notice={false} /></b>
                      {step.description && (
                        <span><TranslatedText as="span" text={step.description} notice={false} /></span>
                      )}
                    </div>
                  </li>
                ))}
              </ol>
            </section>
          )}

          <section className="detail-section">
            <h2>{t('Vé bao gồm')}</h2>
            <ul style={{ margin: 0, paddingLeft: 22, lineHeight: 1.9, color: 'var(--ink-body)' }}>
              {/* What the host typed, not something the platform ships — so it goes
                  the machine-translation way, like the house rules on a stay. */}
              {x.included.map((i, k) => <li key={k}><TranslatedText as="span" text={i} /></li>)}
            </ul>
          </section>

          <ExperienceReviews experience={x} />

          {/* The meeting point is an address in prose above; here it is a place. */}
          <section className="detail-section">
            <h2>{t('Nơi gặp nhau')}</h2>
            <p className="section-sub" style={{ marginBottom: 14 }}>
              <TranslatedText as="span" text={x.meetingPoint} notice={false} /> · {x.city}
            </p>
            <DetailMap latitude={x.latitude} longitude={x.longitude} />
          </section>

          <section className="detail-section">
            <h2>{t('Cần biết')}</h2>
            <div className="xp-know">
              <Know icon="user" title={t('Độ tuổi')}
                    body={x.minAge ? `${t('Từ')} ${x.minAge} ${t('tuổi')}` : t('Mọi lứa tuổi')} />
              <Know icon="globe" title={t('Ngôn ngữ')} body={languagesOf(x.languages)} />
              <Know icon="users" title={t('Số người')}
                    body={`${x.minGuests}–${x.maxGroup} ${t('người mỗi suất')}`} />
              <Know icon="calendar" title={t('Chính sách huỷ')}
                    body={t('Huỷ trước 7 ngày hoàn toàn bộ, trước 24 giờ hoàn 50%.')} />
            </div>
            <p style={{ fontSize: 13.5, color: 'var(--ink-muted)', marginTop: 14, lineHeight: 1.6 }}>
              {t('Suất không đủ')} {x.minGuests} {t('người trước 48 giờ sẽ bị huỷ và hoàn tiền toàn bộ. Bạn tự huỷ thì theo bậc 7 ngày / 24 giờ ở trên.')}
            </p>
          </section>
        </div>

        {/*
          * The rail sells; the dialog books. What stays on screen is the price,
          * the cancellation promise and the next few sessions — the four facts
          * somebody decides on. Choosing a session used to mean scrolling a
          * boxed list inside the panel while the number of people, the private
          * group option and the whole bill fought for the same 360 pixels.
          */}
        <aside className="book-panel">
          <div className="book-price">
            <span className="amount">{t('Từ')} {money(x.pricePerPerson)}</span>
            <span className="per">/ {t('người')}</span>
          </div>

          {/*
            * docs/09 §2.8 — the real ladder is 7 days 100%, 24h–7 days 50%,
            * under 24h nothing. This line used to promise "trước 24 giờ được
            * hoàn toàn bộ", which is the hour a guest drops to half, not the
            * hour they still get everything. Same mistake the services page
            * made with 24h vs 72h.
            */}
          <p style={{ margin: '10px 0 14px', fontSize: 14, color: 'var(--ink-body)' }}>
            <b style={{ color: 'var(--brand)' }}>{t('Huỷ trước 7 ngày')}</b> · {t('hoàn toàn bộ; trước 24 giờ hoàn 50%')}
          </p>

          <button className="btn btn-primary" style={{ width: '100%' }}
                  onClick={() => setPicking(true)}>{t('Xem lịch')}</button>

          <div className="slot-list" style={{ marginTop: 16 }}>
            {open.length ? open.slice(0, 4).map(s => {
              const at = new Date(s.startsAt);
              const ends = new Date(at.getTime() + x.durationMinutes * 60000);
              const scarce = s.seatsLeft > 0 && s.seatsLeft <= 3;
              return (
                <button key={s.id} className={`slot-card ${slotId === s.id ? 'is-on' : ''}`}
                        disabled={s.seatsLeft === 0}
                        onClick={() => { setSlotId(s.id); setPicking(true); }}>
                  <span className="slot-card-when">
                    <b>{DAY().format(at)}</b>
                    <span>{TIME().format(at)} – {TIME().format(ends)}</span>
                  </span>
                  <i className={scarce ? 'is-scarce' : ''}>
                    {s.seatsLeft ? `${t('còn')} ${s.seatsLeft} ${t('chỗ')}` : t('hết chỗ')}
                  </i>
                </button>
              );
            }) : <p className="section-sub">{t('Chưa có suất nào mở.')}</p>}
          </div>

          {open.length > 4 && (
            <button className="link-btn" style={{ marginTop: 12 }}
                    onClick={() => setPicking(true)}>{t('Xem tất cả ngày')}</button>
          )}
        </aside>
      </div>

      {picking && (
        <SlotSheet experience={x} slots={open} slotId={slotId} onPick={setSlotId}
                   seats={seats} setSeats={setSeats} priv={priv} setPriv={setPriv}
                   onContinue={toCheckout} onClose={() => setPicking(false)} />
      )}
    </div>
  );
}

/**
 * docs/01 MR-04 — choosing a session, in the dialog "Xem lịch" opens. Grouped
 * by the day it falls on rather than listed flat, because "thứ tư 14/08" twice
 * in a row is two sessions of the same day and the reader should not have to
 * work that out from a date repeated in two rows.
 */
function SlotSheet({
  experience: x, slots, slotId, onPick, seats, setSeats, priv, setPriv,
  onContinue, onClose
}) {
  const days = useMemo(() => {
    const groups = [];
    for (const s of slots) {
      const at = new Date(s.startsAt);
      const key = at.toDateString();
      const last = groups[groups.length - 1];
      if (last?.key === key) last.slots.push({ ...s, at });
      else groups.push({ key, at, slots: [{ ...s, at }] });
    }
    return groups;
  }, [slots]);

  const chosen = slots.find(s => s.id === slotId);

  return (
    <Sheet title={t('Chọn giờ')} onClose={onClose}
           foot={
             <>
               <span>
                 {chosen
                   ? <><b style={{ fontSize: 16 }}>{money(x.pricePerPerson)}</b>{' '}
                       <span style={{ color: 'var(--ink-muted)', fontSize: 13 }}>/ {t('người')}</span></>
                   : <span style={{ color: 'var(--ink-muted)', fontSize: 13.5 }}>
                       {t('Chọn một suất để tiếp tục.')}
                     </span>}
               </span>
               <button className="btn btn-primary" disabled={!chosen} onClick={onContinue}>
                 {t('Tiếp tục')}
               </button>
             </>
           }>
      <div className="count-row" style={{ paddingTop: 0 }}>
        <div className="tx">
          <b>{seats} {t('người')}</b>
          <span>{t('Tối đa')} {x.maxGroup} {t('người mỗi suất')}</span>
        </div>
        <div className="count-ctl">
          <button type="button" className="round-btn" aria-label={t('Giảm')}
                  disabled={seats <= 1}
                  onClick={() => setSeats(n => Math.max(1, n - 1))}>−</button>
          <span className="num">{seats}</span>
          <button type="button" className="round-btn" aria-label={t('Tăng')}
                  disabled={seats >= x.maxGroup}
                  onClick={() => setSeats(n => Math.min(x.maxGroup, n + 1))}>+</button>
        </div>
      </div>

      <div className="slot-month">
        <span>{MONTH().format(chosen ? new Date(chosen.startsAt) : new Date())}</span>
        <Icon name="calendar" size={20} />
      </div>

      {days.length ? days.map(day => (
        <div key={day.key}>
          <h3 className="slot-day">{dayLabel(day.at)}</h3>
          <div className="slot-list">
            {day.slots.map(s => {
              const ends = new Date(s.at.getTime() + x.durationMinutes * 60000);
              const scarce = s.seatsLeft > 0 && s.seatsLeft <= 3;
              return (
                <button key={s.id} type="button" disabled={s.seatsLeft === 0}
                        className={`slot-card ${slotId === s.id ? 'is-on' : ''}`}
                        onClick={() => onPick(s.id)}>
                  <span className="slot-card-when">
                    <b>{TIME().format(s.at)} – {TIME().format(ends)}</b>
                    <span>
                      {languagesOf(x.languages)} · {money(x.pricePerPerson)} / {t('người')}
                    </span>
                  </span>
                  <i className={scarce ? 'is-scarce' : ''}>
                    {s.seatsLeft ? `${t('còn')} ${s.seatsLeft} ${t('chỗ')}` : t('hết chỗ')}
                  </i>
                </button>
              );
            })}
          </div>
        </div>
      )) : <p className="slot-empty">{t('Chưa có suất nào mở.')}</p>}

      {x.privateGroupPrice != null && (
        <button type="button" className={`opt ${priv ? 'is-on' : ''}`} style={{ marginTop: 20, width: '100%' }}
                onClick={() => setPriv(p => !p)}>
          <b>{t('Thuê trọn nhóm riêng')} — {money(x.privateGroupPrice)}</b>
          <span>{t('Chỉ nhóm bạn, không ghép với khách khác')}</span>
        </button>
      )}

    </Sheet>
  );
}

/**
 * docs/01 MR-04 and docs/07 §2 — "Xác nhận và thanh toán" for a ticket. Same
 * shape as the service checkout: three numbered steps down the left, the seat
 * being bought pinned on the right.
 */
export function ExperienceCheckout() {
  const { slug } = useParams();
  const [params] = useSearchParams();
  const state = useStore();
  const navigate = useNavigate();

  const [x, setX] = useState(null);
  const [missing, setMissing] = useState(false);
  const [quote, setQuote] = useState(null);
  const [busy, setBusy] = useState(false);

  const slotId = Number(params.get('slot')) || 0;
  const seats = Math.max(1, Number(params.get('seats')) || 1);
  const priv = params.get('private') === '1';

  useEffect(() => {
    api.experience(slug).then(setX).catch(() => setMissing(true));
  }, [slug]);

  // docs/09 §2.7 (MR-E-06) — "trừ chỗ ngay khi bắt đầu thanh toán, giữ 10
  // phút". The hold endpoint existed and nothing called it, so two guests on
  // this page could both reach "pay" for the last seat. The quote is read
  // first: asked after the hold, it would count this guest's own seats as
  // taken and grey the button out.
  const [hold, setHold] = useState(null);
  useEffect(() => {
    if (!slotId) return;
    let alive = true;
    api.experienceQuote(slotId, seats, priv)
      .then(q => {
        if (!alive) return;
        setQuote(q);
        if (!q.canBook || !state.user) return;
        return api.holdExperience(slotId, { seats, private: priv })
          .then(h => { if (alive) setHold(h); })
          .catch(() => { if (alive) setHold(null); });
      })
      .catch(e => toast(e.message));
    return () => { alive = false; };
  }, [slotId, seats, priv, state.user]);

  const slot = x?.slots.find(s => s.id === slotId);

  if (missing || (x && !slot)) {
    return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
      <div className="empty-state"><h3>{t('Không tìm thấy trải nghiệm này')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => navigate('/experiences')}>{t('Xem tất cả')}</button></div></div>;
  }

  if (!x || !slot) return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
    <div className="stat skeleton" style={{ height: 320, border: 0 }} /></div>;

  const at = new Date(slot.startsAt);
  const ends = new Date(at.getTime() + x.durationMinutes * 60000);

  const book = async () => {
    if (!state.user) { set({ authMode: 'login', authError: null, overlay: 'login' }); return; }
    setBusy(true);
    try {
      // docs/07 §2 and §4 — what was actually chosen above, not a hard-coded card.
      const typed = document.getElementById('xp-card-number')?.value?.replace(/\D/g, '') ?? '';
      const b = await api.bookExperience(slotId, {
        seats,
        private: priv,
        paymentMethod: state.payMethod ?? 'card',
        cardLast4: state.payCardLast4 ?? (typed.length >= 4 ? typed.slice(-4) : null),
        holdId: hold?.holdId ?? null
      });
      // docs/07 §2.3 — a transfer books nothing yet: the seats are held and the
      // guest goes to the QR. Saying "đã đặt" here would be a lie until the
      // money is found on a statement.
      // docs/07 §13 — a licensed gateway takes the money on its own page.
      if (b.gatewayRedirectUrl) { window.location.assign(b.gatewayRedirectUrl); return; }
      if (b.status === 'AwaitingPayment') { navigate(`/chuyen-khoan/${b.reference}`); return; }
      toast(`${t('Đã đặt — mã')} ${b.reference}`);
      navigate('/experiences/bookings');
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '26px 90px' }}>
      <button className="back-link" onClick={() => navigate(`/experiences/${slug}`)}>
        ← {t('Quay lại')}
      </button>
      <h1 className="section-title" style={{ marginTop: 10 }}>{t('Xác nhận và thanh toán')}</h1>

      <div className="trip-layout">
        <div style={{ minWidth: 0, display: 'grid', gap: 16 }}>
          <section className="pay-step">
            <h2><i>1</i> {t('Vé của bạn')}</h2>
            <div className="kv-grid">
              <Kv icon="calendar" label={t('Suất')}
                  value={`${dayLabel(at)} · ${TIME().format(at)} – ${TIME().format(ends)}`} />
              <Kv icon="users" label={t('Số người')} value={`${seats} ${t('người')}`} />
              <Kv icon="pin" label={t('Điểm hẹn')} value={x.meetingPoint} />
            </div>
            {priv && (
              <p className="pay-demo" style={{ marginTop: 14 }}>
                <Icon name="users" size={16} />
                {t('Thuê trọn nhóm riêng')} — {t('Chỉ nhóm bạn, không ghép với khách khác')}
              </p>
            )}
          </section>

          <section className="pay-step">
            <h2><i>2</i> {t('Cách thanh toán')}</h2>
            <PaymentMethods idPrefix="xp-card" />
          </section>

          <section className="pay-step">
            <h2><i>3</i> {t('Xem lại và xác nhận')}</h2>
            {quote && !quote.canBook &&
              <div className="book-alert is-error" style={{ marginTop: 0 }}>
                <b>{t('Chưa đặt được')}</b><span>{quote.reason}</span>
              </div>}
            <p className="pay-demo" style={{ marginBottom: 14 }}>
              <Icon name="shield" size={16} />
              {t('Huỷ trước 7 ngày hoàn toàn bộ, trước 24 giờ hoàn 50%.')}
            </p>
            <button className="btn btn-primary" style={{ width: '100%' }}
                    disabled={busy || !quote || !quote.canBook} onClick={book}>
              {busy ? t('Đang xử lý…') : t('Xác nhận và thanh toán')}
            </button>
          </section>
        </div>

        <aside className="receipt">
          <div className="receipt-head">
            {!!x.images.length && (
              <img src={x.images[0]} alt="" loading="lazy" decoding="async"
                   style={{ width: 76, height: 76, objectFit: 'cover', borderRadius: 12, flex: '0 0 auto' }} />
            )}
            <div style={{ minWidth: 0 }}>
              <b><TranslatedText as="span" text={x.title} notice={false} /></b>
              <div className="meta">{x.hostName}</div>
              {!!x.reviewCount && (
                <div className="meta">★ {x.rating.toFixed(2)} ({x.reviewCount})</div>
              )}
            </div>
          </div>

          <div className="kv-grid" style={{ marginTop: 18 }}>
            <Kv icon="calendar" label={t('Ngày')}
                value={`${dayLabel(at)} · ${TIME().format(at)} – ${TIME().format(ends)}`} />
            <Kv icon="users" label={t('Số người')} value={`${seats} ${t('người')}`} />
            <Kv icon="globe" label={t('Ngôn ngữ')} value={languagesOf(x.languages)} />
          </div>

          {quote && (
            <div className="book-lines" style={{ marginTop: 18 }}>
              {quote.lines.map(l => (
                <div className="book-line" key={l.key}><span>{t(l.label)}</span><b>{money(l.amount)}</b></div>
              ))}
              <div className="book-line is-total"><span>{t('Tổng')}</span><b>{money(quote.total)}</b></div>
            </div>
          )}
        </aside>
      </div>
    </div>
  );
}

/** A label over its value, for the summary card on the checkout page. */
function Kv({ icon, label, value }) {
  return (
    <div className="kv">
      <span className="kv-label kv-ic">
        {icon && <Icon name={icon} size={14} />}{label}
      </span>
      <b>{value}</b>
    </div>
  );
}

/** One thing worth knowing before booking: an icon, a heading and a single line. */
function Know({ icon, title, body }) {
  return (
    <div className="xp-know-item">
      <span className="xp-know-ic"><Icon name={icon} size={22} /></span>
      <b>{title}</b>
      <span>{body}</span>
    </div>
  );
}

/**
 * docs/09 §2.10 (MR-E-11) — what people who were actually there wrote. Laid out
 * like the stay's review block so the page reads as one site, but scored on the
 * four headings an experience has rather than the six a stay has.
 */
function ExperienceReviews({ experience }) {
  const [rows, setRows] = useState(null);

  useEffect(() => {
    api.experienceReviews(experience.id).then(setRows).catch(err => toast(err.message));
  }, [experience.id]);

  if (!rows) return null;

  if (!rows.length) {
    return (
      <section className="detail-section">
        <h2>{t('Đánh giá')}</h2>
        <p className="section-sub">{t('Buổi này chưa có đánh giá nào. Chỉ người có mặt mới viết được.')}</p>
      </section>
    );
  }

  const average = key => rows.reduce((sum, r) => sum + r[key], 0) / rows.length;

  return (
    <section className="detail-section">
      <h2>{t('Đánh giá')}</h2>

      <div className="rating-summary">
        <span className="rating-big">★ {experience.rating.toFixed(2)}</span>
        <span style={{ fontSize: 15, color: 'var(--ink-muted)' }}>
          · {experience.reviewCount} {t('đánh giá')}
        </span>
      </div>

      <div className="rating-bars">
        {XP_CRITERIA.map(([key, label]) => (
          <div className="rating-bar" key={key}>
            <span>{t(label)}</span>
            <span className="track"><span className="fill" style={{ width: `${(average(key) / 5) * 100}%` }} /></span>
            <span className="val">{average(key).toFixed(1)}</span>
          </div>
        ))}
      </div>

      <div className="review-grid">
        {rows.map(r => (
          <article className="review" key={r.id}>
            <div className="review-head">
              <Avatar url={r.authorAvatarUrl} initials={initialsOf(r.authorName)} />
              <div style={{ minWidth: 0 }}>
                <div className="review-name">{r.authorName}</div>
                <div className="review-when">{longDate(r.createdAt.slice(0, 10))}</div>
              </div>
            </div>
            <div className="topic-row" style={{ margin: '4px 0 0' }}>
              {XP_CRITERIA.map(([key, label]) => (
                <span className="topic-chip" key={key}>{t(label)} <b>★ {r[key]}</b></span>
              ))}
            </div>
            {!!r.comment && <p>{r.comment}</p>}
          </article>
        ))}
      </div>
    </section>
  );
}

/**
 * docs/09 §2.9 — a session is over once it has started and run its length. The
 * server has the last word on who may review (only somebody the host marked
 * present), so this is only about not offering a form that cannot possibly work.
 */
const sessionEnded = r =>
  new Date(r.startsAt).getTime() + r.durationMinutes * 60000 <= Date.now();

/**
 * docs/09 §2.10 — "chỉ người có mặt mới đánh giá được". The ticket now carries
 * the host's mark and whether a review already exists, so a no-show is never
 * offered a form the server would only refuse, and nobody is asked twice.
 */
const canReviewTicket = r =>
  sessionEnded(r) && r.attended === true && !r.hasReview
  && (r.status === 'Confirmed' || r.status === 'Completed');

/**
 * docs/09 §2.10 (MR-E-11) — four criteria of the experience's own and an
 * optional word. Nothing from the stay's form belongs here.
 */
function TicketReview({ booking, onDone }) {
  const [scores, setScores] = useState({ host: 5, asDescribed: 5, safety: 5, value: 5 });
  const [busy, setBusy] = useState(false);

  const stars = key => (
    <div className="star-row" data-field={key}>
      {[1, 2, 3, 4, 5].map(n => (
        <button type="button" key={n} aria-label={`${n} ${t('sao')}`}
                className={`star sm ${n <= scores[key] ? 'is-on' : ''}`}
                onClick={() => setScores(s => ({ ...s, [key]: n }))}>★</button>
      ))}
    </div>
  );

  const submit = async e => {
    e.preventDefault();
    const comment = e.currentTarget.comment.value.trim();
    setBusy(true);
    try {
      await api.reviewExperienceBooking(booking.id, { ...scores, comment });
      toast(t('Đã gửi đánh giá. Cảm ơn bạn.'));
      onDone();
    } catch (err) {
      // "Chỉ người có mặt trong buổi mới đánh giá được", "Bạn đã đánh giá buổi
      // này rồi" — the server's sentence, said as it is.
      toast(err.message);
    } finally { setBusy(false); }
  };

  return (
    <form onSubmit={submit}
          style={{ flexBasis: '100%', minWidth: 0, marginTop: 14, paddingTop: 14, borderTop: '1px solid var(--divider)' }}>
      <b style={{ fontSize: 15 }}>{t('Buổi này thế nào?')}</b>
      {XP_CRITERIA.map(([key, label]) => (
        <div className="count-row" key={key}>
          <div className="tx"><b>{t(label)}</b></div>
          {stars(key)}
        </div>
      ))}

      <label className="form-field" style={{ marginTop: 14 }}>
        <span className="cap">{t('Nhận xét')} <span style={{ fontWeight: 400 }}>{t('(không bắt buộc)')}</span></span>
        <textarea name="comment" rows={4}
                  placeholder={t('Người dẫn kể chuyện thế nào? Bạn có thấy an toàn không?')}
                  style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
      </label>

      <button type="submit" className="btn btn-primary btn-sm" disabled={busy}>
        {busy ? t('Đang gửi…') : t('Gửi đánh giá')}
      </button>
    </form>
  );
}

/** The tickets someone holds, with the one action they have: giving one back. */
export function ExperienceBookings() {
  const state = useStore();
  const navigate = useNavigate();
  const [rows, setRows] = useState(null);
  // Which ticket has its review form open, and which ones this visit has sent.
  const [reviewing, setReviewing] = useState(null);
  const [sent, setSent] = useState([]);

  const load = () => api.experienceBookings().then(setRows).catch(e => toast(e.message));
  useEffect(() => { if (state.user) load(); }, [state.user]);

  if (!state.user) {
    return <div className="shell" style={{ paddingBlock: '60px 90px' }}>
      <div className="empty-state"><h3>{t('Đăng nhập để xem vé của bạn')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>{t('Đăng nhập')}</button>
      </div></div>;
  }

  const cancel = async row => {
    if (!confirm(`${t('Huỷ vé')} ${row.reference}?`)) return;
    try {
      const after = await api.cancelExperienceBooking(row.id);
      toast(after.refundedAmount > 0
        ? `${t('Đã huỷ, hoàn')} ${number(after.refundedAmount)}₫.`
        : t('Đã huỷ. Huỷ sát giờ nên không hoàn tiền.'));
      load();
    } catch (err) { toast(err.message); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <h1 className="section-title">{t('Vé trải nghiệm')}</h1>

      {!rows ? <div className="stat skeleton" style={{ height: 200, border: 0, marginTop: 24 }} />
        : rows.length ? (
          <div style={{ marginTop: 20, display: 'grid', gap: 12 }}>
            {rows.map(r => (
              <article className="host-booking" key={r.id}>
                <div style={{ minWidth: 0 }}>
                  {/* The host named the experience — machine translation, not the dictionary. */}
                  <h3><TranslatedText as="span" text={r.title} notice={false} /></h3>
                  <div className="meta">
                    {longDate(r.startsAt.slice(0, 10))} · {TIME().format(new Date(r.startsAt))} ·
                    {' '}{r.seats} {t('chỗ')}{r.private ? ` · ${t('nhóm riêng')}` : ''} · {money(r.total)}
                  </div>
                  <div className="meta">{t('Mã')} {r.reference} · {r.city}</div>
                  {r.cancelReason && <div className="meta">{t(r.cancelReason)}</div>}
                  {r.refundedAmount > 0 && <div className="meta">{t('Đã hoàn')} {money(r.refundedAmount)}</div>}
                  <span className={`badge ${r.statusBadge}`} style={{ marginTop: 8 }}>{t(r.statusLabel)}</span>
                </div>
                <div className="host-booking-actions">
                  <button className="btn btn-outline btn-sm"
                          onClick={() => navigate(`/experiences/${r.slug}`)}>{t('Xem trải nghiệm')}</button>
                  {r.status === 'Confirmed' &&
                    <button className="btn btn-outline btn-sm" onClick={() => cancel(r)}>{t('Huỷ vé')}</button>}
                  {/* docs/09 §2.10 — only somebody the host marked present, and
                      only once. Both facts come off the ticket now. */}
                  {r.hasReview || sent.includes(r.id)
                    ? (sessionEnded(r) && <span className="badge confirmed">{t('Đã đánh giá')}</span>)
                    : canReviewTicket(r) && (
                        <button className="btn btn-outline btn-sm"
                                onClick={() => setReviewing(id => id === r.id ? null : r.id)}>
                          {reviewing === r.id ? t('Đóng') : t('Đánh giá buổi này')}
                        </button>
                      )}
                  {/* A no-show is told why rather than left wondering. */}
                  {sessionEnded(r) && r.attended === false && (
                    <span className="badge cancelled">{t('Vắng mặt')}</span>
                  )}
                </div>
                {reviewing === r.id && !sent.includes(r.id) && (
                  <TicketReview booking={r}
                                onDone={() => { setSent(ids => [...ids, r.id]); setReviewing(null); load(); }} />
                )}
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-state" style={{ marginTop: 24 }}>
            <h3>{t('Chưa có vé nào')}</h3>
            <button className="btn btn-primary" style={{ marginTop: 18 }}
                    onClick={() => navigate('/experiences')}>{t('Xem trải nghiệm')}</button>
          </div>
        )}
    </div>
  );
}
