import { useEffect, useState } from 'react';
import { api } from '../lib/api.js';
import { toast } from '../lib/store.js';
import { money } from '../lib/format.js';
import { t } from '../lib/i18n.js';

/*
 * A hotel's kinds of room, managed by its host: name, how many, capacity,
 * price, and the two rate plans. The server refuses any field by name
 * (RoomTypeRules) and keeps the listing's own price at its cheapest room.
 */
const BLANK = {
  name: '', summary: '', inventory: 1, maxGuests: 2, beds: 1, sizeSqm: 0, pricePerNight: 0,
  imageUrl: '', features: '', nonRefundableDiscountPercent: 0, breakfastPricePerGuest: 0
};

function RoomForm({ value, onChange }) {
  const set = (k, v) => onChange({ ...value, [k]: v });
  const num = k => e => set(k, Number(e.target.value) || 0);
  return (
    <div className="field-grid" style={{ marginTop: 10 }}>
      <label className="form-field"><span className="cap">{t('Tên loại phòng')}</span>
        <input type="text" maxLength={80} value={value.name} onChange={e => set('name', e.target.value)} /></label>
      <label className="form-field"><span className="cap">{t('Mô tả ngắn')}</span>
        <input type="text" maxLength={200} value={value.summary} onChange={e => set('summary', e.target.value)} /></label>
      <label className="form-field"><span className="cap">{t('Số phòng loại này')}</span>
        <input type="number" min={1} max={500} value={value.inventory} onChange={num('inventory')} /></label>
      <label className="form-field"><span className="cap">{t('Khách tối đa mỗi phòng')}</span>
        <input type="number" min={1} max={20} value={value.maxGuests} onChange={num('maxGuests')} /></label>
      <label className="form-field"><span className="cap">{t('Số giường')}</span>
        <input type="number" min={1} max={10} value={value.beds} onChange={num('beds')} /></label>
      <label className="form-field"><span className="cap">{t('Diện tích (m²)')}</span>
        <input type="number" min={0} max={1000} value={value.sizeSqm} onChange={num('sizeSqm')} /></label>
      <label className="form-field"><span className="cap">{t('Giá mỗi đêm (₫)')}</span>
        <input type="number" min={50000} step={10000} value={value.pricePerNight} onChange={num('pricePerNight')} /></label>
      <label className="form-field"><span className="cap">{t('Giảm cho giá không hoàn tiền (%)')}</span>
        <input type="number" min={0} max={50} value={value.nonRefundableDiscountPercent} onChange={num('nonRefundableDiscountPercent')} /></label>
      <label className="form-field"><span className="cap">{t('Bữa sáng mỗi khách mỗi đêm (₫)')}</span>
        <input type="number" min={0} step={10000} value={value.breakfastPricePerGuest} onChange={num('breakfastPricePerGuest')} /></label>
      <label className="form-field"><span className="cap">{t('Ảnh (đường dẫn https)')}</span>
        <input type="url" value={value.imageUrl ?? ''} onChange={e => set('imageUrl', e.target.value)} /></label>
      <label className="form-field" style={{ gridColumn: '1 / -1' }}>
        <span className="cap">{t('Tiện nghi riêng của phòng (mỗi dòng một món)')}</span>
        <textarea className="field" rows={3} value={value.features ?? ''} onChange={e => set('features', e.target.value)} />
      </label>
    </div>
  );
}

export function HotelRoomsManager({ hotels }) {
  const [rooms, setRooms] = useState(null);
  const [edits, setEdits] = useState({});
  const [adding, setAdding] = useState(null); // listing id with an open "add" form
  const [draft, setDraft] = useState(BLANK);
  const [busy, setBusy] = useState(false);

  const load = () => api.hostRoomTypes().then(setRooms).catch(() => setRooms([]));
  useEffect(() => { load(); }, []);

  if (!hotels.length || !rooms) return null;

  const body = (listingId, v) => ({ ...v, listingId, imageUrl: v.imageUrl?.trim() || null });
  const run = async (fn, done) => {
    setBusy(true);
    try { await fn(); await load(); toast(done); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <div style={{ marginTop: 32 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Loại phòng khách sạn')}</h2>
      <p className="section-sub">{t('Giá hiển thị của khách sạn là giá của loại phòng rẻ nhất. Đơn đã đặt giữ nguyên giá và gói lúc đặt.')}</p>
      {hotels.map(h => {
        const mine = rooms.filter(r => r.listingId === h.id);
        return (
          <section key={h.id} style={{ marginTop: 20 }}>
            <h3 style={{ margin: '0 0 8px', fontSize: 16, fontWeight: 800 }}>{h.title}</h3>
            {!mine.length && <p className="meta">{t('Chưa có loại phòng nào — khách chưa đặt theo phòng được.')}</p>}
            <div style={{ display: 'grid', gap: 12 }}>
              {mine.map(r => {
                const v = edits[r.id];
                return (
                  <article className="host-booking" key={r.id}>
                    <div style={{ minWidth: 0, flexBasis: '100%' }}>
                      <h3>{r.name}</h3>
                      <div className="meta">
                        {r.inventory} {t('phòng')} · {t('tối đa {} khách').replace('{}', r.maxGuests)} · {money(r.pricePerNight)} / {t('đêm')}
                      </div>
                      {v ? <>
                        <RoomForm value={v} onChange={nv => setEdits(e => ({ ...e, [r.id]: nv }))} />
                        <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                          <button className="btn btn-primary btn-sm" disabled={busy}
                                  onClick={() => run(async () => {
                                    await api.updateRoomType(r.id, body(h.id, v));
                                    setEdits(e => ({ ...e, [r.id]: undefined }));
                                  }, t('Đã lưu loại phòng.'))}>{t('Lưu')}</button>
                          <button className="btn btn-outline btn-sm" disabled={busy}
                                  onClick={() => setEdits(e => ({ ...e, [r.id]: undefined }))}>{t('Huỷ')}</button>
                        </div>
                      </> : (
                        <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                          <button className="btn btn-outline btn-sm"
                                  onClick={() => setEdits(e => ({ ...e, [r.id]: { ...BLANK, ...r, imageUrl: r.imageUrl ?? '' } }))}>
                            {t('Sửa')}
                          </button>
                          <button className="btn btn-outline btn-sm" disabled={busy}
                                  onClick={() => {
                                    if (!confirm(t('Xoá loại phòng này?'))) return;
                                    run(() => api.deleteRoomType(r.id), t('Đã xoá loại phòng.'));
                                  }}>{t('Xoá')}</button>
                        </div>
                      )}
                    </div>
                  </article>
                );
              })}
            </div>
            {adding === h.id ? (
              <div style={{ marginTop: 12 }}>
                <RoomForm value={draft} onChange={setDraft} />
                <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                  <button className="btn btn-primary btn-sm" disabled={busy}
                          onClick={() => run(async () => {
                            await api.createRoomType(body(h.id, draft));
                            setDraft(BLANK);
                            setAdding(null);
                          }, t('Đã thêm loại phòng.'))}>{t('Thêm')}</button>
                  <button className="btn btn-outline btn-sm" onClick={() => setAdding(null)}>{t('Huỷ')}</button>
                </div>
              </div>
            ) : (
              <button className="btn btn-outline btn-sm" style={{ marginTop: 12 }}
                      onClick={() => { setDraft(BLANK); setAdding(h.id); }}>{t('+ Thêm loại phòng')}</button>
            )}
          </section>
        );
      })}
    </div>
  );
}
