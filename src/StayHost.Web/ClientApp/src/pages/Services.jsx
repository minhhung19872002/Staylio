import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { set, toast, shareListing, openReport } from '../lib/store.js';
import { api } from '../lib/api.js';
import { money, longDate, dateFormat } from '../lib/format.js';
import { CardCarousel } from '../components/CardCarousel.jsx';
import { PhotoMosaic } from '../components/PhotoMosaic.jsx';
import { Avatar } from '../components/Avatar.jsx';
import { Icon } from '../components/Icon.jsx';
import { DetailMap } from '../components/Maps.jsx';
import { Sheet } from '../components/modals/Sheet.jsx';
import { PaymentMethods } from '../components/PaymentMethods.jsx';
import { t } from '../lib/i18n.js';
import { TranslatedText } from '../components/TranslatedText.jsx';

// Asked for per render so they follow the chosen language (format.js LOCALE).
const TIME = () => dateFormat({ hour: '2-digit', minute: '2-digit' });
const DAY = () => dateFormat({ weekday: 'long', day: 'numeric', month: 'long' });
const MONTH = () => dateFormat({ month: 'long', year: 'numeric' });

const CATEGORIES = [
  ['', 'Tất cả'], ['chef', 'Đầu bếp'], ['photo', 'Chụp ảnh'], ['massage', 'Massage'],
  ['transfer', 'Đưa đón'], ['luggage', 'Giữ hành lý'], ['groceries', 'Đi chợ hộ'],
  ['car', 'Thuê xe']
];

/** The category as a guest reads it, for the "Đầu bếp ở Quận 1" line. */
const CATEGORY_LABEL = Object.fromEntries(CATEGORIES.slice(1));

/** Two letters for somebody with no photo, the way the server builds them. */
const initialsOf = name => (name || '?')
  .trim().split(/\s+/).slice(-2).map(w => w[0] ?? '').join('').toUpperCase() || '?';

/** "1 giờ 30 phút" — shared with the option cards and the slot rows. */
const duration = minutes =>
  minutes >= 60
    ? `${Math.floor(minutes / 60)} ${t('giờ')}${minutes % 60 ? ` ${minutes % 60} ${t('phút')}` : ''}`
    : `${minutes} ${t('phút')}`;

/**
 * docs/09 §5 — a service is scored on four headings of its own, and they are not
 * the experience's four: nobody is being led, and "tổ chức và an toàn" is not
 * what a guest judges a haircut on. Kept in the order ServiceReviews.Criteria
 * lists them.
 */
const SVC_CRITERIA = [
  ['skill', 'Tay nghề'],
  ['asDescribed', 'Đúng như mô tả'],
  ['punctuality', 'Đúng giờ'],
  ['value', 'Đáng giá tiền']
];

/** docs/01 MR-05 → MR-07 — services booked by the slot, at an address you give. */
export function Services() {
  const { slug } = useParams();
  return slug ? <Detail slug={slug} /> : <Browse />;
}

function Browse() {
  const navigate = useNavigate();
  const [category, setCategory] = useState('');
  const [items, setItems] = useState(null);

  useEffect(() => {
    api.services({ category: category || undefined })
      .then(setItems)
      .catch(e => toast(e.message));
  }, [category]);

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <h1 className="section-title">{t('Dịch vụ')}</h1>
      <p className="section-sub">{t('Đầu bếp, chụp ảnh, đưa đón — đặt theo khung giờ, làm tại chỗ bạn ở.')}</p>

      <div className="seg-tabs" style={{ marginTop: 16 }}>
        {CATEGORIES.map(([key, label]) => (
          <button key={key || 'all'} className={`seg-tab ${category === key ? 'is-active' : ''}`}
                  onClick={() => setCategory(key)}>{t(label)}</button>
        ))}
      </div>

      {!items ? (
        <div className="card-grid" style={{ marginTop: 24 }}>
          {Array.from({ length: 4 }, (_, i) => (
            <div className="card skeleton" key={i} style={{ height: 280, border: 0 }} />
          ))}
        </div>
      ) : items.length ? (
        <div className="card-grid" style={{ marginTop: 24 }}>
          {items.map(s => (
            <article className="card" key={s.id}>
              {/* Photos keep the click handler — the carousel controls inside them
                  cannot live within an <a>. The body is the crawlable link. */}
              <div onClick={() => navigate(`/services/${s.slug}`)} style={{ cursor: 'pointer' }}>
                <CardCarousel images={s.images} alt={s.title} />
              </div>
              <a className="card-body" href={`/services/${s.slug}`}
                 onClick={e => {
                   if (e.metaKey || e.ctrlKey || e.shiftKey || e.button === 1) return;
                   e.preventDefault();
                   navigate(`/services/${s.slug}`);
                 }}>
                <div className="card-row">
                  <h3 className="card-title"><TranslatedText as="span" text={s.title} notice={false} /></h3>
                  <div className="card-rating">
                    {s.reviewCount ? `★ ${s.rating.toFixed(2)} (${s.reviewCount})` : `★ ${t('Mới')}`}
                  </div>
                </div>
                {/* Airbnb's service cards read "who does it, where" before the
                    price; the pricing model belongs beside the number instead. */}
                <div className="card-sub card-line">
                  {s.hostName}{s.isPartner ? ` · ${s.partnerName}` : ''} · {s.city}
                </div>
                <div className="card-price">
                  <b>{money(s.basePrice)}</b> <span>/ {t(s.unit)}</span>
                  {s.durationMinutes > 0 && <span> · {duration(s.durationMinutes)}</span>}
                </div>
                <div className="card-perks card-line">
                  {s.travelsToGuest ? `${t('Tới tận nơi trong')} ${s.serviceRadiusKm} km` : t('Khách tới chỗ cung cấp')}
                </div>
              </a>
            </article>
          ))}
        </div>
      ) : (
        <div className="empty-state" style={{ marginTop: 28 }}><h3>{t('Chưa có dịch vụ nào ở nhóm này')}</h3></div>
      )}
    </div>
  );
}

/**
 * docs/09 §3.4 (MR-S-05) — the provider's working week, read the way
 * ServiceRules.WorksOn reads it: Monday is bit 0 and Sunday bit 6.
 */
