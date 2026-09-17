import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { loadPublicProfile, openReport, toast } from '../lib/store.js';
import { api } from '../lib/api.js';
import { monthLabel } from '../lib/format.js';
import { Avatar } from '../components/Avatar.jsx';
import { Card } from '../components/Card.jsx';
import { Icon } from '../components/Icon.jsx';
import { t, joined } from '../lib/i18n.js';

/**
 * docs/01 TK-05, docs/02 C6 — somebody's public profile. Open to anybody, so it
 * shows only what the server chose to publish and never asks who is reading.
 */
export function UserProfile() {
  const state = useStore();
  const { id } = useParams();
  const navigate = useNavigate();

  useEffect(() => { loadPublicProfile(Number(id)); }, [id]);

  const p = state.publicProfile;
  const isMe = state.user?.id === Number(id);

  // docs/01 AT-10 — is this person on my block list?
  const [blocked, setBlocked] = useState(false);
  useEffect(() => {
    if (!state.user || isMe) { setBlocked(false); return; }
    api.blocks().then(list => setBlocked(list.some(b => b.userId === Number(id)))).catch(() => {});
  }, [id, state.user, isMe]);

  // docs/01 XH-01/XH-02 — friend state + their journey (if visible).
  const [friends, setFriends] = useState([]);
  const [journey, setJourney] = useState(null);
  useEffect(() => {
    setJourney(null);
    if (state.user && !isMe) api.friends().then(setFriends).catch(() => setFriends([]));
    api.friendJourney(Number(id)).then(setJourney).catch(() => setJourney(null));
  }, [id, state.user, isMe]);
  const isFriend = friends.some(f => f.userId === Number(id));

  const addFriend = async () => {
    try { const r = await api.sendFriendRequest(Number(id)); toast(r.message || 'Đã gửi lời mời.'); api.friends().then(setFriends).catch(() => {}); }
    catch (err) { toast(err.message); }
  };
  const removeFriend = async () => {
    try { await api.removeFriend(Number(id)); setFriends(fs => fs.filter(f => f.userId !== Number(id))); toast('Đã huỷ kết bạn.'); }
    catch (err) { toast(err.message); }
  };
  // docs/01 XH-03 — hỏi bạn về một nơi họ từng ở.
  const askAbout = async stop => {
    const text = prompt(`Hỏi bạn về "${stop.city}":`, 'Chỗ này ở sao bạn ơi?');
    if (!text?.trim()) return;
    try { await api.sendFriendMessage(Number(id), stop.listingId, text.trim()); toast('Đã gửi câu hỏi cho bạn.'); }
    catch (err) { toast(err.message); }
  };

  const toggleBlock = async () => {
    try {
      if (blocked) { await api.unblockUser(Number(id)); setBlocked(false); toast('Đã bỏ chặn.'); }
      else { await api.blockUser(Number(id)); setBlocked(true); toast('Đã chặn người dùng này.'); }
    } catch (err) { toast(err.message); }
  };

  if (state.publicProfileLoading || !p) {
    return (
      <div className="shell" style={{ paddingBlock: '32px 90px' }}>
        <div className="sk-line skeleton" style={{ width: 240, height: 26 }} />
        <div className="skeleton" style={{ height: 180, borderRadius: 16, marginTop: 20 }} />
      </div>
    );
  }

  const facts = [
    p.location && ['map', p.location],
    p.occupation && ['workspace', p.occupation],
    p.languages.length && ['globe', `${t('Nói')} ${p.languages.join(', ')}`]
  ].filter(Boolean);

  return (
    <div className="shell" style={{ paddingBlock: '24px 90px' }}>
      <button className="back-link" onClick={() => navigate(-1)}>← {t('Quay lại')}</button>

      <div className="profile-head">
        <div className="profile-card">
          <Avatar url={p.avatarUrl} initials={p.initials} className="host-avatar" size={96} />
          <h1 className="profile-name">{p.displayName}</h1>
          <p className="profile-role">
            {p.isHost ? (p.isSuperhost ? t('Siêu chủ nhà') : t('Chủ nhà')) : t('Khách')}
          </p>

          {p.isHost && p.reviewCount > 0 && (
            <div className="profile-stats">
              <div><b>{p.reviewCount}</b><span>{t('Đánh giá')}</span></div>
              {p.rating != null && <div><b>{p.rating} ★</b><span>{t('Điểm trung bình')}</span></div>}
              {p.responseRate && <div><b>{p.responseRate}</b><span>{t('Tỉ lệ phản hồi')}</span></div>}
            </div>
          )}
        </div>

        <div className="profile-about">
          <p className="profile-joined">{joined(p.joinedLabel)}</p>

          {/* docs/01 TK-05 — only what was actually proved is listed. */}
          {!!p.badges.length && (
            <div className="chip-wrap" style={{ marginBottom: 14 }}>
              {p.badges.map(b => <span className="badge confirmed" key={b}>{t(b)}</span>)}
            </div>
          )}

          {!!facts.length && (
            <ul className="profile-facts">
              {facts.map(([icon, text]) => (
                <li key={text}><Icon name={icon} size={18} /> {text}</li>
              ))}
            </ul>
          )}

          {p.bio && <p className="profile-bio">{p.bio}</p>}

          {!!p.interests.length && <>
            <h2 className="profile-sub">{t('Sở thích')}</h2>
            <div className="chip-wrap">
              {p.interests.map(i => <span className="quick-chip" key={i}>{i}</span>)}
            </div>
          </>}

          {isMe
            ? <button className="btn" style={{ marginTop: 18 }}
                      onClick={() => navigate('/cai-dat/ho-so')}>{t('Chỉnh sửa hồ sơ')}</button>
            : <div style={{ display: 'flex', gap: 8, marginTop: 18, flexWrap: 'wrap' }}>
                {p.isHost && p.listings.length > 0 && (
                  <button className="btn"
                          onClick={() => navigate(`/rooms/${p.listings[0].slug}`)}>
                    {t('Xem chỗ nghỉ để nhắn tin')}
                  </button>
                )}
                {/* docs/01 XH-01 — kết bạn / huỷ kết bạn. */}
                {state.user && (
                  isFriend
                    ? <button className="btn btn-outline" onClick={removeFriend}>{t('Bạn bè')} ✓</button>
                    : <button className="btn btn-primary" onClick={addFriend}>{t('Kết bạn')}</button>
                )}
                {/* docs/01 AT-10 — chặn hoặc bỏ chặn người dùng này. */}
                {state.user && (
                  <button className="btn btn-outline" onClick={toggleBlock}>
                    {blocked ? t('Bỏ chặn') : t('Chặn người dùng')}
                  </button>
                )}
              </div>}

          {/* docs/01 XH-01/XH-02 — nơi người này đã đi và sắp đi (nếu được xem). */}
          {journey && (journey.been.length > 0 || journey.upcoming.length > 0) && (
            <div style={{ marginTop: 20 }}>
              <h2 className="profile-sub">{t('Hành trình')}</h2>
              {journey.upcoming.length > 0 && <>
                <div className="meta" style={{ fontWeight: 700, marginTop: 6 }}>{t('Sắp đi')}</div>
                <div className="chip-wrap">
                  {journey.upcoming.map((s, i) => (
                    <span className="quick-chip" key={`u${i}`}>{s.city} · {s.when}</span>
                  ))}
                </div>
              </>}
              {journey.been.length > 0 && <>
                <div className="meta" style={{ fontWeight: 700, marginTop: 10 }}>{t('Đã đến')}</div>
                <div className="chip-wrap">
                  {journey.been.slice(0, 20).map((s, i) => (
                    <span className="quick-chip" key={`b${i}`}
                          style={isFriend ? { cursor: 'pointer' } : undefined}
                          title={isFriend ? t('Hỏi bạn về nơi này') : undefined}
                          onClick={() => { if (isFriend) askAbout(s); }}>
                      {s.city}{isFriend ? ' 💬' : ''}
                    </span>
                  ))}
                </div>
              </>}
            </div>
          )}

          {/* docs/01 XH-03 — nhắn tin hỏi bạn bè. */}
          {state.user && !isMe && isFriend && <FriendChat userId={Number(id)} name={p.displayName} />}
        </div>
      </div>

      {!!p.listings.length && <>
        <h2 className="section-title" style={{ marginTop: 40 }}>
          {t('Chỗ nghỉ của')} {p.displayName}
        </h2>
        <div className="card-grid" style={{ marginTop: 16 }}>
          {p.listings.map(c => <Card key={c.id} card={c} />)}
        </div>
      </>}

      <ReviewList title={`${t('Đánh giá về')} ${p.displayName} ${t('với vai trò chủ nhà')}`} items={p.reviewsAsHost} />
      {/* docs/03 §7 — the three headings hosts score a guest on. */}
      {p.guestScores && (
        <div className="kv-grid" style={{ marginTop: 32 }}>
          {[['Sạch sẽ', p.guestScores.cleanliness], ['Giao tiếp', p.guestScores.communication],
            ['Tuân thủ nội quy', p.guestScores.houseRules]].filter(([, v]) => v != null).map(([label, v]) => (
            <div className="kv" key={label}><span className="kv-label">{t(label)}</span><b>★ {v}</b></div>
          ))}
          <div className="kv">
            <span className="kv-label">{t('Chủ nhà sẽ đón lại')}</span><b>{p.guestScores.wouldHostAgainPercent}%</b>
          </div>
        </div>
      )}
      <ReviewList title={`${t('Đánh giá về')} ${p.displayName} ${t('với vai trò khách')}`} items={p.reviewsAsGuest} />

      {!p.reviewsAsHost.length && !p.reviewsAsGuest.length && (
        <p className="section-sub" style={{ marginTop: 32 }}>{t('Chưa có đánh giá nào.')}</p>
      )}

      {/* docs/01 AT-02 — reporting a person, not just something they wrote. Kept
          off your own profile, where the only thing it could do is waste a
          moderator's time. */}
      {!isMe && (
        <div style={{ marginTop: 40, borderTop: '1px solid var(--divider)', paddingTop: 20 }}>
          <button className="text-btn"
                  onClick={() => openReport('user', Number(id), p.displayName)}>
            ⚑ {t('Báo cáo người dùng này')}
          </button>
        </div>
      )}
    </div>
  );
}

