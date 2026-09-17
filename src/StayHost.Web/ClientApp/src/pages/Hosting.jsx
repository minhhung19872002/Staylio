import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import {
  set, loadHosting, loadHostCalendar, respondBooking, respondChange, requireAuth, toast,
  previewHostCancel
} from '../lib/store.js';
import { api } from '../lib/api.js';
import { money, longDate, todayIso, dateFormat, dateTime } from '../lib/format.js';
import { t } from '../lib/i18n.js';
import { Icon } from '../components/Icon.jsx';
import { Today } from './hosting/Today.jsx';
import { Payout, SuperhostProgress } from './hosting/Payout.jsx';
import { MultiCalendar } from './hosting/MultiCalendar.jsx';
import { Team } from './hosting/Team.jsx';
import { StayDetailsSummary } from '../components/StayDetailsFields.jsx';
import { HostQuestions } from '../components/ListingQuestions.jsx';
import { HotelRoomsManager } from '../components/HotelRoomsManager.jsx';

const TABS = [
  ['today', 'Hôm nay'], ['overview', 'Tổng quan'], ['listings', 'Chỗ nghỉ'],
  ['experiences', 'Trải nghiệm'],
  ['services', 'Dịch vụ'],
  ['calendar', 'Lịch'], ['bookings', 'Đơn đặt'], ['reviews', 'Đánh giá'],
  ['earnings', 'Doanh thu'],
  ['payout', 'Nhận tiền'], ['team', 'Đồng quản lý']
];