function worksOn(detail, date) {
  // `|| 127`, not `?? 127`: a mask of 0 reaches here from rows that predate the
  // field, and it does not mean "works no day" — it means nobody ever answered.
  // ServiceRules.WorkingDays reads it the same way on the server.
  const mask = detail.workingDaysMask || 127;
  const day = date.getDay();
  return (mask & (1 << (day === 0 ? 6 : day - 1))) !== 0;
}

/**
 * docs/09 §3.4 — nothing may be booked closer than this to now
 * (ServiceRules.MinimumNotice). Kept here so the picker never offers a row the
 * server is bound to refuse.
 */
const MINIMUM_NOTICE_MS = 4 * 3600_000;

/** How far ahead the rail and the month grid look. */
const HORIZON_DAYS = 30;

/**
 * How far apart two start times are offered. Half an hour, not two: a provider
 * open 9:00–18:00 has eighteen ways to fit a job into their day and the picker
 * used to offer five of them, so a guest who wanted 10:30 was told to come at
 * 10:00 or 12:00. Consecutive offers overlap — the diary decides which of them
 * survives, not the grid.
 */
const STEP_MINUTES = 30;

/** Every half-hour a job could start inside the working day, for the next month. */
function slotsFor(detail) {
  const out = [];
  const now = new Date();
  const earliest = now.getTime() + MINIMUM_NOTICE_MS;
  const closes = detail.closesAtHour * 60;

  for (let d = 0; d < HORIZON_DAYS; d++) {
    const dayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate() + d);
    // A day the provider does not work at all never becomes a slot to click.
    if (!worksOn(detail, dayStart)) continue;

    // The job has to finish before closing, counted in minutes rather than
    // whole hours: a 90-minute job starting at 16:30 ends at 18:00, and reading
    // that as "hour 18" made it look like it ran past a 18:00 close.
    for (let m = detail.opensAtHour * 60; m + detail.durationMinutes <= closes; m += STEP_MINUTES) {
      const at = new Date(dayStart.getTime());
      at.setMinutes(m);
      if (at.getTime() < earliest) continue;

      const ends = new Date(at.getTime() + detail.durationMinutes * 60000);
      const taken = detail.busy.some(b => at < new Date(b.to) && new Date(b.from) < ends);
      out.push({ at, ends, taken });
    }
  }
  return out;
}

/** "Hôm nay · Thứ Tư, 12 tháng 8" — the day a session falls on, said the short way. */
function dayLabel(date) {
  const today = new Date();
  const tomorrow = new Date(today.getTime() + 86400000);
  const written = DAY().format(date);

  if (sameDay(date, today)) return `${t('Hôm nay')} · ${written}`;
  if (sameDay(date, tomorrow)) return `${t('Ngày mai')} · ${written}`;
  return written;
}

const sameDay = (a, b) =>
  a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();

const dayKey = d => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;

const midnight = d => new Date(d.getFullYear(), d.getMonth(), d.getDate());

/**
 * The weekday initials over a month, in the reader's language, starting Monday —
 * the same anchor Calendar.jsx uses (5 Jan 1970 was a Monday) so the two grids
 * on the site do not start their weeks on different days.
 */
const DOW_NARROW = () => {
  const f = dateFormat({ weekday: 'narrow' });
  return Array.from({ length: 7 }, (_, i) => f.format(new Date(1970, 0, 5 + i)));
};

/** Monday = 0 … Sunday = 6, to match the grid above. */
const mondayIndex = date => (date.getDay() + 6) % 7;

