import { useState } from 'react';
import { useStore } from '../../lib/useStore.js';
import { set, closeOverlay, toast } from '../../lib/store.js';
import { api } from '../../lib/api.js';
import { todayIso } from '../../lib/format.js';
import { Modal } from './Modal.jsx';
import { t } from '../../lib/i18n.js';

/**
 * docs/09 §2.1–§2.3 (MR-E-01) — what a host has to tell the platform about an
 * activity before it may be sold, and the papers its risk band demands.
 *
 * The three tables below mirror ExperienceRules.RiskOf / PublishBlockers on the
 * server. They are here so the host sees the consequence of their own answers
 * while they are still typing; the server remains the authority and checks the
 * same rules again on submit.
 */
const HIGH_RISK = ['diving', 'climbing', 'boat', 'rafting', 'extreme', 'paragliding'];
const MEDIUM_RISK = ['motorbike', 'cycling', 'cooking', 'farm', 'kayak', 'trekking'];

const CATEGORIES = [
  ['walking', 'Đi bộ khám phá'],
  ['food', 'Ẩm thực & ăn uống'],
  ['nature', 'Thiên nhiên'],
  ['arts', 'Nghệ thuật'],
  ['culture', 'Văn hoá & lịch sử'],
  ['wellness', 'Chăm sóc sức khoẻ'],
  ['shopping', 'Mua sắm'],
  ['nightlife', 'Về đêm'],
  ['craft-village', 'Làng nghề'],
  ['photo', 'Chụp ảnh'],
  ['motorbike', 'Tour xe máy'],
  ['cycling', 'Đạp xe'],
  ['cooking', 'Lớp nấu ăn'],
  ['farm', 'Nông trại'],
  ['kayak', 'Chèo kayak'],
  ['trekking', 'Trekking đường dài'],
  ['diving', 'Lặn biển'],
  ['climbing', 'Leo núi'],
  ['boat', 'Đi thuyền'],
  ['rafting', 'Vượt thác'],
  ['extreme', 'Thể thao mạo hiểm'],
  ['paragliding', 'Dù lượn']
];

/**
 * docs/09 §2.3 — the band follows from the activity, not from what the host
 * would like it to be, and children taking part lift a medium activity to high.
 */
function riskOf(category, allowsChildren) {
  const key = (category ?? '').trim().toLowerCase();
  const band = HIGH_RISK.includes(key) ? 'high' : MEDIUM_RISK.includes(key) ? 'medium' : 'low';
  return allowsChildren && band === 'medium' ? 'high' : band;
}

const RISK_LABEL = { low: 'Rủi ro thấp', medium: 'Rủi ro trung bình', high: 'Rủi ro cao' };

const RISK_NOTE = {
  low: 'Hoạt động nhẹ nhàng — không cần nộp thêm giấy tờ.',
  medium: 'Cần cam kết an toàn và phần dặn dò khách trước khi bắt đầu.',
  high: 'Bắt buộc có giấy phép hành nghề, bảo hiểm trách nhiệm còn hạn, cam kết an toàn và số điện thoại khẩn cấp. Không có ngoại lệ tạm thời.'
};

const RISK_STYLE = {
  low: { background: '#e8f7ee', color: '#12734a' },
  medium: { background: '#fff7e6', color: '#96690a' },
  // High is the band that stops a submission, so it is allowed to look like it.
  high: { background: '#fdecea', color: '#b42318', border: '1px solid #f3b4ae', textTransform: 'uppercase', letterSpacing: '.03em' }
};

/**
 * docs/09 §2.2/§2.3 — mirrors ExperienceRules.PublishBlockers. Shown before the
 * host presses "gửi duyệt" so they are not bounced by a server error they could
 * have seen coming; the server still runs the same list and has the last word.
 */
