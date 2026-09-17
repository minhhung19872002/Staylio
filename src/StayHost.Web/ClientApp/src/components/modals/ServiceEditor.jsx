import { useState } from 'react';
import { useStore } from '../../lib/useStore.js';
import { set, closeOverlay, toast } from '../../lib/store.js';
import { api } from '../../lib/api.js';
import { todayIso } from '../../lib/format.js';
import { Modal } from './Modal.jsx';
import { t } from '../../lib/i18n.js';

/**
 * docs/09 §3.2–§3.4 (MR-S-01) — what a provider has to tell the platform before
 * their service may be sold: the job itself, the price model, how far they will
 * travel, which days they work, and the practising certificate their trade
 * demands.
 *
 * The category and certificate rules below mirror ServiceRules.NeedsCertificate
 * and CertificateLapsed on the server. They are here so the provider sees the
 * consequence of their own answers while they are still typing; the server runs
 * the same checks again on save and has the last word.
 */
const CATEGORIES = [
  ['chef', 'Đầu bếp tại nhà'],
  ['meals', 'Đồ ăn nấu sẵn'],
  ['catering', 'Tiệc và catering'],
  ['photo', 'Chụp ảnh'],
  ['massage', 'Massage và spa'],
  ['fitness', 'Huấn luyện thể chất'],
  ['hair', 'Làm tóc'],
  ['makeup', 'Trang điểm'],
  ['nails', 'Làm móng'],
  ['transfer', 'Đưa đón'],
  ['luggage', 'Giữ hành lý'],
  ['groceries', 'Đi chợ hộ'],
  ['cleaning', 'Dọn dẹp theo giờ'],
  ['car', 'Thuê xe có tài'],
  ['translate', 'Phiên dịch'],
  ['childcare', 'Trông trẻ']
];

/** docs/09 §3.2 — the trades that may not be sold at all without a certificate. */
const NEEDS_CERTIFICATE = ['chef', 'massage', 'fitness', 'hair', 'makeup', 'nails'];

const needsCertificate = category =>
  NEEDS_CERTIFICATE.includes((category ?? '').trim().toLowerCase());

/** docs/09 §3.5 — categories whose booking form makes a note mandatory. */
const REQUIRED_NOTE = {
  chef: 'Dị ứng thực phẩm',
  massage: 'Tình trạng sức khoẻ và vùng cần tránh',
  fitness: 'Mục tiêu tập luyện và chấn thương cũ'
};

const PRICING = [
  ['PerSession', 'Trọn buổi', 'buổi'],
  ['PerHour', 'Tính theo giờ', 'giờ'],
  ['PerPerson', 'Tính theo người', 'người'],
  ['PerOrder', 'Tính theo phần', 'phần']
];

const unitOf = pricing => PRICING.find(([k]) => k === pricing)?.[2] ?? 'buổi';

/** Monday is bit 0 and Sunday bit 6, exactly as ServiceRules.WorksOn reads it. */
const WEEKDAYS = [
  [0, 'T2'], [1, 'T3'], [2, 'T4'], [3, 'T5'], [4, 'T6'], [5, 'T7'], [6, 'CN']
];

/** docs/09 §3.2 — how long before expiry the provider is warned. */
const CERTIFICATE_REMINDER_DAYS = 30;

const daysUntil = (iso, today) =>
  Math.round((new Date(`${iso}T00:00:00`) - new Date(`${today}T00:00:00`)) / 86400000);

/**
 * docs/09 §3.2 — mirrors the gate SaveOfferingAsync runs when Publish is set.
 * Read before the provider presses "mở bán" so they are not bounced by a server
 * error they could have seen coming.
 */
