import { useState } from 'react';
import { t } from '../lib/i18n.js';
import { useSlideshow } from '../lib/useSlideshow.js';

/**
 * The listing card's photo slideshow, in a form the experience and service cards
 * can reuse: swipe/arrow through several images with the same slide-and-zoom, plus
 * dots. No favourite heart or badge — those belong to stays, not to activities.
 * The parent stays a link/button, so arrows and dots stop the click bubbling.
 */
export function CardCarousel({ images, alt = '' }) {
  const pics = images?.length ? images : [''];
  const [index, setIndex] = useState(0);
  const slides = useSlideshow(index, setIndex, pics.length);
  const { idx, frameClass } = slides;

  const own = (e, run) => { e.preventDefault(); e.stopPropagation(); run(); };

  return (
    <div className="card-media">
      {pics.map((src, i) =>
        slides.isMounted(i) || Math.abs(i - idx) === 1 || Math.abs(i - idx) === pics.length - 1
          ? <img key={i} src={src} alt={`${alt} — ảnh ${i + 1}`}
                 className={frameClass(i)} loading="lazy" decoding="async" />
          : <img key={i} alt="" aria-hidden="true" className="is-deferred" />
      )}

      {pics.length > 1 && <>
        <button className="carousel-nav prev" onClick={e => own(e, () => slides.step(-1))}
                aria-label={t('Ảnh trước')}>‹</button>
        <button className="carousel-nav next" onClick={e => own(e, () => slides.step(1))}
                aria-label={t('Ảnh tiếp theo')}>›</button>
        <div className="carousel-dots">
          {pics.map((_, i) => (
            <button key={i} className={`bullet ${i === idx ? 'is-on' : ''}`}
                    onClick={e => own(e, () => slides.goTo(i))} aria-label={`${t('Xem ảnh')} ${i + 1}`} />
          ))}
        </div>
      </>}
    </div>
  );
}