function publishBlockers(form, today) {
  const missing = [];
  const risk = riskOf(form.category, form.allowsChildren);

  if (!form.meetingPoint.trim()) missing.push('Điểm hẹn');
  if (!form.description.trim()) missing.push('Lịch trình theo mốc thời gian');

  if (risk !== 'low' && !form.safetyPlan.trim())
    missing.push('Cam kết an toàn và hướng dẫn trước khi bắt đầu');

  if (risk === 'high') {
    if (!form.licenceName.trim()) missing.push('Giấy phép hành nghề');
    else if (form.licenceExpiresOn && form.licenceExpiresOn < today) missing.push('Giấy phép hành nghề còn hạn');

    if (!form.insurancePolicy.trim()) missing.push('Bảo hiểm trách nhiệm');
    else if (form.insuranceExpiresOn && form.insuranceExpiresOn < today) missing.push('Bảo hiểm trách nhiệm còn hạn');

    if (!form.emergencyPhone.trim()) missing.push('Số điện thoại khẩn cấp');
  }

  // Not part of PublishBlockers, but the same gate on the server refuses a
  // submission with no photo, so it belongs in the same list the host reads.
  if (!form.images.length) missing.push('Ảnh thật của hoạt động');

  return missing;
}

const BLANK = {
  id: 0, title: '', city: '', summary: '', description: '',
  durationMinutes: 120, maxGroup: 10, minGuests: 2,
  languages: 'vi', minAge: 0, meetingPoint: '',
  latitude: null, longitude: null, included: '', itinerary: [],
  pricePerPerson: 500000, privateGroupPrice: '', images: [],
  // docs/09 §2.1–§2.3 — the vetting block.
  category: '', allowsChildren: false,
  licenceName: '', licenceExpiresOn: '',
  insurancePolicy: '', insuranceExpiresOn: '',
  safetyPlan: '', emergencyPhone: ''
};

/**
 * The vetting block comes back from /api/experiences/mine like every other
 * field, so it is read from the server rather than remembered on this device —
 * a licence typed on a laptop has to be there on the phone too.
 */
const vettingOf = x => ({
  category: x.category ?? '',
  allowsChildren: x.allowsChildren ?? false,
  licenceName: x.licenceName ?? '',
  licenceExpiresOn: x.licenceExpiresOn ?? '',
  insurancePolicy: x.insurancePolicy ?? '',
  insuranceExpiresOn: x.insuranceExpiresOn ?? '',
  safetyPlan: x.safetyPlan ?? '',
  emergencyPhone: x.emergencyPhone ?? ''
});

/** docs/09 TN-A — how long the reviewer has once it is submitted. */
const REVIEW_WORKING_DAYS = 5;

