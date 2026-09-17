import { useEffect, useState } from 'react';
import { useSlideshow } from '../lib/useSlideshow.js';
import { t } from '../lib/i18n.js';

/**
 * docs/01 MR — the photo block on an experience/service detail page, built the
 * way the Homes page (Detail.jsx <Gallery>) already does it, because that is what
 * airbnb.com/experiences shows too: a five-tile mosaic that opens a full-screen
 * viewer, not a slideshow. The two product types now read alike.
 *
 * Self-contained on purpose — it carries its own open state and photo index
 * rather than the listing overlay plumbing (state.detail / photoIndex), so it
 * drops onto any page that just hands it an images array.
 */
export function PhotoMosaic({ images, alt = '' }) {
  const pics = images?.length ? images : [''];
  // null = closed · a number = that photo full screen · 'grid' = the contact sheet
  const [view, setView] = useState(null);

  const tiles = pics.slice(0, 5);
  while (tiles.length < 5) tiles.push(tiles[tiles.length - 1] ?? '');

  return (
    <>
      <div className="gallery" id="section-photos"
           style={{ gridTemplateColumns: '2fr 1fr 1fr', gridTemplateRows: 'repeat(2,clamp(110px,16vw,215px))' }}>
        {tiles.map((src, i) => (
          <figure className="gallery-tile" key={i} onClick={() => setView(Math.min(i, pics.length - 1))}
                  style={{ margin: 0, ...(i === 0 ? { gridRow: 'span 2' } : {}) }}>
            <img src={src} alt={`${alt} — ảnh ${i + 1}`} loading={i === 0 ? 'eager' : 'lazy'} decoding="async" />
          </figure>
        ))}
        <button className="gallery-all" onClick={() => setView('grid')}>⊞ {t('Hiện tất cả ảnh')}</button>
      </div>

      {view === 'grid' && (
        <ContactSheet images={pics} alt={alt} onPick={setView} onClose={() => setView(null)} />
      )}

      {typeof view === 'number' && (
        <Viewer images={pics} alt={alt} index={view} onIndex={setView}
                onGrid={() => setView('grid')} onClose={() => setView(null)} />
      )}
    </>
  );
}

/**
 * The contact sheet — every photo at once. Its own overlay rather than the
 * shared Modal, because Modal's close is wired to the global overlay state and
 * this gallery keeps its open state locally.
 */
function ContactSheet({ images, alt, onPick, onClose }) {
  useEffect(() => {
    const onKey = e => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = '';
    };
  }, [onClose]);

  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal wide" role="dialog" aria-modal="true" aria-label={`${alt} — ${images.length} ${t('ảnh')}`}>
        <div className="modal-head">
          <button className="modal-close" onClick={onClose} aria-label={t('Đóng')}>✕</button>
          <h2>{alt} — {images.length} ảnh</h2>
          <span style={{ width: 32 }} />
        </div>
        <div className="modal-body">
          <div className="lightbox-grid">
            {images.map((src, i) => (
              <figure key={i}>
                <button className="lightbox-open" onClick={() => onPick(i)} aria-label={`${t('Phóng to ảnh')} ${i + 1}`}>
                  <img src={src} alt={`${alt} — ảnh ${i + 1}`} loading="lazy" decoding="async" />
                </button>
                <figcaption>{t('Ảnh')} {i + 1}</figcaption>
              </figure>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * The full-screen viewer, mirroring Detail.jsx's PhotoLightbox — black stage,
 * slide-and-zoom between photos, arrow keys and Escape, an "N / total" count and
 * a way back to the grid. No share/favourite: those belong to a listing, not to
 * every product with photos.
 */
function Viewer({ images, alt, index, onIndex, onGrid, onClose }) {
  const total = images.length;
  const slides = useSlideshow(index, onIndex, total);
  const { idx } = slides;

  useEffect(() => {
    const onKey = e => {
      if (e.key === 'ArrowLeft') slides.step(-1);
      else if (e.key === 'ArrowRight') slides.step(1);
      else if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  });

  useEffect(() => {
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = ''; };
  }, []);

  return (
    <div className="viewer" role="dialog" aria-modal="true" aria-label={`${alt} — ${t('ảnh')}`}>
      <header className="viewer-bar">
        <button className="viewer-btn" onClick={onClose}>✕ <span>{t('Đóng')}</span></button>
        <span className="viewer-count">{idx + 1} / {total}</span>
        <div className="viewer-actions" />
      </header>

      <div className="viewer-stage">
        {total > 1 && (
          <button className="viewer-nav prev" onClick={() => slides.step(-1)} aria-label={t('Ảnh trước')}>‹</button>
        )}

        {images.map((src, i) =>
          slides.isMounted(i) || Math.abs(i - idx) === 1 || Math.abs(i - idx) === total - 1
            ? <img key={i} src={src} alt={`${alt} — ảnh ${i + 1}`}
                   className={slides.frameClass(i)} decoding="async" />
            : <img key={i} alt="" aria-hidden="true" className="is-deferred" />
        )}

        {total > 1 && (
          <button className="viewer-nav next" onClick={() => slides.step(1)} aria-label={t('Ảnh tiếp theo')}>›</button>
        )}
      </div>

      <footer className="viewer-foot">
        <p className="viewer-caption">{t('Ảnh')} {idx + 1}</p>
        <button className="viewer-grid-link" onClick={onGrid}>⊞ Xem tất cả {total} ảnh</button>
      </footer>
    </div>
  );
}
