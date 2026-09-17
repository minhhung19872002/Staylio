import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { set, loadFavorites, loadWishlists, openWishlist, toast } from '../lib/store.js';
import { api } from '../lib/api.js';
import { money } from '../lib/format.js';
import { Card } from '../components/Card.jsx';
import { CardsMap } from '../components/Maps.jsx';
import { Icon } from '../components/Icon.jsx';
import { t } from '../lib/i18n.js';

/** docs/01 YT-05 — turn a shareable link on/off and copy it. */
function ShareWishlist({ list, onChanged }) {
  const shared = !!list.shareToken;

  const toggle = async () => {
    try {
      await api.shareWishlist(list.id, !shared);
      onChanged?.();
      if (!shared) toast('Đã bật chia sẻ. Sao chép liên kết để gửi bạn bè.');
    } catch (err) { toast(err.message); }
  };

  const copy = async () => {
    const url = `${location.origin}/wishlist/${list.shareToken}`;
    try { await navigator.clipboard.writeText(url); toast('Đã sao chép liên kết chia sẻ.'); }
    catch { toast(url); }
  };

  return (
    <>
      <button className="btn btn-outline btn-sm" onClick={toggle}>
        {shared ? t('Tắt chia sẻ') : t('Chia sẻ')}
      </button>
      {shared && <button className="btn btn-dark btn-sm" onClick={copy}>{t('Sao chép liên kết')}</button>}
    </>
  );
}

/** docs/01 YT-03 — a private note the guest keeps on a saved place. */
function WishlistNote({ listId, listingId, note, onSaved }) {
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(note ?? '');

  const save = async () => {
    try {
      await api.setWishlistNote(listId, listingId, text.trim() || null);
      setEditing(false);
      onSaved?.();
    } catch (err) { toast(err.message); }
  };

  if (!editing) {
    return (
      <button className="wishlist-note-btn" onClick={() => { setText(note ?? ''); setEditing(true); }}>
        {note ? `📝 ${note}` : `+ ${t('Thêm ghi chú')}`}
      </button>
    );
  }

  return (
    <div className="wishlist-note-edit">
      <textarea rows={2} value={text} maxLength={500} autoFocus
                placeholder={t('Ghi chú cho riêng bạn về chỗ này…')}
                onChange={e => setText(e.target.value)} />
      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', marginTop: 6 }}>
        <button className="btn btn-outline btn-sm" onClick={() => setEditing(false)}>{t('Huỷ')}</button>
        <button className="btn btn-dark btn-sm" onClick={save}>{t('Lưu')}</button>
      </div>
    </div>
  );
}

export function Wishlists() {
  const state = useStore();

  useEffect(() => {
    set({ activeWishlist: null });
    loadFavorites();
    loadWishlists();
  }, []);

  return state.activeWishlist ? <One /> : <Index />;
}