function publishBlockers(form, today) {
  const missing = [];

  if (form.title.trim().length < 4) missing.push('Tên dịch vụ ít nhất 4 ký tự');
  if (!form.city.trim()) missing.push('Thành phố');
  if (!(Number(form.basePrice) > 0)) missing.push('Giá lớn hơn 0');
  if (!form.images.length) missing.push('Ảnh thật của dịch vụ');

  if (needsCertificate(form.category)) {
    if (!form.certificateName.trim()) missing.push('Chứng chỉ hành nghề');
    else if (form.certificateExpiresOn && form.certificateExpiresOn < today)
      missing.push('Chứng chỉ hành nghề còn hạn');
  }

  return missing;
}

const BLANK = {
  id: 0, title: '', category: '', city: '', summary: '', description: '',
  pricing: 'PerSession', basePrice: 500000,
  minQuantity: 1, maxQuantity: 4, durationMinutes: 120,
  travelsToGuest: true, serviceRadiusKm: 10,
  travelFeePerKm: 0, maxTravelKm: 0,
  latitude: 0, longitude: 0,
  opensAtHour: 8, closesAtHour: 20,
  workingDaysMask: 127, bufferMinutes: 0, maxJobsPerDay: 0, requiresConfirmation: false,
  onSiteRequirements: '', addOns: [], images: [],
  certificateName: '', certificateExpiresOn: '',
  isPublished: false
};