function Detail({ slug }) {
  const navigate = useNavigate();
  const [s, setS] = useState(null);
  const [missing, setMissing] = useState(false);
  const [when, setWhen] = useState(null);
  const [quantity, setQuantity] = useState(1);
  // docs/09 §3.3 (MR-S-03) — the paid extras ticked, from the cards on the page
  // or from the dialog; both write here and both carry through to checkout.
  const [addOnIds, setAddOnIds] = useState([]);
  const [booking, setBooking] = useState(false);

  const load = () => api.service(slug).then(d => {
    setS(d);
    setQuantity(q => Math.max(q, d.minQuantity));
  }).catch(() => setMissing(true));

  useEffect(() => { load(); }, [slug]);

  if (missing) {
    return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
      <div className="empty-state"><h3>{t('Không tìm thấy dịch vụ này')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => navigate('/services')}>{t('Xem tất cả')}</button></div></div>;
  }

  if (!s) return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
    <div className="stat skeleton" style={{ height: 300, border: 0 }} /></div>;

  /*
   * Choosing an hour ends the dialog and opens the checkout page, the way
   * Airbnb's "Show dates" ends at Confirm and pay. The choice travels in the
   * address bar rather than in router state so a reload, a back button or a
   * pasted link all land on the same booking instead of an empty page.
   */
  const toCheckout = () => {
    const q = new URLSearchParams({ at: when.toISOString(), qty: String(quantity) });
    if (addOnIds.length) q.set('addons', addOnIds.join(','));
    navigate(`/services/${slug}/thanh-toan?${q}`);
  };

  const addOns = s.addOns ?? [];
  // docs/09 §3.3 (MR-S-07) — a service with conditions cannot be booked until the
  // guest has said the place meets them; that answer is what makes §3.6's "khai
  // sai điều kiện" rule fair, so it is a tick of its own, not fine print.
  const requirements = s.onSiteRequirements ?? [];
  const cover = s.images[0];
  const category = t(CATEGORY_LABEL[s.category] ?? 'Dịch vụ');

  /* An extra is picked from the list on the right as well as from inside the
     dialog, so the two share one handler rather than each keeping their own. */
  const toggleAddOn = id =>
    setAddOnIds(ids => ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]);

  const openBooking = () => setBooking(true);

  return (
    <div className="shell" style={{ paddingBlock: '26px 90px' }}>
      <button className="back-link" onClick={() => navigate('/services')}>← {t('Dịch vụ')}</button>

      {/*
        * The provider on the left, what they sell on the right. A service is
        * bought from a person who is going to turn up at your address, so the
        * rail that stays on screen is their face, their trade and their price —
        * and the column that scrolls is everything the guest reads to decide.
        */}
      <div className="svc-page">
        <aside className="svc-rail">
          <div className="svc-id">
            <button className="svc-cover" onClick={() => {
              document.getElementById('svc-portfolio')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }} aria-label={t('Xem bộ ảnh')}>
              <img src={cover} alt={s.title} decoding="async" />
              <Avatar className="avatar svc-face" url={s.hostAvatarUrl} initials={s.hostInitials} />
            </button>

            <h1><TranslatedText as="span" text={s.title} /></h1>
            <p className="svc-id-sub"><TranslatedText as="span" text={s.summary} notice={false} /></p>

            <div className="svc-id-meta">
              {s.reviewCount
                ? <span><b>★ {s.rating.toFixed(2)}</b> · {s.reviewCount} {t('đánh giá')}</span>
                : <span className="muted">{t('Chưa có đánh giá')}</span>}
              {/* "Đầu bếp ở Quận 1" — a trade and a place, the way Airbnb names
                  a provider. Two keys around the city rather than one sentence,
                  so the word order survives translation. */}
              <span className="muted">{category} · {s.city}</span>
              <span className="muted">
                {s.travelsToGuest ? t('Phục vụ tận nơi') : t('Khách tới chỗ cung cấp')}
                {s.isPartner ? ` · ${t('do')} ${s.partnerName} ${t('thực hiện')}` : ''}
              </span>
            </div>

            <div className="svc-id-tools">
              <button onClick={() => shareListing({ title: s.title })} aria-label={t('Chia sẻ')}>
                <Icon name="share" size={19} />
              </button>
            </div>
          </div>

          {/* docs/09 §3.6 — the ladder in one line, where a guest looks for it. */}
          <div className="svc-pill">
            <p><b>{t('Huỷ miễn phí')}</b> · {t('trước 72 giờ được hoàn toàn bộ')}</p>
            <span className="ic"><Icon name="calendar" size={22} /></span>
          </div>

          <div className="svc-buy">
            <span className="svc-buy-price">
              {t('Từ')} <b>{money(s.basePrice)}</b> / {t(s.unit)}
            </span>
            <button className="btn btn-primary" onClick={openBooking}>{t('Xem lịch')}</button>
          </div>
        </aside>

        <div className="svc-body">
          <section className="detail-section" style={{ paddingTop: 0 }}>
            <h2>{t('Dịch vụ này gồm gì')}</h2>
            <TranslatedText as="p" style={{ fontSize: 15.5, lineHeight: 1.75, color: 'var(--ink-body)' }}
                            text={s.description} />
          </section>

          {/*
            * docs/09 §3.3 (MR-S-03) — what is actually on sale, priced one line
            * at a time. The base job is the first card because it is the thing
            * being bought; each extra is a card of its own beneath it, so a guest
            * sees what each one costs before it lands on the bill rather than
            * after. Ticking one here and ticking it in the dialog is the same act.
            */}
          <section className="detail-section">
            <h2>{t('Chọn gói')}</h2>
            <div className="svc-options">
              <button type="button" className="svc-option is-on" onClick={openBooking}>
                <img src={cover} alt="" loading="lazy" decoding="async" />
                <span className="svc-option-body">
                  <span className="svc-option-tag">{t('Gói cơ bản')}</span>
                  <h3><TranslatedText as="span" text={s.title} notice={false} /></h3>
                  <p><TranslatedText as="span" text={s.summary} notice={false} /></p>
                  <span className="svc-option-price">
                    <b>{money(s.basePrice)}</b> / {t(s.unit)} · {duration(s.durationMinutes)}
                  </span>
                </span>
              </button>

              {addOns.map((a, i) => (
                <button type="button" key={a.id}
                        className={`svc-option ${addOnIds.includes(a.id) ? 'is-on' : ''}`}
                        onClick={() => toggleAddOn(a.id)}>
                  <img src={s.images[(i + 1) % s.images.length] ?? cover} alt="" loading="lazy" decoding="async" />
                  <span className="svc-option-body">
                    <span className="svc-option-tag">
                      {addOnIds.includes(a.id) ? t('Đã chọn') : t('Tuỳ chọn thêm')}
                    </span>
                    {/* The provider named these extras themselves. */}
                    <h3><TranslatedText as="span" text={a.name} notice={false} /></h3>
                    <span className="svc-option-price">
                      <b>+{money(a.price)}</b> · {t('cộng vào gói cơ bản')}
                    </span>
                  </span>
                </button>
              ))}
            </div>
            {/* Not "Nhắn cho {tên} nếu…": a sentence with a name dropped into the
                middle of it only reads in languages that put the object there. */}
            <p className="section-sub" style={{ marginTop: 14 }}>
              {t('Muốn điều chỉnh cho hợp hơn? Nhắn cho nhà cung cấp.')}
            </p>
          </section>

          <ServiceReviews service={s} />

          {/* docs/09 §3.2 — vetting is the whole reason this row of a stranger's
              credentials exists, so it is said with the same weight Airbnb gives
              "My qualifications" rather than buried in a fact grid. */}
          <section className="detail-section">
            <h2>{t('Trình độ của tôi')}</h2>
            <div className="svc-qual">
              <div className="svc-qual-head">
                <Avatar url={s.hostAvatarUrl} initials={s.hostInitials} />
                <div>
                  <b>{s.hostName}</b>
                  <span>
                    {category}
                    {s.hostIsSuperhost ? ` · ${t('Siêu chủ nhà')}` : ''}
                  </span>
                </div>
              </div>

              {s.hostYears > 0 && (
                <div className="svc-qual-item">
                  <b>{s.hostYears} {t('năm kinh nghiệm')}</b>
                </div>
              )}

              {s.hostBio && (
                <div className="svc-qual-item">
                  <TranslatedText as="span" text={s.hostBio} />
                </div>
              )}

              {s.certificateName && (
                <div className="svc-qual-item">
                  <b>{t('Chứng chỉ hành nghề')}</b>
                  <span>
                    <TranslatedText as="span" text={s.certificateName} notice={false} />
                    {s.certificateExpiresOn
                      ? ` · ${t('có hiệu lực đến')} ${longDate(s.certificateExpiresOn)}`
                      : ''}
                  </span>
                </div>
              )}

              <div>
                <button className="btn btn-outline" onClick={() => navigate('/messages')}>
                  {t('Nhắn cho nhà cung cấp')}
                </button>
              </div>

              <p className="svc-safe">
                {t('Để được bảo vệ, hãy luôn thanh toán và trao đổi qua Staylio.')}
              </p>
            </div>
          </section>

          {!!s.images.length && (
            <section className="detail-section" id="svc-portfolio">
              <h2>{t('Bộ ảnh của tôi')}</h2>
              <PhotoMosaic images={s.images} alt={s.title} />
            </section>
          )}

          <section className="detail-section">
            <h2>{t('Nơi thực hiện')}</h2>
            <p className="section-sub" style={{ marginBottom: 14 }}>
              {s.travelsToGuest
                ? `${t('Tới tận nơi trong')} ${s.serviceRadiusKm} km ${t('quanh')} ${s.city}`
                : `${t('Khách tới chỗ cung cấp')} · ${s.city}`}
            </p>
            <DetailMap latitude={s.latitude} longitude={s.longitude} />
          </section>

          <section className="detail-section">
            <h2>{t('Cần biết')}</h2>
            <div className="xp-know">
              <Know icon="users" title={t('Nhận việc')}
                    body={`${s.minQuantity}–${s.maxQuantity} ${t(s.unit)} ${t('mỗi lần')}`} />
              <Know icon="calendar" title={t('Giờ nhận')}
                    body={`${s.opensAtHour}:00 – ${s.closesAtHour}:00 · ${t('đặt trước ít nhất 4 giờ')}`} />
              <Know icon="pin" title={t('Phạm vi phục vụ')}
                    body={s.travelsToGuest
                      ? (s.travelFeePerKm > 0
                          ? `${s.serviceRadiusKm} km ${t('miễn phí')}, ${t('xa hơn')} ${money(s.travelFeePerKm)}/km`
                          : `${t('Tới tận nơi trong')} ${s.serviceRadiusKm} km`)
                      : t('Khách tới chỗ cung cấp')} />
              {/* The ladder as ServiceRules actually applies it: 72 hours, then
                  24, then nothing. The page used to promise a full refund at 24
                  hours, which is half of what the guest would really get. */}
              <Know icon="selfcheckin" title={t('Chính sách huỷ')}
                    body={t('Trước 72 giờ hoàn 100%, trước 24 giờ hoàn 50%, sát giờ không hoàn.')} />
            </div>

            {!!requirements.length && (
              <div style={{ marginTop: 20 }}>
                <b style={{ fontSize: 15 }}>{t('Nơi thực hiện cần có')}</b>
                <ul style={{ margin: '8px 0 0', paddingLeft: 20, lineHeight: 1.8, color: 'var(--ink-body)' }}>
                  {requirements.map(r => (
                    <li key={r}><TranslatedText as="span" text={r} notice={false} /></li>
                  ))}
                </ul>
              </div>
            )}
          </section>

          {/* docs/09 §3.2 — "duyệt thủ công, không tự động", and stricter than an
              experience. That is a selling point, so it is said out loud. */}
          <section className="detail-section">
            <div className="svc-trust">
              <h3>{t('Nhà cung cấp trên Staylio đều được thẩm định')}</h3>
              <p>
                {t('Mỗi hồ sơ được người thật xét duyệt: xác minh danh tính, kiểm tra lý lịch tư pháp với dịch vụ tới tận nhà, và chứng chỉ hành nghề còn hạn theo từng nghề.')}
              </p>
            </div>
            {/* docs/01 AT-02 — this used to walk to the help centre, which is a
                reading room, not a report. A service is not one of the four
                report targets, so what is reported is the person offering it. */}
            <p className="svc-report">
              {t('Thấy điều gì bất thường?')}{' '}
              {s.hostUserId
                ? <button className="link-btn"
                          onClick={() => openReport('user', s.hostUserId, s.hostName)}>
                    {t('Báo cáo nhà cung cấp này')}
                  </button>
                : <button className="link-btn" onClick={() => navigate('/help')}>{t('Liên hệ hỗ trợ')}</button>}
            </p>
          </section>
        </div>
      </div>

      {booking && (
        <BookingSheet
          service={s} slots={slotsFor(s)} when={when} onPick={setWhen}
          quantity={quantity} setQuantity={setQuantity}
          onContinue={toCheckout} onClose={() => setBooking(false)} />
      )}
    </div>
  );
}

