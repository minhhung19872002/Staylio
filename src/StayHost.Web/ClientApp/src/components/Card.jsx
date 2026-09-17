import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useSlideshow } from '../lib/useSlideshow.js';
import { useStore } from '../lib/useStore.js';
import { state as store, toggleFavorite } from '../lib/store.js';
import { money, shortDate } from '../lib/format.js';
import { t } from '../lib/i18n.js';
import { TranslatedText } from './TranslatedText.jsx';
import { stayTotal, originalStayTotal, allInPerNight } from '../lib/pricing.js';
import { setHoveredListing } from './Maps.jsx';

/**
 * `variant`:
 *   'search' — the denser /s/ layout: "<loại> tại <thành phố>" heading, the
 *              listing name underneath, bed/bath line and the all-in total.
 *   'rail'   — the two-line landing-page carousel card.
 *   default  — the browse grid card.
 */
export function Card({ card, variant, lazy = false }) {
  const state = useStore();
  const navigate = useNavigate();
  const [index, setIndex] = useState(0);

  const images = card.images?.length ? card.images : [''];
  const badge = card.isGuestFavorite ? t('KHÁCH YÊU THÍCH') : card.isSuperhost ? t('SIÊU CHỦ NHÀ') : null;

  const slides = useSlideshow(index, setIndex, images.length);
  const { idx, frameClass } = slides;

  const open = () => navigate(`/rooms/${card.slug}`);

  // The card is a link, so an arrow must not carry the click through to it.
  const own = (event, run) => {
    event.preventDefault();
    event.stopPropagation();
    run();
  };

  return (
    <article
      className={`card ${variant === 'search' ? 'card-search' : ''}`}
      data-listing={card.id}
      onMouseEnter={() => setHoveredListing(card.id, true)}
      onMouseLeave={() => setHoveredListing(card.id, false)}
    >
      <div className="card-media" onClick={open} role="link" tabIndex={-1}>
        {images.map((src, i) =>
          // Only the visible frame, its neighbours and the one on its way out
          // carry a real src, so a grid of cards costs a handful of images
          // rather than hundreds.
          slides.isMounted(i) || Math.abs(i - idx) === 1 || Math.abs(i - idx) === images.length - 1
            ? <img key={i} src={src} alt={`${card.title} — ảnh ${i + 1}`}
                   className={frameClass(i)}
                   loading={i === 0 && !lazy ? 'eager' : 'lazy'} decoding="async" />
            : <img key={i} alt="" aria-hidden="true" className="is-deferred" />
        )}

        {badge && <span className="card-badge">{badge}</span>}

        <button className={`card-fav ${card.isFavorite ? 'is-on' : ''}`}
                onClick={e => { e.preventDefault(); e.stopPropagation(); toggleFavorite(card.id); }}
                aria-label={`${card.isFavorite ? t('Bỏ lưu') : t('Lưu')} ${card.title}`}
                aria-pressed={!!card.isFavorite}>♥</button>

        {images.length > 1 && <>
          <button className="carousel-nav prev" onClick={e => own(e, () => slides.step(-1))}
                  aria-label={t('Ảnh trước')}>‹</button>
          <button className="carousel-nav next" onClick={e => own(e, () => slides.step(1))}
                  aria-label={t('Ảnh tiếp theo')}>›</button>
          <div className="carousel-dots">
            {images.map((_, i) => (
              <button key={i} className={`bullet ${i === idx ? 'is-on' : ''}`}
                      onClick={e => own(e, () => slides.goTo(i))} aria-label={`${t('Xem ảnh')} ${i + 1}`} />
            ))}
          </div>
        </>}
      </div>

      {/* A real <a href>, not a div that navigates. A crawler follows links and
          nothing else: while this was a click handler, none of the listings had
          a single inbound link anywhere on the site, so Google had no way to
          reach one even after finding the home page. The title inside it is also
          the anchor text, which is among the strongest signals a listing page
          gets for the words in its own name.

          preventDefault keeps the single-page navigation — the address is there
          for the crawler and for open-in-new-tab, not to trigger a full reload.
          The media block above stays a click handler because it contains the
          favourite button and the carousel arrows, and interactive controls
          inside a link is both invalid HTML and a keyboard trap. */}
      <a className="card-body" href={`/rooms/${card.slug}`}
         onClick={e => { if (e.metaKey || e.ctrlKey || e.shiftKey || e.button === 1) return; e.preventDefault(); open(); }}>
        {variant === 'search' ? <SearchBody card={card} />
          : variant === 'rail' ? <RailBody card={card} />
            : <BrowseBody card={card} showTotal={state.showTotalPrice} />}
      </a>
    </article>
  );
}

