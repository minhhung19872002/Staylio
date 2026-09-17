import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import { useStore } from '../../lib/useStore.js';
import { set, saveListing, removeListing, closeOverlay, toast } from '../../lib/store.js';
import { api } from '../../lib/api.js';
import { money } from '../../lib/format.js';
import { AmenityIcon } from '../Icon.jsx';
import { Modal } from './Modal.jsx';
import { t } from '../../lib/i18n.js';

/**
 * docs/01 CN-01 — publishing a listing is a sequence of small steps, saved as
 * you go, so leaving halfway and coming back loses nothing. The draft lives in
 * localStorage until the listing has an id, then on the server.
 */
const STEPS = [
  ['type', 'Loại hình'],
  ['place', 'Vị trí'],
  ['capacity', 'Sức chứa'],
  ['beds', 'Giường'],
  ['amenities', 'Tiện nghi'],
  ['photos', 'Ảnh'],
  ['words', 'Tiêu đề & mô tả'],
  ['price', 'Giá'],
  ['rules', 'Nhận đơn & huỷ'],
  ['checkin', 'Nhận phòng'],
  ['legal', 'Pháp lý'],
  ['review', 'Xem trước']
];

const DRAFT_KEY = 'sh_listing_draft';

const BLANK_PRICING = {
  weeklyDiscountPercent: 10, monthlyDiscountPercent: 20,
  earlyBirdDays: 0, earlyBirdPercent: 0, lastMinuteDays: 0, lastMinutePercent: 0,
  weekendSurchargeRate: 0.15, freeGuestThreshold: 2, extraGuestFee: 0,
  petsAllowed: false, maxPets: 2, petFee: 0, petFeePerNight: false,
  loyaltyDiscountPercent: 0
};

// docs/01 CĐ-03 — the arrival guide. Blank rather than absent so the editor is
// never handed an undefined it has to guard on every field.
const BLANK_CHECKIN = {
  checkInFrom: '14:00', checkInTo: '22:00', checkOutBefore: '12:00',
  method: 'Host', addressLine: '', directions: '',
  wifiName: '', wifiPassword: '', applianceNotes: '', doorCode: '', hostPhone: ''
};

/** docs/01 CĐ-03 — how a guest gets in; the last three need a code (CĐ-04). */
const CHECKIN_METHODS = [
  ['Host', 'Chủ nhà đón và giao chìa khoá'],
  ['Reception', 'Nhận chìa khoá ở quầy lễ tân'],
  ['Keypad', 'Tự nhận phòng bằng mã cửa'],
  ['Lockbox', 'Tự nhận phòng bằng hộp khoá'],
  ['SmartLock', 'Tự nhận phòng bằng khoá thông minh']
];

const NEEDS_CODE = ['Keypad', 'Lockbox', 'SmartLock'];

const BLANK_LEGAL = {
  licenseNumber: '', hasSecurityCameras: false, securityCameraNote: '',
  hasWeaponsOnProperty: false, hasDangerousAnimals: false
};

const BLANK = {
  id: 0, title: '', city: '', typeKey: 'house', roomTypeKey: 'entire',
  bedrooms: 1, beds: 1, bathrooms: 1, maxGuests: 2,
  pricePerNight: 800000, cleaningFee: 200000, minNights: 1,
  instantBook: true, instantBookRequiresVerified: false, instantBookRequiresGoodReviews: false,
  requireGuestPhoto: false, requireVerifiedToBook: false, acceptsPayAtProperty: false, hotelStars: 0,
  isPublished: false, cancellationTier: 'Moderate',
  description: '', highlight: '', images: [], imageCaptions: [], amenityKeys: [],
  latitude: null, longitude: null,
  pricing: BLANK_PRICING, bedLayout: [], legal: BLANK_LEGAL, checkIn: BLANK_CHECKIN,
  wizardStep: 0, isComplete: false
};

const TIERS = [
  ['Flexible', 'Linh hoạt', 'Hoàn 100% nếu huỷ trước 24 giờ; sau đó mất đêm đầu.'],
  ['Moderate', 'Vừa phải', 'Hoàn 100% nếu huỷ trước 5 ngày, sau đó 50%.'],
  ['Strict', 'Chặt', 'Hoàn 100% trước 30 ngày, 50% trước 7 ngày, sau đó không hoàn.'],
  ['SuperStrict', 'Rất chặt', 'Hoàn 50% nếu huỷ trước 7 ngày, sau đó không hoàn.'],
  ['NonRefundable', 'Không hoàn', 'Không hoàn tiền phòng; đổi lại khách được giảm 10%.'],
  ['LongTermStrict', 'Dài hạn – chặt', 'Cho kỳ ở từ 28 đêm: sau 30 ngày, khách trả 30 đêm đầu.']
];

const ROOM_TYPES = [
  ['entire', 'Nguyên căn', 'Khách dùng trọn chỗ nghỉ'],
  ['private', 'Phòng riêng', 'Phòng riêng, chia sẻ khu vực chung'],
  ['shared', 'Phòng chung', 'Ngủ chung phòng với khách khác']
];

const BED_TYPES = ['Giường đôi', 'Giường đơn', 'Giường tầng', 'Sofa giường', 'Nệm dưới sàn'];