export function Hosting() {
  const state = useStore();
  const navigate = useNavigate();

  useEffect(() => { if (state.user) loadHosting(); }, [state.user]);

  /*
   * Arriving from the "Đăng nhà cho thuê" button on /host, which cannot open the
   * editor itself — App closes every overlay on a route change, so the intent
   * has to survive the navigation and be acted on once this page is here.
   *
   * Above the early returns below, because a hook that only sometimes runs is
   * not a hook.
   */
  const location = useLocation();
  useEffect(() => {
    if (!location.state?.newListing || !state.user) return;
    set({ editingListing: null, overlay: 'listing-editor' });
    navigate(location.pathname, { replace: true, state: null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state, state.user]);

  // Links in notifications and emails name a tab (/hosting?tab=earnings). The
  // page ignored the query, so every one of them opened on "Hôm nay".
  useEffect(() => {
    const wanted = new URLSearchParams(location.search).get('tab');
    if (wanted && TABS.some(([key]) => key === wanted) && state.hostingTab !== wanted)
      set({ hostingTab: wanted });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search]);

  if (!state.user) {
    return (
      <div className="shell" style={{ paddingBlock: '60px 90px' }}>
        <div className="empty-state">
          <h3>{t('Đăng nhập để quản lý chỗ nghỉ')}</h3>
          <p>{t('Trang chủ nhà cho bạn xem đơn đặt, lịch và doanh thu.')}</p>
          <button className="btn btn-primary" style={{ marginTop: 18 }}
                  onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>{t('Đăng nhập')}</button>
        </div>
      </div>
    );
  }

  const d = state.hosting;

  if (state.hostingLoading || !d) {
    return (
      <div className="shell" style={{ paddingBlock: '34px 90px' }}>
        <div className="sk-line skeleton" style={{ width: 260, height: 26 }} />
        <div className="stat-grid" style={{ marginTop: 24 }}>
          {Array.from({ length: 4 }, (_, i) => <div className="stat skeleton" key={i} style={{ height: 112, border: 0 }} />)}
        </div>
      </div>
    );
  }

  const newListing = () => {
    if (!requireAuth()) return;
    set({ editingListing: null, overlay: 'listing-editor' });
  };

  if (d.listingCount === 0) {
    return (
      <div className="shell" style={{ paddingBlock: '40px 90px' }}>
        <h1 className="section-title">{t('Trang chủ nhà')}</h1>
        <p className="section-sub">{t('Bạn chưa có chỗ nghỉ nào. Đăng chỗ đầu tiên để bắt đầu nhận đặt.')}</p>
        <div className="empty-state" style={{ marginTop: 22 }}>
          <h3>{t('Đăng chỗ nghỉ đầu tiên')}</h3>
          <p>{t('Mất khoảng 5 phút: mô tả không gian, thêm ảnh và đặt giá.')}</p>
          <button className="btn btn-primary" style={{ marginTop: 18 }} onClick={newListing}>{t('+ Đăng chỗ nghỉ')}</button>
        </div>
      </div>
    );
  }

  const tab = state.hostingTab;

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <div className="page-head">
        <div>
          <h1 className="section-title">{t('Trang chủ nhà')}</h1>
          <p className="section-sub">
            {t('Xin chào')} {state.user.fullName} — {d.publishedCount}/{d.listingCount} {t('chỗ nghỉ đang hiển thị')}
          </p>
        </div>
        <button className="btn btn-primary btn-sm" onClick={newListing}>{t('+ Đăng chỗ nghỉ')}</button>
      </div>

      <nav className="seg-tabs" role="tablist">
        {TABS.map(([key, label]) => (
          <button role="tab" key={key} aria-selected={tab === key}
                  className={`seg-tab ${tab === key ? 'is-active' : ''}`}
                  onClick={() => set({ hostingTab: key })}>{t(label)}</button>
        ))}
      </nav>

      {tab === 'today' && <Today />}
      {tab === 'overview' && <Overview d={d} navigate={navigate} />}
      {tab === 'listings' && (
        <div className="host-listing-grid" style={{ marginTop: 24 }}>
          {d.listings.map(l => <ListingCard key={l.id} listing={l} navigate={navigate} />)}
        </div>
      )}
      {tab === 'listings' && <HotelRoomsManager hotels={d.listings.filter(l => l.typeKey === 'hotel')} />}
      {tab === 'experiences' && <HostExperiences />}
      {tab === 'services' && <HostServices />}
      {tab === 'calendar' && <MultiCalendar />}
      {tab === 'bookings' && <Bookings d={d} navigate={navigate} />}
      {tab === 'reviews' && <><HostQuestions /><HostReviews /></>}
      {tab === 'earnings' && <Earnings d={d} />}
      {tab === 'payout' && <><Payout /><SuperhostProgress /></>}
      {tab === 'team' && <Team />}
    </div>
  );
}

function Overview({ d, navigate }) {
  const cards = [
    [t('Chỗ nghỉ đang hiển thị'), `${d.publishedCount}/${d.listingCount}`, t('Bản nháp không hiện với khách')],
    [t('Lượt đặt sắp tới'), String(d.upcomingBookings), `${money(d.earningsUpcoming)} ${t('sẽ nhận')}`],
    [t('Đã nhận đến nay'), money(d.earningsToDate), t('Sau phí dịch vụ Staylio')],
    [t('Điểm đánh giá'), d.totalReviews ? `★ ${d.averageRating.toFixed(2)}` : t('Chưa có'), `${d.totalReviews} ${t('đánh giá')}`]
  ];

  const pending = d.bookings.filter(b => b.status === 'PendingHostApproval');

  return <>
    <div className="stat-grid" style={{ marginTop: 24 }}>
      {cards.map(([label, value, note]) => (
        <div className="stat" key={label}>
          <div className="value" style={{ fontSize: 'clamp(22px,2.6vw,28px)' }}>{value}</div>
          <div className="label">{label}</div>
          <div className="note">{note}</div>
        </div>
      ))}
    </div>

    {!!pending.length && (
      <section style={{ marginTop: 38 }}>
        <h2 className="section-title" style={{ fontSize: 20 }}>{t('Cần bạn xử lý')} ({pending.length})</h2>
        <p className="section-sub">{t('Khách đang chờ bạn xác nhận.')}</p>
        {pending.map(b => <BookingRow key={b.id} booking={b} navigate={navigate} />)}
      </section>
    )}

    <section style={{ marginTop: 38 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Chỗ nghỉ của bạn')}</h2>
      <div className="host-listing-grid" style={{ marginTop: 16 }}>
        {d.listings.slice(0, 4).map(l => <ListingCard key={l.id} listing={l} navigate={navigate} />)}
      </div>
    </section>
  </>;
}

function ListingCard({ listing: l, navigate }) {
  const [advice, setAdvice] = useState(null);
  const [busy, setBusy] = useState(false);

  const openCalendar = async () => {
    await loadHostCalendar(l.id);
    set({ overlay: 'host-block', hostMonthOffset: 0 });
  };

  // docs/01 QL-09 + QL-18 — pull the price suggestion and improvement checklist.
  const toggleAdvice = async () => {
    if (advice) { setAdvice(null); return; }
    try { setAdvice(await api.listingAdvice(l.id)); }
    catch (err) { toast(err.message); }
  };

  // docs/01 CN-15 — clone into a fresh draft.
  const duplicate = async () => {
    if (busy) return;
    setBusy(true);
    try { await api.duplicateListing(l.id); await loadHosting(); toast(t('Đã tạo bản sao ở dạng nháp.')); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <article className="host-listing">
      <div className="host-listing-media">
        {l.images.length
          ? <img src={l.images[0]} alt={l.title} loading="lazy" decoding="async" />
          : <div className="skeleton" style={{ width: '100%', height: '100%' }} />}
        <span className={`badge ${l.isPublished ? 'confirmed' : 'pending'} host-listing-state`}>
          {l.isPublished ? t('Đang hiển thị') : t('Bản nháp')}
        </span>
      </div>
      <div className="host-listing-body">
        <h3>{l.title}</h3>
        {/* docs/01 AT-01 — a place still in review is not yet public. */}
        {l.reviewStatus === 'Pending' && (
          <div className="badge pending" style={{ marginBottom: 6 }}>{t('Đang chờ duyệt')}</div>
        )}
        {l.reviewStatus === 'Rejected' && (
          <div className="meta" style={{ color: 'var(--danger, #c0392b)', marginBottom: 6 }}>
            {t('Bị từ chối')}{l.reviewNote ? `: ${l.reviewNote}` : ''}. {t('Chỉnh sửa và lưu để gửi lại duyệt.')}
          </div>
        )}
        <div className="meta">{l.city} · {l.bedrooms} {t('phòng ngủ')} · {l.maxGuests} {t('khách')}</div>
        <div className="meta">
          <b style={{ color: 'var(--ink)' }}>{money(l.pricePerNight)}</b> {t('/ đêm')}
          {l.reviewCount ? ` · ★ ${l.rating.toFixed(2)} (${l.reviewCount})` : ` · ${t('Chưa có đánh giá')}`}
        </div>
        <div className="meta">{l.upcomingBookings} {t('lượt đặt sắp tới')} · {t('đã nhận')} {money(l.earningsToDate)}</div>
        <div className="host-listing-actions">
          <button className="btn btn-outline btn-sm"
                  onClick={() => set({ editingListing: l, overlay: 'listing-editor' })}>{t('Chỉnh sửa')}</button>
          <button className="btn btn-outline btn-sm" onClick={openCalendar}>{t('Lịch')}</button>
          {/* docs/01 TĐ-22 — the guidebook is post-publish content, so it lives
              beside the listing rather than inside the create wizard. */}
          <button className="btn btn-outline btn-sm"
                  onClick={() => set({ editingListing: l, overlay: 'guidebook-editor' })}>{t('Cẩm nang')}</button>
          <button className="btn btn-outline btn-sm" onClick={toggleAdvice}>
            {advice ? t('Ẩn gợi ý') : t('Gợi ý')}
          </button>
          <button className="btn btn-outline btn-sm" onClick={duplicate} disabled={busy}>{t('Nhân bản')}</button>
          <button className="btn btn-outline btn-sm" onClick={() => navigate(`/rooms/${l.slug}`)}>{t('Xem trang')}</button>
        </div>
        {advice && <ListingAdvice advice={advice} />}
      </div>
    </article>
  );
}

/** docs/01 QL-09 + QL-18 — suggested price and improvement checklist for one listing. */
function ListingAdvice({ advice }) {
  return (
    <div className="host-advice" style={{ marginTop: 12, padding: 12, background: 'var(--surface-2, #f6f6f6)', borderRadius: 10 }}>
      {/* QL-09 — a price to consider; the host applies it, nothing changes on its own. */}
      <div className="meta" style={{ marginBottom: 8 }}>
        <b style={{ color: 'var(--ink)' }}>{t('Gợi ý giá:')} </b>
        {advice.price.isFirm ? `${money(advice.price.suggestedPrice)} · ` : ''}{t(advice.price.rationale)}
      </div>
      {/* QL-18 — concrete improvements with a rough sense of impact. */}
      {advice.improvements.length === 0
        ? <div className="meta">{t('Tin đăng đang ở trạng thái tốt, chưa có gì cần cải thiện.')}</div>
        : (
          <ul style={{ margin: 0, paddingLeft: 18 }}>
            {advice.improvements.map((i, idx) => (
              <li key={idx} className="meta" style={{ marginBottom: 4 }}>
                <b style={{ color: 'var(--ink)' }}>{t(i.area)}:</b> {t(i.suggestion)}
                <span style={{ color: 'var(--brand, #e5484d)' }}> — {t(i.estimatedImpact)}</span>
              </li>
            ))}
          </ul>
        )}
    </div>
  );
}

/**
 * docs/09 §2.1–§2.3 (MR-E-01) — the host's own experiences. Submitting one sends
 * it to a reviewer rather than putting it on sale, so the list says which state
 * each is in instead of a bare published/draft.
 */
/**
 * docs/09 §2.2 — where an experience stands with the reviewer. "Not on sale"
 * covers four different situations and the host needs to tell them apart: still
 * a draft, waiting in the queue, sent back to fix, or refused.
 */
function experienceState(x) {
  if (x.isPublished) return { label: 'Đang bán vé', tone: 'confirmed' };

  switch (x.moderationStatus) {
    case 'PendingReview': return { label: 'Đang chờ duyệt', tone: 'pending' };
    case 'ChangesRequested': return { label: 'Cần chỉnh lại', tone: 'pending' };
    case 'Rejected': return { label: 'Bị từ chối', tone: 'cancelled' };
    default: return { label: 'Bản nháp', tone: 'pending' };
  }
}

/** A session stamped the way a host reads a diary: day, month, hour, minute. */
// Asked for per render so they follow the chosen language (format.js LOCALE).
const SESSION_STAMP = () => dateFormat({
  day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
});
const SESSION_TIME = () => dateFormat({ hour: '2-digit', minute: '2-digit' });

/** Sessions still on sale — a called-off one is history, not inventory. */
const openSlotCount = x => (x.slots ?? []).filter(s => s.status !== 'Cancelled').length;

function HostExperiences() {
  const state = useStore();
  const [rows, setRows] = useState(null);
  // Which experience has its register open. One at a time: the register is a
  // list of names to read down, not something to compare side by side.
  const [registerFor, setRegisterFor] = useState(null);
  const [slotsFor, setSlotsFor] = useState(null);

  const load = () => api.myExperiences().then(setRows).catch(err => toast(err.message));
  // Reload when the editor closes, so a just-submitted experience shows up.
  useEffect(() => { if (!state.overlay) load(); }, [state.overlay]);

  const open = x => set({ editingExperience: x ?? null, overlay: 'experience-editor' });

  return (
    <div style={{ marginTop: 24 }}>
      <div className="page-head" style={{ marginBottom: 0 }}>
        <div>
          <h2 className="section-title" style={{ fontSize: 20 }}>{t('Trải nghiệm của bạn')}</h2>
          <p className="section-sub">
            {t('Hoạt động bán theo vé. Gửi duyệt xong, Staylio xem trong 5 ngày làm việc rồi mới mở bán.')}
          </p>
        </div>
        <button className="btn btn-primary btn-sm" onClick={() => open(null)}>{t('+ Đăng trải nghiệm')}</button>
      </div>

      {!rows ? <div className="stat skeleton" style={{ height: 160, border: 0, marginTop: 16 }} />
        : rows.length ? (
          <div style={{ marginTop: 16, display: 'grid', gap: 12 }}>
            {rows.map(x => (
              <article className="host-booking" key={x.id}>
                <div style={{ minWidth: 0 }}>
                  <h3>{x.title}</h3>
                  <div className="meta">{x.city} · {money(x.pricePerPerson)} / {t('người')} · {t('tối đa')} {x.maxGroup} {t('người')}</div>
                  <div className="meta">{x.meetingPoint || t('Chưa có điểm hẹn')}</div>
                  {/* docs/09 §2.2 — a submission waiting on a reviewer must not
                      look like one that was turned down, so the badge reads the
                      moderation state rather than the published flag alone. */}
                  <span className={`badge ${experienceState(x).tone}`} style={{ marginTop: 8 }}>
                    {t(experienceState(x).label)}
                  </span>
                  {x.reviewerNote && !x.isPublished && (
                    <div className="meta" style={{ marginTop: 6 }}>{x.reviewerNote}</div>
                  )}
                </div>
                <div className="host-booking-actions">
                  <button className="btn btn-outline btn-sm" onClick={() => open(x)}>{t('Chỉnh sửa')}</button>
                  {/* docs/09 §2.9 (MR-E-09) — the day itself: who turned up. */}
                  <button className="btn btn-outline btn-sm"
                          onClick={() => setRegisterFor(id => id === x.id ? null : x.id)}>
                    {registerFor === x.id ? t('Đóng điểm danh') : t('Điểm danh')}
                  </button>
                  {/* docs/01 MR-02 — an experience with no session on the
                      calendar cannot sell a single ticket, so this is the last
                      step of listing one, not an extra. */}
                  <button className="btn btn-outline btn-sm"
                          onClick={() => setSlotsFor(id => id === x.id ? null : x.id)}>
                    {slotsFor === x.id ? t('Đóng suất') : `${t('Suất')} (${openSlotCount(x)})`}
                  </button>
                </div>
                {/* Full width of the card: the register is a list, not a column. */}
                {registerFor === x.id && (
                  <div style={{ flexBasis: '100%', minWidth: 0 }}>
                    <SessionRegister experience={x} />
                  </div>
                )}
                {slotsFor === x.id && (
                  <div style={{ flexBasis: '100%', minWidth: 0 }}>
                    <SessionPlanner experience={x} onChanged={load} />
                  </div>
                )}
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-state" style={{ marginTop: 20 }}>
            <h3>{t('Chưa có trải nghiệm nào')}</h3>
            <p>{t('Kể một hoạt động bạn dẫn được và bán theo vé cho khách.')}</p>
            <button className="btn btn-primary" style={{ marginTop: 18 }}
                    onClick={() => open(null)}>{t('+ Đăng trải nghiệm')}</button>
          </div>
        )}
    </div>
  );
}

/**
 * docs/09 §3.2 (MR-S-02) — where a service stands. Unlike an experience there is
 * no reviewer queue: it is on sale or it is not, and the one thing that takes it
 * off sale by itself is a practising certificate that ran out.
 */
function serviceState(s, today) {
  const lapsed = s.certificateExpiresOn && s.certificateExpiresOn < today;
  if (lapsed) return { label: 'Chứng chỉ hết hạn — đã ẩn', tone: 'cancelled' };
  return s.isPublished
    ? { label: 'Đang bán', tone: 'confirmed' }
    : { label: 'Bản nháp', tone: 'pending' };
}

/** docs/09 §3.2 — the provider is warned thirty days before the certificate lapses. */
const CERTIFICATE_REMINDER_DAYS = 30;

function certificateWarning(s, today) {
  if (!s.certificateExpiresOn || s.certificateExpiresOn < today) return null;
  const days = Math.round(
    (new Date(`${s.certificateExpiresOn}T00:00:00`) - new Date(`${today}T00:00:00`)) / 86400000);
  return days <= CERTIFICATE_REMINDER_DAYS ? days : null;
}

/**
 * docs/09 §3.2–§3.4 (MR-S-01) — the provider's own services. Sold by the slot at
 * the guest's address, so the row says how far they travel and which days they
 * work rather than a bed count.
 */
function HostServices() {
  const state = useStore();
  const [rows, setRows] = useState(null);
  const today = todayIso();

  const load = () => api.myServices().then(setRows).catch(err => toast(err.message));
  // Reload when the editor closes, so a just-saved service shows up.
  useEffect(() => { if (!state.overlay) load(); }, [state.overlay]);

  const open = s => set({ editingService: s ?? null, overlay: 'service-editor' });

  return (
    <div style={{ marginTop: 24 }}>
      <div className="page-head" style={{ marginBottom: 0 }}>
        <div>
          <h2 className="section-title" style={{ fontSize: 20 }}>{t('Dịch vụ của bạn')}</h2>
          <p className="section-sub">
            {t('Việc bán theo khung giờ, làm tại chỗ khách ở. Chứng chỉ hành nghề hết hạn thì dịch vụ tự ẩn khỏi tìm kiếm.')}
          </p>
        </div>
        <button className="btn btn-primary btn-sm" onClick={() => open(null)}>{t('+ Đăng dịch vụ')}</button>
      </div>

      {!rows ? <div className="stat skeleton" style={{ height: 160, border: 0, marginTop: 16 }} />
        : rows.length ? (
          <div style={{ marginTop: 16, display: 'grid', gap: 12 }}>
            {rows.map(s => (
              <article className="host-booking" key={s.id}>
                <div style={{ minWidth: 0 }}>
                  <h3>{s.title}</h3>
                  <div className="meta">
                    {s.city} · {t(s.pricingLabel)} · {money(s.basePrice)} / {t(s.unit)} · {s.durationMinutes} {t('phút')}
                  </div>
                  <div className="meta">
                    {s.travelsToGuest
                      ? `${t('Tới tận nơi trong')} ${s.serviceRadiusKm} km`
                      : t('Khách tới chỗ cung cấp')}
                    {' · '}{s.opensAtHour}:00–{s.closesAtHour}:00
                    {s.maxJobsPerDay > 0 ? ` · ${t('tối đa')} ${s.maxJobsPerDay} ${t('đơn/ngày')}` : ''}
                  </div>
                  {!!(s.addOns ?? []).length && (
                    <div className="meta">{(s.addOns ?? []).length} {t('tuỳ chọn thêm')}</div>
                  )}
                  <span className={`badge ${serviceState(s, today).tone}`} style={{ marginTop: 8 }}>
                    {t(serviceState(s, today).label)}
                  </span>
                  {/* docs/09 §3.2 — the thirty-day warning, said where they will see it. */}
                  {certificateWarning(s, today) !== null && (
                    <div className="meta" style={{ marginTop: 6 }}>
                      {t('Chứng chỉ còn')} {certificateWarning(s, today)}{' '}
                      {t('ngày là hết hạn — gia hạn trước khi dịch vụ tự ẩn.')}
                    </div>
                  )}
                </div>
                <div className="host-booking-actions">
                  <button className="btn btn-outline btn-sm" onClick={() => open(s)}>{t('Chỉnh sửa')}</button>
                </div>
              </article>
            ))}
            <ProviderJobs />
          </div>
        ) : (
          <div className="empty-state" style={{ marginTop: 20 }}>
            <h3>{t('Chưa có dịch vụ nào')}</h3>
            <p>{t('Nhận nấu ăn, chụp ảnh, đưa đón — bán theo khung giờ cho khách đang ở gần bạn.')}</p>
            <button className="btn btn-primary" style={{ marginTop: 18 }}
                    onClick={() => open(null)}>{t('+ Đăng dịch vụ')}</button>
          </div>
        )}
    </div>
  );
}

/**
 * docs/09 §3.5, §3.6 — the jobs somebody has actually been booked for.
 *
 * The console used to show only the services on sale, so the note docs/09 §3.5
 * forces a guest to write — food allergies, what to avoid in a massage — was
 * collected and then shown to nobody. It leads this list for that reason.
 */
function ProviderJobs() {
  const [jobs, setJobs] = useState(null);
  const [busy, setBusy] = useState(0);

  const load = () => api.serviceJobs().then(setJobs).catch(() => setJobs([]));
  useEffect(() => { load(); }, []);

  // docs/09 §3.6 (DV-D) — the guest keeps half, the provider keeps half. It ends
  // the job, so it asks first.
  const misdeclared = async job => {
    const note = prompt(t('Thiếu gì ở nơi làm việc? (không bắt buộc)'));
    if (note === null) return;
    setBusy(job.id);
    try {
      await api.reportMisdeclared(job.id, note.trim() || null);
      await load();
      toast(t('Đã ghi nhận. Bạn được trả một nửa giá trị đơn cho công đi lại.'));
    } catch (err) { toast(err.message); }
    finally { setBusy(0); }
  };

  // docs/09 §3.5 — a job waiting on this provider's yes.
  const respond = async (job, accept) => {
    let reason = null;
    if (!accept) {
      reason = prompt(t('Lý do từ chối (khách sẽ thấy)'));
      if (reason === null) return;
    }
    setBusy(job.id);
    try {
      if (accept) await api.acceptServiceJob(job.id);
      else await api.declineServiceJob(job.id, reason.trim() || null);
      await load();
      toast(accept ? t('Đã xác nhận đơn.') : t('Đã từ chối. Khách được hoàn toàn bộ.'));
    } catch (err) { toast(err.message); }
    finally { setBusy(0); }
  };

  // docs/09 §3.6 — pulling out of a confirmed job refunds the guest in full,
  // gives them balance on top, and costs the provider a fine. Said first.
  const cancel = async job => {
    const reason = prompt(t('Huỷ đơn này? Khách được hoàn toàn bộ kèm số dư đền bù, và bạn chịu phí phạt huỷ. Lý do:'));
    if (reason === null) return;
    setBusy(job.id);
    try {
      await api.cancelServiceJob(job.id, reason.trim() || null);
      await load();
      toast(t('Đã huỷ đơn.'));
    } catch (err) { toast(err.message); }
    finally { setBusy(0); }
  };

  if (!jobs?.length) return null;

  return (
    <section style={{ marginTop: 10 }}>
      <h3 style={{ margin: '0 0 4px', fontSize: 16, fontWeight: 800 }}>{t('Đơn bạn nhận được')}</h3>
      <p className="section-sub" style={{ marginBottom: 12 }}>
        {t('Giờ hẹn, địa chỉ và ghi chú bắt buộc của khách.')}
      </p>

      <div style={{ display: 'grid', gap: 10 }}>
        {jobs.map(j => (
          <article className="host-booking" key={j.id}>
            <div style={{ minWidth: 0 }}>
              <h3>{j.offeringTitle}</h3>
              <div className="meta">
                {j.guestName} · {dateTime(j.startsAt)} · {j.quantity} {t(j.unit)}
              </div>
              <div className="meta">{j.address}</div>
              {/* The whole reason this list exists. */}
              {j.note && (
                <div className="meta" style={{ marginTop: 6, color: 'var(--ink)', fontWeight: 700 }}>
                  {t('Khách dặn:')} {j.note}
                </div>
              )}
              {j.guestPhone && <div className="meta">{t('Điện thoại:')} {j.guestPhone}</div>}
              <div className="meta">
                {money(j.total)} · {t('bạn nhận')} <b style={{ color: 'var(--ink)' }}>{money(j.providerPayout)}</b>
              </div>
              <span className={`badge ${j.statusBadge}`} style={{ marginTop: 8 }}>{t(j.statusLabel)}</span>
              {j.cancelReason && <div className="meta" style={{ marginTop: 6 }}>{t(j.cancelReason)}</div>}
              {j.canRespond && j.respondBy && (
                <div className="meta" style={{ marginTop: 6 }}>{t('Trả lời trước')} {dateTime(j.respondBy)}</div>
              )}
            </div>
            {(j.canReportMisdeclared || j.canRespond || j.canCancel) && (
              <div className="host-booking-actions">
                {j.canRespond && <>
                  <button className="btn btn-primary btn-sm" disabled={busy === j.id}
                          onClick={() => respond(j, true)}>{t('Xác nhận')}</button>
                  <button className="btn btn-outline btn-sm" disabled={busy === j.id}
                          onClick={() => respond(j, false)}>{t('Từ chối')}</button>
                </>}
                {j.canCancel && !j.canRespond && (
                  <button className="btn btn-outline btn-sm" disabled={busy === j.id}
                          onClick={() => cancel(j)}>{t('Huỷ đơn')}</button>
                )}
                {j.canReportMisdeclared && (
                  <button className="btn btn-outline btn-sm" disabled={busy === j.id}
                          onClick={() => misdeclared(j)}>{t('Không đủ điều kiện tại chỗ')}</button>
                )}
              </div>
            )}
          </article>
        ))}
      </div>
    </section>
  );
}

/**
 * docs/01 ĐG-07, docs/03 §7 — the host's one public answer to a review, within
 * thirty days. `HostReply` on the listing page has always rendered an answer;
 * there was simply nowhere to write one, so it never rendered anything.
 */
function HostReviews() {
  const [rows, setRows] = useState(null);
  const [draft, setDraft] = useState({});
  const [busyId, setBusyId] = useState(null);

  const load = () => api.hostReviews().then(setRows).catch(err => toast(err.message));
  useEffect(() => { load(); }, []);

  const send = async r => {
    const text = (draft[r.id] ?? '').trim();
    if (text.length < 10) { toast(t('Phản hồi cần tối thiểu 10 ký tự.')); return; }
    setBusyId(r.id);
    try {
      await api.replyToReview(r.id, text);
      setDraft(d => ({ ...d, [r.id]: '' }));
      await load();
      toast(t('Đã gửi phản hồi công khai.'));
    } catch (err) { toast(err.message); }
    finally { setBusyId(null); }
  };

  if (!rows) return <div className="stat skeleton" style={{ height: 160, border: 0, marginTop: 24 }} />;

  const waiting = rows.filter(r => r.canReply).length;

  return (
    <div style={{ marginTop: 24 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Đánh giá về chỗ của bạn')}</h2>
      <p className="section-sub">
        {rows.length
          ? `${rows.length} ${t('đánh giá')} · ${waiting} ${t('còn trả lời được')}`
          : t('Chưa có đánh giá công khai nào.')}
      </p>

      <div style={{ marginTop: 16, display: 'grid', gap: 12 }}>
        {rows.map(r => (
          <article className="host-booking" key={r.id}>
            <div style={{ minWidth: 0, flexBasis: '100%' }}>
              <h3>★ {r.rating.toFixed(1)} · {r.authorName}</h3>
              <div className="meta">{r.listingTitle} · {longDate(r.createdAt.slice(0, 10))}</div>
              <p style={{ margin: '10px 0 0', fontSize: 14.5, lineHeight: 1.6 }}>{r.text}</p>

              {r.hostReply ? (
                <div className="review-reply" style={{ marginLeft: 0, marginTop: 12 }}>
                  <b>{t('Phản hồi của bạn')}</b>
                  <p>{r.hostReply}</p>
                </div>
              ) : r.canReply ? (
                <div style={{ marginTop: 12 }}>
                  <textarea className="field" rows={3}
                            placeholder={t('Trả lời công khai — chỉ được một lần cho mỗi đánh giá.')}
                            value={draft[r.id] ?? ''}
                            onChange={e => setDraft(d => ({ ...d, [r.id]: e.target.value }))} />
                  <div style={{ display: 'flex', gap: 10, alignItems: 'center', marginTop: 8 }}>
                    <button className="btn btn-primary btn-sm" disabled={busyId === r.id} onClick={() => send(r)}>
                      {busyId === r.id ? t('Đang gửi…') : t('Gửi phản hồi')}
                    </button>
                    <span className="meta">
                      {t('Hạn trả lời:')} {longDate(r.replyDeadline.slice(0, 10))}
                    </span>
                  </div>
                </div>
              ) : (
                <p className="meta" style={{ marginTop: 12 }}>
                  {t('Đã quá 30 ngày, đánh giá này không trả lời được nữa.')}
                </p>
              )}
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}

/* docs/09 §2.5 — Monday is bit 0, exactly as ExperienceRules reads the mask. */
const WEEKDAYS = [
  ['T2', 0], ['T3', 1], ['T4', 2], ['T5', 3], ['T6', 4], ['T7', 5], ['CN', 6]
];

/**
 * docs/01 MR-02, docs/09 §2.5 — the sessions of one experience: what is on sale,
 * one more start time, or a weekly pattern expanded by the server.
 *
 * The endpoints behind this have been complete and tested since the experience
 * line was built; nothing on any screen called them, so the only sessions that
 * ever existed were the seeded ones and a host who listed an experience of their
 * own had nothing to sell.
 */
function SessionPlanner({ experience, onChanged }) {
  const [busy, setBusy] = useState(false);
  const [mode, setMode] = useState('one');
  const [at, setAt] = useState('');
  const [capacity, setCapacity] = useState(experience.maxGroup ?? 8);
  const [mask, setMask] = useState([]);
  const [repeatAt, setRepeatAt] = useState('09:00');
  const [from, setFrom] = useState(todayIso());
  const [weeks, setWeeks] = useState(4);

  const slots = (experience.slots ?? []).filter(s => s.status !== 'Cancelled');

  const toggleDay = bit =>
    setMask(m => (m.includes(bit) ? m.filter(b => b !== bit) : [...m, bit]));

  const submit = async () => {
    // The server takes both shapes in one request, so a host who wants a weekly
    // pattern plus one extra evening does not have to save twice.
    const body = mode === 'one'
      ? { startsAt: at ? [new Date(at).toISOString()] : [], capacity: Number(capacity) || null }
      : {
          capacity: Number(capacity) || null,
          repeatWeekdayMask: mask.reduce((acc, bit) => acc | (1 << bit), 0),
          repeatAt: `${repeatAt}:00`,
          repeatFrom: from,
          repeatWeeks: Number(weeks) || 0
        };

    if (mode === 'one' && !at) { toast(t('Chọn ngày giờ bắt đầu.')); return; }
    if (mode === 'repeat' && !mask.length) { toast(t('Chọn ít nhất một thứ trong tuần.')); return; }

    setBusy(true);
    try {
      await api.addExperienceSlots(experience.id, body);
      setAt('');
      await onChanged();
      toast(t('Đã thêm suất.'));
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const cancel = async slot => {
    // docs/09 §2.6 — calling a session off refunds everyone already on it, so
    // the number of tickets is said out loud before the host confirms.
    // One whole-sentence key with a slot, not three pieces glued together: the
    // number sits in a different place in Japanese and Korean.
    const reason = prompt(slot.seatsTaken > 0
      ? t('Huỷ suất này sẽ hoàn tiền cho {} khách đã đặt. Lý do:').replace('{}', slot.seatsTaken)
      : t('Lý do huỷ suất (khách sẽ đọc được):'));
    if (!reason?.trim()) return;
    try {
      await api.cancelExperienceSlot(slot.id, reason.trim());
      await onChanged();
      toast(t('Đã huỷ suất.'));
    } catch (err) { toast(err.message); }
  };

  return (
    <div className="notice" style={{ marginTop: 14 }}>
      <div className="pill-row" style={{ marginBottom: 14 }}>
        <button className={`pill ${mode === 'one' ? 'is-on' : ''}`} onClick={() => setMode('one')}>
          {t('Một suất')}
        </button>
        <button className={`pill ${mode === 'repeat' ? 'is-on' : ''}`} onClick={() => setMode('repeat')}>
          {t('Lặp hằng tuần')}
        </button>
      </div>

      <div style={{ display: 'grid', gap: 12, gridTemplateColumns: 'repeat(auto-fit,minmax(min(100%,160px),1fr))' }}>
        {mode === 'one' ? (
          <label className="form-field"><span className="cap">{t('Bắt đầu lúc')}</span>
            <input type="datetime-local" className="field" value={at} onChange={e => setAt(e.target.value)} /></label>
        ) : (
          <>
            <label className="form-field"><span className="cap">{t('Giờ bắt đầu')}</span>
              <input type="time" className="field" value={repeatAt} onChange={e => setRepeatAt(e.target.value)} /></label>
            <label className="form-field"><span className="cap">{t('Từ ngày')}</span>
              <input type="date" className="field" value={from} onChange={e => setFrom(e.target.value)} /></label>
            <label className="form-field"><span className="cap">{t('Số tuần')}</span>
              <input type="number" min={1} max={26} className="field" value={weeks}
                     onChange={e => setWeeks(e.target.value)} /></label>
          </>
        )}
        <label className="form-field"><span className="cap">{t('Số chỗ mỗi suất')}</span>
          <input type="number" min={1} max={experience.maxGroup ?? 30} className="field" value={capacity}
                 onChange={e => setCapacity(e.target.value)} /></label>
      </div>

      {mode === 'repeat' && (
        <div className="pill-row" style={{ marginTop: 12 }}>
          {WEEKDAYS.map(([label, bit]) => (
            <button key={bit} className={`pill ${mask.includes(bit) ? 'is-on' : ''}`}
                    onClick={() => toggleDay(bit)}>{t(label)}</button>
          ))}
        </div>
      )}

      <button className="btn btn-primary btn-sm" style={{ marginTop: 14 }} disabled={busy} onClick={submit}>
        {busy ? t('Đang thêm…') : t('Thêm vào lịch')}
      </button>

      {slots.length ? (
        <div className="table-wrap" style={{ marginTop: 16 }}>
          <table className="admin-table">
            <thead>
              <tr><th>{t('Suất')}</th><th>{t('Đã bán')}</th><th /></tr>
            </thead>
            <tbody>
              {slots.map(s => (
                <tr key={s.id}>
                  <td>{SESSION_STAMP().format(new Date(s.startsAt))}
                    {s.isPrivate ? <span>{t('Thuê trọn nhóm')}</span> : null}</td>
                  <td>{s.seatsTaken}/{s.capacity}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button className="btn btn-outline btn-sm" onClick={() => cancel(s)}>{t('Huỷ suất')}</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="meta" style={{ marginTop: 14 }}>
          {t('Chưa có suất nào — trải nghiệm chưa bán được vé nào cho tới khi có ít nhất một suất.')}
        </p>
      )}
    </div>
  );
}

/**
 * docs/09 §2.9 (MR-E-09) — the register for one session. Whether it may be taken
 * at all is the server's answer (`canMark`), not a reading of this machine's
 * clock: marking somebody absent from a session that has not started is a guess.
 * So before the start time the names are shown and the two buttons are off,
 * with the reason written out rather than left to a failed click.
 */
function SessionRegister({ experience }) {
  const slots = experience.slots.filter(s => s.status !== 'Cancelled');
  // Open on the session being run right now — the latest one already under way —
  // and fall back to the next one so the host can read the names beforehand.
  const [slotId, setSlotId] = useState(() => {
    const now = Date.now();
    const started = slots.filter(s => new Date(s.startsAt).getTime() <= now);
    return (started.length ? started[started.length - 1] : slots[0])?.id ?? null;
  });
  const [roster, setRoster] = useState(null);
  const [busyId, setBusyId] = useState(null);

  useEffect(() => {
    if (!slotId) { setRoster(null); return; }
    let live = true;
    setRoster(null);
    api.experienceRoster(slotId)
      .then(r => { if (live) setRoster(r); })
      .catch(err => toast(err.message));
    return () => { live = false; };
  }, [slotId]);

  if (!slots.length) {
    return (
      <div className="notice notice-warn">
        {t('Trải nghiệm này chưa có suất nào để điểm danh. Thêm suất trước đã.')}
      </div>
    );
  }

  /* A mark also closes the ticket on the server, so the register is read back
     instead of being patched here — the screen then says what the data says. */
  const mark = async (bookingId, attended) => {
    setBusyId(bookingId);
    try {
      await api.markExperienceAttendance(bookingId, attended);
      setRoster(await api.experienceRoster(slotId));
    } catch (err) { toast(err.message); }
    finally { setBusyId(null); }
  };

  return (
    <div style={{ marginTop: 14, paddingTop: 14, borderTop: '1px solid var(--divider)' }}>
      <label className="form-field" style={{ maxWidth: 360, marginBottom: 4 }}>
        <span className="cap">{t('Suất')}</span>
        <select value={slotId ?? ''} onChange={e => setSlotId(Number(e.target.value))}
                style={{ padding: '8px 10px', border: '1px solid var(--line)', borderRadius: 10, fontSize: 14 }}>
          {slots.map(s => (
            <option key={s.id} value={s.id}>
              {SESSION_STAMP().format(new Date(s.startsAt))} · {s.seatsTaken}/{s.capacity} {t('chỗ')}
            </option>
          ))}
        </select>
      </label>
      <p className="meta">{t('Chỉ hiện suất sắp tới và suất vừa diễn ra.')}</p>

      {!roster ? <div className="stat skeleton" style={{ height: 120, border: 0, marginTop: 12 }} /> : <>
        <div className="meta" style={{ marginTop: 10 }}>
          {SESSION_STAMP().format(new Date(roster.startsAt))} → {SESSION_TIME().format(new Date(roster.endsAt))} ·
          {' '}{roster.seatsTaken}/{roster.capacity} {t('chỗ đã bán')} · {roster.guests.length} {t('vé')}
        </div>

        {/* Not yet: say so instead of letting the host click into an error. */}
        {!roster.canMark ? (
          <div className="notice notice-warn">
            <b>{t('Chưa tới giờ bắt đầu nên chưa điểm danh được.')}</b>{' '}
            {t('Danh sách dưới đây để bạn xem trước; điểm danh mở ngay khi suất bắt đầu.')}
          </div>
        ) : (
          <div className="notice notice-ok">
            {t('Khách tới muộn quá')} {roster.lateAllowanceMinutes} {t('phút thì bạn có quyền bắt đầu mà không cần chờ; khách đó không được hoàn tiền.')}
          </div>
        )}

        {roster.guests.length === 0 ? (
          <p className="meta" style={{ marginTop: 12 }}>{t('Suất này chưa có ai đặt.')}</p>
        ) : (
          <div style={{ marginTop: 12, display: 'grid', gap: 8 }}>
            {roster.guests.map(g => (
              <div className="count-row" key={g.bookingId} style={{ gap: 12 }}>
                <div className="tx" style={{ minWidth: 0 }}>
                  <b>{g.guestName}</b>
                  <span>
                    {t('Mã')} {g.reference} · {g.seats} {t('chỗ')}{g.isPrivate ? ` · ${t('nhóm riêng')}` : ''}
                    {g.attended == null
                      ? ` · ${t('Chưa điểm danh')}`
                      : ` · ${g.attended ? t('Có mặt') : t('Vắng')}${g.markedAt ? ` ${SESSION_TIME().format(new Date(g.markedAt))}` : ''}`}
                  </span>
                </div>
                <div className="host-booking-actions">
                  <button className={`btn btn-sm ${g.attended === true ? 'btn-primary' : 'btn-outline'}`}
                          disabled={!roster.canMark || busyId === g.bookingId}
                          onClick={() => mark(g.bookingId, true)}>{t('Có mặt')}</button>
                  <button className={`btn btn-sm ${g.attended === false ? 'btn-primary' : 'btn-outline'}`}
                          disabled={!roster.canMark || busyId === g.bookingId}
                          onClick={() => mark(g.bookingId, false)}>{t('Vắng')}</button>
                </div>
              </div>
            ))}
          </div>
        )}
      </>}
    </div>
  );
}

/**
 * docs/02 G6 — six groups, not one list. The host side had exactly the same
 * shape the guest's /trips had: every booking in one run, so "who is arriving"
 * and "who left last spring" sat in the same column.
 *
 * The order matters. A stay ending today is read out of "đang ở" into "sắp trả
 * phòng" because that is the one the host has work for; a booking is only in one
 * group, and the first rule that claims it wins.
 */
const HOST_GROUPS = [
  ['awaiting', 'Chờ duyệt'],
  ['leaving', 'Sắp trả phòng'],
  ['inhouse', 'Đang ở'],
  ['upcoming', 'Sắp tới'],
  ['done', 'Đã hoàn tất'],
  ['cancelled', 'Đã huỷ']
];

const HOST_CANCELLED = ['Declined', 'Expired', 'PaymentFailed', 'CancelledByGuest', 'CancelledByHost'];

export function hostGroupOf(b, today, tomorrow) {
  if (b.status === 'PendingHostApproval') return 'awaiting';
  if (HOST_CANCELLED.includes(b.status)) return 'cancelled';
  if (b.status === 'Completed' || b.checkOut < today) return 'done';

  const inHouse = b.status === 'InProgress' || (b.checkIn <= today && today < b.checkOut);
  if (inHouse) return b.checkOut <= tomorrow ? 'leaving' : 'inhouse';

  // Still to be paid for, still to arrive: both are work ahead, not work now.
  return 'upcoming';
}

function Bookings({ d, navigate }) {
  const [group, setGroup] = useState(null);

  if (!d.bookings.length) {
    return (
      <div className="empty-state" style={{ marginTop: 24 }}>
        <h3>{t('Chưa có lượt đặt nào')}</h3>
        <p>{t('Khi có khách đặt, đơn sẽ hiện ở đây.')}</p>
      </div>
    );
  }

  const today = todayIso();
  const tomorrow = todayIso(1);
  const counts = Object.fromEntries(HOST_GROUPS.map(([key]) => [key, 0]));
  for (const b of d.bookings) counts[hostGroupOf(b, today, tomorrow)] += 1;

  // Open on the first group with something in it, so a host whose only work is
  // an arrival next week does not land on an empty "chờ duyệt".
  const active = group ?? HOST_GROUPS.find(([key]) => counts[key] > 0)?.[0] ?? 'awaiting';
  const rows = d.bookings.filter(b => hostGroupOf(b, today, tomorrow) === active);

  return (
    <div style={{ marginTop: 24 }}>
      <div className="seg-tabs" role="tablist" style={{ marginBottom: 18 }}>
        {HOST_GROUPS.map(([key, label]) => (
          <button role="tab" key={key} aria-selected={active === key}
                  className={`seg-tab ${active === key ? 'is-active' : ''}`}
                  onClick={() => setGroup(key)}>
            {t(label)} ({counts[key]})
          </button>
        ))}
      </div>

      {rows.length
        ? rows.map(b => <BookingRow key={b.id} booking={b} navigate={navigate} />)
        : <p className="section-sub">{t('Không có đơn nào trong nhóm này.')}</p>}
    </div>
  );
}

/** docs/03 §3 — the host has 24 hours before the request expires by itself. */
function RespondDeadline({ at }) {
  const minutes = Math.round((new Date(at) - Date.now()) / 60000);
  if (minutes <= 0) return null;

  return (
    <span className="badge pending">
      {minutes < 60 ? `${t('Còn')} ${minutes} ${t('phút để trả lời')}` : `${t('Còn')} ${Math.round(minutes / 60)} ${t('giờ để trả lời')}`}
    </span>
  );
}

/** docs/07 §2.5 — the host confirms the guest handed over the money. */
async function markCashCollected(b) {
  if (!confirm(`${t('Xác nhận đã nhận đủ tiền mặt cho đơn')} ${b.reference}?`)) return;
  try {
    const res = await api.cashCollected(b.id);
    await loadHosting();
    toast(res.alreadyRecorded
      ? t('Đơn này đã được ghi nhận trước đó.')
      : t('Đã ghi nhận. Phí dịch vụ sẽ trừ vào lần chuyển tiền kế tiếp.'));
  } catch (err) { toast(err.message); }
}

function BookingRow({ booking: b, navigate }) {
  const awaitingHost = b.status === 'PendingHostApproval';

  return (
    <article className="host-booking">
      <div style={{ minWidth: 0 }}>
        <h3>{b.listingTitle}</h3>
        <div className="meta">
          {b.guestName}{b.guestEmail ? ` · ${b.guestEmail}` : ''}
          {b.guestPhone ? ` · ${b.guestPhone}` : ''} · {t('mã')} {b.reference}
        </div>
        {b.paidAtProperty && (
          <div className="meta">
            <span className={`badge ${b.cashCollectedAt ? 'confirmed' : 'pending'}`}>
              {b.cashCollectedAt ? t('Đã nhận tiền tại nơi ở') : t('Khách trả khi nhận phòng')}
            </span>
          </div>
        )}
        <div className="meta">
          {longDate(b.checkIn)} → {longDate(b.checkOut)} · {b.nights} {t('đêm')} · {b.guests} {t('khách')}
          {b.roomTypeName && <> · {b.rooms > 1 ? `${b.rooms} × ` : ''}{b.roomTypeName}</>}
        </div>
        <StayDetailsSummary details={b.details} />
        {b.guestNote && <div className="meta">{t('Lời nhắn:')} <i>{b.guestNote}</i></div>}
        <div className="meta">
          {t('Khách trả')} <b style={{ color: 'var(--ink)' }}>{money(b.total)}</b> ·
          {' '}{t('bạn nhận')} <b style={{ color: 'var(--brand)' }}>{money(b.hostPayout)}</b>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
          <span className={`badge ${b.statusBadge}`}>{t(b.statusLabel, 'status')}</span>
          {awaitingHost && b.requestExpiresAt && <RespondDeadline at={b.requestExpiresAt} />}
        </div>

        {/* docs/01 CĐ-06 — a change the guest asked for, with accept/reject. */}
        {b.pendingChange && (
          <div className="notice notice-warn" style={{ marginTop: 10 }}>
            <b>{t('Yêu cầu đổi lịch:')}</b> {longDate(b.pendingChange.newCheckIn)} → {longDate(b.pendingChange.newCheckOut)} ·
            {' '}{b.pendingChange.newGuests} {t('khách')} · {t(b.pendingChange.differenceLabel)}
            <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
              <button className="btn btn-primary btn-sm" onClick={() => respondChange(b.id, b.pendingChange.id, true)}>{t('Chấp nhận đổi')}</button>
              <button className="btn btn-outline btn-sm" onClick={() => respondChange(b.id, b.pendingChange.id, false)}>{t('Từ chối')}</button>
            </div>
          </div>
        )}
      </div>
      <div className="host-booking-actions">
        {awaitingHost && <>
          <button className="btn btn-primary btn-sm" onClick={() => respondBooking(b.id, 'confirm')}>{t('Xác nhận')}</button>
          <button className="btn btn-outline btn-sm" onClick={() => respondBooking(b.id, 'decline')}>{t('Từ chối')}</button>
        </>}
        {b.status === 'Completed' && (
          <button className="btn btn-outline btn-sm"
                  onClick={() => set({ guestReviewBooking: b, overlay: 'guest-review' })}>{t('Đánh giá khách')}</button>
        )}
        {/* docs/01 QL-13 — never a bare "huỷ": the warning comes first. */}
        {b.status === 'Confirmed' && (
          <button className="btn btn-outline btn-sm" onClick={() => previewHostCancel(b.id)}>{t('Huỷ đơn')}</button>
        )}
        {/* docs/07 §2.5 — the only moment a pay-at-property booking touches the
            books: the host says the cash is in hand, and the service fee is
            billed against their next transfer. */}
        {b.paidAtProperty && !b.cashCollectedAt
          && ['Confirmed', 'InProgress', 'Completed'].includes(b.status) && (
          <button className="btn btn-primary btn-sm" onClick={() => markCashCollected(b)}>
            {t('Đã nhận tiền')}
          </button>
        )}
        <button className="btn btn-outline btn-sm" onClick={() => navigate('/messages')}>{t('Nhắn khách')}</button>
      </div>
    </article>
  );
}

function Earnings({ d }) {
  const state = useStore();
  const hostRate = state.meta?.fees?.hostServiceFeeRate ?? 0.03;

  if (!d.earningsByMonth.length) {
    return (
      <div className="empty-state" style={{ marginTop: 24 }}>
        <h3>{t('Chưa có doanh thu')}</h3>
        <p>{t('Biểu đồ sẽ hiện khi bạn có lượt đặt đầu tiên.')}</p>
      </div>
    );
  }

  const pct = Math.round(hostRate * 100);

  return (
    <div style={{ marginTop: 24 }}>
      <div className="stat-grid">
        <div className="stat">
          <div className="value">{money(d.earningsToDate)}</div>
          <div className="label">{t('Đã nhận')}</div>
          <div className="note">{t('Các kỳ nghỉ đã hoàn tất')}</div>
        </div>
        <div className="stat">
          <div className="value">{money(d.earningsUpcoming)}</div>
          <div className="label">{t('Sắp nhận')}</div>
          <div className="note">{d.upcomingBookings} {t('lượt đặt sắp tới')}</div>
        </div>
        <div className="stat">
          <div className="value">{money(d.earningsToDate + d.earningsUpcoming)}</div>
          <div className="label">{t('Tổng cộng')}</div>
          <div className="note">{t('Sau phí dịch vụ chủ nhà')} {pct}%</div>
        </div>
      </div>

      <HostReport />
      <TaxReport />

      <section style={{ marginTop: 34 }}>
        <h2 className="section-title" style={{ fontSize: 20 }}>{t('Cách Staylio tính tiền')}</h2>
        <div className="know-grid" style={{ marginTop: 16 }}>
          <div className="know">
            <h3><Icon name="star" size={18} /> {t('Phí dịch vụ')}</h3>
            <ul><li>{t('Staylio giữ')} {pct}% {t('trên tạm tính của mỗi lượt đặt thành công.')}</li></ul>
          </div>
          <div className="know">
            <h3><Icon name="heart" size={18} /> {t('Bạn nhận')}</h3>
            <ul><li>{t('Toàn bộ tiền phòng và phí dọn dẹp trừ phí dịch vụ, chuyển 24 giờ sau khi khách nhận phòng.')}</li></ul>
          </div>
          <div className="know">
            <h3><Icon name="globe" size={18} /> {t('Huỷ & hoàn')}</h3>
            <ul><li>{t('Phí vệ sinh luôn được hoàn 100% cho khách ở mọi chính sách huỷ.')}</li></ul>
          </div>
        </div>
      </section>
    </div>
  );
}

/**
 * docs/01 TC-04, docs/02 G7 — the year written down for whoever does the host's
 * tax. Only completed stays are in it, counted by the year they ended; the CSV
 * carries every stay behind the totals so the file can be checked rather than
 * trusted.
 */
function TaxReport() {
  const [report, setReport] = useState(null);
  const [year, setYear] = useState(null);

  useEffect(() => {
    api.taxReport(year).then(setReport).catch(err => toast(err.message));
  }, [year]);

  if (!report) return null;

  const rows = report.months.filter(m => m.stays > 0);

  return (
    <section style={{ marginTop: 34 }}>
      <div className="page-head" style={{ marginBottom: 0 }}>
        <h2 className="section-title" style={{ fontSize: 20 }}>{t('Báo cáo thuế')}</h2>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {report.years.length > 1 && (
            <select value={report.year} onChange={e => setYear(Number(e.target.value))}
                    style={{ padding: '8px 10px', border: '1px solid var(--line)', borderRadius: 10, fontSize: 14 }}>
              {report.years.map(y => <option key={y} value={y}>{t('Năm')} {y}</option>)}
            </select>
          )}
          <a className="btn btn-outline btn-sm" download
             href={`/api/host/tax-report.csv?year=${report.year}`}>{t('Tải báo cáo thuế')}</a>
        </div>
      </div>

      <p className="section-sub">{t(report.note)}</p>

      {report.stays === 0 ? (
        <p className="section-sub" style={{ marginTop: 12 }}>{t('Năm')} {report.year} {t('chưa có đơn nào hoàn tất.')}</p>
      ) : (
        <>
          <div className="table-wrap" style={{ marginTop: 16 }}>
            <table className="admin-table">
              <thead>
                <tr>
                  <th>{t('Tháng')}</th><th>{t('Số đơn')}</th>
                  <th style={{ textAlign: 'right' }}>{t('Khách trả')}</th>
                  <th style={{ textAlign: 'right' }}>{t('Thuế')}</th>
                  <th style={{ textAlign: 'right' }}>{t('Phí dịch vụ')}</th>
                  <th style={{ textAlign: 'right' }}>{t('Bạn nhận')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(m => (
                  <tr key={m.month}>
                    <td>{t(m.label)}</td>
                    <td>{m.stays}</td>
                    <td style={{ textAlign: 'right' }}>{money(m.guestPaid)}</td>
                    <td style={{ textAlign: 'right' }}>{money(m.tax)}</td>
                    <td style={{ textAlign: 'right' }}>{money(m.hostServiceFee)}</td>
                    <td style={{ textAlign: 'right' }}>{money(m.hostPayout)}</td>
                  </tr>
                ))}
                <tr>
                  <td><b>{t('Cả năm')} {report.year}</b></td>
                  <td><b>{report.stays}</b></td>
                  <td style={{ textAlign: 'right' }}><b>{money(report.guestPaid)}</b></td>
                  <td style={{ textAlign: 'right' }}><b>{money(report.tax)}</b></td>
                  <td style={{ textAlign: 'right' }}><b>{money(report.hostServiceFee)}</b></td>
                  <td style={{ textAlign: 'right' }}><b>{money(report.hostPayout)}</b></td>
                </tr>
              </tbody>
            </table>
          </div>

          {!!report.taxes.length && (
            <div className="table-wrap" style={{ marginTop: 16 }}>
              <table className="admin-table">
                <thead>
                  <tr><th>{t('Loại thuế')}</th><th>{t('Số đơn')}</th><th style={{ textAlign: 'right' }}>{t('Số tiền')}</th></tr>
                </thead>
                <tbody>
                  {/* Not `t` — that name belongs to the translator in this file. */}
                  {report.taxes.map(line => (
                    <tr key={line.name}>
                      <td>{t(line.name)}</td>
                      <td>{line.stays}</td>
                      <td style={{ textAlign: 'right' }}>{money(line.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </section>
  );
}

/*
 * docs/02 G7 — the host's report, in the four blocks the screen asks for.
 *
 * Three of them were already here in pieces: the view counts (QL-16), the tax
 * year (TC-04) and the improvement checklist (QL-18). What was missing is what
 * turns numbers into a decision — money split by whether it has actually
 * arrived, the rate a room really sold for next to what the neighbours charge,
 * and which of the six review categories is moving.
 */
const REVIEW_CATEGORIES = [
  ['cleanliness', 'Mức độ sạch sẽ'], ['accuracy', 'Độ chính xác'], ['checkIn', 'Nhận phòng'],
  ['communication', 'Giao tiếp'], ['location', 'Vị trí'], ['value', 'Giá trị']
];

function HostReport() {
  const [report, setReport] = useState(null);
  const [days, setDays] = useState(30);

  useEffect(() => {
    api.hostReport(days).then(setReport).catch(err => toast(err.message));
  }, [days]);

  if (!report) return null;

  const months = report.months ?? [];
  // One scale for both stacked parts, so "đã trả" and "sắp trả" are comparable
  // between months rather than each column being scaled to itself.
  const peak = Math.max(...months.map(m => Number(m.paid) + Number(m.upcoming)), 1);

  return <>
    <section style={{ marginTop: 34 }}>
      <div className="page-head" style={{ marginBottom: 0 }}>
        <div>
          <h2 className="section-title" style={{ fontSize: 20 }}>{t('Thu nhập theo tháng')}</h2>
          <p className="section-sub">{t('Tính theo tháng khách trả phòng, vì đó là tháng bạn kiếm được khoản đó.')}</p>
        </div>
        <a className="btn btn-outline btn-sm" href="/api/host/earnings.csv" download>{t('Tải file doanh thu')}</a>
      </div>

      <div style={{ display: 'flex', gap: 18, marginTop: 12, fontSize: 13, color: 'var(--ink-muted)' }}>
        <span><i style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 3, background: 'var(--brand)' }} /> {t('Đã trả')}</span>
        <span><i style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 3, background: 'var(--divider)' }} /> {t('Sắp trả')}</span>
      </div>

      <div className="bar-chart">
        {months.map(m => {
          const paid = Number(m.paid);
          const soon = Number(m.upcoming);
          return (
            <div className="bar-col" key={`${m.year}-${m.month}`}
                 title={`${m.label}: ${t('đã trả')} ${money(paid)} · ${t('sắp trả')} ${money(soon)} · ${m.nights} ${t('đêm')}`}>
              {/* Stacked, not side by side: the two together are the month's
                  income, and showing them apart invites reading the taller one
                  as the total. */}
              <div style={{ width: '100%', maxWidth: 54, display: 'flex', flexDirection: 'column',
                            justifyContent: 'flex-end', height: '100%' }}>
                <div className="bar" style={{ height: `${(soon / peak) * 100}%`, minHeight: soon > 0 ? 4 : 0,
                                              background: 'var(--divider)', borderRadius: '6px 6px 0 0' }} />
                <div className="bar" style={{ height: `${(paid / peak) * 100}%`, minHeight: paid > 0 ? 4 : 0,
                                              borderRadius: soon > 0 ? 0 : '6px 6px 0 0' }} />
              </div>
              <span className="bar-label">{m.label}</span>
              <span className="bar-value">{money(paid + soon)}</span>
            </div>
          );
        })}
      </div>
    </section>

    <section style={{ marginTop: 34 }}>
      <div className="page-head" style={{ marginBottom: 0 }}>
        <h2 className="section-title" style={{ fontSize: 20 }}>{t('Hiệu suất tin đăng')}</h2>
        <select value={days} onChange={e => setDays(Number(e.target.value))}
                style={{ padding: '8px 10px', border: '1px solid var(--line)', borderRadius: 10, fontSize: 14 }}>
          <option value={7}>{t('7 ngày qua')}</option>
          <option value={30}>{t('30 ngày qua')}</option>
          <option value={90}>{t('90 ngày qua')}</option>
        </select>
      </div>

      {report.listings.length === 0 ? (
        <p className="section-sub" style={{ marginTop: 12 }}>{t('Bạn chưa có tin đăng nào.')}</p>
      ) : (
        <div className="table-wrap" style={{ marginTop: 16 }}>
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('Tin đăng')}</th>
                <th style={{ textAlign: 'right' }}>{t('Lượt xem')}</th>
                <th style={{ textAlign: 'right' }}>{t('Lượt lưu')}</th>
                <th style={{ textAlign: 'right' }}>{t('Lượt đặt')}</th>
                <th style={{ textAlign: 'right' }}>{t('Xem → đặt')}</th>
                <th style={{ textAlign: 'right' }}>{t('Lấp đầy')}</th>
                <th style={{ textAlign: 'right' }}>{t('Giá trung bình')}</th>
                <th style={{ textAlign: 'right' }}>{t('Mặt bằng khu vực')}</th>
              </tr>
            </thead>
            <tbody>
              {report.listings.map(r => (
                <tr key={r.listingId}>
                  <td>{r.title}{!r.isPublished && <span className="meta"> · {t('ẩn')}</span>}</td>
                  <td style={{ textAlign: 'right' }}>{r.views}</td>
                  <td style={{ textAlign: 'right' }}>{r.saves}</td>
                  <td style={{ textAlign: 'right' }}>{r.bookings}</td>
                  <td style={{ textAlign: 'right' }}>{r.conversionPercent}%</td>
                  <td style={{ textAlign: 'right' }}>{r.occupancyPercent}%</td>
                  {/* Nothing sold in the window is a fact, not a gap — an em dash
                      says so, where "0₫" would read as a room given away. */}
                  <td style={{ textAlign: 'right' }}>
                    {Number(r.avgNightlyRate) > 0 ? money(r.avgNightlyRate) : '—'}
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    {r.marketSample > 0
                      ? <>{money(r.marketMedian)}<div className="meta">{r.marketSample} {t('chỗ tương đương')}</div></>
                      : <span className="meta">{t('chưa đủ để so')}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>

    <section style={{ marginTop: 34 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Điểm đánh giá theo tháng')}</h2>
      <p className="section-sub">
        {t('Điểm tổng chỉ nói điểm đã đổi. Sáu hạng mục nói hạng mục nào đổi.')}
      </p>

      {report.reviews.length === 0 ? (
        <p className="section-sub" style={{ marginTop: 12 }}>
          {report.reviewCount === 0
            ? t('Chưa có đánh giá nào trong 12 tháng qua.')
            : t('Chưa đủ đánh giá để vẽ xu hướng.')}
        </p>
      ) : (
        <div className="table-wrap" style={{ marginTop: 16 }}>
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('Tháng')}</th>
                <th style={{ textAlign: 'right' }}>{t('Số đánh giá')}</th>
                <th style={{ textAlign: 'right' }}>{t('Điểm tổng')}</th>
                {REVIEW_CATEGORIES.map(([key, label]) => (
                  <th key={key} style={{ textAlign: 'right' }}>{t(label)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {report.reviews.map(m => (
                <tr key={`${m.year}-${m.month}`}>
                  <td>{m.label}</td>
                  <td style={{ textAlign: 'right' }}>{m.count}</td>
                  <td style={{ textAlign: 'right' }}><b>★ {m.overall.toFixed(2)}</b></td>
                  {REVIEW_CATEGORIES.map(([key]) => (
                    <td key={key} style={{ textAlign: 'right' }}>{m[key].toFixed(2)}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  </>;
}