function RailBody({ card }) {
  const { nights, total } = stayTotal(card);
  return <>
    <h3 className="card-title">{t(card.typeLabel)} {t('tại')} {card.city}</h3>
    <div className="card-sub card-inline">
      <b>{money(total)}</b> {t('cho')} {nights} {t('đêm')}
      {card.reviewCount ? ` · ★ ${card.rating.toFixed(2)}` : ` · ${t('Mới')}`}
    </div>
  </>;
}

function BrowseBody({ card, showTotal }) {
  const { nights, total } = stayTotal(card);
  return <>
    <div className="card-row">
      <h3 className="card-title"><TranslatedText as="span" text={card.title} notice={false} /></h3>
      <div className="card-rating">{card.reviewCount ? `★ ${card.rating.toFixed(2)}` : t('Mới')}</div>
    </div>
    <div className="card-sub">{card.city} · {card.bedrooms} {t('phòng ngủ')}</div>
    <div className="card-sub">{t(card.typeLabel)} · {t(card.roomTypeLabel)}</div>
    <div className="card-price">
      {/* docs/01 TM-20 — the same stay, priced the way the guest asked to see it. */}
      {showTotal
        ? <><b>{money(allInPerNight(card))}</b> <span>/ {t('đêm')} · {money(total)} {t('tổng')} {nights} {t('đêm')}</span></>
        : <><b>{money(card.pricePerNight)}</b> <span>/ {t('đêm')}</span></>}
    </div>
  </>;
}

function SearchBody({ card }) {
  const { nights, total } = stayTotal(card);
  const original = originalStayTotal(card);

  return <>
    <div className="card-row">
      <h3 className="card-title">{t(card.typeLabel)} {t('tại')} {card.city}</h3>
      <div className="card-rating">
        {card.reviewCount ? `★ ${card.rating.toFixed(2)} (${card.reviewCount})` : `★ ${t('Mới')}`}
      </div>
    </div>
    <div className="card-sub card-name"><TranslatedText as="span" text={card.title} notice={false} /></div>
    <div className="card-sub">{card.bedrooms} {t('phòng ngủ')} · {card.beds} {t('giường')} · {card.bathrooms} {t('phòng tắm')}</div>
    <div className="card-price">
      {original && <><s>{money(original)}</s> </>}
      <b>{money(total)}</b> <span>{t('cho')} {nights} {t('đêm')}</span>
    </div>
    <MatchedDates card={card} />
    {/* docs/03 §4 — this line used to read "Đã gồm phí · Huỷ miễn phí" on every
        result, non-refundable places included. The fee half is always true; the
        cancellation half is only true for two of the six tiers. */}
    <div className="card-perks">
      {card.freeCancellation ? t('Đã gồm phí · Huỷ miễn phí') : t('Đã gồm phí và thuế')}
    </div>
  </>;
}

/**
 * docs/01 TM-06 — a flexible search puts each card on the dates that place has
 * free, so the card has to say which ones rather than let the guest assume.
 */
function MatchedDates({ card }) {
  const shifted = card.stayCheckIn && card.stayCheckIn !== store.checkIn;
  if (!shifted) return null;

  return (
    <div className="card-dates">{shortDate(card.stayCheckIn)} – {shortDate(card.stayCheckOut)}</div>
  );
}

export function CardSkeleton() {
  return (
    <div className="card" aria-hidden="true">
      <div className="card-media skeleton" />
      <div className="card-body">
        <div className="sk-line skeleton" style={{ width: '80%' }} />
        <div className="sk-line skeleton" style={{ width: '55%' }} />
        <div className="sk-line skeleton" style={{ width: '40%' }} />
      </div>
    </div>
  );
}
