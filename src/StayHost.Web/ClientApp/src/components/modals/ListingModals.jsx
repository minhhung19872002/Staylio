import { useStore } from '../../lib/useStore.js';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  set, holdDates, payHeld, releaseHold, openSplit, openOverlay, closeOverlay,
  shareListing, toggleFavorite, applyCoupon, toast, openReport
} from '../../lib/store.js';
import { money, longDate, parseIso, isoOf } from '../../lib/format.js';
import { AmenityIcon } from '../Icon.jsx';
import { HostReply, StarDistribution } from '../../pages/Detail.jsx';
import { api } from '../../lib/api.js';
import { useSlideshow } from '../../lib/useSlideshow.js';
import { Modal } from './Modal.jsx';
import { PaymentMethods } from '../PaymentMethods.jsx';
import { FALLBACK_METHODS } from '../../lib/payments.js';
import { t } from '../../lib/i18n.js';

const PHOTO_CAPTIONS = ['Ảnh chính', 'Phòng khách', 'Phòng ngủ', 'Không gian ngoài trời', 'Phòng tắm'];

export function PhotosModal() {
  const state = useStore();
  const c = state.detail?.card;
  if (!c) return null;

  const index = state.photoIndex;

  // A focused index turns the grid into a single-photo viewer with arrows.
  if (index != null) return <PhotoLightbox card={c} index={index} />;

  return (
    <Modal title={`${c.title} — ${c.images.length} ${t('ảnh')}`} size="wide">
      <div className="lightbox-grid">
        {c.images.map((src, i) => (
          <figure key={i}>
            <button className="lightbox-open" onClick={() => set({ photoIndex: i })} aria-label={`${t('Phóng to ảnh')} ${i + 1}`}>
              <img src={src} alt={`${c.title} — ${t('ảnh')} ${i + 1}`} loading="lazy" decoding="async" />
            </button>
            <figcaption>{PHOTO_CAPTIONS[i] ? t(PHOTO_CAPTIONS[i]) : `${t('Ảnh')} ${i + 1}`}</figcaption>
          </figure>
        ))}
      </div>
    </Modal>
  );
}

/**
 * The photo viewer: the whole screen, black, with the photo and nothing else
 * competing for attention. Moving between photos uses the slide-and-zoom of
 * codepen.io/daniesy/pen/JoWOpR — the one arriving grows from half size while
 * the one being replaced slides out the side you came from.
 *
 * This one does not use Modal: a white card with a header around a photograph
 * is exactly what a viewer is meant to get out of the way of.
 */
function PhotoLightbox({ card, index }) {
  const total = card.images.length;
  const slides = useSlideshow(index, i => set({ photoIndex: i }), total);
  const { idx } = slides;

  // Arrow keys are how anybody looks through photos full screen, and Escape is
  // how they leave. Re-bound every render so the handler sees the current index.
  useEffect(() => {
    const onKey = e => {
      if (e.key === 'ArrowLeft') slides.step(-1);
      else if (e.key === 'ArrowRight') slides.step(1);
      else if (e.key === 'Escape') closeOverlay();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  });

  // The page behind must not scroll while the viewer has the screen.
  useEffect(() => {
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = ''; };
  }, []);

  return (
    <div className="viewer" role="dialog" aria-modal="true" aria-label={`${card.title} — ${t('ảnh')}`}>
      <header className="viewer-bar">
        <button className="viewer-btn" onClick={closeOverlay}>✕ <span>{t('Đóng')}</span></button>
        <span className="viewer-count">{idx + 1} / {total}</span>
        <div className="viewer-actions">
          <button className="viewer-btn" onClick={() => shareListing(card)} aria-label={t('Chia sẻ')}>⤴</button>
          <button className={`viewer-btn ${card.isFavorite ? 'is-on' : ''}`}
                  onClick={() => toggleFavorite(card.id)}
                  aria-label={card.isFavorite ? t('Bỏ lưu') : t('Lưu chỗ nghỉ')}
                  aria-pressed={!!card.isFavorite}>♥</button>
        </div>
      </header>

      <div className="viewer-stage">
        {total > 1 && (
          <button className="viewer-nav prev" onClick={() => slides.step(-1)} aria-label={t('Ảnh trước')}>‹</button>
        )}

        {card.images.map((src, i) =>
          // Only the photo on screen, its two neighbours and the one still
          // sliding away are worth downloading.
          slides.isMounted(i) || Math.abs(i - idx) === 1 || Math.abs(i - idx) === total - 1
            ? <img key={i} src={src} alt={`${card.title} — ${t('ảnh')} ${i + 1}`}
                   className={slides.frameClass(i)} decoding="async" />
            : <img key={i} alt="" aria-hidden="true" className="is-deferred" />
        )}

        {total > 1 && (
          <button className="viewer-nav next" onClick={() => slides.step(1)} aria-label={t('Ảnh tiếp theo')}>›</button>
        )}
      </div>

      <footer className="viewer-foot">
        <p className="viewer-caption">{PHOTO_CAPTIONS[idx] ? t(PHOTO_CAPTIONS[idx]) : `${t('Ảnh')} ${idx + 1}`}</p>
        <button className="viewer-grid-link" onClick={() => set({ photoIndex: null })}>
          ⊞ {t('Xem tất cả')} {total} {t('ảnh')}
        </button>
      </footer>
    </div>
  );
}