/**
 * docs/09 §3.5 — everything a service needs before it can be sent, in the
 * dialog the price card opens. It used to sit permanently in a side panel, which
 * meant a guest read the address field and the allergy note before they had
 * chosen a time — and the page could show only the first fourteen slots, because
 * a taller list would have pushed the price off the screen.
 */
function BookingSheet({
  service: s, slots, when, onPick, quantity, setQuantity, onContinue, onClose
}) {
  /*
   * Two views of one choice: 'when' is guests, a day and an hour and nothing
   * else, and 'calendar' is the month grid behind the calendar button — a
   * separate view rather than a popover, because a month grid inside a
   * scrolling dialog has nowhere to hang. Everything that only matters once an
   * hour exists — the address, the extras, the bill, the card — lives on the
   * checkout page this hands over to.
   */
  const [pane, setPane] = useState('when');
  const [chosenDay, setChosenDay] = useState(null);

  /** Every free slot, filed under the day it falls on. */
  const byDay = useMemo(() => {
    const map = new Map();
    for (const slot of slots) {
      const key = dayKey(slot.at);
      const list = map.get(key);
      if (list) list.push(slot); else map.set(key, [slot]);
    }
    return map;
  }, [slots]);

  const hasRoom = date => (byDay.get(dayKey(date)) ?? []).some(x => !x.taken);

  /**
   * A month of days whether or not the provider works them. Airbnb greys the
   * closed days rather than hiding them, and it is the difference between "they
   * are busy on Tuesday" and "Tuesday does not exist".
   */
  const rail = useMemo(() => {
    const start = midnight(new Date());
    return Array.from({ length: HORIZON_DAYS }, (_, i) =>
      new Date(start.getFullYear(), start.getMonth(), start.getDate() + i));
  }, []);

  // Derived rather than stored, so the dialog opens on the first day with room
  // without an effect that would have to fight the guest's own choice for it.
  const day = chosenDay ?? (when ? midnight(when) : rail.find(hasRoom) ?? null);

  const pickDay = date => {
    setChosenDay(date);
    // The chosen time belongs to the day it was chosen on. Keeping it while the
    // rail moved elsewhere is how a guest ends up booking a different date from
    // the one highlighted in front of them.
    if (when && !sameDay(when, date)) onPick(null);
    setPane('when');
  };

  const daySlots = day ? (byDay.get(dayKey(day)) ?? []) : [];
  const anyRoom = rail.some(hasRoom);

  const foot = pane === 'calendar'
    ? <button className="btn btn-dark" style={{ width: '100%' }}
              onClick={() => setPane('when')}>{t('Tiếp tục')}</button>
    : <>
        <span>
          {when
            ? <><b style={{ fontSize: 16 }}>{money(s.basePrice)}</b>{' '}
                <span style={{ color: 'var(--ink-muted)', fontSize: 13 }}>/ {t(s.unit)}</span></>
            : <span style={{ color: 'var(--ink-muted)', fontSize: 13.5 }}>
                {t('Chọn một khung giờ để tiếp tục.')}
              </span>}
        </span>
        <button className="btn btn-primary" disabled={!when} onClick={onContinue}>
          {t('Tiếp tục')}
        </button>
      </>;

  return (
    <Sheet title={t(pane === 'calendar' ? 'Chọn ngày' : 'Chọn khung giờ')} onClose={onClose} foot={foot}>
      {pane === 'calendar' ? (
        <MonthGrid rail={rail} day={day} hasRoom={hasRoom} onPick={pickDay} />
      ) : <>
        {/* docs/09 §3.3 — a flat-rate visit is one price however many people are
            named, so there is nothing to count. */}
        {s.pricing !== 'PerSession' && (
          <div className="count-row" style={{ paddingTop: 0 }}>
            <div className="tx">
              <b>{quantity} {t(s.unit)}</b>
              <span>{t('Nhận từ')} {s.minQuantity} {t('đến')} {s.maxQuantity}</span>
            </div>
            <div className="count-ctl">
              <button type="button" className="round-btn" aria-label={t('Giảm')}
                      disabled={quantity <= s.minQuantity}
                      onClick={() => setQuantity(q => Math.max(s.minQuantity, q - 1))}>−</button>
              <span className="num">{quantity}</span>
              <button type="button" className="round-btn" aria-label={t('Tăng')}
                      disabled={quantity >= s.maxQuantity}
                      onClick={() => setQuantity(q => Math.min(s.maxQuantity, q + 1))}>+</button>
            </div>
          </div>
        )}

        <div className="slot-month">
          <span>{MONTH().format(day ?? new Date())}</span>
          <button type="button" className="slot-month-cal"
                  aria-label={t('Mở lịch để tìm ngày còn trống')}
                  onClick={() => setPane('calendar')}>
            <Icon name="calendar" size={20} />
          </button>
        </div>

        {anyRoom ? <>
          {/*
            * A month of days on one scrolling rail rather than a stack of
            * headings. The stack made the reader scroll past every hour of
            * Tuesday to find out whether Wednesday had anything at all, and the
            * dialog could only ever be a fortnight long before it became
            * unreadable.
            */}
          <div className="slot-rail" role="group" aria-label={t('Chọn ngày')}>
            {rail.map(date => {
              const free = hasRoom(date);
              const on = day && sameDay(day, date);
              return (
                <button key={dayKey(date)} type="button" disabled={!free}
                        className={`slot-rail-day ${on ? 'is-on' : ''}`}
                        aria-pressed={!!on}
                        aria-label={`${DAY().format(date)}${free ? '' : ` — ${t('hết chỗ')}`}`}
                        onClick={() => pickDay(date)}>
                  <span>{DOW_NARROW()[mondayIndex(date)]}</span>
                  <b>{date.getDate()}</b>
                </button>
              );
            })}
          </div>

          {day && <>
            <h3 className="slot-day">{dayLabel(day)}</h3>

            {/* What is being bought at that hour, said once above the times
                rather than repeated on every one of them. */}
            <div className="slot-what">
              {!!s.images.length && <img src={s.images[0]} alt="" loading="lazy" decoding="async" />}
              <div>
                <b><TranslatedText as="span" text={s.title} notice={false} /></b>
                <span>{money(s.basePrice)} / {t(s.unit)} · {duration(s.durationMinutes)}</span>
              </div>
            </div>

            {daySlots.length ? (
              <div className="time-chips">
                {daySlots.map(({ at, taken }) => (
                  <button key={at.toISOString()} type="button" disabled={taken}
                          className={`time-chip ${when && +when === +at ? 'is-on' : ''}`}
                          onClick={() => onPick(at)}>
                    {TIME().format(at)}
                  </button>
                ))}
              </div>
            ) : <p className="slot-empty">{t('Ngày này không còn giờ trống.')}</p>}
          </>}
        </> : <p className="slot-empty">{t('Tháng tới chưa có khung giờ nào trống.')}</p>}
      </>}
    </Sheet>
  );
}