export function ServiceEditor() {
  const state = useStore();
  const editing = state.editingService;

  const [form, setForm] = useState(() => {
    if (!editing) return { ...BLANK };
    return {
      ...BLANK,
      id: editing.id,
      title: editing.title ?? '',
      category: editing.category ?? '',
      city: editing.city ?? '',
      summary: editing.summary ?? '',
      description: editing.description ?? '',
      pricing: editing.pricing ?? 'PerSession',
      basePrice: editing.basePrice ?? 0,
      minQuantity: editing.minQuantity ?? 1,
      maxQuantity: editing.maxQuantity ?? 1,
      durationMinutes: editing.durationMinutes ?? 120,
      travelsToGuest: editing.travelsToGuest ?? true,
      serviceRadiusKm: editing.serviceRadiusKm ?? 0,
      travelFeePerKm: editing.travelFeePerKm ?? 0,
      maxTravelKm: editing.maxTravelKm ?? 0,
      latitude: editing.latitude ?? 0,
      longitude: editing.longitude ?? 0,
      opensAtHour: editing.opensAtHour ?? 8,
      closesAtHour: editing.closesAtHour ?? 20,
      workingDaysMask: editing.workingDaysMask ?? 127,
      maxJobsPerDay: editing.maxJobsPerDay ?? 0,
      requiresConfirmation: !!editing.requiresConfirmation,
      bufferMinutes: editing.bufferMinutes ?? 0,
      onSiteRequirements: (editing.onSiteRequirements ?? []).join('\n'),
      addOns: (editing.addOns ?? []).map(a => ({ name: a.name, price: a.price })),
      images: editing.images ?? [],
      certificateName: editing.certificateName ?? '',
      certificateExpiresOn: editing.certificateExpiresOn ?? '',
      isPublished: editing.isPublished ?? false
    };
  });

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  const field = (key, value) => setForm(f => ({ ...f, [key]: value }));
  const num = (key, value) => field(key, Number(value) || 0);

  const today = todayIso();
  const missing = publishBlockers(form, today);
  const unit = unitOf(form.pricing);

  // docs/09 §3.2 — a certificate inside the thirty-day window still sells, but
  // the listing hides itself the day it lapses, so say so now.
  const expiringSoon = form.certificateExpiresOn
    && form.certificateExpiresOn >= today
    && daysUntil(form.certificateExpiresOn, today) <= CERTIFICATE_REMINDER_DAYS;

  const persist = async publish => {
    setSaving(true);
    setError(null);

    const body = {
      id: form.id || null,
      title: form.title.trim(),
      category: form.category || null,
      city: form.city.trim(),
      summary: form.summary.trim(),
      description: form.description.trim(),
      pricing: form.pricing,
      basePrice: Number(form.basePrice) || 0,
      minQuantity: Number(form.minQuantity) || 1,
      maxQuantity: Number(form.maxQuantity) || 1,
      durationMinutes: Number(form.durationMinutes) || 0,
      travelsToGuest: form.travelsToGuest,
      serviceRadiusKm: Number(form.serviceRadiusKm) || 0,
      latitude: Number(form.latitude) || 0,
      longitude: Number(form.longitude) || 0,
      opensAtHour: Number(form.opensAtHour) || 0,
      closesAtHour: Number(form.closesAtHour) || 0,
      images: form.images,
      publish,
      // docs/09 §3.3–§3.4 — the journey, the working week and the place itself.
      travelFeePerKm: Number(form.travelFeePerKm) || 0,
      maxTravelKm: Number(form.maxTravelKm) || 0,
      workingDaysMask: form.workingDaysMask,
      bufferMinutes: Number(form.bufferMinutes) || 0,
      maxJobsPerDay: Number(form.maxJobsPerDay) || 0,
      requiresConfirmation: !!form.requiresConfirmation,
      onSiteRequirements: form.onSiteRequirements.split('\n').map(s => s.trim()).filter(Boolean),
      addOns: form.addOns
        .filter(a => a.name.trim())
        .map(a => ({ name: a.name.trim(), price: Number(a.price) || 0 })),
      // docs/09 §3.2 — sent whatever the category, so switching category back and
      // forth never silently drops a certificate the provider already typed.
      certificateName: form.certificateName.trim() || null,
      certificateExpiresOn: form.certificateExpiresOn || null
    };

    try {
      const saved = await api.saveService(body);
      setForm(f => ({ ...f, id: saved.id }));
      return saved;
    } catch (err) {
      setError(t(err.message));
      return null;
    } finally {
      setSaving(false);
    }
  };

  const saveDraft = async () => {
    const saved = await persist(false);
    if (saved) { toast(t('Đã lưu nháp. Bạn quay lại lúc nào cũng được.')); closeOverlay(); }
  };

  // docs/09 §3.2 — unlike an experience this does go on sale, but only if the
  // certificate the trade demands is on file and still in date.
  const publish = async () => {
    if (missing.length) {
      setError(`${t('Còn thiếu:')} ${missing.map(m => t(m)).join(', ')}.`);
      return;
    }
    const saved = await persist(true);
    if (saved) {
      toast(t('Đã mở bán dịch vụ. Khách tìm thấy ngay từ bây giờ.'));
      closeOverlay();
    }
  };

  return (
    <Modal title={form.id ? t('Chỉnh sửa dịch vụ') : t('Đăng dịch vụ mới')} size="wide" foot={<>
      <button className="text-btn" onClick={saveDraft} disabled={saving}>{t('Lưu nháp & thoát')}</button>
      <button className="btn btn-primary btn-sm" onClick={publish} disabled={saving || !!missing.length}>
        {saving ? t('Đang lưu…') : form.isPublished ? t('Lưu & tiếp tục bán') : t('Mở bán')}
      </button>
    </>}>
      {error && <div className="form-error">{error}</div>}

      <section className="modal-section">
        <h3>{t('Dịch vụ của bạn')}</h3>
        <label className="form-field">
          <span className="cap">{t('Tên dịch vụ *')}</span>
          <input value={form.title} maxLength={80} onChange={e => field('title', e.target.value)}
                 placeholder={t('Đầu bếp nấu bữa tối tại nhà')} required />
        </label>
        <div className="grid-2">
          <label className="form-field"><span className="cap">{t('Danh mục *')}</span>
            <select value={form.category} onChange={e => field('category', e.target.value)}>
              <option value="" disabled>{t('— Chọn danh mục —')}</option>
              {CATEGORIES.map(([k, label]) => <option key={k} value={k}>{t(label)}</option>)}
            </select></label>
          <label className="form-field"><span className="cap">{t('Thành phố *')}</span>
            <input value={form.city} onChange={e => field('city', e.target.value)} placeholder="Đà Nẵng" required /></label>
        </div>
        <label className="form-field">
          <span className="cap">{t('Giới thiệu ngắn')}</span>
          <input value={form.summary} maxLength={160} onChange={e => field('summary', e.target.value)}
                 placeholder={t('Một câu khách đọc trên thẻ dịch vụ.')} />
        </label>
        <label className="form-field">
          <span className="cap">{t('Mô tả công việc')}</span>
          <textarea rows={4} value={form.description} onChange={e => field('description', e.target.value)}
                    placeholder={t('Bạn làm những gì, mang theo gì, khách cần chuẩn bị gì.')}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>
        {REQUIRED_NOTE[form.category] && (
          <p className="field-note">
            {t('Danh mục này bắt buộc khách điền trước khi đặt:')} {t(REQUIRED_NOTE[form.category])}.
          </p>
        )}
      </section>

      {/* docs/09 §3.3 (MR-S-03) — the price model the category actually fits. */}
      <section className="modal-section">
        <h3>{t('Giá tính thế nào')}</h3>
        <span className="hint">
          {t('Đầu bếp thường tính theo người, chụp ảnh theo buổi, huấn luyện theo giờ. Chọn đúng thì khách hiểu ngay con số họ thấy.')}
        </span>

        <div className="grid-2" style={{ marginTop: 14 }}>
          <label className="form-field"><span className="cap">{t('Mô hình giá *')}</span>
            <select value={form.pricing} onChange={e => field('pricing', e.target.value)}>
              {PRICING.map(([k, label]) => <option key={k} value={k}>{t(label)}</option>)}
            </select></label>
          <label className="form-field"><span className="cap">{t('Giá gốc (₫) *')}</span>
            <input type="number" min={0} step={10000} value={form.basePrice}
                   onChange={e => num('basePrice', e.target.value)} required /></label>
        </div>
        <div className="field-grid">
          <label className="form-field"><span className="cap">{t('Nhận ít nhất')} ({t(unit)})</span>
            <input type="number" min={1} max={99} value={form.minQuantity}
                   onChange={e => num('minQuantity', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Nhận nhiều nhất')} ({t(unit)})</span>
            <input type="number" min={form.minQuantity} max={99} value={form.maxQuantity}
                   onChange={e => num('maxQuantity', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Thời lượng một buổi (phút)')}</span>
            <input type="number" min={15} max={720} step={15} value={form.durationMinutes}
                   onChange={e => num('durationMinutes', e.target.value)} /></label>
        </div>
        <p className="field-note">
          {t('Trọn buổi thì số lượng không nhân giá — mọi mô hình khác thì có.')}
        </p>
      </section>

      <ServiceAddOns form={form} setForm={setForm} />

      {/* docs/09 §3.3 (MR-S-04) — how far, and what the journey costs. */}
      <section className="modal-section">
        <h3>{t('Bạn tới tận nơi hay khách tới chỗ bạn')}</h3>

        <label className="check-row">
          <input type="checkbox" checked={form.travelsToGuest}
                 onChange={e => field('travelsToGuest', e.target.checked)} />
          <span>{t('Tôi tới tận nơi khách ở')}</span>
        </label>

        {form.travelsToGuest && <>
          <div className="field-grid" style={{ marginTop: 14 }}>
            <label className="form-field"><span className="cap">{t('Bán kính phục vụ (km)')}</span>
              <input type="number" min={0} max={200} value={form.serviceRadiusKm}
                     onChange={e => num('serviceRadiusKm', e.target.value)} /></label>
            <label className="form-field"><span className="cap">{t('Phí di chuyển mỗi km ngoài bán kính (₫)')}</span>
              <input type="number" min={0} step={1000} value={form.travelFeePerKm}
                     onChange={e => num('travelFeePerKm', e.target.value)} /></label>
            <label className="form-field"><span className="cap">{t('Đi thêm tối đa (km)')}</span>
              <input type="number" min={0} max={200} value={form.maxTravelKm}
                     onChange={e => num('maxTravelKm', e.target.value)} /></label>
          </div>
          <p className="field-note">
            {t('Để phí mỗi km bằng 0 nghĩa là ngoài bán kính thì không nhận. Có phí thì khách ở xa vẫn đặt được, phần vượt bán kính hiện thành một dòng riêng khi tính giá.')}
          </p>
        </>}

        <div className="grid-2">
          <label className="form-field"><span className="cap">{t('Vĩ độ nơi bạn xuất phát')}</span>
            <input type="number" step="0.0001" value={form.latitude}
                   onChange={e => num('latitude', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Kinh độ nơi bạn xuất phát')}</span>
            <input type="number" step="0.0001" value={form.longitude}
                   onChange={e => num('longitude', e.target.value)} /></label>
        </div>
        <p className="field-note">{t('Bán kính phục vụ đo từ điểm này.')}</p>
      </section>

      {/* docs/09 §3.4 (MR-S-05) — the working week and how tightly it may pack. */}
      <section className="modal-section">
        <h3>{t('Lịch làm việc')}</h3>

        <span className="cap">{t('Ngày nhận việc')}</span>
        <div className="pill-row" style={{ margin: '8px 0 4px' }}>
          {WEEKDAYS.map(([bit, label]) => (
            <button type="button" key={bit}
                    className={`pill ${(form.workingDaysMask & (1 << bit)) !== 0 ? 'is-on' : ''}`}
                    onClick={() => field('workingDaysMask', form.workingDaysMask ^ (1 << bit))}>
              {t(label)}
            </button>
          ))}
        </div>
        <p className="field-note">{t('Bỏ chọn hết thì hệ thống hiểu là nhận cả tuần.')}</p>

        <div className="field-grid">
          <label className="form-field"><span className="cap">{t('Mở nhận từ giờ')}</span>
            <input type="number" min={0} max={23} value={form.opensAtHour}
                   onChange={e => num('opensAtHour', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Nhận đến giờ')}</span>
            <input type="number" min={1} max={24} value={form.closesAtHour}
                   onChange={e => num('closesAtHour', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Nghỉ giữa hai đơn (phút)')}</span>
            <input type="number" min={0} max={240} step={5} value={form.bufferMinutes}
                   onChange={e => num('bufferMinutes', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Số đơn tối đa mỗi ngày')}</span>
            <input type="number" min={0} max={20} value={form.maxJobsPerDay}
                   onChange={e => num('maxJobsPerDay', e.target.value)} /></label>
        </div>
        {/* docs/09 §3.5 — "đặt ngay hoặc chờ nhà cung cấp xác nhận". */}
        <label className="check-row" style={{ marginTop: 12 }}>
          <input type="checkbox" checked={!!form.requiresConfirmation}
                 onChange={e => setForm(f => ({ ...f, requiresConfirmation: e.target.checked }))} />
          <span>{t('Tôi xác nhận từng đơn trước khi nhận (trong 24 giờ; không xác nhận thì khách được hoàn tiền)')}</span>
        </label>
        <p className="field-note">
          {t('Để 0 phút nghỉ là dùng mức mặc định 30 phút, và 0 đơn mỗi ngày là không giới hạn. Hệ thống còn cộng thêm thời gian di chuyển giữa hai địa chỉ, nên hai đơn quá xa nhau sẽ bị chặn.')}
        </p>
      </section>

      {/* docs/09 §3.3 (MR-S-07) — what the place must have before you turn up. */}
      <section className="modal-section">
        <h3>{t('Yêu cầu tại chỗ')}</h3>
        <span className="hint">
          {t('Mỗi dòng một điều kiện. Khách phải tích xác nhận có đủ trước khi đặt được — đó là căn cứ để bạn vẫn nhận 50% nếu tới nơi mới biết khai sai.')}
        </span>
        <label className="form-field" style={{ marginTop: 14 }}>
          <span className="cap">{t('Nơi thực hiện cần có — mỗi dòng một mục')}</span>
          <textarea rows={4} value={form.onSiteRequirements}
                    onChange={e => field('onSiteRequirements', e.target.value)}
                    placeholder={'Bếp có bếp từ hoặc bếp ga\nBàn ăn cho 6 người\nỔ điện gần khu làm việc'}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>
      </section>

      {/* docs/09 §3.2 (MR-S-02) — the practising certificate and its expiry. */}
      <section className="modal-section">
        <h3>{t('Chứng chỉ hành nghề')}</h3>
        <span className="hint">
          {needsCertificate(form.category)
            ? t('Danh mục này bắt buộc có chứng chỉ hành nghề còn hạn. Thiếu hoặc đã hết hạn thì không mở bán được.')
            : t('Danh mục này không bắt buộc, nhưng có chứng chỉ thì khách yên tâm hơn.')}
        </span>

        <div className="grid-2" style={{ marginTop: 14 }}>
          <label className="form-field">
            <span className="cap">{needsCertificate(form.category) ? t('Tên chứng chỉ *') : t('Tên chứng chỉ')}</span>
            <input value={form.certificateName} onChange={e => field('certificateName', e.target.value)}
                   placeholder={t('Chứng nhận an toàn thực phẩm — số 2026/1187')} /></label>
          <label className="form-field"><span className="cap">{t('Chứng chỉ hết hạn ngày')}</span>
            <input type="date" value={form.certificateExpiresOn}
                   onChange={e => field('certificateExpiresOn', e.target.value)} /></label>
        </div>

        {form.certificateExpiresOn && form.certificateExpiresOn < today && (
          <div className="notice notice-warn">
            {t('Chứng chỉ đã hết hạn. Dịch vụ tự ẩn khỏi tìm kiếm cho tới khi bạn gia hạn.')}
          </div>
        )}
        {expiringSoon && (
          <div className="notice notice-warn">
            {t('Chứng chỉ còn')} {daysUntil(form.certificateExpiresOn, today)}{' '}
            {t('ngày là hết hạn. Đến ngày đó dịch vụ tự ẩn khỏi tìm kiếm.')}
          </div>
        )}
      </section>

      <ServicePhotos form={form} setForm={setForm} />

      {/* docs/09 §3.2 — the same list the server checks, read before it is run. */}
      <section className="modal-section">
        <h3>{t('Trước khi mở bán')}</h3>
        {missing.length ? (
          <div className="notice notice-warn">
            <b>{t('Còn thiếu:')}</b>
            <ul style={{ margin: '6px 0 0', paddingLeft: 18, lineHeight: 1.8 }}>
              {missing.map(m => <li key={m}>{t(m)}</li>)}
            </ul>
          </div>
        ) : (
          <div className="notice notice-ok">{t('Đã đủ điều kiện. Bạn mở bán được ngay.')}</div>
        )}
      </section>
    </Modal>
  );
}

/**
 * docs/09 §3.3 (MR-S-03) — the paid extras: a set menu, a longer session, more
 * edited photos. Each is a line of its own on the guest's quote, so the provider
 * names it the way the guest should read it.
 */
function ServiceAddOns({ form, setForm }) {
  const change = (i, key, value) => setForm(f => ({
    ...f,
    addOns: f.addOns.map((a, x) => (x === i ? { ...a, [key]: value } : a))
  }));

  return (
    <section className="modal-section">
      <h3>{t('Tuỳ chọn thêm có giá riêng')}</h3>
      <span className="hint">
        {t('Thực đơn nâng cấp, thêm giờ, thêm ảnh chỉnh sửa. Khách tích chọn lúc đặt và mỗi mục hiện thành một dòng giá riêng.')}
      </span>

      {form.addOns.map((a, i) => (
        <div className="grid-2" key={i} style={{ alignItems: 'end' }}>
          <label className="form-field"><span className="cap">{t('Tên tuỳ chọn')}</span>
            <input value={a.name} maxLength={80} placeholder={t('Thực đơn hải sản')}
                   onChange={e => change(i, 'name', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Giá cộng thêm (₫)')}</span>
            <div style={{ display: 'flex', gap: 8 }}>
              <input type="number" min={0} step={10000} value={a.price} style={{ flex: '1 1 auto', minWidth: 0 }}
                     onChange={e => change(i, 'price', Number(e.target.value) || 0)} />
              <button type="button" className="text-btn" aria-label={t('Xoá tuỳ chọn')}
                      onClick={() => setForm(f => ({ ...f, addOns: f.addOns.filter((_, x) => x !== i) }))}>✕</button>
            </div>
          </label>
        </div>
      ))}

      <button type="button" className="btn btn-outline btn-sm" style={{ marginTop: 6 }}
              onClick={() => setForm(f => ({ ...f, addOns: [...f.addOns, { name: '', price: 0 }] }))}>
        {t('+ Thêm tuỳ chọn')}
      </button>
      {!form.addOns.length && (
        <p className="field-note" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('Chưa có tuỳ chọn nào — không bắt buộc phải có.')}
        </p>
      )}
    </section>
  );
}

/** docs/09 §3.2 — real photos of the provider's own work; none, no sale. */
function ServicePhotos({ form, setForm }) {
  const state = useStore();
  const [over, setOver] = useState(false);

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

      setForm(f => ({ ...f, images: [...f.images, ...payload.urls] }));
      toast(`${t('Đã tải lên')} ${payload.urls.length} ${t('ảnh')}.`);
    } catch (err) {
      toast(err.message);
    } finally {
      set({ uploading: false });
    }
  };

  return (
    <section className="modal-section">
      <h3>{t('Ảnh dịch vụ')}</h3>
      <span className="hint">{t('Ít nhất một ảnh thật của chính công việc bạn làm. Ảnh đầu tiên là ảnh bìa.')}</span>

      <label className={`dropzone ${over ? 'is-over' : ''}`} style={{ marginTop: 14 }}
             onDragOver={e => { e.preventDefault(); setOver(true); }}
             onDragEnter={e => { e.preventDefault(); setOver(true); }}
             onDragLeave={() => setOver(false)}
             onDrop={e => {
               e.preventDefault();
               setOver(false);
               const files = [...(e.dataTransfer?.files ?? [])].filter(f => f.type.startsWith('image/'));
               if (files.length) upload(files);
               else if (e.dataTransfer?.files?.length) toast(t('Chỉ nhận tệp ảnh.'));
             }}>
        <input type="file" accept="image/jpeg,image/png,image/webp,image/avif" multiple hidden
               onChange={e => { upload(e.target.files); e.target.value = ''; }} />
        <b>{state.uploading ? t('Đang tải ảnh lên…') : over ? t('Thả ảnh vào đây') : t('Kéo ảnh vào đây hoặc bấm để chọn')}</b>
        <span>{t('JPG, PNG, WebP hoặc AVIF · tối đa 8MB mỗi ảnh')}</span>
      </label>

      {!!form.images.length && (
        <div className="thumb-grid" style={{ marginTop: 16 }}>
          {form.images.map((url, i) => (
            <figure className="thumb" key={`${url}-${i}`}>
              <img src={url} alt={`${t('Ảnh')} ${i + 1}`} loading="lazy" />
              <button type="button" className="thumb-remove" aria-label={`${t('Xoá ảnh')} ${i + 1}`}
                      onClick={() => setForm(f => ({ ...f, images: f.images.filter((_, x) => x !== i) }))}>✕</button>
            </figure>
          ))}
        </div>
      )}
    </section>
  );
}