/**
 * docs/01 TĐ-04 — grouped, with the amenities this place does not have kept in
 * the list and struck through rather than quietly omitted.
 */
export function AmenitiesModal() {
  const state = useStore();
  const d = state.detail;
  if (!d) return null;

  const groups = d.allAmenities.reduce((acc, a) => {
    (acc[a.group] ||= []).push(a);
    return acc;
  }, {});

  return (
    <Modal title={t('Nơi này có những gì')}>
      {Object.entries(groups).map(([group, items]) => (
        <section className="modal-section" key={group}>
          {/* The group name is server data like the amenity labels beside it, so
              it goes through the dictionary too — without this the headings stay
              Vietnamese above a list that is not. */}
          <h3>{t(group)}</h3>
          <div style={{ display: 'grid', gap: 2, marginTop: 12 }}>
            {[...items].sort((a, b) => Number(b.available) - Number(a.available)).map(a => (
              <div className={`amenity ${a.available ? '' : 'is-missing'}`} key={a.key}
                   style={{ padding: '14px 0', borderBottom: '1px solid #f0f0f0' }}>
                <span className="ic"><AmenityIcon name={a.key} /></span><span>{t(a.label)}</span>
              </div>
            ))}
          </div>
        </section>
      ))}
    </Modal>
  );
}

const REVIEW_SORTS = [['recent', 'Mới nhất'], ['high', 'Điểm cao nhất'], ['low', 'Điểm thấp nhất']];

/* docs/01 TĐ-11 — the codes the server can put on a review, named for reading. */
const LANGUAGE_NAME = {
  vi: 'Tiếng Việt', en: 'Tiếng Anh', ja: 'Tiếng Nhật', ko: 'Tiếng Hàn',
  zh: 'Tiếng Trung', fr: 'Tiếng Pháp', de: 'Tiếng Đức', es: 'Tiếng Tây Ban Nha'
};

/**
 * docs/01 TĐ-21 — what a hundred reviews keep saying, so they can be taken in
 * without being read one by one. The score on each row is the average given by
 * the people who raised that subject, not the overall score: that is the whole
 * point of the row.
 */
export function ReviewThemes({ themes }) {
  if (!themes?.length) return null;

  return (
    <div className="review-themes">
      <h4>{t('Khách hay nhắc tới')}</h4>
      <div className="pill-row">
        {themes.map(x => (
          <span className="pill" key={x.key}>
            {t(x.label)} · ★ {x.rating.toFixed(1)} ·
            {/* One whole-sentence key with a slot: gluing "{n}" to a translated
                "lượt nhắc" puts the number on the wrong side in Japanese, and a
                template literal is invisible to scripts/i18n_audit.py.
                The separator before it is a middot, not just a margin: six
                pixels between "4.7" and "96" reads as "4.796". */}
            <b style={{ marginLeft: 5, fontWeight: 500, opacity: 0.7 }}>
              {t('{} lượt nhắc').replace('{}', x.mentions)}
            </b>
          </span>
        ))}
      </div>
    </div>
  );
}
// NOTE: labels above are wrapped with t() at the render site.