/**
 * docs/09 §3.5 and docs/07 §2 — "Xác nhận và thanh toán", a page of its own the
 * way Airbnb ends its picker on one. Three numbered steps down the left and the
 * booking itself pinned on the right, so what is being paid for stays in sight
 * while the address and the card are filled in.
 *
 * The choice arrives in the query string rather than in router state: a reload,
 * a back button and a pasted link then all land on the same booking instead of
 * an empty page.
 */
export function ServiceCheckout() {
  const { slug } = useParams();
  const [params] = useSearchParams();
  const state = useStore();
  const navigate = useNavigate();

  const [s, setS] = useState(null);
  const [missing, setMissing] = useState(false);
  const [address, setAddress] = useState('');
  const [note, setNote] = useState('');
  const [conditionsOk, setConditionsOk] = useState(false);
  const [addOnIds, setAddOnIds] = useState(
    (params.get('addons') ?? '').split(',').map(Number).filter(Boolean));
  const [quote, setQuote] = useState(null);
  const [busy, setBusy] = useState(false);

  const at = params.get('at');
  const when = at ? new Date(at) : null;
  const quantity = Math.max(1, Number(params.get('qty')) || 1);

  useEffect(() => {
    api.service(slug).then(setS).catch(() => setMissing(true));
  }, [slug]);

  useEffect(() => {
    if (!s || !when) return;
    // The address decides whether the provider will come at all, so it is part
    // of the quote rather than something checked at the end (docs/01 MR-05).
    api.quoteService(s.id, {
      startsAt: when.toISOString(),
      quantity,
      address: address.trim() || null,
      latitude: s.latitude + 0.01,
      longitude: s.longitude + 0.01,
      addOnIds,
      conditionsConfirmed: conditionsOk
    }).then(setQuote).catch(e => toast(e.message));
    // `when` is `at` parsed, so the string covers it and the object would only
    // re-run this on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [s, at, quantity, address, addOnIds, conditionsOk]);

  if (missing || (at && Number.isNaN(when?.getTime()))) {
    return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
      <div className="empty-state"><h3>{t('Không tìm thấy dịch vụ này')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => navigate('/services')}>{t('Xem tất cả')}</button></div></div>;
  }

  if (!s || !when) return <div className="shell" style={{ paddingBlock: '40px 90px' }}>
    <div className="stat skeleton" style={{ height: 320, border: 0 }} /></div>;

  const addOns = s.addOns ?? [];
  const requirements = s.onSiteRequirements ?? [];
  const ends = new Date(when.getTime() + s.durationMinutes * 60000);
  const blocked = (requirements.length > 0 && !conditionsOk)
    || (!!s.requiredNote && !note.trim());

  const toggleAddOn = id =>
    setAddOnIds(ids => ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]);

  const book = async () => {
    if (!state.user) { set({ authMode: 'login', authError: null, overlay: 'login' }); return; }
    setBusy(true);
    try {
      // docs/07 §2 and §4 — whatever the guest picked above: a saved card keeps
      // its own last four, a freshly typed one is read off the field. Sending
      // "card / 4242" regardless would make the whole step decoration.
      const typed = document.getElementById('svc-card-number')?.value?.replace(/\D/g, '') ?? '';
      const b = await api.bookService(s.id, {
        startsAt: when.toISOString(),
        quantity,
        address: address.trim() || null,
        latitude: s.latitude + 0.01,
        longitude: s.longitude + 0.01,
        note: note.trim() || null,
        paymentMethod: state.payMethod ?? 'card',
        cardLast4: state.payCardLast4 ?? (typed.length >= 4 ? typed.slice(-4) : null),
        addOnIds,
        conditionsConfirmed: conditionsOk
      });
      // docs/07 §2.3 — see the experience checkout: a transfer goes to the QR.
      // docs/07 §13 — a licensed gateway takes the money on its own page.
      if (b.gatewayRedirectUrl) { window.location.assign(b.gatewayRedirectUrl); return; }
      if (b.status === 'AwaitingPayment') { navigate(`/chuyen-khoan/${b.reference}`); return; }
      toast(`${t('Đã đặt — mã')} ${b.reference}`);
      navigate('/services/bookings');
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '26px 90px' }}>
      <button className="back-link" onClick={() => navigate(`/services/${slug}`)}>
        ← {t('Quay lại')}
      </button>
      <h1 className="section-title" style={{ marginTop: 10 }}>{t('Xác nhận và thanh toán')}</h1>

      <div className="trip-layout">
        <div style={{ minWidth: 0, display: 'grid', gap: 16 }}>
          <section className="pay-step">
            <h2><i>1</i> {t('Thông tin buổi làm')}</h2>

            {s.travelsToGuest && (
              <label className="form-field">
                <span className="cap">{t('Địa chỉ thực hiện')}</span>
                <input value={address} placeholder={t('Số nhà, đường, phường')}
                       onChange={e => setAddress(e.target.value)} />
              </label>
            )}

            {/* docs/09 §3.3 (MR-S-03) — each extra is priced on its own and shows
                up as its own line on the bill, so nothing grows quietly. */}
            {!!addOns.length && (
              <div style={{ marginTop: 14 }}>
                <span className="cap">{t('Tuỳ chọn thêm')}</span>
                <div style={{ display: 'grid', gap: 8, marginTop: 8 }}>
                  {addOns.map(a => (
                    <label className="check-row" key={a.id}>
                      <input type="checkbox" checked={addOnIds.includes(a.id)}
                             onChange={() => toggleAddOn(a.id)} />
                      <span style={{ flex: '1 1 auto' }}>
                        <TranslatedText as="span" text={a.name} notice={false} />
                      </span>
                      <b>+{money(a.price)}</b>
                    </label>
                  ))}
                </div>
              </div>
            )}

            {/* docs/09 §3.3 (MR-S-07) — the provider turns up expecting these. */}
            {!!requirements.length && (
              <div className="book-alert" style={{ marginTop: 14 }}>
                <b>{t('Nơi thực hiện cần có')}</b>
                <ul style={{ margin: '6px 0 0', paddingLeft: 18, fontSize: 13, lineHeight: 1.7, color: '#5c5c5c' }}>
                  {requirements.map(r => (
                    <li key={r}><TranslatedText as="span" text={r} notice={false} /></li>
                  ))}
                </ul>
                <label className="check-row" style={{ marginTop: 10, alignItems: 'flex-start' }}>
                  <input type="checkbox" checked={conditionsOk}
                         onChange={e => setConditionsOk(e.target.checked)} />
                  <span style={{ fontSize: 13, lineHeight: 1.5 }}>
                    {t('Tôi xác nhận nơi thực hiện có đủ những điều kiện trên.')}
                  </span>
                </label>
                <span style={{ fontSize: 12.5, marginTop: 6, display: 'block', color: '#5c5c5c', lineHeight: 1.5 }}>
                  {t('Khai sai điều kiện thì nhà cung cấp vẫn được nhận 50% giá trị đơn.')}
                </span>
              </div>
            )}

            <label className="form-field" style={{ marginTop: 14 }}>
              <span className="cap">
                {s.requiredNote
                  ? <>{t(s.requiredNote)} <span style={{ color: 'var(--danger, #c0392b)' }}>*</span></>
                  : <>{t('Ghi chú')} <span style={{ fontWeight: 400 }}>{t('(không bắt buộc)')}</span></>}
              </span>
              <input value={note} placeholder={s.requiredNote ? t(s.requiredNote) : t('Có người dị ứng hải sản…')}
                     onChange={e => setNote(e.target.value)} />
              {s.requiredNote && !note.trim() &&
                <span className="hint" style={{ color: 'var(--ink-muted)', fontSize: 13 }}>
                  {t('Dịch vụ này bắt buộc điền thông tin trên trước khi đặt.')}</span>}
            </label>
          </section>

          {/* docs/07 §2 — the same choice a stay offers, from the same list. */}
          <section className="pay-step">
            <h2><i>2</i> {t('Cách thanh toán')}</h2>
            <PaymentMethods idPrefix="svc-card" />
          </section>

          <section className="pay-step">
            <h2><i>3</i> {t('Xem lại và xác nhận')}</h2>
            {quote && !quote.canBook &&
              <div className="book-alert is-error" style={{ marginTop: 0 }}>
                <b>{t('Chưa đặt được')}</b><span>{quote.reason}</span>
              </div>}
            <p className="pay-demo" style={{ marginBottom: 14 }}>
              <Icon name="shield" size={16} />
              {t('Trước 72 giờ hoàn 100%, trước 24 giờ hoàn 50%, sát giờ không hoàn.')}
            </p>
            <button className="btn btn-primary" style={{ width: '100%' }}
                    disabled={busy || !quote || !quote.canBook || blocked}
                    onClick={book}>
              {busy ? t('Đang xử lý…') : t('Xác nhận và thanh toán')}
            </button>
          </section>
        </div>

        {/* What is being paid for, pinned while the forms are filled in. */}
        <aside className="receipt">
          <div className="receipt-head">
            {!!s.images.length && (
              <img src={s.images[0]} alt="" loading="lazy" decoding="async"
                   style={{ width: 76, height: 76, objectFit: 'cover', borderRadius: 12, flex: '0 0 auto' }} />
            )}
            <div style={{ minWidth: 0 }}>
              <b><TranslatedText as="span" text={s.title} notice={false} /></b>
              <div className="meta">{s.hostName}</div>
              {!!s.reviewCount && (
                <div className="meta">★ {s.rating.toFixed(2)} ({s.reviewCount})</div>
              )}
            </div>
          </div>

          <div className="kv-grid" style={{ marginTop: 18 }}>
            <Kv icon="calendar" label={t('Ngày')}
                value={`${dayLabel(when)} · ${TIME().format(when)} – ${TIME().format(ends)}`} />
            <Kv icon="users" label={t('Số lượng')} value={`${quantity} ${t(s.unit)}`} />
            <Kv icon="pin" label={t('Nơi thực hiện')}
                value={s.travelsToGuest ? (address.trim() || t('Chưa điền')) : `${t('Khách tới chỗ cung cấp')} · ${s.city}`} />
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

/**
 * The month grid behind the calendar button: two months at a time, closed and
 * fully booked days struck through, the chosen one filled in. Weeks start on
 * Monday, the same as the stay calendar, so the site has one idea of where a
 * week begins.
 */
function MonthGrid({ rail, day, hasRoom, onPick }) {
  const first = rail[0];
  const months = [
    new Date(first.getFullYear(), first.getMonth(), 1),
    new Date(first.getFullYear(), first.getMonth() + 1, 1)
  ];
  const dow = DOW_NARROW();
  const bookable = new Set(rail.filter(hasRoom).map(dayKey));

  return (
    <div className="cal-wrap" data-months="2">
      {months.map(monthStart => {
        const year = monthStart.getFullYear();
        const month = monthStart.getMonth();
        const days = new Date(year, month + 1, 0).getDate();
        const lead = mondayIndex(monthStart);

        return (
          <div key={`${year}-${month}`}>
            <div className="cal-head"><b>{MONTH().format(monthStart)}</b></div>
            <div className="cal-grid">
              {dow.map((d, i) => <span className="cal-dow" key={i}>{d}</span>)}
              {Array.from({ length: lead }, (_, i) => <span key={`pad${i}`} />)}
              {Array.from({ length: days }, (_, i) => {
                const date = new Date(year, month, i + 1);
                const free = bookable.has(dayKey(date));
                return (
                  <button key={i} type="button" disabled={!free}
                          className={`cal-day ${day && sameDay(day, date) ? 'is-edge' : ''}`}
                          aria-label={DAY().format(date)}
                          onClick={() => onPick(date)}>
                    {i + 1}
                  </button>
                );
              })}
            </div>
          </div>
        );
      })}
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
 * docs/09 §5 — what people who actually had the job done wrote, on the four
 * headings a service has. Laid out like the stay's and the experience's review
 * blocks so the site reads as one place, scored on its own criteria.
 */
function ServiceReviews({ service }) {
  const [rows, setRows] = useState(null);

  useEffect(() => {
    api.serviceReviews(service.id).then(setRows).catch(err => toast(err.message));
  }, [service.id]);

  if (!rows) return null;

  const average = key => rows.reduce((sum, r) => sum + r[key], 0) / rows.length;

  return (
    <section className="detail-section">
      <h2>{t('Đánh giá')}</h2>

      {/* The headline score comes off the offering, so it is shown whenever there
          is one — saying "chưa có đánh giá nào" under a ★4.70 the rail is already
          advertising would be the page contradicting itself. */}
      {service.reviewCount > 0 ? (
        <div className="rating-summary">
          <span className="rating-big">★ {service.rating.toFixed(2)}</span>
          <span style={{ fontSize: 15, color: 'var(--ink-muted)' }}>
            · {service.reviewCount} {t('đánh giá')}
          </span>
        </div>
      ) : (
        <p className="section-sub">
          {t('Dịch vụ này chưa có đánh giá nào. Chỉ người đã dùng mới viết được.')}
        </p>
      )}

      {!rows.length ? null : <>
      <div className="rating-bars">
        {SVC_CRITERIA.map(([key, label]) => (
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
              {SVC_CRITERIA.map(([key, label]) => (
                <span className="topic-chip" key={key}>{t(label)} <b>★ {r[key]}</b></span>
              ))}
            </div>
            {!!r.comment && <p>{r.comment}</p>}
          </article>
        ))}
      </div>
      </>}
    </section>
  );
}

/**
 * docs/09 §5 — a service is over when its hours are up; there is no register to
 * sign the way an experience has, so the job ending is the whole test.
 */
const jobEnded = r =>
  new Date(r.startsAt).getTime() + r.durationMinutes * 60000 <= Date.now();

const canReviewJob = r =>
  jobEnded(r) && !r.hasReview && (r.status === 'Confirmed' || r.status === 'Completed');

/** docs/09 §5 — four criteria of the service's own and an optional word. */
function JobReview({ booking, onDone }) {
  const [scores, setScores] = useState({ skill: 5, asDescribed: 5, punctuality: 5, value: 5 });
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
      await api.reviewServiceBooking(booking.id, { ...scores, comment });
      toast(t('Đã gửi đánh giá. Cảm ơn bạn.'));
      onDone();
    } catch (err) {
      // "Buổi này chưa kết thúc nên chưa đánh giá được", "Bạn đã đánh giá đơn
      // này rồi" — the server's sentence, said as it is.
      toast(err.message);
    } finally { setBusy(false); }
  };

  return (
    <form onSubmit={submit}
          style={{ flexBasis: '100%', minWidth: 0, marginTop: 14, paddingTop: 14, borderTop: '1px solid var(--divider)' }}>
      <b style={{ fontSize: 15 }}>{t('Dịch vụ này thế nào?')}</b>
      {SVC_CRITERIA.map(([key, label]) => (
        <div className="count-row" key={key}>
          <div className="tx"><b>{t(label)}</b></div>
          {stars(key)}
        </div>
      ))}

      <label className="form-field" style={{ marginTop: 14 }}>
        <span className="cap">{t('Nhận xét')} <span style={{ fontWeight: 400 }}>{t('(không bắt buộc)')}</span></span>
        <textarea name="comment" rows={4}
                  placeholder={t('Họ làm có đúng hẹn không? Kết quả có như bạn mong đợi?')}
                  style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
      </label>

      <button type="submit" className="btn btn-primary btn-sm" disabled={busy}>
        {busy ? t('Đang gửi…') : t('Gửi đánh giá')}
      </button>
    </form>
  );
}

export function ServiceBookings() {
  const state = useStore();
  const navigate = useNavigate();
  const [rows, setRows] = useState(null);
  // Which job has its review form open, and which ones this visit has sent.
  const [reviewing, setReviewing] = useState(null);
  const [sent, setSent] = useState([]);

  const load = () => api.serviceBookings().then(setRows).catch(e => toast(e.message));
  useEffect(() => { if (state.user) load(); }, [state.user]);

  if (!state.user) {
    return <div className="shell" style={{ paddingBlock: '60px 90px' }}>
      <div className="empty-state"><h3>{t('Đăng nhập để xem dịch vụ đã đặt')}</h3>
        <button className="btn btn-primary" style={{ marginTop: 18 }}
                onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>{t('Đăng nhập')}</button>
      </div></div>;
  }

  const cancel = async row => {
    if (!confirm(`${t('Huỷ đơn')} ${row.reference}?`)) return;
    try {
      const after = await api.cancelServiceBooking(row.id);
      toast(after.refundedAmount > 0 ? t('Đã huỷ và hoàn tiền.') : t('Đã huỷ. Huỷ sát giờ nên không hoàn tiền.'));
      load();
    } catch (err) { toast(err.message); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <h1 className="section-title">{t('Dịch vụ đã đặt')}</h1>

      {!rows ? <div className="stat skeleton" style={{ height: 200, border: 0, marginTop: 24 }} />
        : rows.length ? (
          <div style={{ marginTop: 20, display: 'grid', gap: 12 }}>
            {rows.map(r => (
              <article className="host-booking" key={r.id}>
                <div style={{ minWidth: 0 }}>
                  <h3><TranslatedText as="span" text={r.title} notice={false} /></h3>
                  <div className="meta">
                    {longDate(r.startsAt.slice(0, 10))} · {TIME().format(new Date(r.startsAt))} ·
                    {' '}{r.quantity} {t(r.unit)} · {money(r.total)}
                  </div>
                  <div className="meta">{t('Mã')} {r.reference}{r.address ? ` · ${r.address}` : ''}</div>
                  {r.note && <div className="meta">{t('Ghi chú')}: {r.note}</div>}
                  {r.cancelReason && <div className="meta">{t(r.cancelReason)}</div>}
                  {r.refundedAmount > 0 && <div className="meta">{t('Đã hoàn')} {money(r.refundedAmount)}</div>}
                  <span className={`badge ${r.statusBadge}`} style={{ marginTop: 8 }}>{t(r.statusLabel)}</span>
                </div>
                <div className="host-booking-actions">
                  <button className="btn btn-outline btn-sm"
                          onClick={() => navigate(`/services/${r.slug}`)}>{t('Xem dịch vụ')}</button>
                  {(r.status === 'Confirmed' || r.status === 'Requested') &&
                    <button className="btn btn-outline btn-sm" onClick={() => cancel(r)}>{t('Huỷ')}</button>}
                  {/* docs/09 §5 — only once the job is over, and only once. */}
                  {r.hasReview || sent.includes(r.id)
                    ? (jobEnded(r) && <span className="badge confirmed">{t('Đã đánh giá')}</span>)
                    : canReviewJob(r) && (
                        <button className="btn btn-outline btn-sm"
                                onClick={() => setReviewing(id => id === r.id ? null : r.id)}>
                          {reviewing === r.id ? t('Đóng') : t('Đánh giá dịch vụ')}
                        </button>
                      )}
                </div>
                {reviewing === r.id && !sent.includes(r.id) && (
                  <JobReview booking={r}
                             onDone={() => { setSent(ids => [...ids, r.id]); setReviewing(null); load(); }} />
                )}
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-state" style={{ marginTop: 24 }}>
            <h3>{t('Chưa đặt dịch vụ nào')}</h3>
            <button className="btn btn-primary" style={{ marginTop: 18 }}
                    onClick={() => navigate('/services')}>{t('Xem dịch vụ')}</button>
          </div>
        )}
    </div>
  );
}