export function ListingWizard() {
  const state = useStore();
  const meta = state.meta;
  const editing = state.editingListing;

  const [form, setForm] = useState(() => {
    if (editing) {
      return {
        ...BLANK, ...editing,
        pricing: { ...BLANK_PRICING, ...(editing.pricing ?? {}) },
        legal: { ...BLANK_LEGAL, ...(editing.legal ?? {}) },
        checkIn: { ...BLANK_CHECKIN, ...(editing.checkIn ?? {}) },
        bedLayout: editing.bedLayout ?? [],
        imageCaptions: editing.imageCaptions ?? []
      };
    }
    // CN-01 — an unfinished new listing is restored from the device.
    try {
      const saved = JSON.parse(localStorage.getItem(DRAFT_KEY) ?? 'null');
      if (saved) return { ...BLANK, ...saved };
    } catch { /* a corrupt draft is not worth a crash */ }
    return BLANK;
  });

  const [step, setStep] = useState(() => Math.min(form.wizardStep ?? 0, STEPS.length - 1));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  // Autosave. New listings go to the device; saved ones keep their server copy
  // up to date only when the host moves between steps, not on every keystroke.
  useEffect(() => {
    if (form.id) return;
    try { localStorage.setItem(DRAFT_KEY, JSON.stringify({ ...form, wizardStep: step })); }
    catch { /* full storage must not block the wizard */ }
  }, [form, step]);

  const field = (key, value) => setForm(f => ({ ...f, [key]: value }));
  const num = (key, value) => field(key, Number(value) || 0);
  const rule = (key, value) => setForm(f => ({ ...f, pricing: { ...f.pricing, [key]: value } }));
  const legal = (key, value) => setForm(f => ({ ...f, legal: { ...f.legal, [key]: value } }));
  const arrival = (key, value) => setForm(f => ({ ...f, checkIn: { ...f.checkIn, [key]: value } }));

  const blocked = blockedReason(form, step);

  const persist = async ({ publish }) => {
    setSaving(true);
    setError(null);

    const body = {
      ...form,
      isPublished: publish ? true : form.isPublished,
      isComplete: publish,
      wizardStep: publish ? 0 : step
    };

    const saved = await saveListing(form.id || null, body);
    setSaving(false);

    if (!saved) {
      setError('Chưa lưu được. Kiểm tra lại các trường bắt buộc.');
      return null;
    }

    setForm(f => ({ ...f, id: saved.id }));
    if (!form.id) { try { localStorage.removeItem(DRAFT_KEY); } catch { /* nothing to clear */ } }
    return saved;
  };

  const next = async () => {
    if (blocked) { setError(blocked); return; }
    setError(null);

    // Saving on the way out of the first step turns the draft into a real row,
    // which is what makes "quay lại sau" work across devices.
    if (step === 0 && !form.id && form.title.trim() && form.city.trim()) await persist({ publish: false });
    setStep(s => Math.min(STEPS.length - 1, s + 1));
  };

  const publish = async () => {
    if (form.images.length < 5) {
      setError('Cần tối thiểu 5 ảnh trước khi hiển thị công khai.');
      setStep(STEPS.indexOf(STEPS.find(s => s[0] === 'photos')));
      return;
    }
    const saved = await persist({ publish: true });
    if (saved) closeOverlay();
  };

  const saveDraft = async () => {
    const saved = await persist({ publish: false });
    if (saved) { toast('Đã lưu nháp. Bạn quay lại lúc nào cũng được.'); closeOverlay(); }
  };

  const [key] = STEPS[step];

  return (
    <Modal title={form.id ? t('Chỉnh sửa chỗ nghỉ') : t('Đăng chỗ nghỉ mới')} size="wide" foot={<>
      <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
        <button className="text-btn" onClick={saveDraft} disabled={saving}>{t('Lưu nháp & thoát')}</button>
        {form.id > 0 && (
          <button className="text-btn" onClick={async () => {
            if (!confirm('Xoá hẳn chỗ nghỉ này?')) return;
            closeOverlay();
            await removeListing(form.id);
          }}>{t('Xoá')}</button>
        )}
      </div>
      <div style={{ display: 'flex', gap: 10 }}>
        {step > 0 && <button className="btn btn-outline btn-sm" onClick={() => setStep(s => s - 1)}>{t('Quay lại')}</button>}
        {step < STEPS.length - 1
          ? <button className="btn btn-primary btn-sm" onClick={next} disabled={saving}>{t('Tiếp tục')}</button>
          : <button className="btn btn-primary btn-sm" onClick={publish} disabled={saving}>
              {saving ? t('Đang đăng…') : t('Đăng chỗ nghỉ')}
            </button>}
      </div>
    </>}>
      <Stepper step={step} onPick={setStep} />

      {error && <div className="form-error">{error}</div>}

      {key === 'type' && <StepType form={form} field={field} meta={meta} />}
      {key === 'place' && <StepPlace form={form} field={field} meta={meta} />}
      {key === 'capacity' && <StepCapacity form={form} num={num} />}
      {key === 'beds' && <StepBeds form={form} setForm={setForm} />}
      {key === 'amenities' && <StepAmenities form={form} setForm={setForm} meta={meta} />}
      {key === 'photos' && <StepPhotos form={form} setForm={setForm} />}
      {key === 'words' && <StepWords form={form} field={field} />}
      {key === 'price' && <StepPrice form={form} num={num} rule={rule} meta={meta} />}
      {key === 'rules' && <StepRules form={form} field={field} num={num} />}
      {key === 'checkin' && <StepCheckIn form={form} arrival={arrival} />}
      {key === 'legal' && <StepLegal form={form} legal={legal} />}
      {key === 'review' && <StepReview form={form} onJump={setStep} />}
    </Modal>
  );
}

/** What still has to be filled in before this step is done. */
function blockedReason(form, step) {
  const [key] = STEPS[step];

  if (key === 'type' && !form.city.trim()) return 'Vui lòng nhập thành phố.';
  if (key === 'words' && form.title.trim().length < 8) return 'Tiêu đề cần ít nhất 8 ký tự.';
  if (key === 'words' && form.description.trim().length < 40) return 'Mô tả cần ít nhất 40 ký tự.';
  if (key === 'photos' && form.images.length < 5) return 'Cần tối thiểu 5 ảnh để đăng công khai.';
  if (key === 'price' && form.pricePerNight < 50000) return 'Giá mỗi đêm tối thiểu 50.000₫.';
  // docs/01 CĐ-04 — a keypad with no code is a door the guest cannot open.
  if (key === 'checkin' && NEEDS_CODE.includes(form.checkIn.method) && !form.checkIn.doorCode?.trim())
    return 'Cách vào nhà này cần một mã cửa để khách tự vào được.';
  return null;
}

/**
 * The twelve steps, as a rail you can also jump around in.
 *
 * It scrolls sideways because twelve pills do not fit a modal, and its scrollbar
 * is hidden like every other rail here. That left two problems worth fixing
 * together: the pill at the edge was sliced in half with nothing to say more
 * existed, and — the real one — pressing "Tiếp tục" past the eighth step made a
 * step active that was entirely off screen, so the host lost their place in the
 * middle of the form.
 */
function Stepper({ step, onPick }) {
  const rail = useRef(null);
  const [edges, setEdges] = useState({ left: false, right: false });

  // Which way there is more to see. Read after every scroll and on resize, so a
  // rail that stops overflowing stops claiming to.
  const readEdges = () => {
    const el = rail.current;
    if (!el) return;
    const slack = el.scrollWidth - el.clientWidth;
    setEdges({
      left: el.scrollLeft > 1,
      right: slack > 1 && el.scrollLeft < slack - 1
    });
  };

  // Bring the active step into view. Done by hand rather than with
  // scrollIntoView, which walks up and scrolls the modal body vertically too.
  useEffect(() => {
    const el = rail.current;
    const active = el?.children[step];
    if (!el || !active) return;

    const target = active.offsetLeft - (el.clientWidth - active.offsetWidth) / 2;
    el.scrollTo({ left: Math.max(0, target), behavior: 'smooth' });

    // After the smooth scroll lands, not before it starts.
    const timer = setTimeout(readEdges, 320);
    return () => clearTimeout(timer);
  }, [step]);

  useEffect(() => {
    readEdges();
    window.addEventListener('resize', readEdges);
    return () => window.removeEventListener('resize', readEdges);
  }, []);

  return (
    <div ref={rail} onScroll={readEdges}
         className={`wizard-steps ${edges.left ? 'more-left' : ''} ${edges.right ? 'more-right' : ''}`}>
      {STEPS.map(([key, label], i) => (
        <button key={key} className={`wizard-step ${i === step ? 'is-active' : ''} ${i < step ? 'is-done' : ''}`}
                onClick={() => onPick(i)}>
          <span className="n">{i < step ? '✓' : i + 1}</span>{t(label)}
        </button>
      ))}
    </div>
  );
}

