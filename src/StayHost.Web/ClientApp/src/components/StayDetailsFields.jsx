import { t } from '../lib/i18n.js';

/*
 * What the guest tells the host about the stay: roughly when they arrive, who
 * is staying when it is not the booker, a work trip, and special requests.
 * The keys mirror StayDetails.Requests on the server, which refuses any other.
 */
export const SPECIAL_REQUESTS = [
  ['quiet-room', 'Phòng yên tĩnh'],
  ['high-floor', 'Tầng cao'],
  ['crib', 'Cũi cho em bé'],
  ['extra-bed', 'Giường phụ'],
  ['early-check-in', 'Nhận phòng sớm'],
  ['late-check-out', 'Trả phòng muộn'],
  ['accessible', 'Lối đi cho xe lăn'],
  ['airport-pickup', 'Đón tại sân bay']
];

const pad = h => String(h).padStart(2, '0');

export const emptyStayDetails = () => ({
  estimatedArrivalHour: null, forSomeoneElse: false, stayingGuestName: '',
  isBusinessTrip: false, specialRequests: []
});

/** From the server's StayDetailsDto into the form's shape. */
export const stayDetailsFrom = d => ({
  estimatedArrivalHour: d?.estimatedArrivalHour ?? null,
  forSomeoneElse: !!d?.stayingGuestName,
  stayingGuestName: d?.stayingGuestName ?? '',
  isBusinessTrip: !!d?.isBusinessTrip,
  specialRequests: d?.specialRequests ?? []
});

/** The form's shape into what the API takes. */
export const stayDetailsBody = v => ({
  estimatedArrivalHour: v.estimatedArrivalHour,
  stayingGuestName: v.forSomeoneElse ? (v.stayingGuestName.trim() || null) : null,
  isBusinessTrip: !!v.isBusinessTrip,
  specialRequests: v.specialRequests
});

export function StayDetailsFields({ value, onChange }) {
  const patch = p => onChange({ ...value, ...p });
  const toggle = key => patch({
    specialRequests: value.specialRequests.includes(key)
      ? value.specialRequests.filter(k => k !== key)
      : [...value.specialRequests, key]
  });

  return (
    <div>
      <label className="form-field">
        <span className="cap">{t('Giờ đến dự kiến')} <span style={{ fontWeight: 400 }}>{t('(không bắt buộc)')}</span></span>
        <select value={value.estimatedArrivalHour ?? ''}
                onChange={e => patch({ estimatedArrivalHour: e.target.value === '' ? null : Number(e.target.value) })}>
          <option value="">{t('Chưa rõ')}</option>
          {Array.from({ length: 24 }, (_, h) => (
            <option key={h} value={h}>{pad(h)}:00 – {pad((h + 1) % 24)}:00</option>
          ))}
        </select>
      </label>

      <label className="check-row" style={{ marginBottom: 10 }}>
        <input type="checkbox" checked={value.forSomeoneElse}
               onChange={e => patch({ forSomeoneElse: e.target.checked })} />
        <span>{t('Tôi đặt cho người khác')}</span>
      </label>
      {value.forSomeoneElse && (
        <label className="form-field">
          <span className="cap">{t('Họ tên người lưu trú')}</span>
          <input type="text" maxLength={120} value={value.stayingGuestName}
                 onChange={e => patch({ stayingGuestName: e.target.value })} />
        </label>
      )}

      <label className="check-row" style={{ marginBottom: 14 }}>
        <input type="checkbox" checked={value.isBusinessTrip}
               onChange={e => patch({ isBusinessTrip: e.target.checked })} />
        <span>{t('Đây là chuyến công tác')}</span>
      </label>

      <div className="cap" style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--ink-muted)', marginBottom: 6 }}>
        {t('Yêu cầu đặc biệt')} <span style={{ fontWeight: 400 }}>{t('(chủ nhà sẽ cố gắng đáp ứng, không bảo đảm)')}</span>
      </div>
      <div className="pill-row">
        {SPECIAL_REQUESTS.map(([key, label]) => (
          <button type="button" key={key} aria-pressed={value.specialRequests.includes(key)}
                  className={`pill ${value.specialRequests.includes(key) ? 'is-on' : ''}`}
                  onClick={() => toggle(key)}>{t(label)}</button>
        ))}
      </div>
    </div>
  );
}

/** Read-only lines, for the host's booking row and the guest's trip page. */
export function StayDetailsSummary({ details, style }) {
  if (!details) return null;
  const lines = [];
  if (details.arrivalLabel) lines.push([t('Giờ đến dự kiến'), details.arrivalLabel]);
  if (details.stayingGuestName) lines.push([t('Người lưu trú'), details.stayingGuestName]);
  if (details.isBusinessTrip) lines.push([t('Mục đích'), t('Công tác')]);
  if (details.specialRequestLabels?.length) {
    lines.push([t('Yêu cầu đặc biệt'), details.specialRequestLabels.map(l => t(l)).join(', ')]);
  }
  if (!lines.length) return null;
  return (
    <div style={style}>
      {lines.map(([k, v]) => <div className="meta" key={k}>{k}: <b style={{ color: 'var(--ink)' }}>{v}</b></div>)}
    </div>
  );
}