export function ExperienceEditor() {
  const state = useStore();
  const editing = state.editingExperience;

  const [form, setForm] = useState(() => {
    if (!editing) return { ...BLANK };
    return {
      ...BLANK,
      id: editing.id,
      title: editing.title ?? '',
      city: editing.city ?? '',
      summary: editing.summary ?? '',
      description: editing.description ?? '',
      durationMinutes: editing.durationMinutes ?? 120,
      maxGroup: editing.maxGroup ?? 10,
      minGuests: editing.minGuests ?? 2,
      languages: (editing.languages ?? ['vi']).join(', '),
      minAge: editing.minAge ?? 0,
      meetingPoint: editing.meetingPoint ?? '',
      latitude: editing.latitude ?? null,
      longitude: editing.longitude ?? null,
      included: (editing.included ?? []).join('\n'),
      itinerary: (editing.itinerary ?? []).map(step => ({ ...step })),
      pricePerPerson: editing.pricePerPerson ?? 0,
      privateGroupPrice: editing.privateGroupPrice ?? '',
      images: editing.images ?? [],
      ...vettingOf(editing)
    };
  });

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  const field = (key, value) => setForm(f => ({ ...f, [key]: value }));
  const num = (key, value) => field(key, Number(value) || 0);

  const setSteps = next => setForm(f => ({ ...f, itinerary: next }));
  const addStep = () => setSteps([...form.itinerary, { title: '', description: '', imageUrl: '' }]);
  const removeStep = i => setSteps(form.itinerary.filter((_, k) => k !== i));
  const editStep = (i, patch) =>
    setSteps(form.itinerary.map((step, k) => (k === i ? { ...step, ...patch } : step)));
  // Reordering is the ordinary edit here: a host writes the stops down as they
  // remember them, then puts them in the order they happen.
  const moveStep = (i, by) => {
    const next = [...form.itinerary];
    const [step] = next.splice(i, 1);
    next.splice(i + by, 0, step);
    setSteps(next);
  };

  const risk = riskOf(form.category, form.allowsChildren);
  const missing = publishBlockers(form, todayIso());

  const persist = async publish => {
    setSaving(true);
    setError(null);

    const body = {
      id: form.id || null,
      title: form.title.trim(),
      city: form.city.trim(),
      summary: form.summary.trim(),
      description: form.description.trim(),
      durationMinutes: Number(form.durationMinutes) || 0,
      maxGroup: Number(form.maxGroup) || 0,
      minGuests: Number(form.minGuests) || 0,
      languages: form.languages.split(',').map(s => s.trim()).filter(Boolean),
      minAge: Number(form.minAge) || 0,
      meetingPoint: form.meetingPoint.trim(),
      latitude: Number(form.latitude) || 0,
      longitude: Number(form.longitude) || 0,
      included: form.included.split('\n').map(s => s.trim()).filter(Boolean),
      // docs/01 MR-01 — a stop with no title is a row the host started and
      // abandoned, so it is dropped rather than saved half-written.
      itinerary: form.itinerary
        .filter(step => step.title.trim())
        .map(step => ({
          title: step.title.trim(),
          description: (step.description ?? '').trim(),
          imageUrl: (step.imageUrl ?? '').trim() || null
        })),
      pricePerPerson: Number(form.pricePerPerson) || 0,
      privateGroupPrice: form.privateGroupPrice === '' || form.privateGroupPrice == null
        ? null : Number(form.privateGroupPrice),
      images: form.images,
      publish,
      // docs/09 §2.1–§2.3 — sent whatever the band, so switching category back
      // and forth never silently drops paperwork the host already typed.
      category: form.category || null,
      allowsChildren: form.allowsChildren,
      licenceName: form.licenceName.trim() || null,
      licenceExpiresOn: form.licenceExpiresOn || null,
      insurancePolicy: form.insurancePolicy.trim() || null,
      insuranceExpiresOn: form.insuranceExpiresOn || null,
      safetyPlan: form.safetyPlan.trim() || null,
      emergencyPhone: form.emergencyPhone.trim() || null
    };

    try {
      const saved = await api.saveExperience(body);
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

  // docs/09 §2.2 (MR-E-03) — this is not a publish button. It puts the activity
  // in a queue a person works through; nothing goes on sale until they approve.
  const submit = async () => {
    if (missing.length) {
      setError(`${t('Còn thiếu:')} ${missing.map(m => t(m)).join(', ')}.`);
      return;
    }
    const saved = await persist(true);
    if (saved) {
      toast(t('Đã gửi trải nghiệm đi duyệt. Staylio trả lời trong 5 ngày làm việc.'));
      closeOverlay();
    }
  };

  return (
    <Modal title={form.id ? t('Chỉnh sửa trải nghiệm') : t('Đăng trải nghiệm mới')} size="wide" foot={<>
      <button className="text-btn" onClick={saveDraft} disabled={saving}>{t('Lưu nháp & thoát')}</button>
      <button className="btn btn-primary btn-sm" onClick={submit} disabled={saving || !!missing.length}>
        {saving ? t('Đang gửi…') : t('Gửi duyệt')}
      </button>
    </>}>
      {error && <div className="form-error">{error}</div>}

      <section className="modal-section">
        <h3>{t('Trải nghiệm của bạn')}</h3>
        <label className="form-field">
          <span className="cap">{t('Tên trải nghiệm *')}</span>
          <input value={form.title} maxLength={80} onChange={e => field('title', e.target.value)}
                 placeholder={t('Chèo kayak rừng dừa Bảy Mẫu lúc bình minh')} required />
        </label>
        <div className="grid-2">
          <label className="form-field"><span className="cap">{t('Thành phố *')}</span>
            <input value={form.city} onChange={e => field('city', e.target.value)} placeholder="Hội An" required /></label>
          <label className="form-field"><span className="cap">{t('Điểm hẹn *')}</span>
            <input value={form.meetingPoint} onChange={e => field('meetingPoint', e.target.value)}
                   placeholder={t('Bến thuyền Cẩm Thanh, cổng số 2')} /></label>
        </div>
        <label className="form-field">
          <span className="cap">{t('Giới thiệu ngắn')}</span>
          <input value={form.summary} maxLength={160} onChange={e => field('summary', e.target.value)}
                 placeholder={t('Một câu khách đọc trên thẻ trải nghiệm.')} />
        </label>
        <label className="form-field">
          <span className="cap">{t('Lịch trình theo mốc thời gian *')}</span>
          <textarea rows={5} value={form.description} onChange={e => field('description', e.target.value)}
                    placeholder={t('06:00 gặp nhau ở bến · 06:30 xuống thuyền · 08:00 ăn sáng tại nhà dân · 09:30 kết thúc.')}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>
        <label className="form-field">
          <span className="cap">{t('Vé bao gồm — mỗi dòng một mục')}</span>
          <textarea rows={3} value={form.included} onChange={e => field('included', e.target.value)}
                    placeholder={'Thuyền và áo phao\nHướng dẫn viên nói tiếng Anh\nNước uống'}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>

        {/*
          * docs/01 MR-01 — the running order. A list of stops rather than one more
          * textarea because each stop carries three things, and somebody deciding
          * whether to give up a day reads the order before they read the prose.
          * Leaving it empty is fine — the page then has no itinerary section.
          */}
        <div className="form-field">
          <span className="cap">{t('Hành trình')}</span>
          <div className="xp-step-editor">
            {form.itinerary.map((step, i) => (
              <div className="xp-step-row" key={i}>
                <span className="xp-step-no">{i + 1}</span>
                <div className="xp-step-fields">
                  <input value={step.title} maxLength={120}
                         placeholder={t('Ví dụ: Xuống địa đạo')}
                         onChange={e => editStep(i, { title: e.target.value })} />
                  <input value={step.description ?? ''} maxLength={400}
                         placeholder={t('Một câu về mốc này')}
                         onChange={e => editStep(i, { description: e.target.value })} />
                  <input value={step.imageUrl ?? ''} maxLength={600}
                         placeholder={t('Đường dẫn ảnh (không bắt buộc)')}
                         onChange={e => editStep(i, { imageUrl: e.target.value })} />
                </div>
                <div className="xp-step-tools">
                  <button type="button" className="text-btn" disabled={i === 0}
                          onClick={() => moveStep(i, -1)} aria-label={t('Lên trên')}>↑</button>
                  <button type="button" className="text-btn" disabled={i === form.itinerary.length - 1}
                          onClick={() => moveStep(i, 1)} aria-label={t('Xuống dưới')}>↓</button>
                  <button type="button" className="text-btn" onClick={() => removeStep(i)}>{t('Bỏ')}</button>
                </div>
              </div>
            ))}
          </div>
          <button type="button" className="btn btn-outline btn-sm" style={{ marginTop: 10 }}
                  onClick={addStep}>{t('+ Thêm mốc')}</button>
        </div>

      </section>

      {/* docs/09 §2.1 (MR-E-01) — the activity, and with it the risk band. */}
      <section className="modal-section">
        <h3>{t('Đây là hoạt động gì?')}</h3>
        <span className="hint">
          {t('Loại hoạt động quyết định mức rủi ro, và mức rủi ro quyết định giấy tờ bạn phải nộp. Chọn đúng việc mình thật sự làm.')}
        </span>

        <label className="form-field" style={{ marginTop: 14 }}>
          <span className="cap">{t('Loại hoạt động *')}</span>
          <select value={form.category} onChange={e => field('category', e.target.value)}>
            <option value="" disabled>{t('— Chọn loại hoạt động —')}</option>
            <optgroup label={t('Rủi ro thấp')}>
              {CATEGORIES.filter(([k]) => riskOf(k, false) === 'low')
                .map(([k, label]) => <option key={k} value={k}>{t(label)}</option>)}
            </optgroup>
            <optgroup label={t('Rủi ro trung bình')}>
              {CATEGORIES.filter(([k]) => riskOf(k, false) === 'medium')
                .map(([k, label]) => <option key={k} value={k}>{t(label)}</option>)}
            </optgroup>
            <optgroup label={t('Rủi ro cao')}>
              {CATEGORIES.filter(([k]) => riskOf(k, false) === 'high')
                .map(([k, label]) => <option key={k} value={k}>{t(label)}</option>)}
            </optgroup>
          </select>
        </label>

        <label className="check-row" style={{ marginTop: 4 }}>
          <input type="checkbox" checked={form.allowsChildren}
                 onChange={e => field('allowsChildren', e.target.checked)} />
          <span>{t('Có trẻ em tham gia')}</span>
        </label>
        <p className="field-note" style={{ marginTop: 8 }}>
          {t('Trẻ em tham gia một hoạt động rủi ro trung bình sẽ đẩy nó lên mức rủi ro cao.')}
        </p>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', marginTop: 10 }}>
          <span className="badge" style={RISK_STYLE[risk]}>{t(RISK_LABEL[risk])}</span>
          <span style={{ fontSize: 13, color: 'var(--ink-muted)' }}>{t(RISK_NOTE[risk])}</span>
        </div>
      </section>

      {/* docs/09 §2.2–§2.3 — only what this band actually demands is asked for. */}
      {risk !== 'low' && (
        <section className="modal-section">
          <h3>{t('Hồ sơ an toàn')}</h3>
          <span className="hint">
            {risk === 'high'
              ? t('Mức rủi ro cao: thiếu bất kỳ giấy tờ nào là không gửi duyệt được, giấy hết hạn tính như không có.')
              : t('Mức rủi ro trung bình: cần một bản cam kết an toàn và phần dặn dò khách trước khi bắt đầu.')}
          </span>

          <label className="form-field" style={{ marginTop: 14 }}>
            <span className="cap">{t('Cam kết an toàn và hướng dẫn trước khi bắt đầu *')}</span>
            <textarea rows={4} value={form.safetyPlan} onChange={e => field('safetyPlan', e.target.value)}
                      placeholder={t('Khách được phát áo phao và nghe dặn dò 10 phút trước khi xuống thuyền. Gặp sự cố thì tấp vào bờ phía nào, gọi ai.')}
                      style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
          </label>

          {risk === 'high' && <>
            <div className="grid-2">
              <label className="form-field"><span className="cap">{t('Giấy phép hành nghề *')}</span>
                <input value={form.licenceName} onChange={e => field('licenceName', e.target.value)}
                       placeholder="PADI Divemaster #123456" /></label>
              <label className="form-field"><span className="cap">{t('Giấy phép hết hạn ngày')}</span>
                <input type="date" value={form.licenceExpiresOn}
                       onChange={e => field('licenceExpiresOn', e.target.value)} /></label>
            </div>
            <div className="grid-2">
              <label className="form-field"><span className="cap">{t('Bảo hiểm trách nhiệm *')}</span>
                <input value={form.insurancePolicy} onChange={e => field('insurancePolicy', e.target.value)}
                       placeholder={t('Bảo Việt — hợp đồng TNDS 2026/0912')} /></label>
              <label className="form-field"><span className="cap">{t('Bảo hiểm hết hạn ngày')}</span>
                <input type="date" value={form.insuranceExpiresOn}
                       onChange={e => field('insuranceExpiresOn', e.target.value)} /></label>
            </div>
            <label className="form-field"><span className="cap">{t('Số điện thoại khẩn cấp *')}</span>
              <input type="tel" value={form.emergencyPhone} onChange={e => field('emergencyPhone', e.target.value)}
                     placeholder="0912 345 678" /></label>
          </>}
        </section>
      )}

      <section className="modal-section">
        <h3>{t('Suất chạy thế nào')}</h3>
        <div className="field-grid">
          <label className="form-field"><span className="cap">{t('Thời lượng (phút)')}</span>
            <input type="number" min={30} max={1440} step={15} value={form.durationMinutes}
                   onChange={e => num('durationMinutes', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Nhóm tối đa')}</span>
            <input type="number" min={1} max={50} value={form.maxGroup}
                   onChange={e => num('maxGroup', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Tối thiểu để chạy suất')}</span>
            <input type="number" min={1} max={form.maxGroup} value={form.minGuests}
                   onChange={e => num('minGuests', e.target.value)} /></label>
          <label className="form-field"><span className="cap">{t('Tuổi tối thiểu')}</span>
            <input type="number" min={0} max={21} value={form.minAge}
                   onChange={e => num('minAge', e.target.value)} /></label>
        </div>
        <label className="form-field">
          <span className="cap">{t('Ngôn ngữ dẫn — cách nhau bằng dấu phẩy')}</span>
          <input value={form.languages} onChange={e => field('languages', e.target.value)} placeholder="vi, en" />
        </label>
      </section>

      <section className="modal-section">
        <h3>{t('Giá vé')}</h3>
        <div className="grid-2">
          <label className="form-field"><span className="cap">{t('Giá mỗi người (₫) *')}</span>
            <input type="number" min={0} step={10000} value={form.pricePerPerson}
                   onChange={e => num('pricePerPerson', e.target.value)} required /></label>
          <label className="form-field"><span className="cap">{t('Giá trọn nhóm riêng (₫)')}</span>
            <input type="number" min={0} step={10000} value={form.privateGroupPrice}
                   placeholder={t('Bỏ trống nếu không nhận nhóm riêng')}
                   onChange={e => field('privateGroupPrice', e.target.value)} /></label>
        </div>
      </section>

      <ExperiencePhotos form={form} setForm={setForm} />

      {/* docs/09 §2.2 — the same list the server checks, read before it is run. */}
      <section className="modal-section">
        <h3>{t('Trước khi gửi duyệt')}</h3>
        {missing.length ? (
          <div className="notice notice-warn">
            <b>{t('Còn thiếu:')}</b>
            <ul style={{ margin: '6px 0 0', paddingLeft: 18, lineHeight: 1.8 }}>
              {missing.map(m => <li key={m}>{t(m)}</li>)}
            </ul>
          </div>
        ) : (
          <div className="notice notice-ok">{t('Đã đủ hồ sơ cho mức rủi ro này. Bạn gửi duyệt được.')}</div>
        )}
        <p className="field-note" style={{ marginTop: 12 }}>
          {t('Gửi duyệt không phải là mở bán. Trải nghiệm vào hàng chờ để một người của Staylio xem, thường trong')}
          {' '}{REVIEW_WORKING_DAYS} {t('ngày làm việc. Chỉ khi được duyệt thì mới bán vé được.')}
        </p>
      </section>
    </Modal>
  );
}

/** docs/09 §2.1 — real photos of the activity; the server refuses a submission without one. */
function ExperiencePhotos({ form, setForm }) {
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
      <h3>{t('Ảnh hoạt động')}</h3>
      <span className="hint">{t('Ít nhất một ảnh thật của chính buổi bạn dẫn. Ảnh đầu tiên là ảnh bìa.')}</span>

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