/* ------------------------------------------------------------------- steps */

function StepType({ form, field, meta }) {
  return <>
    <section className="modal-section">
      <h3>{t('Chỗ nghỉ của bạn là loại gì?')}</h3>
      <div className="pill-row" style={{ marginTop: 14 }}>
        {(meta?.categories ?? []).filter(c => c.key !== 'all').map(c => (
          <button key={c.key} className={`pill ${form.typeKey === c.key ? 'is-on' : ''}`}
                  onClick={() => field('typeKey', c.key)}>{t(c.label)}</button>
        ))}
      </div>
    </section>

    {form.typeKey === 'hotel' && (
      <section className="modal-section">
        <h3>{t('Hạng sao khách sạn')}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          {[0, 1, 2, 3, 4, 5].map(n => (
            <button key={n} type="button" className={`pill ${(form.hotelStars ?? 0) === n ? 'is-on' : ''}`}
                    onClick={() => field('hotelStars', n)}>{n ? '★'.repeat(n) : t('Chưa xếp hạng')}</button>
          ))}
        </div>
      </section>
    )}

    <section className="modal-section">
      <h3>{t('Khách được dùng bao nhiêu?')}</h3>
      <div className="opt-grid">
        {ROOM_TYPES.map(([v, label, hint]) => (
          <button key={v} className={`opt ${form.roomTypeKey === v ? 'is-on' : ''}`}
                  onClick={() => field('roomTypeKey', v)}>
            <b>{t(label)}</b><span>{t(hint)}</span>
          </button>
        ))}
      </div>
    </section>

    <CityPicker form={form} field={field} meta={meta} />
  </>;
}

/**
 * The city is chosen, not typed.
 *
 * It used to be a free-text box with the known cities merely suggested, and the
 * catalogue groups by this string exactly — city rails, "chỗ nghỉ ở X" links,
 * the market comparison of docs/01 CN-10. A host who wrote "Thành phố Hồ Chí
 * Minh" where the catalogue said "TP. Hồ Chí Minh" created a second city holding
 * one listing, and their new place turned up in none of the places they looked.
 *
 * There is still a way to name a city the platform has never covered — refusing
 * that would mean a host in one simply cannot publish — but it is a deliberate
 * second step rather than the default.
 */
function CityPicker({ form, field, meta }) {
  const known = meta?.cities ?? [];
  const isKnown = known.includes(form.city);
  const [other, setOther] = useState(() => !!form.city && !isKnown);

  return (
    <section className="modal-section">
      <h3>{t('Ở thành phố nào?')}</h3>
      <span className="hint">
        {t('Chọn từ danh sách để chỗ nghỉ của bạn nằm chung khu vực với các tin khác — tự gõ một cách viết khác sẽ tách nó ra thành một khu vực riêng.')}
      </span>

      <label className="form-field">
        <span className="cap">{t('Thành phố *')}</span>
        <select value={other ? '__other' : form.city}
                onChange={e => {
                  if (e.target.value === '__other') { setOther(true); field('city', ''); return; }
                  setOther(false);
                  field('city', e.target.value);
                }}>
          <option value="" disabled>{t('— Chọn thành phố —')}</option>
          {known.map(c => <option key={c} value={c}>{c}</option>)}
          <option value="__other">{t('Thành phố khác…')}</option>
        </select>
      </label>

      {other && (
        <label className="form-field">
          <span className="cap">{t('Tên thành phố')}</span>
          <input value={form.city} onChange={e => field('city', e.target.value)}
                 placeholder={t('Ví dụ: Buôn Ma Thuột')} required />
        </label>
      )}
    </section>
  );
}