function ReviewList({ title, items }) {
  const navigate = useNavigate();
  if (!items.length) return null;

  return <>
    <h2 className="section-title" style={{ marginTop: 40 }}>{title}</h2>
    <div className="profile-reviews">
      {items.map(r => (
        <article className="profile-review" key={`${title}:${r.id}`}>
          <div className="review-head">
            <Avatar initials={r.authorInitials} />
            <div style={{ minWidth: 0 }}>
              {r.authorUserId
                ? <button className="link-btn" onClick={() => navigate(`/users/${r.authorUserId}`)}>{r.authorName}</button>
                : <b>{r.authorName}</b>}
              <span style={{ display: 'block', fontSize: 12.5, color: 'var(--ink-muted)' }}>{monthLabel(r.when)}</span>
            </div>
            <span style={{ marginLeft: 'auto', fontWeight: 700, fontSize: 13.5 }}>★ {r.rating}</span>
          </div>
          <p className="profile-review-text">{r.text}</p>
          {r.listingSlug && (
            <button className="link-btn" style={{ fontSize: 12.5 }}
                    onClick={() => navigate(`/rooms/${r.listingSlug}`)}>{r.listingTitle}</button>
          )}
        </article>
      ))}
    </div>
  </>;
}

/** docs/01 XH-03 — a lightweight conversation with a friend. */
function FriendChat({ userId, name }) {
  const [msgs, setMsgs] = useState(null);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  const load = () => api.friendMessages(userId).then(setMsgs).catch(() => setMsgs([]));
  useEffect(() => { load(); }, [userId]);

  const send = async () => {
    const body = text.trim();
    if (!body || busy) return;
    setBusy(true);
    try { await api.sendFriendMessage(userId, null, body); setText(''); await load(); }
    catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  if (!msgs) return null;
  return (
    <div style={{ marginTop: 22 }}>
      <h2 className="profile-sub">{t('Nhắn tin với')} {name}</h2>
      <div style={{ display: 'grid', gap: 6, margin: '8px 0', maxHeight: 260, overflowY: 'auto' }}>
        {msgs.length === 0
          ? <p className="meta">{t('Chưa có tin nhắn. Hỏi bạn ấy về một nơi đã ở!')}</p>
          : msgs.map(m => (
              <div key={m.id} className={`bubble ${m.mine ? 'mine' : ''}`}>
                {m.listingTitle && <span className="bubble-note">{t('Về:')} {m.listingTitle}</span>}
                <p>{m.body}</p>
              </div>
            ))}
      </div>
      <div style={{ display: 'flex', gap: 8 }}>
        <input style={{ flex: 1, padding: '10px 12px', border: '1px solid var(--line)', borderRadius: 10, fontSize: 16 }}
               value={text} placeholder={t('Nhắn cho bạn…')} onChange={e => setText(e.target.value)}
               onKeyDown={e => { if (e.key === 'Enter') send(); }} />
        <button className="btn btn-primary btn-sm" disabled={busy || !text.trim()} onClick={send}>{t('Gửi')}</button>
      </div>
    </div>
  );
}