export function ReviewsModal() {
  const state = useStore();
  const d = state.detail;
  if (!d) return null;

  const term = state.reviewQuery.trim().toLowerCase();
  const lang = state.reviewLanguage;

  // docs/01 TĐ-11 — only the languages actually present, so the picker never
  // offers a row that filters everything away.
  const languages = [...new Set(d.reviews.map(r => r.language).filter(Boolean))];

  const list = d.reviews
    .filter(r => !term || r.text.toLowerCase().includes(term) || r.authorName.toLowerCase().includes(term))
    .filter(r => lang === 'all' || r.language === lang)
    .sort((a, b) =>
      state.reviewSort === 'high' ? b.rating - a.rating
        : state.reviewSort === 'low' ? a.rating - b.rating
          : 0);

  return (
    <Modal title={`★ ${d.card.rating.toFixed(2)} · ${d.reviews.length} ${t('đánh giá')}`} size="wide">
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', marginBottom: 20 }}>
        <input type="search" className="field" style={{ flex: '1 1 220px' }} placeholder={t('Tìm trong đánh giá')}
               value={state.reviewQuery} onChange={e => set({ reviewQuery: e.target.value })} />
        <select className="field" style={{ flex: '0 0 200px', width: 'auto' }}
                value={state.reviewSort} onChange={e => set({ reviewSort: e.target.value })}>
          {REVIEW_SORTS.map(([v, l]) => <option key={v} value={v}>{t(l)}</option>)}
        </select>
        {/* docs/01 TĐ-11 — read the ones written in a language you read. */}
        {languages.length > 1 && (
          <select className="field" style={{ flex: '0 0 180px', width: 'auto' }}
                  aria-label={t('Lọc theo ngôn ngữ')}
                  value={lang} onChange={e => set({ reviewLanguage: e.target.value })}>
            <option value="all">{t('Mọi ngôn ngữ')}</option>
            {languages.map(code => (
              <option key={code} value={code}>{t(LANGUAGE_NAME[code] ?? code)}</option>
            ))}
          </select>
        )}
      </div>
      <StarDistribution counts={d.ratingBreakdown.starCounts} total={d.reviews.length} />
      <ReviewThemes themes={d.reviewThemes} />

      {!list.length && <p style={{ fontSize: 14, color: 'var(--ink-muted)' }}>{t('Không có đánh giá nào khớp từ khoá.')}</p>}
      <div className="review-grid">
        {list.map(r => (
          <article className="review" key={r.id ?? `${r.authorName}-${r.when}`}>
            <div className="review-head">
              <span className="avatar" aria-hidden="true">{r.authorInitials}</span>
              <div style={{ minWidth: 0 }}>
                <div className="review-name">{r.authorName}</div>
                <div className="review-when">{r.authorLocation ? `${r.authorLocation} · ` : ''}{r.when}</div>
              </div>
              <span style={{ marginLeft: 'auto', fontSize: 13, fontWeight: 700 }}>★ {r.rating.toFixed(1)}</span>
            </div>
            <p>{r.text}</p>
            <HostReply review={r} />
            {/* docs/01 ĐG-10 — the same flag the detail page carries. It was on
                the four reviews shown there and not on the hundred behind
                "xem tất cả", which is where somebody actually reads them. */}
            {r.id && (
              <button className="text-btn" style={{ marginTop: 8, fontSize: 12.5 }}
                      onClick={() => { closeOverlay(); openReport('review', r.id, `Đánh giá của ${r.authorName}`); }}>
                ⚑ {t('Báo cáo đánh giá')}
              </button>
            )}
          </article>
        ))}
      </div>
    </Modal>
  );
}

/* ---------------------------------------------------------------- checkout */

const CHECKOUT_STEPS = ['Chuyến đi', 'Thanh toán', 'Xác nhận'];
// NOTE: step labels above are wrapped with t() at the render site.