/** docs/01 CN-03 — the host drags the pin until it sits on the right building. */
function StepPlace({ form, field, meta }) {
  const hostRef = useRef(null);
  const mapRef = useRef(null);
  const markerRef = useRef(null);
  const fieldRef = useRef(field);
  fieldRef.current = field;

  const start = [form.latitude ?? 16.0, form.longitude ?? 107.5];

  useEffect(() => {
    const map = L.map(hostRef.current).setView(start, form.latitude ? 15 : 5);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap', maxZoom: 18
    }).addTo(map);

    // Leaflet's default marker loads marker-icon.png from a path the bundler
    // never emits, so it rendered as a broken image with the alt text "Marker"
    // showing through. Every other map in the app draws its pins with a divIcon;
    // this one now does too, and depends on no image file at all.
    const marker = L.marker(start, {
      draggable: true,
      icon: L.divIcon({
        className: '',
        html: '<span class="pin-marker" aria-hidden="true"></span>',
        iconSize: [28, 38],
        iconAnchor: [14, 38]
      })
    }).addTo(map);
    marker.on('dragend', () => {
      const { lat, lng } = marker.getLatLng();
      fieldRef.current('latitude', Number(lat.toFixed(6)));
      fieldRef.current('longitude', Number(lng.toFixed(6)));
    });
    map.on('click', e => {
      marker.setLatLng(e.latlng);
      fieldRef.current('latitude', Number(e.latlng.lat.toFixed(6)));
      fieldRef.current('longitude', Number(e.latlng.lng.toFixed(6)));
    });

    mapRef.current = map;
    markerRef.current = marker;

    const t = setTimeout(() => map.invalidateSize(), 60);
    return () => { clearTimeout(t); map.remove(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <section className="modal-section">
      <h3>{t('Ghim vị trí chính xác')}</h3>
      <span className="hint">
        {t('Kéo ghim hoặc bấm lên bản đồ. Khách chỉ thấy vị trí gần đúng trong bán kính ~300m cho tới khi đơn được xác nhận.')}
      </span>
      <div ref={hostRef} className="detail-map" style={{ marginTop: 14, height: 320 }} />
      <div className="field-grid" style={{ marginTop: 14 }}>
        <label className="form-field"><span className="cap">{t('Vĩ độ')}</span>
          <input value={form.latitude ?? ''} readOnly placeholder={t('Chưa ghim')} /></label>
        <label className="form-field"><span className="cap">{t('Kinh độ')}</span>
          <input value={form.longitude ?? ''} readOnly placeholder={t('Chưa ghim')} /></label>
      </div>
      {!form.latitude && (
        <p style={{ fontSize: 12.5, color: 'var(--ink-muted)', marginTop: 10 }}>
          {t('Chưa ghim thì Staylio dùng toạ độ trung tâm')} {form.city || t('thành phố')} {t('— kém chính xác hơn.')}
        </p>
      )}
      {/* meta is unused here but kept in the signature so every step takes the same props */}
      {void meta}
    </section>
  );
}

function StepCapacity({ form, num }) {
  const rows = [
    ['maxGuests', 'Số khách tối đa', 1, 30],
    ['bedrooms', 'Phòng ngủ', 0, 20],
    ['beds', 'Giường', 1, 40],
    ['bathrooms', 'Phòng tắm', 0, 20]
  ];

  return (
    <section className="modal-section">
      <h3>{t('Chỗ nghỉ đón được bao nhiêu khách?')}</h3>
      <span className="hint">{t('Em bé dưới 2 tuổi không tính vào sức chứa.')}</span>
      {rows.map(([key, label, min, max]) => (
        <div className="count-row" key={key}>
          <div className="tx"><b>{t(label)}</b></div>
          <div className="count-ctl">
            <button type="button" className="round-btn" disabled={form[key] <= min}
                    onClick={() => num(key, form[key] - 1)}>−</button>
            <span className="num">{form[key]}</span>
            <button type="button" className="round-btn" disabled={form[key] >= max}
                    onClick={() => num(key, form[key] + 1)}>+</button>
          </div>
        </div>
      ))}
    </section>
  );
}

/** docs/01 CN-05 — which beds are in which room. */
function StepBeds({ form, setForm }) {
  const rooms = form.bedLayout.length
    ? form.bedLayout
    : Array.from({ length: Math.max(1, form.bedrooms) }, (_, i) => ({
        name: `Phòng ngủ ${i + 1}`, beds: ['Giường đôi']
      }));

  const update = next => setForm(f => ({ ...f, bedLayout: next }));

  return (
    <section className="modal-section">
      <h3>{t('Bố trí giường theo từng phòng')}</h3>
      <span className="hint">{t('Khách nhìn phần này để biết nhóm mình ngủ thế nào.')}</span>

      <div style={{ display: 'grid', gap: 14, marginTop: 14 }}>
        {rooms.map((room, i) => (
          <div key={i} style={{ border: '1px solid var(--divider)', borderRadius: 12, padding: 14 }}>
            <label className="form-field" style={{ marginBottom: 10 }}>
              <span className="cap">{t('Tên phòng')}</span>
              {/* The default name is stored in Vietnamese on purpose — that is what
                  lets every guest see it in their own language — so it is translated
                  for display here rather than at the point it is generated. Typing
                  over it replaces it with whatever the host actually calls the room. */}
              <input value={t(room.name)}
                     onChange={e => update(rooms.map((r, x) => (x === i ? { ...r, name: e.target.value } : r)))} />
            </label>

            <div style={{ display: 'grid', gap: 8 }}>
              {room.beds.map((bed, b) => (
                <div className="cal-row" key={b}>
                  <select className="field" style={{ flex: 1 }} value={bed}
                          onChange={e => update(rooms.map((r, x) => x === i
                            ? { ...r, beds: r.beds.map((y, z) => (z === b ? e.target.value : y)) }
                            : r))}>
                    {BED_TYPES.map(bt => <option key={bt} value={bt}>{t(bt)}</option>)}
                  </select>
                  <button className="text-btn" onClick={() => update(rooms.map((r, x) => x === i
                    ? { ...r, beds: r.beds.filter((_, z) => z !== b) }
                    : r))}>{t('Bỏ')}</button>
                </div>
              ))}
            </div>

            <button className="btn btn-outline btn-sm" style={{ marginTop: 10 }}
                    onClick={() => update(rooms.map((r, x) => x === i
                      ? { ...r, beds: [...r.beds, 'Giường đơn'] }
                      : r))}>{t('+ Thêm giường')}</button>
          </div>
        ))}
      </div>

      <div style={{ display: 'flex', gap: 10, marginTop: 14 }}>
        <button className="btn btn-outline btn-sm"
                onClick={() => update([...rooms, { name: `Phòng ngủ ${rooms.length + 1}`, beds: ['Giường đôi'] }])}>
          {t('+ Thêm phòng')}
        </button>
        {rooms.length > 1 && (
          <button className="btn btn-outline btn-sm" onClick={() => update(rooms.slice(0, -1))}>{t('Bớt phòng')}</button>
        )}
      </div>
    </section>
  );
}

function StepAmenities({ form, setForm, meta }) {
  const groups = (meta?.amenities ?? []).reduce((acc, a) => {
    (acc[a.group] ||= []).push(a);
    return acc;
  }, {});

  const toggle = key => setForm(f => ({
    ...f,
    amenityKeys: f.amenityKeys.includes(key)
      ? f.amenityKeys.filter(k => k !== key)
      : [...f.amenityKeys, key]
  }));

  return <>
    {Object.entries(groups).map(([group, items]) => (
      <section className="modal-section" key={group}>
        <h3>{t(group)}</h3>
        <div className="pill-row" style={{ marginTop: 14 }}>
          {items.map(a => (
            <button key={a.key} className={`pill ${form.amenityKeys.includes(a.key) ? 'is-on' : ''}`}
                    onClick={() => toggle(a.key)}>
              <AmenityIcon name={a.key} size={16} /> {t(a.label)}
            </button>
          ))}
        </div>
      </section>
    ))}
  </>;
}

/** docs/01 CN-07 — five photos minimum, drag to reorder, a label on each. */
function StepPhotos({ form, setForm }) {
  const state = useStore();
  const [dragging, setDragging] = useState(null);
  // Whether a file is currently hovering the drop zone, so it can say so.
  const [over, setOver] = useState(false);

  const captionOf = i => form.imageCaptions[i] ?? (i === 0 ? t('Ảnh bìa') : `${t('Ảnh')} ${i + 1}`);

  const move = (from, to) => {
    if (from === to || from == null) return;
    setForm(f => {
      const images = [...f.images];
      const captions = f.images.map((_, i) => f.imageCaptions[i] ?? '');
      const [img] = images.splice(from, 1);
      const [cap] = captions.splice(from, 1);
      images.splice(to, 0, img);
      captions.splice(to, 0, cap);
      return { ...f, images, imageCaptions: captions };
    });
  };

  const upload = async files => {
    const list = Array.from(files ?? []);
    if (!list.length) return;

    set({ uploading: true });
    try {
      const body = new FormData();
      list.forEach(f => body.append('files', f));
      const res = await fetch('/api/uploads/images', { method: 'POST', body, credentials: 'same-origin' });
      const payload = await res.json().catch(() => null);
      if (!res.ok) throw new Error(payload?.message ?? 'Tải ảnh thất bại.');

      setForm(f => ({
        ...f,
        images: [...f.images, ...payload.urls],
        imageCaptions: [...f.imageCaptions, ...payload.urls.map(() => '')]
      }));
      toast(`Đã tải lên ${payload.urls.length} ảnh.`);
    } catch (err) {
      toast(err.message);
    } finally {
      set({ uploading: false });
    }
  };

  return (
    <section className="modal-section">
      <h3>{t('Ảnh chỗ nghỉ')}</h3>
      <span className="hint">
        {t('Tối thiểu 5 ảnh. Kéo để đổi thứ tự — ảnh đầu tiên là ảnh bìa. Đặt nhãn cho từng phòng để khách biết mình đang xem gì. Hiện có')} <b>{form.images.length}/5</b>.
      </span>

      {/*
        * docs/01 CN-07 — a box that looks like a drop zone has to accept a drop.
        * It only opened a file picker before, so dragging a photo onto it made
        * the browser navigate away to that photo and lose the half-filled form.
        */}
      <label className={`dropzone ${over ? 'is-over' : ''}`} style={{ marginTop: 14 }}
             onDragOver={e => { e.preventDefault(); setOver(true); }}
             onDragEnter={e => { e.preventDefault(); setOver(true); }}
             onDragLeave={() => setOver(false)}
             onDrop={e => {
               e.preventDefault();
               setOver(false);
               const files = [...(e.dataTransfer?.files ?? [])].filter(f => f.type.startsWith('image/'));
               if (files.length) upload(files);
               else if (e.dataTransfer?.files?.length) toast('Chỉ nhận tệp ảnh.');
             }}>
        <input type="file" accept="image/jpeg,image/png,image/webp,image/avif" multiple hidden
               onChange={e => { upload(e.target.files); e.target.value = ''; }} />
        <b>{state.uploading ? t('Đang tải ảnh lên…') : over ? t('Thả ảnh vào đây') : t('Kéo ảnh vào đây hoặc bấm để chọn')}</b>
        <span>{t('JPG, PNG, WebP hoặc AVIF · tối đa 8MB mỗi ảnh')}</span>
      </label>

      {!!form.images.length && (
        <div className="thumb-grid" style={{ marginTop: 16 }}>
          {form.images.map((url, i) => (
            <figure className={`thumb is-draggable ${dragging === i ? 'is-dragging' : ''}`} key={`${url}-${i}`}
                    draggable
                    onDragStart={() => setDragging(i)}
                    onDragEnd={() => setDragging(null)}
                    onDragOver={e => e.preventDefault()}
                    onDrop={() => { move(dragging, i); setDragging(null); }}>
              <img src={url} alt={captionOf(i)} loading="lazy" />
              <input className="thumb-caption" value={form.imageCaptions[i] ?? ''}
                     placeholder={i === 0 ? t('Ảnh bìa') : `${t('Ảnh')} ${i + 1}`}
                     onChange={e => setForm(f => {
                       const captions = f.images.map((_, x) => f.imageCaptions[x] ?? '');
                       captions[i] = e.target.value;
                       return { ...f, imageCaptions: captions };
                     })} />
              <button type="button" className="thumb-remove" aria-label={`${t('Xoá ảnh')} ${i + 1}`}
                      onClick={() => setForm(f => ({
                        ...f,
                        images: f.images.filter((_, x) => x !== i),
                        imageCaptions: f.images.map((_, x) => f.imageCaptions[x] ?? '').filter((_, x) => x !== i)
                      }))}>✕</button>
            </figure>
          ))}
        </div>
      )}
    </section>
  );
}

/**
 * docs/01 CN-08 — the blank box is what stops a host finishing a listing, so the
 * wizard offers titles and a first draft built from what it already knows. They
 * are suggestions to edit, never text that gets saved without being read.
 */
function StepWords({ form, field }) {
  const [ideas, setIdeas] = useState(null);
  const [busy, setBusy] = useState(false);

  const suggest = async () => {
    setBusy(true);
    try {
      setIdeas(await api.copySuggestions({
        typeKey: form.typeKey,
        roomTypeKey: form.roomTypeKey,
        city: form.city,
        bedrooms: form.bedrooms,
        maxGuests: form.maxGuests,
        amenityKeys: form.amenityKeys
      }));
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return <>
    <section className="modal-section">
      <h3>{t('Đặt tên cho chỗ nghỉ')}</h3>
      <span className="hint">{t('Ngắn, cụ thể, nói được điều đặc biệt. Tối đa 60 ký tự.')}</span>

      <button type="button" className="btn btn-sm" style={{ marginBottom: 12 }}
              onClick={suggest} disabled={busy || !form.city.trim()}>
        {busy ? t('Đang nghĩ…') : ideas ? t('Gợi ý khác') : t('✨ Gợi ý tiêu đề & mô tả')}
      </button>
      {!form.city.trim() && <p className="field-note">{t('Nhập thành phố ở bước Vị trí để nhận gợi ý.')}</p>}

      {!!ideas?.titles.length && (
        <div className="idea-list">
          {ideas.titles.map(title => (
            <button type="button" className="idea" key={title} onClick={() => field('title', title)}>
              <span>{title}</span><b>{t('Dùng')}</b>
            </button>
          ))}
        </div>
      )}
      <label className="form-field">
        <span className="cap">{t('Tiêu đề *')} <span style={{ fontWeight: 400 }}>({form.title.length}/60)</span></span>
        <input value={form.title} maxLength={60} onChange={e => field('title', e.target.value)}
               placeholder={t('Villa hồ bơi riêng gần biển Mỹ Khê')} required />
      </label>
    </section>

    <section className="modal-section">
      <h3>{t('Mô tả')}</h3>
      {ideas?.description && (
        <button type="button" className="idea" style={{ marginBottom: 10 }}
                onClick={() => field('description', ideas.description)}>
          <span>{ideas.description}</span><b>{t('Dùng')}</b>
        </button>
      )}
      <label className="form-field">
        <span className="cap">{t('Giới thiệu chỗ nghỉ *')} <span style={{ fontWeight: 400 }}>{t('(tối thiểu 40 ký tự)')}</span></span>
        <textarea rows={6} value={form.description} onChange={e => field('description', e.target.value)}
                  placeholder={t('Kể về không gian, vị trí và điều khiến chỗ nghỉ của bạn đặc biệt.')}
                  style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
      </label>
      <label className="form-field">
        <span className="cap">{t('Điểm nổi bật')}</span>
        <input value={form.highlight ?? ''} onChange={e => field('highlight', e.target.value)}
               placeholder={t('Hồ bơi riêng nhìn ra vườn dừa')} />
      </label>
    </section>
  </>;
}

function StepPrice({ form, num, rule, meta }) {
  const guestFee = meta?.fees?.guestServiceFeeRate ?? 0.14;
  const hostFee = meta?.fees?.hostServiceFeeRate ?? 0.03;
  const guestPays = form.pricePerNight + form.cleaningFee;

  // docs/01 CN-10 — what comparable places in the same city charge. Re-read when
  // the host changes their own number so the verdict keeps up with them.
  const [market, setMarket] = useState(null);
  useEffect(() => {
    if (!form.city.trim()) { setMarket(null); return; }
    api.marketPrice({
      city: form.city,
      roomTypeKey: form.roomTypeKey,
      bedrooms: form.bedrooms,
      price: form.pricePerNight
    }).then(setMarket).catch(() => setMarket(null));
  }, [form.city, form.roomTypeKey, form.bedrooms, form.pricePerNight]);

  // docs/01 CN-14 — a what-if income estimate that tracks the price the host types.
  const [income, setIncome] = useState(null);
  useEffect(() => {
    if (!(form.pricePerNight >= 50000)) { setIncome(null); return; }
    api.incomeEstimate({ pricePerNight: form.pricePerNight, cleaningFee: form.cleaningFee })
      .then(setIncome).catch(() => setIncome(null));
  }, [form.pricePerNight, form.cleaningFee]);

  return <>
    {market && (
      <section className="modal-section">
        <h3>{t('Giá thị trường ở')} {market.city}</h3>
        <span className="hint">
          {market.sampleSize > 0
            ? `${t('Dựa trên')} ${market.sampleSize} ${t('chỗ nghỉ tương đương đang hiển thị.')}`
            : t('Chưa có dữ liệu so sánh.')}
        </span>
        {market.sampleSize > 0 && (
          <div className="market-band">
            <div><span className="cap">{t('Thấp')}</span><b>{money(market.low)}</b></div>
            <div className="is-mid"><span className="cap">{t('Trung vị')}</span><b>{money(market.median)}</b></div>
            <div><span className="cap">{t('Cao')}</span><b>{money(market.high)}</b></div>
          </div>
        )}
        {market.verdict && <p className="field-note" style={{ marginTop: 10 }}>{market.verdict}</p>}
      </section>
    )}
    <section className="modal-section">
      <h3>{t('Giá mỗi đêm')}</h3>
      <div className="field-grid">
        <label className="form-field"><span className="cap">{t('Giá mỗi đêm (₫) *')}</span>
          <input type="number" min={50000} step={10000} value={form.pricePerNight}
                 onChange={e => num('pricePerNight', e.target.value)} required /></label>
        <label className="form-field"><span className="cap">{t('Phí dọn dẹp (₫)')}</span>
          <input type="number" min={0} step={10000} value={form.cleaningFee}
                 onChange={e => num('cleaningFee', e.target.value)} /></label>
      </div>
      <p style={{ marginTop: 12, fontSize: 13.5, color: 'var(--ink-body)', lineHeight: 1.6 }}>
        {t('Một đêm: khách trả khoảng')} <b>{money(Math.round(guestPays * (1 + guestFee)))}</b> {t('(đã gồm phí dịch vụ')} {Math.round(guestFee * 100)}%{t(', chưa gồm thuế),')}
        {' '}{t('bạn nhận')} <b>{money(Math.round(guestPays * (1 - hostFee)))}</b> {t('sau phí')} {Math.round(hostFee * 100)}%.
      </p>
    </section>

    {/* docs/01 CN-14 — ước lượng thu nhập theo các mức lấp đầy khác nhau. */}
    {income && income.scenarios.length > 0 && (
      <section className="modal-section">
        <h3>{t('Ước lượng thu nhập')}</h3>
        <span className="hint">{t('Số ròng sau phí dịch vụ chủ nhà, theo vài mức lấp đầy. Chỉ để tham khảo.')}</span>
        <div className="market-band" style={{ marginTop: 10 }}>
          {income.scenarios.map(s => (
            <div key={s.occupancyPercent} className={s.occupancyPercent === 60 ? 'is-mid' : ''}>
              <span className="cap">{s.label} ({s.occupancyPercent}%)</span>
              <b>{money(s.monthlyNet)}/{t('tháng')}</b>
              <span className="cap" style={{ marginTop: 2 }}>≈ {money(s.annualNet)}/{t('năm')}</span>
            </div>
          ))}
        </div>
      </section>
    )}

    <section className="modal-section">
      <h3>{t('Giảm giá')}</h3>
      <span className="hint">{t('Chỉ một mức theo độ dài và một mức theo thời điểm đặt được áp dụng.')}</span>
      <div className="field-grid">
        <label className="form-field"><span className="cap">{t('Ở từ 7 đêm (%)')}</span>
          <input type="number" min={0} max={60} value={form.pricing.weeklyDiscountPercent}
                 onChange={e => rule('weeklyDiscountPercent', Number(e.target.value) || 0)} /></label>
        <label className="form-field"><span className="cap">{t('Ở từ 28 đêm (%)')}</span>
          <input type="number" min={0} max={60} value={form.pricing.monthlyDiscountPercent}
                 onChange={e => rule('monthlyDiscountPercent', Number(e.target.value) || 0)} /></label>
        <label className="form-field"><span className="cap">{t('Đặt sớm: trước bao nhiêu ngày')}</span>
          <input type="number" min={0} max={365} value={form.pricing.earlyBirdDays}
                 onChange={e => rule('earlyBirdDays', Number(e.target.value) || 0)} /></label>
        <label className="form-field"><span className="cap">{t('Đặt sớm (%)')}</span>
          <input type="number" min={0} max={60} value={form.pricing.earlyBirdPercent}
                 onChange={e => rule('earlyBirdPercent', Number(e.target.value) || 0)} /></label>
        <label className="form-field"><span className="cap">{t('Staylio Thân thiết (%)')}</span>
          <input type="number" min={0} max={30} value={form.pricing.loyaltyDiscountPercent ?? 0}
                 onChange={e => rule('loyaltyDiscountPercent', Number(e.target.value) || 0)} /></label>
      </div>
      <span className="hint">{t('Giảm cho khách đã hoàn tất từ 3 chuyến trong 2 năm; khách từ 10 chuyến được thêm 5%. Để 0 nếu không tham gia.')}</span>
    </section>

    <section className="modal-section">
      <h3>{t('Phụ thu')}</h3>
      <div className="field-grid">
        <label className="form-field"><span className="cap">{t('Số khách đã gồm trong giá')}</span>
          <input type="number" min={1} max={form.maxGuests} value={form.pricing.freeGuestThreshold}
                 onChange={e => rule('freeGuestThreshold', Number(e.target.value) || 1)} /></label>
        <label className="form-field"><span className="cap">{t('Phụ thu mỗi khách thêm / đêm (₫)')}</span>
          <input type="number" min={0} step={10000} value={form.pricing.extraGuestFee}
                 onChange={e => rule('extraGuestFee', Number(e.target.value) || 0)} /></label>
        <label className="form-field"><span className="cap">{t('Phí thú cưng (₫)')}</span>
          <input type="number" min={0} step={10000} value={form.pricing.petFee}
                 onChange={e => rule('petFee', Number(e.target.value) || 0)} /></label>
      </div>
      <div className="pill-row" style={{ marginTop: 14 }}>
        <button type="button" className={`pill ${form.pricing.petsAllowed ? 'is-on' : ''}`}
                onClick={() => rule('petsAllowed', !form.pricing.petsAllowed)}>
          {form.pricing.petsAllowed ? t('Nhận thú cưng') : t('Không nhận thú cưng')}
        </button>
      </div>
    </section>
  </>;
}

function StepRules({ form, field, num }) {
  return <>
    <section className="modal-section">
      <h3>{t('Cách nhận đơn')}</h3>
      <div className="opt-grid">
        <button className={`opt ${form.instantBook ? 'is-on' : ''}`} onClick={() => field('instantBook', true)}>
          <b>{t('Đặt ngay')}</b><span>{t('Khách đặt và trả tiền luôn, không cần bạn duyệt.')}</span>
        </button>
        <button className={`opt ${!form.instantBook ? 'is-on' : ''}`} onClick={() => field('instantBook', false)}>
          <b>{t('Duyệt yêu cầu')}</b><span>{t('Bạn có 24 giờ để đồng ý. Ngày không bị khoá trong lúc chờ.')}</span>
        </button>
      </div>

      {/* docs/01 ĐP-03 — conditions on who may instant-book. A guest who fails
          one is not turned away; their booking becomes a request you review. */}
      {form.instantBook && (
        <div style={{ marginTop: 14, display: 'grid', gap: 8 }}>
          <label className="check-row">
            <input type="checkbox" checked={!!form.instantBookRequiresVerified}
                   onChange={e => field('instantBookRequiresVerified', e.target.checked)} />
            <span>{t('Chỉ nhận Đặt ngay từ khách đã xác minh danh tính')}</span>
          </label>
          <label className="check-row">
            <input type="checkbox" checked={!!form.instantBookRequiresGoodReviews}
                   onChange={e => field('instantBookRequiresGoodReviews', e.target.checked)} />
            <span>{t('Chỉ nhận Đặt ngay từ khách có đánh giá tốt')}</span>
          </label>
        </div>
      )}

      {/* docs/01 ĐP-10 — hard preconditions for any booking, instant or request. */}
      <div style={{ marginTop: 18, display: 'grid', gap: 8 }}>
        <span className="cap">{t('Yêu cầu bắt buộc với khách')}</span>
        <label className="check-row">
          <input type="checkbox" checked={!!form.requireGuestPhoto}
                 onChange={e => field('requireGuestPhoto', e.target.checked)} />
          <span>{t('Khách phải có ảnh đại diện')}</span>
        </label>
        <label className="check-row">
          <input type="checkbox" checked={!!form.requireVerifiedToBook}
                 onChange={e => field('requireVerifiedToBook', e.target.checked)} />
          <span>{t('Khách phải xác minh danh tính')}</span>
        </label>
      </div>

      {/* docs/07 §2.5 — off unless the host turns it on, and never turned on by
          the platform: the protection being given up is theirs. */}
      <div style={{ marginTop: 18, display: 'grid', gap: 8 }}>
        <span className="cap">{t('Trả tiền tại nơi ở')}</span>
        <label className="check-row">
          <input type="checkbox" checked={!!form.acceptsPayAtProperty}
                 onChange={e => field('acceptsPayAtProperty', e.target.checked)} />
          <span>{t('Cho phép khách trả tiền cho tôi khi nhận phòng')}</span>
        </label>
        {form.acceptsPayAtProperty && (
          <p className="notice notice-warn" style={{ margin: 0 }}>
            {t('Khách trả tiền mặt cho bạn khi nhận phòng. Staylio không giữ tiền, nên nếu khách không tới thì không có khoản nào để bù. Phí dịch vụ của đơn được trừ vào lần chuyển tiền kế tiếp của bạn.')}
          </p>
        )}
      </div>
    </section>

    <section className="modal-section">
      <h3>{t('Số đêm tối thiểu')}</h3>
      <label className="form-field" style={{ maxWidth: 240 }}>
        <span className="cap">{t('Đêm')}</span>
        <input type="number" min={1} max={90} value={form.minNights}
               onChange={e => num('minNights', e.target.value)} />
      </label>
    </section>

    <section className="modal-section">
      <h3>{t('Chính sách huỷ')}</h3>
      <span className="hint">{t('Chính sách càng linh hoạt thì càng nhiều khách đặt.')}</span>
      <div className="opt-grid">
        {TIERS.map(([key, label, hint]) => (
          <button key={key} className={`opt ${form.cancellationTier === key ? 'is-on' : ''}`}
                  onClick={() => field('cancellationTier', key)}>
            <b>{t(label)}</b><span>{t(hint)}</span>
          </button>
        ))}
      </div>
    </section>
  </>;
}

/** docs/01 CN-12 — what has to be declared before a listing can go public. */
/**
 * docs/01 CĐ-03 — the arrival guide. Everything here reaches one guest with a
 * confirmed stay and nobody else (docs/03 §10), and the door code only inside
 * the last 48 hours (CĐ-04) — the screen says so, because a host typing their
 * own door code deserves to know where it goes.
 */
function StepCheckIn({ form, arrival }) {
  const c = form.checkIn;
  const needsCode = NEEDS_CODE.includes(c.method);

  return (
    <section className="modal-section">
      <h3>{t('Hướng dẫn nhận phòng')}</h3>
      <span className="hint">
        {t('Chỉ khách đã được xác nhận mới đọc được phần này. Mã cửa hiện với khách từ 48 giờ trước giờ nhận phòng.')}
      </span>

      <div className="grid-3" style={{ marginTop: 14 }}>
        <label className="form-field"><span className="cap">{t('Nhận phòng từ')}</span>
          <input type="time" value={c.checkInFrom}
                 onChange={e => arrival('checkInFrom', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Đến')}</span>
          <input type="time" value={c.checkInTo}
                 onChange={e => arrival('checkInTo', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Trả phòng trước')}</span>
          <input type="time" value={c.checkOutBefore}
                 onChange={e => arrival('checkOutBefore', e.target.value)} /></label>
      </div>

      <label className="form-field"><span className="cap">{t('Cách vào nhà')}</span>
        <select value={c.method} onChange={e => arrival('method', e.target.value)}>
          {CHECKIN_METHODS.map(([key, label]) => <option key={key} value={key}>{t(label)}</option>)}
        </select>
      </label>

      {needsCode && (
        <label className="form-field"><span className="cap">{t('Mã cửa / mã hộp khoá')}</span>
          <input value={c.doorCode ?? ''} maxLength={40} placeholder={t('Ví dụ: 4207#')}
                 onChange={e => arrival('doorCode', e.target.value)} /></label>
      )}

      <label className="form-field"><span className="cap">{t('Địa chỉ đầy đủ')}</span>
        {/* A sample Vietnamese address, and the property is in Vietnam whoever the
            host is — nothing here is language, so nothing here is translated. */}
        <input value={c.addressLine ?? ''} placeholder="123 Nguyễn Văn Linh, Hải Châu, Đà Nẵng"
               onChange={e => arrival('addressLine', e.target.value)} /></label>

      <label className="form-field"><span className="cap">{t('Số điện thoại liên hệ')}</span>
        <input type="tel" value={c.hostPhone ?? ''} placeholder="0912 345 678"
               onChange={e => arrival('hostPhone', e.target.value)} /></label>

      <div className="grid-2">
        <label className="form-field"><span className="cap">{t('Tên wifi')}</span>
          <input value={c.wifiName ?? ''} placeholder="Staylio-201"
                 onChange={e => arrival('wifiName', e.target.value)} /></label>
        <label className="form-field"><span className="cap">{t('Mật khẩu wifi')}</span>
          <input value={c.wifiPassword ?? ''} onChange={e => arrival('wifiPassword', e.target.value)} /></label>
      </div>

      <label className="form-field"><span className="cap">{t('Chỉ đường')}</span>
        <textarea rows={3} value={c.directions ?? ''}
                  placeholder={t('Từ sân bay đi taxi khoảng 20 phút. Toà nhà ngay góc ngã tư, cổng sơn xanh.')}
                  onChange={e => arrival('directions', e.target.value)}
                  style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
      </label>

      <label className="form-field"><span className="cap">{t('Hướng dẫn thiết bị — mỗi dòng một mục')}</span>
        <textarea rows={4} value={c.applianceNotes ?? ''}
                  placeholder={`Điều hoà: bấm nút xanh, để 26°C.
Bình nóng lạnh: bật công tắc ngoài phòng tắm, chờ 10 phút.`}
                  onChange={e => arrival('applianceNotes', e.target.value)}
                  style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
      </label>
    </section>
  );
}

function StepLegal({ form, legal }) {
  return (
    <section className="modal-section">
      <h3>{t('Khai báo pháp lý & an toàn')}</h3>
      <span className="hint">
        {t('Khai sai hoặc giấu thiết bị ghi hình là lý do gỡ tin đăng và huỷ tư cách chủ nhà.')}
      </span>

      <label className="form-field" style={{ marginTop: 14 }}>
        <span className="cap">{t('Số giấy phép kinh doanh lưu trú')} <span style={{ fontWeight: 400 }}>{t('(nếu khu vực yêu cầu)')}</span></span>
        <input value={form.legal.licenseNumber ?? ''} placeholder={t('Ví dụ: 0312345678')}
               onChange={e => legal('licenseNumber', e.target.value)} />
      </label>

      <div className="count-row">
        <div className="tx">
          <b>{t('Có thiết bị ghi hình trong hoặc quanh nhà')}</b>
          <span>{t('Camera an ninh, chuông cửa có hình, thiết bị ghi âm.')}</span>
        </div>
        <button type="button" className={`pill ${form.legal.hasSecurityCameras ? 'is-on' : ''}`}
                onClick={() => legal('hasSecurityCameras', !form.legal.hasSecurityCameras)}>
          {form.legal.hasSecurityCameras ? t('Có') : t('Không')}
        </button>
      </div>

      {form.legal.hasSecurityCameras && (
        <label className="form-field">
          <span className="cap">{t('Đặt ở đâu, ghi gì')}</span>
          <textarea rows={3} value={form.legal.securityCameraNote ?? ''}
                    placeholder={t('Một camera ngoài cổng, chỉ quay lối vào, không có trong nhà.')}
                    onChange={e => legal('securityCameraNote', e.target.value)}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>
      )}

      <div className="count-row">
        <div className="tx"><b>{t('Có vũ khí trong nhà')}</b><span>{t('Kể cả vũ khí đã cất giữ an toàn.')}</span></div>
        <button type="button" className={`pill ${form.legal.hasWeaponsOnProperty ? 'is-on' : ''}`}
                onClick={() => legal('hasWeaponsOnProperty', !form.legal.hasWeaponsOnProperty)}>
          {form.legal.hasWeaponsOnProperty ? t('Có') : t('Không')}
        </button>
      </div>

      <div className="count-row">
        <div className="tx"><b>{t('Có vật nuôi nguy hiểm')}</b><span>{t('Chó giữ nhà cỡ lớn, vật nuôi hoang dã.')}</span></div>
        <button type="button" className={`pill ${form.legal.hasDangerousAnimals ? 'is-on' : ''}`}
                onClick={() => legal('hasDangerousAnimals', !form.legal.hasDangerousAnimals)}>
          {form.legal.hasDangerousAnimals ? t('Có') : t('Không')}
        </button>
      </div>
    </section>
  );
}

/** docs/01 CN-13 — the host sees it the way a guest will, before publishing. */
function StepReview({ form, onJump }) {
  const jump = key => onJump(STEPS.findIndex(s => s[0] === key));

  const rows = [
    ['type', 'Loại hình', `${form.typeKey} · ${t(ROOM_TYPES.find(r => r[0] === form.roomTypeKey)?.[1] ?? '')}`],
    ['place', 'Vị trí', form.latitude ? `${form.city} · ${t('đã ghim')}` : `${form.city} · ${t('chưa ghim')}`],
    ['capacity', 'Sức chứa', `${form.maxGuests} ${t('khách')} · ${form.bedrooms} ${t('phòng ngủ')} · ${form.beds} ${t('giường')}`],
    ['photos', 'Ảnh', `${form.images.length} ${t('ảnh')}${form.images.length < 5 ? t(' — chưa đủ 5') : ''}`],
    ['price', 'Giá', `${money(form.pricePerNight)}/${t('đêm')} · ${t('dọn dẹp')} ${money(form.cleaningFee)}`],
    ['rules', 'Nhận đơn', form.instantBook ? t('Đặt ngay') : t('Duyệt yêu cầu')],
    ['legal', 'Pháp lý', form.legal.hasSecurityCameras ? t('Có khai báo camera') : t('Không có thiết bị ghi hình')]
  ];

  return <>
    <section className="modal-section">
      <h3>{t('Khách sẽ thấy thế này')}</h3>
      <div style={{ display: 'flex', gap: 14, alignItems: 'flex-start', marginTop: 14 }}>
        {form.images[0]
          ? <img src={form.images[0]} alt="" style={{ width: 200, height: 150, objectFit: 'cover', borderRadius: 12 }} />
          : <div className="skeleton" style={{ width: 200, height: 150, borderRadius: 12 }} />}
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 17, fontWeight: 800 }}>{form.title || t('Chưa có tiêu đề')}</div>
          <div style={{ fontSize: 13.5, color: 'var(--ink-muted)', marginTop: 4 }}>
            {form.city} · {form.bedrooms} {t('phòng ngủ')} · {form.beds} {t('giường')} · {form.bathrooms} {t('phòng tắm')}
          </div>
          <div style={{ fontSize: 15, fontWeight: 700, marginTop: 8 }}>{money(form.pricePerNight)} / {t('đêm')}</div>
          <p style={{ fontSize: 13.5, lineHeight: 1.6, color: 'var(--ink-body)', marginTop: 10 }}>
            {form.description || t('Chưa có mô tả.')}
          </p>
        </div>
      </div>
    </section>

    <section className="modal-section">
      <h3>{t('Kiểm tra lần cuối')}</h3>
      <div style={{ display: 'grid', gap: 8, marginTop: 12 }}>
        {rows.map(([key, label, value]) => (
          <div className="cal-row" key={key}>
            <div style={{ flex: 1, minWidth: 0, fontSize: 13.5 }}>
              <b>{t(label)}</b><span style={{ color: 'var(--ink-muted)' }}> · {value}</span>
            </div>
            <button className="text-btn" onClick={() => jump(key)}>{t('Sửa')}</button>
          </div>
        ))}
      </div>
    </section>
  </>;
}
