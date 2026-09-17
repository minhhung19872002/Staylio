import { useEffect, useLayoutEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import {
  loadDetail, toggleFavorite, totalGuests, guestLabel, bumpTotalGuests,
  clearDates, openOverlay, openReport, set, setRoom, setRatePlan, shareListing
} from '../lib/store.js';
import { money, longDate, nightsBetween, monthLabel, dateTime } from '../lib/format.js';
import { Avatar } from '../components/Avatar.jsx';
import { Card } from '../components/Card.jsx';
import { Calendar } from '../components/Calendar.jsx';
import { DetailMap } from '../components/Maps.jsx';
import { Icon, AmenityIcon } from '../components/Icon.jsx';
import { TranslatedText } from '../components/TranslatedText.jsx';
import { GuestQuestions } from '../components/ListingQuestions.jsx';
import { PriceLines, ReviewStay, HelpfulButton } from '../components/modals/ListingModals.jsx';
import { t } from '../lib/i18n.js';
import { setPageMeta, setStructuredData, listingJsonLd, breadcrumbJsonLd, canonicalUrl } from '../lib/seo.js';
import { NotFound } from './NotFound.jsx';

const RATING_LABELS = {
  cleanliness: 'Mức độ sạch sẽ',
  accuracy: 'Độ chính xác',
  checkIn: 'Nhận phòng',
  communication: 'Giao tiếp',
  location: 'Vị trí',
  value: 'Giá trị'
};

/**
 * The photos are the first thing on the page, so going back to them means going
 * back to the top — which is also how the site header, let go on this page,
 * comes back. Anything below scrolls to itself.
 */
const scrollTo = id => {
  if (id === 'section-photos') { window.scrollTo({ top: 0, behavior: 'smooth' }); return; }
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

export function Detail() {
  const state = useStore();
  const { slug } = useParams();
  const navigate = useNavigate();

  useEffect(() => { loadDetail(slug); }, [slug]);

  // The room's own title and city, so a search result says what the place is
  // instead of repeating the site name — and a share on Zalo or Messenger shows
  // this room rather than the home page.
  useEffect(() => {
    const d = state.detail;
    if (!d || d.card?.slug !== slug) return;

    const c = d.card;
    const citySlug = state.meta?.cityLinks?.find(x => x.name === c.city)?.slug;

    setPageMeta({
      title: `${c.title} — ${c.city} | Staylio`,
      description: (d.description || '').replace(/\s+/g, ' ').trim().slice(0, 155)
        || `${c.typeLabel} tại ${c.city}, ${c.maxGuests} khách, ${c.bedrooms} phòng ngủ.`,
      // The room's own first photo, so pasting the link into Zalo or Messenger
      // shows the place rather than the site's default card.
      image: (c.images || [])[0],
    });

    setStructuredData({
      '@context': 'https://schema.org',
      '@graph': [
        listingJsonLd(c, { description: d.description, url: canonicalUrl(`/rooms/${slug}`) }),
        breadcrumbJsonLd([
          { name: 'Trang chủ', path: '/' },
          // The slug comes from the server's own list rather than being derived
          // here. Cities.Key strips prefixes — "TP. Hồ Chí Minh" routes as
          // "ho-chi-minh", not "tp-ho-chi-minh" — so a normalisation written
          // again in JavaScript would put a broken address into the breadcrumb,
          // and broken structured data is worse than none. No match, no step.
          ...(citySlug ? [{ name: c.city, path: `/thanh-pho/${citySlug}` }] : []),
          { name: c.title, path: `/rooms/${slug}` },
        ]),
      ],
    });
  }, [state.detail, state.meta, slug]);

  // On a listing page the site header scrolls away and the section nav takes the
  // top of the screen instead. You have already chosen the place by this point;
  // what is worth a permanent strip of the window is moving around inside it.
  // The class goes on the body because the header lives outside this page.
  //
  // Before paint, or the first frame of the page is drawn with the header still
  // sticky and everything below it 99px lower, and you watch it jump.
  useLayoutEffect(() => {
    document.body.classList.add('page-detail');
    return () => document.body.classList.remove('page-detail');
  }, []);

  // A slug with nothing behind it used to sit on the skeleton forever, which is
  // both a dead end for a guest and the blank 200 page a crawler indexes.
  if (state.detailMissing) return (
    <NotFound
      title={t('Không tìm thấy chỗ nghỉ này')}
      body={t('Tin đăng có thể đã được gỡ, hoặc đường dẫn bị sai một ký tự.')} />
  );

  if (state.detailLoading || !state.detail) return <DetailSkeleton />;

  const d = state.detail;
  const c = d.card;
  const nights = nightsBetween(state.checkIn, state.checkOut);

  const share = () => shareListing(c);

  return <>
    <div className="shell" style={{ paddingBlock: '20px 110px' }}>
      <button className="back-link" onClick={() => navigate('/')}>← {t('Tất cả chỗ nghỉ')}</button>

      <div className="detail-head">
        <div style={{ minWidth: 0 }}>
          <h1>{c.title}</h1>
          <div className="detail-sub">
            <span>★ {c.rating.toFixed(2)}</span>
            <span className="dot">·</span>
            <u onClick={() => openOverlay('reviews')}>{d.reviews.length} {t('đánh giá')}</u>
            {c.isSuperhost && <><span className="dot">·</span><span>{t('Siêu chủ nhà')}</span></>}
            {/* docs/01 TĐ-23 — only when the calendar backs it up; the reason is
                the tooltip so the claim is checkable against the picker below. */}
            {d.rareFind && <>
              <span className="dot">·</span>
              <span className="rare-find" title={d.rareFind.reason}>{t(d.rareFind.label)}</span>
            </>}
            <span className="dot">·</span>
            <u onClick={() => scrollTo('section-location')}>{c.city}, {c.country}</u>
          </div>
        </div>
        <div className="detail-actions">
          <button className="text-btn" onClick={share}>⤴ {t('Chia sẻ')}</button>
          <button className={`text-btn ${c.isFavorite ? 'is-on' : ''}`} onClick={() => toggleFavorite(c.id)}>
            ♥ {c.isFavorite ? t('Đã lưu') : t('Lưu chỗ nghỉ')}
          </button>
        </div>
      </div>

      <Gallery card={c} />
      <SubNav detail={d} />

      <div className="detail-body">
        <div className="detail-main">
          <GuestFavoriteBanner detail={d} card={c} />
          <Summary detail={d} card={c} />
          <HostRow host={d.host} />
          <Highlights detail={d} card={c} />
          <Sleeping bedrooms={d.bedrooms} />
          <Amenities detail={d} />
          <CalendarSection nights={nights} city={c.city} />
          <Reviews detail={d} card={c} />
          <GuestQuestions listingId={c.id} isOwnListing={false} />
          <Location card={c} landmarks={d.landmarks} />
          <Guidebook detail={d} />
          <HostProfile detail={d} />
          <ThingsToKnow detail={d} />
        </div>

        <BookPanel detail={d} card={c} />
      </div>

      {!!d.similar.length && (
        <section style={{ marginTop: 44, borderTop: '1px solid var(--divider)', paddingTop: 32 }}>
          <h2 className="section-title" style={{ fontSize: 22 }}>{t('Chỗ nghỉ tương tự')}</h2>
          <p className="section-sub">{t('Cùng khu vực hoặc cùng loại hình với')} {c.title}</p>
          <div className="card-grid">{d.similar.map(s => <Card key={s.id} card={s} lazy />)}</div>
        </section>
      )}
    </div>

    <MobileBar nights={nights} card={c} />
  </>;
}

/*
 * The stand-in shown while the listing loads. It has to be laid out exactly like
 * the page it stands in for, or the swap moves everything: the old one put the
 * photos first and the title underneath, so when the real page arrived — title
 * first, photos below — the heading jumped 418px up the screen. That jump is
 * what a click on a card felt like.
 *
 * Every measurement below is the real page's, taken off it: 20px above, the back
 * link, 8px, the title block, 16px, then the same photo mosaic.
 */
function DetailSkeleton() {
  const tile = { borderRadius: 12 };

  return (
    <div className="shell" style={{ paddingBlock: '20px 110px' }} aria-busy="true" aria-label={t('Đang tải chỗ nghỉ')}>
      <div className="skeleton" style={{ width: 150, height: 30, borderRadius: 8 }} />

      <div style={{ marginTop: 8, height: 61 }}>
        <div className="sk-line skeleton" style={{ width: 'min(420px, 70%)', height: 30, marginBottom: 9 }} />
        <div className="sk-line skeleton" style={{ width: 'min(320px, 55%)', height: 16 }} />
      </div>

      <div className="gallery" style={{
        marginTop: 16,
        gridTemplateColumns: '2fr 1fr 1fr',
        gridTemplateRows: 'repeat(2, clamp(110px, 16vw, 215px))'
      }}>
        <div className="skeleton" style={{ ...tile, gridRow: 'span 2' }} />
        {[1, 2, 3, 4].map(i => <div key={i} className="skeleton" style={tile} />)}
      </div>
    </div>
  );
}

function Gallery({ card }) {
  const imgs = card.images.slice(0, 5);
  // A place with fewer than five photos still gets the five-tile mosaic; the
  // padding repeats the last one, so a click on it means that photo.
  while (imgs.length < 5) imgs.push(imgs[imgs.length - 1] ?? '');

  // Clicking a photo goes straight to it in the viewer. The grid is what the
  // button is for — nobody who clicked a particular picture wanted a contact
  // sheet of all of them first.
  const openAt = i => {
    set({ photoIndex: Math.min(i, card.images.length - 1) });
    openOverlay('photos');
  };

  return (
    <div className="gallery" id="section-photos"
         style={{ gridTemplateColumns: '2fr 1fr 1fr', gridTemplateRows: 'repeat(2,clamp(110px,16vw,215px))' }}>
      {imgs.map((src, i) => (
        <figure className="gallery-tile" key={i} onClick={() => openAt(i)}
                style={{ margin: 0, ...(i === 0 ? { gridRow: 'span 2' } : {}) }}>
          <img src={src} alt={`${card.title} — ${t('ảnh')} ${i + 1}`} loading={i === 0 ? 'eager' : 'lazy'} decoding="async" />
        </figure>
      ))}
      <button className="gallery-all"
              onClick={() => { set({ photoIndex: null }); openOverlay('photos'); }}>⊞ {t('Hiện tất cả ảnh')}</button>
    </div>
  );
}

/** Sticky in-page nav, mirroring airbnb.com's NAV_DEFAULT section. */
function SubNav({ detail }) {
  const links = [
    ['section-photos', t('Ảnh')], ['section-amenities', t('Tiện nghi')],
    ['section-reviews', t('Đánh giá')], ['section-location', t('Vị trí')],
    // Only when the host wrote one — a link to an absent section scrolls nowhere.
    ...(detail.guidebook?.length ? [['section-guidebook', t('Cẩm nang')]] : [])
  ];
  return (
    <nav className="detail-nav" aria-label={t('Mục trong trang')}>
      {links.map(([target, label]) => <button key={target} onClick={() => scrollTo(target)}>{label}</button>)}
    </nav>
  );
}

function GuestFavoriteBanner({ detail, card }) {
  if (!card.isGuestFavorite || !card.reviewCount) return null;

  return (
    <div className="gf-banner">
      <div className="gf-title">
        <span className="gf-laurel"><Icon name="star" size={30} filled weight={0} /></span>
        <div>
          <b>{t('Khách yêu thích')}</b>
          <span>{t('Một trong những chỗ nghỉ được yêu thích nhất trên Staylio')}</span>
        </div>
      </div>
      <div className="gf-metric"><b>{card.rating.toFixed(2)}</b><span>★★★★★</span></div>
      <div className="gf-metric"><b>{detail.reviews.length}</b><span>{t('đánh giá')}</span></div>
    </div>
  );
}

function Summary({ detail, card }) {
  return (
    <div className="summary-block">
      {/* typeLabel is the server's own wording (CatalogService.CategoryLabel), a
          closed set — so it goes through the dictionary like the rest of the chrome. */}
      <div className="summary-title">{t(card.typeLabel)} {t('tại')} {card.city} {t('do')} {detail.host.name} {t('cho thuê')}</div>
      <div className="summary-meta">
        {card.maxGuests} {t('khách')} · {card.bedrooms} {t('phòng ngủ')} · {card.beds} {t('giường')} · {card.bathrooms} {t('phòng tắm')}
      </div>
      {/* docs/01 TĐ-03 — a description is the host's own prose, so it is machine
          translated automatically and said to be, with the original a tap away. */}
      <TranslatedText as="p" className="summary-desc" text={detail.description} />
      {card.highlight && (
        <p className="summary-desc" style={{ marginTop: 10 }}>
          <b>{t('Điểm nổi bật:')}</b>{' '}
          <TranslatedText as="span" text={card.highlight} />
        </p>
      )}
    </div>
  );
}

function HostRow({ host }) {
  return (
    <div className="host-row">
      <Avatar url={host.avatarUrl} initials={host.initials} className="host-avatar" />
      <div>
        <div className="host-name">{t('Chủ nhà:')} {host.name}</div>
        <div className="host-meta">
          {host.isSuperhost ? `${t('Siêu chủ nhà')} · ` : ''}{host.yearsHosting} {t('năm kinh nghiệm đón khách')}
        </div>
      </div>
    </div>
  );
}

function Highlights({ detail, card }) {
  const items = [
    card.isSuperhost && {
      ic: 'star', title: `${detail.host.name} ${t('là Siêu chủ nhà')}`,
      sub: t('Siêu chủ nhà là những chủ nhà giàu kinh nghiệm, được đánh giá cao.')
    },
    card.amenityKeys.includes('selfcheckin') && {
      ic: 'selfcheckin', title: t('Tự nhận phòng'),
      sub: t('Bạn có thể tự nhận phòng bằng khoá số ở cửa.')
    },
    /*
     * docs/03 §4 — the headline and the sentence under it both come from the
     * listing's own tier. This used to hard-code "Huỷ miễn phí trước 48 giờ",
     * which is not one of the six policies, and printed it directly above the
     * sentence saying the place was non-refundable.
     */
    { ic: 'heart', title: t(card.cancellationHeadline), sub: t(detail.cancellationPolicy) },
    card.amenityKeys.includes('workspace') && {
      ic: 'workspace', title: t('Không gian riêng để làm việc'),
      sub: t('Phòng có bàn làm việc và wifi tốc độ cao, phù hợp làm việc từ xa.')
    }
  ].filter(Boolean);

  return (
    <div className="highlights">
      {items.map(i => (
        <div className="highlight" key={i.title}>
          <span className="ic"><Icon name={i.ic} size={24} /></span>
          <span className="tx"><b>{i.title}</b><span>{i.sub}</span></span>
        </div>
      ))}
    </div>
  );
}

/** docs/01 TĐ-05 — which beds are in which room, as the host described them. */
function Sleeping({ bedrooms }) {
  if (!bedrooms?.length) return null;

  return (
    <section className="detail-section" id="section-sleeping">
      <h2>{t('Nơi bạn sẽ ngủ')}</h2>
      <div className="sleep-grid">
        {bedrooms.map((room, i) => (
          <div className="sleep-card" key={`${room.name}-${i}`}>
            <span className="ic"><Icon name="house" size={26} /></span>
            <b>{t(room.name)}</b>
            <span>{summariseBeds(room.beds)}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

/**
 * "2 giường đôi, 1 giường đơn" rather than a bare list. The bed kinds are the
 * fixed set the wizard offers (ListingWizard BED_TYPES), so they translate;
 * the lowercasing happens after t() or the English word would stay capitalised.
 */
function summariseBeds(beds) {
  const counts = new Map();
  for (const bed of beds) counts.set(bed, (counts.get(bed) ?? 0) + 1);
  return [...counts].map(([bed, n]) => `${n} ${t(bed).toLowerCase()}`).join(', ');
}

/**
 * docs/01 TĐ-04 — the present amenities come first, then the notable ones this
 * place does not have, struck through so absence is stated rather than implied.
 */
function Amenities({ detail }) {
  const present = detail.allAmenities.filter(a => a.available);
  const missing = detail.allAmenities.filter(a => !a.available);
  const preview = [...present.slice(0, 8), ...missing.slice(0, 4)];

  return (
    <section className="detail-section" id="section-amenities">
      <h2>{t('Tiện nghi tại đây')}</h2>
      <div className="amenity-grid">
        {preview.map(a => (
          <div className={`amenity ${a.available ? '' : 'is-missing'}`} key={a.key}>
            <span className="ic"><AmenityIcon name={a.key} /></span><span>{t(a.label)}</span>
          </div>
        ))}
      </div>
      {detail.allAmenities.length > preview.length && (
        <button className="btn btn-outline btn-sm" style={{ marginTop: 18 }} onClick={() => openOverlay('amenities')}>
          {t('Hiện tất cả')} {detail.allAmenities.length} {t('tiện nghi')}
        </button>
      )}
    </section>
  );
}

function CalendarSection({ nights, city }) {
  const state = useStore();

  return (
    <section className="detail-section" id="section-calendar">
      <h2>{nights} {t('đêm')} {t('tại')} {city}</h2>
      <p style={{ margin: '-8px 0 18px', fontSize: 14, color: 'var(--ink-muted)' }}>
        {longDate(state.checkIn)} – {longDate(state.checkOut)}
      </p>
      <Calendar months={4} />
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 14 }}>
        <button className="text-btn" onClick={clearDates}>{t('Xoá ngày')}</button>
      </div>
    </section>
  );
}

/**
 * Airbnb tags reviews with recurring topics and a count. We derive the same
 * signal from keyword hits so the chips reflect what guests actually wrote.
 */
const REVIEW_TOPICS = [
  ['Sạch sẽ', ['sạch', 'gọn gàng', 'ngăn nắp']],
  ['Vị trí', ['vị trí', 'đi bộ', 'gần', 'trung tâm', 'biển']],
  ['Chủ nhà', ['chủ nhà', 'phản hồi', 'chu đáo', 'nhiệt tình', 'tinh tế']],
  ['Tiện nghi', ['bếp', 'wifi', 'giường', 'điều hoà', 'máy lạnh', 'trái cây']],
  ['Yên tĩnh', ['yên tĩnh', 'ngủ ngon', 'thoải mái']],
  ['Đáng tiền', ['giá', 'hợp lý', 'đáng']]
];

function Reviews({ detail, card }) {
  const navigate = useNavigate();
  const rb = detail.ratingBreakdown;
  const shown = detail.reviews.slice(0, 6);

  const topics = REVIEW_TOPICS
    .map(([label, keywords]) => ({
      label,
      n: detail.reviews.filter(r => keywords.some(k => r.text.toLowerCase().includes(k))).length
    }))
    .filter(t => t.n > 0)
    .sort((a, b) => b.n - a.n);

  return (
    <section className="detail-section" id="section-reviews">
      <div className="rating-summary">
        <span className="rating-big">★ {card.rating.toFixed(2)}</span>
        <span style={{ fontSize: 15, color: 'var(--ink-muted)' }}>· {detail.reviews.length} {t('đánh giá')}</span>
        {card.isGuestFavorite && <span className="badge confirmed" style={{ marginLeft: 6 }}>{t('Khách yêu thích')}</span>}
      </div>

      <StarDistribution counts={rb.starCounts} total={detail.reviews.length} />

      <div className="rating-bars">
        {Object.entries(RATING_LABELS).map(([key, label]) => (
          <div className="rating-bar" key={key}>
            <span>{t(label)}</span>
            <span className="track"><span className="fill" style={{ width: `${(rb[key] / 5) * 100}%` }} /></span>
            <span className="val">{rb[key].toFixed(1)}</span>
          </div>
        ))}
      </div>

      {!!topics.length && (
        <div className="topic-row">
          {topics.map(topic => <span className="topic-chip" key={topic.label}>{t(topic.label)} <b>{topic.n}</b></span>)}
        </div>
      )}

      <div className="review-grid">
        {shown.map(r => (
          <article className="review" key={r.id ?? `${r.authorName}-${r.when}`}>
            <div className="review-head">
              <Avatar initials={r.authorInitials} />
              <div style={{ minWidth: 0 }}>
                {/* docs/01 TK-05 — a real guest's name opens their profile. */}
                {r.authorUserId
                  ? <button className="link-btn review-name"
                            onClick={() => navigate(`/users/${r.authorUserId}`)}>{r.authorName}</button>
                  : <div className="review-name">{r.authorName}</div>}
                {/* `when` is Profiles.MonthLabel — "Tháng 8, 2026", a shape the
                    dictionary reduces to a number pair. */}
                <div className="review-when">{r.authorLocation ? `${r.authorLocation} · ` : ''}{monthLabel(r.when)}</div>
              </div>
            </div>
            <ReviewStay review={r} />
            <TranslatedText as="p" text={r.text} />
            <HostReply review={r} />
            <HelpfulButton review={r} />
            {/* docs/01 AT-02 — a review is reportable in its own right; before
                this the only way to flag one was to report the whole listing. */}
            {r.id && (
              <button className="text-btn" style={{ marginTop: 8, fontSize: 12.5 }}
                      onClick={() => openReport('review', r.id, `Đánh giá của ${r.authorName}`)}>
                ⚑ {t('Báo cáo đánh giá')}
              </button>
            )}
          </article>
        ))}
      </div>

      {detail.reviews.length > shown.length && (
        <button className="btn btn-outline btn-sm" style={{ marginTop: 20 }} onClick={() => openOverlay('reviews')}>
          {t('Hiện tất cả')} {detail.reviews.length} {t('đánh giá')}
        </button>
      )}
    </section>
  );
}

/** docs/01 TĐ-10 — how the reviews split across five stars, five first. */
export function StarDistribution({ counts, total }) {
  if (!counts?.length || !total) return null;

  return (
    <div className="star-dist">
      {counts.map((n, i) => (
        <div className="star-dist-row" key={i}>
          <span>{5 - i} {t('sao')}</span>
          <span className="track"><span className="fill" style={{ width: `${(n / total) * 100}%` }} /></span>
          <span>{n}</span>
        </div>
      ))}
    </div>
  );
}

/** docs/01 TĐ-12 — the host's single public answer, under the review. */
export function HostReply({ review }) {
  if (!review.hostReply) return null;

  return (
    <div className="review-reply">
      <b>{t('Phản hồi của chủ nhà')}{review.hostRepliedAt ? ` · ${longDate(review.hostRepliedAt.slice(0, 10))}` : ''}</b>
      <p>{review.hostReply}</p>
    </div>
  );
}

/** Neighbourhood context derived from what the listing actually offers. */
function neighbourhoodHighlights(c) {
  const out = [];
  if (c.amenityKeys.includes('beach')) out.push(['beach', t('Sát biển'), t('Đi bộ vài phút là xuống tới bãi tắm.')]);
  if (c.amenityKeys.includes('bike')) out.push(['bike', t('Dễ đi lại'), t('Có xe đạp miễn phí để khám phá quanh khu.')]);
  if (c.amenityKeys.includes('view')) out.push(['view', t('Tầm nhìn đẹp'), t('Khung cảnh mở, ít bị che chắn.')]);
  if (c.amenityKeys.includes('parking')) out.push(['parking', t('Đỗ xe thuận tiện'), t('Có chỗ đậu xe ngay tại chỗ nghỉ.')]);
  if (out.length < 2) out.push(['house', `${t('Trung tâm')} ${c.city}`, t('Gần quán ăn, cà phê và điểm tham quan chính.')]);
  return out.slice(0, 3);
}

function Location({ card, landmarks }) {
  return (
    <section className="detail-section" id="section-location">
      <h2>{t('Vị trí chỗ nghỉ')}</h2>
      <p style={{ margin: '-8px 0 16px', fontSize: 14.5, color: 'var(--ink-muted)' }}>{card.city}, {card.country}</p>
      <DetailMap latitude={card.latitude} longitude={card.longitude} />

      {/* docs/01 TĐ-13 — how far the places somebody came for actually are. */}
      {!!landmarks?.length && (
        <ul className="landmarks">
          {landmarks.map(l => (
            <li key={l.name}><span>{l.name}</span><b>{l.distance}</b></li>
          ))}
        </ul>
      )}

      <h3 style={{ margin: '22px 0 12px', fontSize: 15, fontWeight: 700 }}>{t('Điểm nổi bật của khu vực')}</h3>
      <div className="hood-grid">
        {neighbourhoodHighlights(card).map(([ic, title, text]) => (
          <div className="hood" key={title}>
            <span className="ic"><Icon name={ic} size={22} /></span>
            <div><b>{title}</b><span>{text}</span></div>
          </div>
        ))}
      </div>

      <p style={{ margin: '16px 0 0', fontSize: 13.5, color: 'var(--ink-muted)' }}>
        {t('Vị trí chính xác được cung cấp sau khi đặt chỗ thành công.')}
      </p>
    </section>
  );
}

/**
 * docs/01 TĐ-22 — the host's own local guidebook.
 *
 * Everything here is written by a person, so it goes through TranslatedText
 * rather than the interface dictionary: only the headings are ours to translate,
 * and those already arrive as Vietnamese keys the dictionary knows.
 */
function Guidebook({ detail }) {
  const groups = detail.guidebook;
  if (!groups?.length) return null;

  return (
    <section className="detail-section" id="section-guidebook">
      <h2>{t('Cẩm nang của chủ nhà')}</h2>
      {/* No name inside this sentence: Korean and Japanese put the object before
          the verb, and a name spliced mid-clause lands in the wrong place. */}
      <p style={{ margin: '-8px 0 18px', fontSize: 14.5, color: 'var(--ink-muted)' }}>
        {t('Những chỗ chủ nhà thật sự hay lui tới, không phải danh sách chép trên mạng.')}
      </p>

      <div className="guidebook">
        {groups.map(g => (
          <div className="guidebook-group" key={g.category}>
            <h3>{t(g.label)}</h3>
            <ul>
              {g.places.map(p => (
                <li key={p.id}>
                  <div className="guidebook-place">
                    <TranslatedText as="b" text={p.name} />
                    {p.distance && <span className="guidebook-far">{p.distance}</span>}
                  </div>
                  {p.note && <TranslatedText as="p" className="guidebook-note" text={p.note} />}
                  {p.address && <span className="guidebook-addr">{p.address}</span>}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </section>
  );
}

function HostProfile({ detail }) {
  const h = detail.host;
  const navigate = useNavigate();

  return (
    <section className="detail-section" id="section-host">
      <h2>{t('Gặp gỡ chủ nhà')}</h2>
      <div style={{ display: 'flex', gap: 20, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, minWidth: 220 }}>
          <Avatar url={h.avatarUrl} initials={h.initials} className="host-avatar" size={64} />
          <div>
            {/* docs/01 TK-05 — the name opens the profile when there is one to open. */}
            {h.userId
              ? <button className="link-btn" style={{ fontSize: 20, fontWeight: 800 }}
                        onClick={() => navigate(`/users/${h.userId}`)}>{h.name}</button>
              : <div style={{ fontSize: 20, fontWeight: 800 }}>{h.name}</div>}
            <div className="host-meta">{h.isSuperhost ? t('Siêu chủ nhà') : t('Chủ nhà')}</div>
          </div>
        </div>
        <div style={{ display: 'grid', gap: 8, fontSize: 14, color: 'var(--ink-body)', minWidth: 200 }}>
          <div><b>{h.totalReviews}</b> {t('đánh giá')}</div>
          <div><b>★ {h.averageRating.toFixed(2)}</b> {t('điểm trung bình')}</div>
          <div><b>{h.listingCount}</b> {t('chỗ nghỉ đang cho thuê')}</div>
          <div>{t(monthLabel(h.joinedLabel))}</div>
        </div>
      </div>
      {h.bio && <TranslatedText as="p" className="summary-desc" style={{ marginTop: 18 }} text={h.bio} />}
      <div style={{ display: 'grid', gap: 6, marginTop: 16, fontSize: 14, color: 'var(--ink-body)' }}>
        <div>{t('Tỉ lệ phản hồi:')} <b>{h.responseRate}</b></div>
        <div>{t('Thời gian phản hồi:')} <b>{t(h.responseTime)}</b></div>
        {/* docs/01 TĐ-14 — what they speak, and who else answers for this place.
            The language names come from Profiles.SpokenLanguages, a closed list;
            the co-host names are people, so they are left as written. */}
        {!!h.languages?.length && <div>{t('Ngôn ngữ:')} <b>{h.languages.map(l => t(l)).join(', ')}</b></div>}
        {!!h.coHosts?.length && <div>{t('Đồng quản lý:')} <b>{h.coHosts.join(', ')}</b></div>}
      </div>
      {/* One door for both cases (docs/01 TN-01). This used to fork: reachable
          hosts got a button that sent a sentence the guest never wrote, and the
          rest got a dialog that threw the message away. The dialog does the
          sending now, and it is the only way in. The label carries no name —
          Korean and Japanese would put it on the wrong side of the verb. */}
      <button className="btn btn-outline btn-sm" style={{ marginTop: 18 }}
              onClick={() => openOverlay('contact-host')}>
        {t('Nhắn tin cho chủ nhà')}
      </button>
    </section>
  );
}

function ThingsToKnow({ detail }) {
  return (
    <section className="detail-section" id="section-know">
      <h2>{t('Những điều cần biết')}</h2>
      <div className="know-grid">
        <div className="know">
          <h3>{t('Nội quy nhà')}</h3>
          <ul>
            {/* docs/01 CĐ-03 — the hours the host actually set, ahead of the free-text rules. */}
            {detail.checkInWindow && <li><TranslatedText as="span" text={detail.checkInWindow} /></li>}
            {detail.houseRules.map(r => <li key={r}><TranslatedText as="span" text={r} /></li>)}
          </ul>
        </div>
        <div className="know">
          <h3>{t('An toàn & chỗ ở')}</h3>
          <ul>{detail.safetyInfo.map(r => <li key={r}><TranslatedText as="span" text={r} /></li>)}</ul>
        </div>
        <div className="know">
          <h3>{t('Chính sách huỷ')}</h3>
          <ul><li>{t(detail.cancellationPolicy)}</li></ul>
        </div>
        {/* ChildPolicy.Lines — fixed sentences, so the dictionary covers them. */}
        {!!detail.childPolicy?.length && (
          <div className="know">
            <h3>{t('Trẻ em và giường phụ')}</h3>
            <ul>{detail.childPolicy.map(r => <li key={r}>{t(r)}</li>)}</ul>
          </div>
        )}
      </div>

      {/* docs/01 ĐG-12 — public record of the host pulling out of confirmed stays. */}
      {!!detail.cancellationNotes?.length && (
        <div className="cancel-notes">
          <h3>{t('Lịch sử huỷ đơn của chủ nhà')}</h3>
          {/* CancellationNotes.Compose writes these three sentences, not the host. */}
          <ul>{detail.cancellationNotes.map((n, i) => <li key={i}><TranslatedText as="span" text={n} /></li>)}</ul>
        </div>
      )}
    </section>
  );
}

/**
 * docs/01 MR-08 and MR-09 — a hotel sells rooms of a kind, so the guest picks
 * one before checkout and the price follows the room rather than the property.
 */
function RoomPicker({ rooms }) {
  const state = useStore();

  // Pick the first room that still has one free, so the panel is never in a
  // state where nothing is selected and the price means nothing.
  useEffect(() => {
    if (!rooms?.length) { if (state.roomTypeId) setRoom(null); return; }
    if (rooms.some(r => r.id === state.roomTypeId && r.available > 0)) return;
    setRoom(rooms.find(r => r.available > 0)?.id ?? rooms[0].id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rooms]);

  if (!rooms?.length) return null;

  return (
    <div className="room-picker">
      <p className="cap" style={{ margin: '14px 0 8px' }}>{t('Chọn loại phòng')}</p>
      {rooms.map(r => (
        <button key={r.id} className={`room-option ${state.roomTypeId === r.id ? 'is-on' : ''}`}
                disabled={r.available === 0} onClick={() => setRoom(r.id)}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <b>{r.name}</b>
            <span>{r.summary}</span>
            <i>{r.available > 0 ? `${t('Còn')} ${r.available}/${r.inventory} ${t('phòng')}` : t('Hết phòng cho ngày này')}</i>
          </div>
          <div className="room-price">{money(r.pricePerNight)}<span>/ {t('đêm')}</span></div>
        </button>
      ))}
      <RoomCount room={rooms.find(r => r.id === state.roomTypeId)} />
      <RatePlanOptions room={rooms.find(r => r.id === state.roomTypeId)} />
    </div>
  );
}

/** How many rooms of the chosen kind — up to what is free, and never more than nine. */
function RoomCount({ room }) {
  const state = useStore();
  if (!room || room.available < 2) return null;
  const max = Math.min(room.available, 9);
  const n = Math.min(state.roomCount, max);
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', margin: '10px 0 4px' }}>
      <span className="cap">{t('Số phòng')}</span>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <button className="round-btn" aria-label={t('Bớt một phòng')} disabled={n <= 1}
                onClick={() => setRatePlan({ roomCount: n - 1 })}>−</button>
        <b>{n}</b>
        <button className="round-btn" aria-label={t('Thêm một phòng')} disabled={n >= max}
                onClick={() => setRatePlan({ roomCount: n + 1 })}>+</button>
      </div>
    </div>
  );
}

/** Booking.com's rate plans: a cheaper non-refundable rate, and breakfast. */
function RatePlanOptions({ room }) {
  const state = useStore();
  if (!room || (!room.nonRefundableDiscountPercent && !room.breakfastPricePerGuest)) return null;
  return (
    <div style={{ display: 'grid', gap: 8, margin: '10px 0 4px' }}>
      {room.nonRefundableDiscountPercent > 0 && (
        <label className="check-row">
          <input type="checkbox" checked={state.nonRefundableRate}
                 onChange={e => setRatePlan({ nonRefundableRate: e.target.checked })} />
          <span>{t('Giá không hoàn tiền — rẻ hơn {}%').replace('{}', room.nonRefundableDiscountPercent)}</span>
        </label>
      )}
      {room.breakfastPricePerGuest > 0 && (
        <label className="check-row">
          <input type="checkbox" checked={state.breakfast}
                 onChange={e => setRatePlan({ breakfast: e.target.checked })} />
          <span>{t('Thêm bữa sáng — {} mỗi khách mỗi đêm').replace('{}', money(room.breakfastPricePerGuest))}</span>
        </label>
      )}
      {state.nonRefundableRate && (
        <p className="meta" style={{ margin: 0 }}>{t('Huỷ hoặc đổi ngày sẽ không được hoàn tiền phòng.')}</p>
      )}
    </div>
  );
}

function BookPanel({ detail, card }) {
  const state = useStore();
  const q = state.quote;
  const result = state.bookingResult;
  const rooms = detail?.roomTypes ?? null;

  // docs/07 §2.5 — no sign-in gate. The checkout asks a signed-out guest for a
  // name, an email and a phone instead, and books on those.
  const checkout = () => {
    set({ checkoutStep: 0, bookingError: null, rulesMissing: false, overlay: 'checkout' });
  };

  return (
    <aside className="book-panel">
      <div className="book-price">
        <span className="amount">{money(card.pricePerNight)}</span>
        <span className="per">/ {t('đêm')}{rooms ? ` · ${t('phòng rẻ nhất')}` : ''}</span>
        {card.isGuestFavorite && <span className="per" style={{ marginLeft: 'auto' }}>★ {card.rating.toFixed(2)}</span>}
      </div>

      <RoomPicker rooms={rooms} />

      <div className="book-fields">
        <div className="book-dates">
          <button className="book-field" onClick={() => openOverlay('dates')}>
            <span className="cap">{t('NHẬN PHÒNG')}</span>
            <span className="val">{longDate(state.checkIn)}</span>
          </button>
          <button className="book-field" onClick={() => openOverlay('dates')}>
            <span className="cap">{t('TRẢ PHÒNG')}</span>
            <span className="val">{longDate(state.checkOut)}</span>
          </button>
        </div>
        <div className="book-guests">
          <button className="book-guests-open" onClick={() => openOverlay('guests')}>
            <span className="cap">{t('KHÁCH')}</span>
            <span className="val">{guestLabel()}</span>
          </button>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <button className="round-btn lg" aria-label={t('Giảm số khách')}
                    disabled={totalGuests() <= 1} onClick={() => bumpTotalGuests(-1)}>−</button>
            <button className="round-btn lg" aria-label={t('Tăng số khách')}
                    disabled={totalGuests() >= card.maxGuests * (state.roomTypeId ? state.roomCount : 1)}
                    onClick={() => bumpTotalGuests(1)}>+</button>
          </div>
        </div>
      </div>

      <button className="btn btn-primary btn-block" style={{ marginTop: 16 }} onClick={checkout}>{t('Đặt chỗ ngay')}</button>
      <p className="book-note">{t('Bạn chưa bị trừ tiền ở bước này')}</p>

      {q ? <>
        <PriceLines q={q} />
        {q.guestsExceeded && (
          <div className="book-alert is-error">
            <b>{t('Vượt quá sức chứa')}</b>
            <span>{t('Chỗ nghỉ này nhận tối đa')} {q.maxGuests} {t('khách')}.</span>
          </div>
        )}
      </> : <div className="book-lines"><div className="book-line"><span>{t('Đang tính giá…')}</span></div></div>}

      {result && <BookingOutcome result={result} />}

      {state.bookingError && (
        <div className="book-alert is-error"><b>{t('Không đặt được')}</b><span>{state.bookingError}</span></div>
      )}

      <button className="text-btn" style={{ marginTop: 14, justifyContent: 'center' }}
              onClick={() => openReport('listing', card.id, card.title)}>⚑ {t('Báo cáo chỗ nghỉ này')}</button>
    </aside>
  );
}

/**
 * What just happened, in the panel the checkout modal was covering.
 *
 * It used to say one thing for all three endings: "đã giữ chỗ cho bạn … chủ
 * nhà sẽ xác nhận trong 12 giờ". A request-to-book holds nothing — docs/03 §2
 * is explicit that only instant book takes the dates off the market — and the
 * window is 24 hours, not 12. Rather than correct the number, the deadline is
 * now read off the booking itself, so it cannot drift from the rule again.
 */
function BookingOutcome({ result }) {
  const navigate = useNavigate();
  const summary = `${result.nights} ${t('đêm')} · ${result.guests} ${t('khách')} · ${money(result.total)}.`;

  if (result.status === 'PendingHostApproval') {
    return (
      <div className="book-alert">
        <b>{t('Đã gửi yêu cầu')} · {result.reference}</b>
        <span>
          {summary} {t('Chủ nhà có đến')} {dateTime(result.requestExpiresAt)} {t('để trả lời.')}{' '}
          {t('Yêu cầu chưa giữ ngày và bạn chưa bị trừ tiền.')}
          {result.paymentMethod === 'vietqr' &&
            ` ${t('Chủ nhà đồng ý rồi bạn mới chuyển khoản — mã sẽ được gửi qua email.')}`}
        </span>
        <button className="text-btn" style={{ marginTop: 8 }} onClick={() => navigate('/trips')}>
          {t('Xem chuyến đi')}
        </button>
      </div>
    );
  }

  // docs/07 §2.3 — held, unpaid, waiting on a transfer the guest still has to make.
  if (result.status === 'PendingPayment') {
    return (
      <div className="book-alert">
        <b>{t('Chờ bạn chuyển khoản')} · {result.reference}</b>
        <span>{summary} {t('Chỗ được giữ tới')} {dateTime(result.holdExpiresAt)}.</span>
        <button className="btn btn-primary btn-sm" style={{ marginTop: 10 }}
                onClick={() => navigate(`/chuyen-khoan/${result.reference}`)}>
          {t('Mở mã chuyển khoản')}
        </button>
      </div>
    );
  }

  return (
    <div className="book-alert">
      <b>{t('Đã đặt xong')} · {result.reference}</b>
      <span>{summary} {t('Chi tiết nằm trong mục Chuyến đi.')}</span>
      <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginTop: 8, flexWrap: 'wrap' }}>
        <button className="text-btn" onClick={() => navigate('/trips')}>
          {t('Xem chuyến đi')}
        </button>
        {/* docs/02 D3 — the confirmation is the moment somebody puts the dates
            in their own calendar, not a week later on the trip page. */}
        <a className="text-btn" href={`/api/bookings/${result.id}/calendar.ics`}>
          {t('Thêm vào lịch của tôi')}
        </a>
      </div>
    </div>
  );
}

function MobileBar({ nights, card }) {
  const state = useStore();
  const q = state.quote;

  // docs/07 §2.5 — no sign-in gate. The checkout asks a signed-out guest for a
  // name, an email and a phone instead, and books on those.
  const checkout = () => {
    set({ checkoutStep: 0, bookingError: null, rulesMissing: false, overlay: 'checkout' });
  };

  return (
    <div className="mobile-bar">
      <div style={{ minWidth: 0 }}>
        <div className="amount">{money(q ? q.total : card.pricePerNight)}</div>
        <div className="meta">{nights} {t('đêm')} · {totalGuests()} {t('khách')}</div>
      </div>
      <button className="btn btn-primary" onClick={checkout}>{t('Đặt chỗ ngay')}</button>
    </div>
  );
}