export function CheckoutModal() {
  const state = useStore();
  const navigate = useNavigate();
  const d = state.detail;
  const q = state.quote;
  const [busy, setBusy] = useState(false);
  /* docs/07 §7 — one key per attempt the guest makes, reused by every retry. */
  const attemptKey = useRef(null);

  /*
   * A refusal at the foot of a scrolled modal is a refusal nobody reads. Every
   * "I pressed the button and nothing happened" reported today was one of
   * these: the dates going while the guest thought, a hold running out, the
   * house rules untouched. The server said so each time, at the bottom of a box
   * the guest was looking at the top of.
   */
  const errorBox = useRef(null);

  useEffect(() => {
    if (state.bookingError) errorBox.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, [state.bookingError]);

  // docs/01 ĐP-02 — moving past the trip step takes the dates off the market
  // for 15 minutes; walking away puts them straight back.
  useEffect(() => () => { releaseHold(); }, []);

  if (!d || !q) return null;

  const step = state.checkoutStep;
  const blocked = q.guestsExceeded || q.belowMinNights;
  const isRequest = !d.card.instantBook;

  const next = async () => {
    // docs/01 ĐP-10 — the server refuses without this tick, and refusing from
    // the server put the reason at the bottom of a scrolled modal. Answered
    // here instead, next to the box that has to be ticked.
    if (step === 0 && state.detail?.houseRules?.length && !state.agreedToRules) {
      set({ rulesMissing: true });
      document.getElementById('house-rules-agree')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      return;
    }

    if (step === 0 && !isRequest && !state.held) {
      setBusy(true);
      const held = await holdDates({
        guestName: state.checkoutName || state.user?.fullName || null,
        guestEmail: state.checkoutEmail || state.user?.email || null,
        guestPhone: state.checkoutPhone || null,
        guestNote: state.checkoutNote || null
      });
      setBusy(false);
      if (!held) return;
    }
    set({ checkoutStep: step + 1 });
  };

  const confirm = async () => {
    const card = document.getElementById('card-number')?.value?.replace(/\D/g, '') ?? '';
    attemptKey.current ??= `pay-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
    const payment = {
      paymentMethod: state.payMethod,
      // docs/07 §4 — a saved card is picked, not retyped.
      cardLast4: state.payCardLast4 ?? (card.length >= 4 ? card.slice(-4) : null),
      // docs/01 ĐP-06 — a deposit now, the rest taken 14 days before check-in.
      payDeposit: state.payDeposit,
      depositAmount: state.payDeposit ? Math.ceil(q.total / 2) : null,
      // docs/07 §7 — the same key on a retry, so a lost reply cannot become a
      // second charge. New for each fresh attempt the guest makes themselves.
      idempotencyKey: attemptKey.current,
      // docs/07 §4 — keep this card at the gateway, or pay with one already
      // kept there. With a live gateway the first is also how Staylio learns
      // the card's last four digits at all (§14.2).
      saveCard: !!state.paySaveCard,
      savedCardId: state.payCardId ?? null
    };

    setBusy(true);

    // docs/01 ĐP-07 — a split does not charge anyone here: it holds the dates
    // for a day and sends everybody, the organiser included, their own link.
    if (state.splitBill && !isRequest) {
      const emails = (state.splitEmails ?? '').split(/[,\s]+/).map(e => e.trim()).filter(e => e.includes('@'));
      const opened = await openSplit(emails);
      setBusy(false);
      if (opened) closeOverlay();
      return;
    }

    // A request to book has nothing to pay for yet — it goes to the host first.
    const result = isRequest
      ? await holdDates({
          guestName: state.checkoutName || state.user?.fullName || null,
          guestEmail: state.checkoutEmail || state.user?.email || null,
          guestPhone: state.checkoutPhone || null,
          guestNote: state.checkoutNote || null,
          ...payment
        })
      : await payHeld(payment);
    setBusy(false);

    if (!result) {
      // docs/07 §7 — the key exists so a lost reply cannot become a second
      // charge, not so a guest who was refused is stuck with the refusal. A
      // fresh press is a fresh attempt and gets a fresh key; the in-flight
      // request was protected by `busy`.
      attemptKey.current = null;
      return;
    }

    // docs/07 §13 — the money for this method moves on a licensed gateway's own
    // page, so the last thing this checkout does is send the guest there.
    // Nothing has been charged and nothing is in the ledger yet; the booking is
    // still holding its dates and the confirmation arrives from the gateway.
    if (result.gatewayRedirectUrl) {
      set({ held: null });
      window.location.assign(result.gatewayRedirectUrl);
      return;
    }

    set({ bookingResult: result, held: null });
    closeOverlay();

    // Where the guest looks next. Closing a full-screen modal and writing the
    // outcome into the booking panel was not enough: that panel is sticky and
    // its foot sits below the fold, so a request-to-book ended with the form
    // vanishing and nothing visibly taking its place.

    // docs/07 §2.3 — a booking still pending payment after /pay is one paid by
    // transfer: the dates are held and the money has not moved yet.
    if (result.status === 'PendingPayment') {
      navigate(`/chuyen-khoan/${result.reference}`);
      return;
    }

    // docs/03 §3 — a request goes to the host first. Nothing is held and
    // nothing is charged, so the honest destination is the trip itself.
    if (result.status === 'PendingHostApproval') {
      toast(`${t('Đã gửi yêu cầu — mã')} ${result.reference}`);
      navigate('/trips');
    }
  };

  return (
    <Modal title={t('Đặt chỗ')} foot={<>
      <div style={{ minWidth: 0 }}>
        <div style={{ fontSize: 16, fontWeight: 800 }}>
          {money(state.payDeposit ? Math.ceil(q.total / 2) : q.total)}
        </div>
        <div style={{ fontSize: 12, color: 'var(--ink-muted)' }}>
          {state.payDeposit ? `${t('trả trước · tổng')} ${money(q.total)}` : `${q.nights} ${t('đêm · đã gồm thuế')}`}
        </div>
      </div>
      <div style={{ display: 'flex', gap: 10 }}>
        {step > 0 && <button className="btn btn-outline btn-sm" onClick={() => set({ checkoutStep: step - 1 })}>{t('Quay lại')}</button>}
        {step < 2
          ? <button className="btn btn-primary btn-sm" disabled={blocked || busy} onClick={next}>
              {busy ? t('Đang giữ chỗ…') : t('Tiếp tục')}
            </button>
          : <button className="btn btn-primary btn-sm" disabled={blocked || busy} onClick={confirm}>
              {busy ? t('Đang xử lý…') : isRequest ? t('Gửi yêu cầu đặt') : t('Xác nhận và thanh toán')}
            </button>}
      </div>
    </>}>
      <HoldCountdown held={state.held} />
      <div className="stepper-bar">
        {CHECKOUT_STEPS.map((label, i) => (
          <div key={label} className={`step-dot ${i === step ? 'is-active' : ''} ${i < step ? 'is-done' : ''}`}>
            <span className="n">{i < step ? '✓' : i + 1}</span>{t(label)}
          </div>
        ))}
      </div>

      <div style={{ display: 'flex', gap: 14, alignItems: 'center', padding: '18px 0', borderBottom: '1px solid var(--divider)' }}>
        <img src={d.card.images[0]} alt="" style={{ width: 96, height: 72, objectFit: 'cover', borderRadius: 12 }} />
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 700 }}>{d.card.title}</div>
          <div style={{ fontSize: 13.5, color: 'var(--ink-muted)' }}>
            {d.card.city} · ★ {d.card.rating.toFixed(2)} ({d.card.reviewCount})
          </div>
        </div>
      </div>

      {step === 0 && <StepTrip q={q} />}
      {step === 1 && <StepPayment q={q} />}
      {step === 2 && <StepReview q={q} />}

      {blocked && (
        <div className="book-alert is-error">
          <b>{t('Chưa đặt được')}</b>
          <span>{q.guestsExceeded
            ? `${t('Chỗ nghỉ này nhận tối đa')} ${q.maxGuests} ${t('khách')}.`
            : `${t('Chỗ nghỉ này yêu cầu tối thiểu')} ${q.minNights} ${t('đêm')}.`}</span>
        </div>
      )}

      {state.bookingError && (
        <div className="book-alert is-error" ref={errorBox}>
          <b>{t('Không đặt được')}</b><span>{state.bookingError}</span>
        </div>
      )}
    </Modal>
  );
}

function StepTrip({ q }) {
  const state = useStore();

  return <>
    <section className="modal-section">
      <h3>{t('Chuyến đi của bạn')}</h3>
      <div style={{ display: 'grid', gap: 12, marginTop: 14, fontSize: 14.5 }}>
        <div className="book-line">
          <span><b>{t('Ngày')}</b><br />{longDate(state.checkIn)} – {longDate(state.checkOut)}</span>
          <button className="text-btn" onClick={() => openOverlay('dates')}>{t('Chỉnh sửa')}</button>
        </div>
        <div className="book-line">
          <span><b>{t('Khách')}</b><br />{q.guests} {t('khách')}</span>
          <button className="text-btn" onClick={() => openOverlay('guests')}>{t('Chỉnh sửa')}</button>
        </div>
      </div>
    </section>

    {/* docs/01 ĐP-10 — the house rules, agreed to before booking. Shown only when
        the listing has any; the server requires the tick in that case. */}
    {!!state.detail?.houseRules?.length && (
      <section className="modal-section">
        <h3>{t('Nội quy nhà')}</h3>
        <ul style={{ margin: '10px 0 0', paddingLeft: 18, fontSize: 14, lineHeight: 1.7 }}>
          {state.detail.houseRules.map((r, i) => <li key={i}>{r}</li>)}
        </ul>
        <label className="check-row" id="house-rules-agree"
               style={{
                 marginTop: 12,
                 // The one thing standing between the guest and the next step,
                 // marked as such. The refusal used to be a sentence at the very
                 // bottom of the modal, past the contact form and the
                 // cancellation policy — so pressing "Tiếp tục" read as nothing
                 // happening at all.
                 ...(state.rulesMissing
                   ? { outline: '2px solid var(--danger, #c1121f)', outlineOffset: 6, borderRadius: 8 }
                   : {})
               }}>
          <input type="checkbox" checked={state.agreedToRules}
                 onChange={e => set({ agreedToRules: e.target.checked, rulesMissing: false })} />
          <span>{t('Tôi đã đọc và đồng ý với nội quy nhà')}</span>
        </label>

        {state.rulesMissing && (
          <p style={{ margin: '10px 0 0', fontSize: 13.5, color: 'var(--danger, #c1121f)' }}>
            {t('Cần đồng ý nội quy nhà trước khi tiếp tục.')}
          </p>
        )}
      </section>
    )}

    <section className="modal-section">
      <h3>{t('Thông tin liên hệ')}</h3>

      {/* docs/07 §2.5 — somebody who is not signed in is not asked to sign in.
          The promise that makes people willing is the second half: the booking
          stays findable afterwards. */}
      {!state.user && (
        <p className="notice" style={{ marginTop: 12 }}>
          {t('Bạn có thể đặt mà không cần tài khoản. Chúng tôi gửi mã đơn qua email; dùng mã đơn và email đó để xem hoặc huỷ đặt chỗ bất cứ lúc nào.')}
          {' '}
          <button className="link-btn"
                  onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>
            {t('Hoặc đăng nhập')}
          </button>
        </p>
      )}

      <div style={{ marginTop: 14 }}>
        <label className="form-field"><span className="cap">{t('Họ tên')}</span>
          <input type="text" placeholder={t('Nguyễn Văn A')}
                 value={state.checkoutName || state.user?.fullName || ''}
                 onChange={e => set({ checkoutName: e.target.value })} /></label>
        <label className="form-field"><span className="cap">{t('Email')}</span>
          <input type="email" placeholder={t('ban@email.com')}
                 value={state.checkoutEmail || state.user?.email || ''}
                 onChange={e => set({ checkoutEmail: e.target.value })} /></label>
        {/* An account already carries a phone; a stranger has to leave one, and
            a host expecting them at the door needs it more than usually. */}
        {!state.user && (
          <label className="form-field"><span className="cap">{t('Số điện thoại')}</span>
            <input type="tel" placeholder="0901234567"
                   value={state.checkoutPhone}
                   onChange={e => set({ checkoutPhone: e.target.value })} /></label>
        )}
        <label className="form-field">
          <span className="cap">{t('Lời nhắn cho chủ nhà')} <span style={{ fontWeight: 400 }}>{t('(không bắt buộc)')}</span></span>
          <textarea rows={3} placeholder={t('Chúng mình đến khoảng 20:00…')}
                    value={state.checkoutNote} onChange={e => set({ checkoutNote: e.target.value })}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>
      </div>
    </section>

    <section className="modal-section">
      <h3>{t('Chính sách huỷ')}</h3>
      <p style={{ fontSize: 14, lineHeight: 1.6, color: 'var(--ink-body)', margin: '10px 0 0' }}>
        <b>{t(q.cancellationTier)}</b> — {t(q.cancellationSummary)}
      </p>
    </section>
  </>;
}

/**
 * docs/01 ĐP-09 — a promo code field. The code is priced by the server, so the
 * discount and any refusal both come back on the quote; this only sends what the
 * guest typed and shows what came back.
 */
function CouponField({ q }) {
  const state = useStore();
  const [code, setCode] = useState(state.couponCode ?? '');
  const [busy, setBusy] = useState(false);

  const apply = async () => {
    setBusy(true);
    await applyCoupon(code);
    setBusy(false);
  };

  const clear = async () => {
    setCode('');
    await applyCoupon('');
  };

  return (
    <div style={{ marginTop: 18 }}>
      <label className="cap" htmlFor="coupon-code">{t('Mã giảm giá')}</label>
      <div style={{ display: 'flex', gap: 8, marginTop: 6 }}>
        <input id="coupon-code" value={code} disabled={busy}
               onChange={e => setCode(e.target.value.toUpperCase())}
               placeholder={t('VD: CHAOMUNG10')}
               style={{ flex: 1, padding: '11px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        {q.couponApplied
          ? <button type="button" className="btn btn-outline btn-sm" onClick={clear}>{t('Bỏ mã')}</button>
          : <button type="button" className="btn btn-dark btn-sm" disabled={busy || !code.trim()} onClick={apply}>{t('Áp dụng')}</button>}
      </div>
      {q.couponApplied && (
        <p className="notice notice-ok" style={{ marginTop: 8 }}>
          {t('Đã áp mã, giảm')} {money(q.couponDiscount)}.
        </p>
      )}
      {q.couponError && (
        <p className="notice notice-warn" style={{ marginTop: 8 }}>{q.couponError}</p>
      )}
    </div>
  );
}

/** Spend the guest's balance on this booking, against the room charge only. */
function CreditChoice({ q }) {
  const state = useStore();
  const [balance, setBalance] = useState(null);

  useEffect(() => {
    if (!state.user) return;
    api.wallet().then(w => setBalance(w.balance)).catch(() => setBalance(0));
  }, [state.user]);

  if (!balance) return null;

  const room = q.roomBeforeDiscount - q.roomDiscount;
  const usable = Math.min(balance, room);

  return (
    <>
      <button type="button" className={`opt ${state.useCredit ? 'is-on' : ''}`} style={{ marginTop: 18 }}
              onClick={() => set({ useCredit: !state.useCredit })}>
        <b>{t('Dùng số dư')} {money(usable)}</b>
        <span>{t('Bạn đang có')} {money(balance)}. {t('Số dư chỉ trừ vào tiền phòng.')}</span>
      </button>

      {/* docs/07 §3 — nothing to charge today is not the same as nothing to
          charge later; the method on file is how docs/06 §3.3 collects. */}
      {state.useCredit && usable >= q.total && (
        <p className="notice notice-warn">
          {t('Số dư của bạn đủ trả toàn bộ đơn này. Vẫn cần một phương thức dự phòng cho các phát sinh về sau như đổi lịch hoặc bồi thường — bạn sẽ không bị trừ tiền bây giờ.')}
        </p>
      )}
    </>
  );
}

/**
 * docs/01 ĐP-06 — half now and the rest automatically, but only when there is
 * still runway for a second charge. Inside two weeks of check-in the option is
 * not offered at all, which is what the rule says.
 */
function DepositChoice({ q }) {
  const state = useStore();
  const days = Math.round((parseIso(state.checkIn) - new Date()) / 86400000);
  if (days <= 14) return null;

  const half = Math.ceil(q.total / 2);
  const dueOn = parseIso(state.checkIn);
  dueOn.setDate(dueOn.getDate() - 14);

  return (
    <div style={{ display: 'grid', gap: 10, marginTop: 18 }}>
      <button type="button" className={`opt ${!state.payDeposit ? 'is-on' : ''}`}
              onClick={() => set({ payDeposit: false })}>
        <b>{t('Trả toàn bộ')} {money(q.total)}</b><span>{t('Xong luôn, không phải nhớ gì thêm')}</span>
      </button>
      <button type="button" className={`opt ${state.payDeposit ? 'is-on' : ''}`}
              onClick={() => set({ payDeposit: true })}>
        <b>{t('Trả trước')} {money(half)}</b>
        <span>{t('Phần còn lại')} {money(q.total - half)} {t('tự động thu ngày')} {longDate(isoOf(dueOn))}</span>
      </button>
    </div>
  );
}

/**
 * docs/01 ĐP-07 — invite up to fifteen other people to pay their share. The
 * booking is only confirmed when the last one does, so this replaces paying
 * rather than sitting alongside it.
 */
function SplitChoice({ q }) {
  const state = useStore();
  const [emails, setEmails] = useState('');

  const people = emails.split(/[,\s]+/).map(e => e.trim()).filter(e => e.includes('@'));
  const each = people.length ? Math.floor(q.total / (people.length + 1)) : 0;

  return (
    <div style={{ marginTop: 18 }}>
      <button type="button" className={`opt ${state.splitBill ? 'is-on' : ''}`}
              onClick={() => set({ splitBill: !state.splitBill })}>
        <b>{t('Chia hoá đơn với người khác')}</b>
        <span>{t('Mỗi người nhận một liên kết và trả phần của mình')}</span>
      </button>

      {state.splitBill && <div style={{ marginTop: 12 }}>
        <label className="form-field">
          <span className="cap">{t('Email những người cùng trả (cách nhau bằng dấu phẩy)')}</span>
          <input value={emails} placeholder={t('an@vidu.vn, binh@vidu.vn')}
                 onChange={e => { setEmails(e.target.value); set({ splitEmails: e.target.value }); }} />
        </label>
        <p style={{ fontSize: 13, color: 'var(--ink-muted)', margin: 0, lineHeight: 1.6 }}>
          {people.length
            ? `${people.length + 1} ${t('người · mỗi người khoảng')} ${money(each)} ${t('· bạn trả phần lẻ')}`
            : `${t('Tối đa')} ${16} ${t('người kể cả bạn.')}`}
          <br />{t('Đơn chỉ được xác nhận khi tất cả đã trả, trong vòng 24 giờ.')}
        </p>
      </div>}
    </div>
  );
}

function StepPayment({ q }) {
  const state = useStore();
  const method = state.payMethod;
  const listingId = state.detail?.card?.id ?? null;
  const [refused, setRefused] = useState(null);

  // Only for the §2.4 footnote below; the list itself belongs to <PaymentMethods>.
  useEffect(() => {
    api.paymentCatalogue(listingId).then(setRefused).catch(() => { /* no footnote, then */ });
  }, [listingId]);

  return (
    <section className="modal-section">
      <h3>{t('Chọn cách thanh toán')}</h3>
      <div style={{ marginTop: 14 }}>
        {/* The same picker the service and experience checkouts use, ids and
            all: `card-number` is what confirm() reads back. The listing goes in
            because docs/07 §2.5 belongs to a place, not to the platform. */}
        <PaymentMethods idPrefix="card" listingId={listingId} />
      </div>

      <CouponField q={q} />
      <CreditChoice q={q} />
      <DepositChoice q={q} />
      <SplitChoice q={q} />

      {/* docs/07 §2.5 — the one method where Staylio takes nothing, so the
          guest is told exactly that before they confirm. */}
      {method === 'property' && (
        <p className="notice" style={{ marginTop: 18 }}>
          {t('Bạn trả {} trực tiếp cho chủ nhà khi nhận phòng. Staylio không thu trước và không giữ tiền của đơn này.')
            .replace('{}', money(q.total))}
        </p>
      )}

      {method === 'momo' && (
        <p style={{ marginTop: 18, fontSize: 14, color: 'var(--ink-body)', lineHeight: 1.6 }}>
          {t('Sau khi xác nhận, bạn sẽ được chuyển sang ứng dụng MoMo để hoàn tất thanh toán')} {money(q.total)}.
        </p>
      )}

      {method === 'zalopay' && (
        <p style={{ marginTop: 18, fontSize: 14, color: 'var(--ink-body)', lineHeight: 1.6 }}>
          {t('Sau khi xác nhận, bạn sẽ được chuyển sang ứng dụng ZaloPay để hoàn tất thanh toán')} {money(q.total)}.
        </p>
      )}

      {/* docs/07 §2.4 — the ways Staylio does not take, with the one reason. */}
      {!!refused?.notAccepted?.length && (
        <p style={{ marginTop: 18, fontSize: 12.5, color: 'var(--ink-muted)', lineHeight: 1.6 }}>
          {t('Staylio không nhận')} {refused.notAccepted.join(', ').toLowerCase()}. {refused.refusalReason}
        </p>
      )}
    </section>
  );
}

function StepReview({ q }) {
  const state = useStore();

  // The catalogue the payment step loaded, falling back to the §2.1 group if
  // that call never came back. This line used to read FALLBACK_METHODS with no
  // import behind it, which threw on render — and since this is the step the
  // "Tiếp tục" button leads to, checkout ended in a modal that vanished.
  const method = (state.paymentMethods ?? FALLBACK_METHODS).find(m => m.key === state.payMethod);

  return <>
    <section className="modal-section">
      <h3>{t('Kiểm tra lần cuối')}</h3>
      <div style={{ display: 'grid', gap: 12, marginTop: 14, fontSize: 14.5 }}>
        <div className="book-line"><span>{t('Ngày')}</span><span>{longDate(state.checkIn)} – {longDate(state.checkOut)}</span></div>
        <div className="book-line"><span>{t('Khách')}</span><span>{q.guests} {t('khách')}</span></div>
        <div className="book-line"><span>{t('Thanh toán bằng')}</span><span>{method ? t(method.label) : t('Thẻ')}</span></div>
      </div>
    </section>

    <section className="modal-section">
      <h3>{t('Chi tiết giá')}</h3>
      <PriceLines q={q} />
    </section>

    <section className="modal-section">
      <h3>{t('Chính sách huỷ')}</h3>
      <p style={{ fontSize: 14, lineHeight: 1.6, color: 'var(--ink-body)', margin: '10px 0 0' }}>
        <b>{t(q.cancellationTier)}</b> — {t(q.cancellationSummary)}
      </p>
    </section>
  </>;
}

/**
 * docs/01 ĐP-02 — the guest can see exactly how long the dates are theirs.
 * Ticking every second is cheap and makes the deadline feel real.
 */
function HoldCountdown({ held }) {
  const [left, setLeft] = useState(() => remaining(held));

  useEffect(() => {
    if (!held?.holdExpiresAt) return undefined;
    const id = setInterval(() => setLeft(remaining(held)), 1000);
    return () => clearInterval(id);
  }, [held]);

  if (!held?.holdExpiresAt || left <= 0) return null;

  const mm = String(Math.floor(left / 60)).padStart(2, '0');
  const ss = String(left % 60).padStart(2, '0');

  return (
    <div className="book-alert" style={{ marginBottom: 4 }}>
      <b>{t('Đang giữ chỗ cho bạn')} · {mm}:{ss}</b>
      <span>{t('Hết giờ mà chưa thanh toán xong thì ngày sẽ được mở lại cho khách khác.')}</span>
    </div>
  );
}

const remaining = held =>
  held?.holdExpiresAt ? Math.max(0, Math.round((new Date(held.holdExpiresAt) - Date.now()) / 1000)) : 0;

/**
 * Renders whatever line items the quote carries. The server owns the running
 * order and the labels (docs/00 §6.8), so adding a rule there shows up here
 * without a front-end change.
 */
export function PriceLines({ q, className = '' }) {
  const state = useStore();
  const lines = q.lines ?? legacyLines(q);

  return (
    <div className={`book-lines ${className}`} style={{ marginTop: 14 }}>
      {lines.map((l, i) => (
        <div className="book-line" key={i} style={l.amount < 0 ? { color: 'var(--brand-dark)' } : undefined}>
          <u>{t(l.label)}</u><span>{l.amount < 0 ? `−${money(-l.amount)}` : money(l.amount)}</span>
        </div>
      ))}
      <div className="book-rule" />
      <div className="book-total"><span>{t('Tổng')} ({state.currency.code})</span><span>{money(q.total)}</span></div>
      {/* docs/07 §6 — a converted figure is a courtesy, and the section says so
          out loud: show the original beside it, and say the bank's own rate
          decides what actually leaves the card. Grep found neither the original
          price nor any such note anywhere in the client before this. */}
      {state.currency.code !== 'VND' && (
        <p className="section-sub" style={{ margin: '8px 0 0', fontSize: 12.5, lineHeight: 1.5 }}>
          {t('Giá gốc')}: {new Intl.NumberFormat('vi-VN').format(Math.round(q.total))}₫. {' '}
          {t('Bạn được thu bằng tiền Việt; số tiền trên thẻ theo tỉ giá của ngân hàng bạn.')}
        </p>
      )}
    </div>
  );
}

/** Fallback for quotes from before the server started sending `lines`. */
function legacyLines(q) {
  const out = [
    { label: `${money(q.pricePerNight)} × ${q.nights} ${t('đêm')}`, amount: q.subtotal + (q.lengthDiscount ?? 0) }
  ];
  if (q.lengthDiscount > 0) out.push({ label: `${t('Giảm giá ở dài ngày')} (${q.lengthDiscountPercent}%)`, amount: -q.lengthDiscount });
  out.push({ label: t('Phí dọn dẹp'), amount: q.cleaningFee });
  out.push({ label: t('Phí dịch vụ Staylio'), amount: q.serviceFee });
  if (q.tax) out.push({ label: t('Thuế'), amount: q.tax });
  return out;
}