function Index() {
  const state = useStore();
  const navigate = useNavigate();
  const lists = state.wishlists ?? [];

  const create = async () => {
    const name = prompt('Tên danh sách mới', 'Chuyến đi sắp tới');
    if (!name?.trim()) return;
    try { await api.createWishlist(name.trim()); await loadWishlists(); toast('Đã tạo danh sách.'); }
    catch (err) { toast(err.message); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px' }}>
      <div className="page-head">
        <div>
          <h1 className="section-title">{t('Danh sách yêu thích')}</h1>
          <p className="section-sub">
            {lists.length} {t('danh sách')} · {lists.reduce((n, l) => n + l.count, 0)} {t('chỗ nghỉ đã lưu')}
          </p>
        </div>
        <button className="btn btn-outline btn-sm" onClick={create}>+ {t('Tạo danh sách')}</button>
      </div>

      {lists.length ? (
        <div className="wl-grid">
          {lists.map(list => (
            <article className="wl-card" key={list.id} onClick={() => {
              openWishlist(list.id);
              window.scrollTo({ top: 0, behavior: 'instant' });
            }}>
              <div className="wl-cover">
                {list.coverImages?.length
                  ? list.coverImages.slice(0, 4).map((url, i) => <img src={url} alt="" key={i} loading="lazy" decoding="async" />)
                  : <div className="wl-empty"><Icon name="heart" size={28} /></div>}
              </div>
              <div className="wl-body">
                <b>{t(list.name)}</b>
                <span>{list.count} {t('chỗ nghỉ')}{list.isDefault ? ` · ${t('mặc định')}` : ''}</span>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="empty-state" style={{ marginTop: 24 }}>
          <h3>{t('Chưa lưu chỗ nghỉ nào')}</h3>
          <p>{t('Nhấn ♥ trên bất kỳ chỗ nghỉ để lưu lại đây.')}</p>
          <button className="btn btn-primary" style={{ marginTop: 18 }} onClick={() => navigate('/')}>{t('Khám phá chỗ nghỉ')}</button>
        </div>
      )}
    </div>
  );
}

/** docs/01 YT-07 — a side-by-side comparison of 2–5 listings. */
function CompareTable({ cards, onClose, navigate }) {
  const rows = [
    ['Giá / đêm', c => money(c.pricePerNight)],
    ['Đánh giá', c => c.reviewCount ? `★ ${c.rating.toFixed(2)} (${c.reviewCount})` : t('Chưa có')],
    ['Thành phố', c => c.city],
    ['Loại', c => t(c.roomTypeLabel)],
    ['Khách tối đa', c => `${c.maxGuests}`],
    ['Phòng ngủ', c => `${c.bedrooms}`],
    ['Giường', c => `${c.beds}`],
    ['Phòng tắm', c => `${c.bathrooms}`],
    ['Đặt ngay', c => c.instantBook ? '✓' : '—'],
    ['Siêu chủ nhà', c => c.isSuperhost ? '✓' : '—'],
    ['Phí dọn dẹp', c => money(c.cleaningFee)]
  ];

  return (
    <section style={{ marginTop: 20, marginBottom: 8 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
        <h2 className="section-title" style={{ fontSize: 18 }}>{t('So sánh')} {cards.length} {t('chỗ')}</h2>
        <button className="text-btn" onClick={onClose}>{t('Đóng')}</button>
      </div>
      <div className="table-wrap">
        <table className="admin-table compare-table">
          <thead>
            <tr>
              <th />
              {cards.map(c => (
                <th key={c.id} style={{ minWidth: 150 }}>
                  <button className="text-btn" style={{ fontWeight: 700, textAlign: 'left' }}
                          onClick={() => navigate(`/rooms/${c.slug}`)}>{c.title}</button>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(([label, fn]) => (
              <tr key={label}>
                <td style={{ fontWeight: 600, color: 'var(--ink-muted)' }}>{t(label)}</td>
                {cards.map(c => <td key={c.id}>{fn(c)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/**
 * docs/01 YT-01 — many lists, so a saved place has to be able to move between
 * them. The endpoint has always accepted it (an item already saved just changes
 * list); nothing offered it, so the only way to reorganise was unsave and save
 * again from the listing page, which threw away the note of YT-03 on the way.
 */
function MoveToList({ listId, listingId, lists }) {
  const others = (lists ?? []).filter(l => l.id !== listId);
  if (!others.length) return null;

  const move = async id => {
    if (!id) return;
    try {
      await api.moveToWishlist(Number(id), listingId);
      await Promise.all([openWishlist(listId), loadWishlists()]);
      toast('Đã chuyển sang danh sách khác.');
    } catch (err) { toast(err.message); }
  };

  return (
    <select className="wishlist-note-btn" value="" aria-label={t('Chuyển sang danh sách')}
            onChange={e => move(e.target.value)}>
      <option value="">↦ {t('Chuyển sang danh sách')}</option>
      {others.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}
    </select>
  );
}

function One() {
  const state = useStore();
  const navigate = useNavigate();
  const detail = state.activeWishlist;
  const list = detail.list;
  const [compare, setCompare] = useState(null);   // docs/01 YT-07
  const [showMap, setShowMap] = useState(false);  // docs/01 YT-04

  // docs/01 YT-07 — compare up to five saved places side by side.
  const openCompare = async () => {
    const ids = detail.items.slice(0, 5).map(e => e.card.id);
    if (ids.length < 2) { toast('Cần ít nhất 2 chỗ để so sánh.'); return; }
    try { setCompare(await api.compareListings(ids)); }
    catch (err) { toast(err.message); }
  };

  const rename = async () => {
    const name = prompt('Tên danh sách', list.name);
    if (!name?.trim() || name.trim() === list.name) return;
    try { await api.renameWishlist(list.id, name.trim()); await openWishlist(list.id); toast('Đã đổi tên.'); }
    catch (err) { toast(err.message); }
  };

  const remove = async () => {
    if (!confirm('Xoá danh sách này? Các chỗ nghỉ trong đó cũng bị bỏ lưu.')) return;
    try {
      await api.deleteWishlist(list.id);
      set({ activeWishlist: null });
      await Promise.all([loadWishlists(), loadFavorites()]);
      toast('Đã xoá danh sách.');
    } catch (err) { toast(err.message); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '26px 90px' }}>
      <button className="back-link" onClick={() => { set({ activeWishlist: null }); loadWishlists(); }}>
        ← {t('Tất cả danh sách')}
      </button>

      <div className="page-head" style={{ marginTop: 8 }}>
        <div>
          <h1 className="section-title">{list.name}</h1>
          <p className="section-sub">{list.count} {t('chỗ nghỉ')}</p>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button className="btn btn-outline btn-sm" onClick={rename}>{t('Đổi tên')}</button>
          {detail.items.length >= 2 && (
            <button className="btn btn-outline btn-sm" onClick={openCompare}>{t('So sánh')}</button>
          )}
          <ShareWishlist list={list} onChanged={() => openWishlist(list.id)} />
          {!list.isDefault && <button className="btn btn-outline btn-sm" onClick={remove}>{t('Xoá danh sách')}</button>}
        </div>
      </div>

      {compare && <CompareTable cards={compare} onClose={() => setCompare(null)} navigate={navigate} />}

      {/* docs/01 YT-04 — where the saved places actually are. Off by default:
          a list of two in the same street learns nothing from a map, and the
          tiles are a network round trip nobody asked for. */}
      {detail.items.length > 0 && (
        <div style={{ marginTop: 20 }}>
          <button className="text-btn" onClick={() => setShowMap(v => !v)}>
            {showMap ? t('Ẩn bản đồ') : t('Xem trên bản đồ')}
          </button>
          {showMap && (
            <div style={{ marginTop: 12 }}>
              <CardsMap cards={detail.items.map(e => e.card)} />
            </div>
          )}
        </div>
      )}

      {detail.items.length ? (
        <div className="card-grid" style={{ marginTop: 20 }}>
          {detail.items.map(e => (
            <div key={e.card.id}>
              <Card card={e.card} lazy />
              <WishlistNote listId={list.id} listingId={e.card.id} note={e.note}
                            onSaved={() => openWishlist(list.id)} />
              <MoveToList listId={list.id} listingId={e.card.id} lists={state.wishlists} />
            </div>
          ))}
        </div>
      ) : (
        <div className="empty-state" style={{ marginTop: 24 }}>
          <h3>{t('Danh sách này còn trống')}</h3>
          <p>{t('Nhấn ♥ trên chỗ nghỉ bạn thích để thêm vào đây.')}</p>
          <button className="btn btn-primary" style={{ marginTop: 18 }} onClick={() => navigate('/')}>{t('Khám phá chỗ nghỉ')}</button>
        </div>
      )}
    </div>
  );
}
