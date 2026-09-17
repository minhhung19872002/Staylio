import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { useMedia } from '../lib/useMedia.js';
import {
  set, state as store, isDiscovery, activeFilterCount, guestLabel, totalGuests,
  loadSuggestions, loadNotifications, becomeHost, logout, toast, openOverlay, openMenu, toggleAmenity, settleDates,
  clearDates, resetCalendarView, featureOn
} from '../lib/store.js';
import { api } from '../lib/api.js';
import { applySearch } from '../lib/nav.js';
import { dateRangeLabel, shortDate, debounce } from '../lib/format.js';
import { t } from '../lib/i18n.js';
import { Avatar } from './Avatar.jsx';
import { Icon, BrandMark } from './Icon.jsx';
import { DateFields, GuestFields, Suggestions } from './modals/SearchModals.jsx';

// docs/01 TM-02 — four rows, and each one goes somewhere. The last two used to
// answer with a toast saying the demo only had homes, which stopped being true
// once MR-01 and MR-05 shipped their own pages.
const TABS = [
  { key: 'all', label: 'Tất cả', icon: 'all', path: '/' },
  { key: 'homes', label: 'Chỗ ở', icon: 'house', path: '/' },
  { key: 'experiences', label: 'Trải nghiệm', icon: 'boutique', path: '/experiences' },
  { key: 'services', label: 'Dịch vụ', icon: 'pool', path: '/services' }
];

const debouncedSuggest = debounce(() => loadSuggestions(), 180);
const debouncedSearch = debounce(() => applySearch(), 320);

export function Header() {
  const state = useStore();
  const location = useLocation();
  const navigate = useNavigate();

  const onBrowse = location.pathname === '/';
  const discovery = onBrowse && isDiscovery();

  // Any click outside the menus or the destination field closes them.
  useEffect(() => {
    const onDocClick = e => {
      const patch = {};
      if (store.menu && !e.target.closest?.('.menu-anchor')) patch.menu = null;
      if (store.suggestOpen && !e.target.closest?.('.search-anchor')) patch.suggestOpen = false;
      if (Object.keys(patch).length) set(patch);
    };
    document.addEventListener('click', onDocClick);
    return () => document.removeEventListener('click', onDocClick);
  }, []);

  const submitSearch = e => {
    e.preventDefault();
    set({ suggestOpen: false });
    applySearch({ replace: false });
    if (!onBrowse) navigate(`/?${new URLSearchParams(location.search)}`);
  };

  const onQueryInput = value => {
    set({ q: value, suggestOpen: true });
    debouncedSuggest();
    if (onBrowse) debouncedSearch();
  };

  return <>
    <div className="header-main">
      <button className="brand" onClick={() => navigate('/')} aria-label={t('Staylio — về trang chủ')}>
        <BrandMark />
        <span className="brand-text">Staylio</span>
      </button>

      {/* Real addresses, not click handlers. This nav is on every page, so it is
          the only site-wide route into the experience and service catalogues —
          while these were buttons, neither line had an inbound link anywhere.
          aria-current replaces aria-pressed: these are links now, not toggles. */}
      <nav className="nav-tabs" aria-label={t('Loại dịch vụ')}>
        {TABS.map(tab => (
          <a key={tab.key} className={`nav-tab ${state.tab === tab.key ? 'is-active' : ''}`}
             href={tab.path}
             aria-current={state.tab === tab.key ? 'page' : undefined}
             onClick={e => {
               if (e.metaKey || e.ctrlKey || e.shiftKey || e.button === 1) return;
               e.preventDefault();
               set({ tab: tab.key });
               navigate(tab.path);
             }}>
            <span className="ic"><Icon name={tab.icon} size={22} /></span>
            <span>{t(tab.label)}</span>
          </a>
        ))}
      </nav>

      <div className="header-actions">
        <a className="ghost-btn host-link" href={state.user?.isHost ? '/hosting' : '/host'}
           onClick={e => {
             if (e.metaKey || e.ctrlKey || e.shiftKey || e.button === 1) return;
             e.preventDefault();
             navigate(state.user?.isHost ? '/hosting' : '/host');
           }}>
          {state.user?.isHost ? t('Trang chủ nhà') : t('Cho thuê nhà')}
        </a>

        {state.user && (
          <div className="menu-anchor">
            <button className="icon-btn bell" aria-label={t('Thông báo')} aria-expanded={state.menu === 'bell'}
                    onClick={() => { openMenu('bell'); if (store.menu === 'bell') loadNotifications(); }}>
              <Icon name="star" size={18} />
              {!!state.notifications?.unread && <span className="bell-dot">{state.notifications.unread}</span>}
            </button>
            {state.menu === 'bell' && <BellMenu />}
          </div>
        )}

        <button className="icon-btn" onClick={() => openOverlay('language')}
                aria-label={t('Chọn ngôn ngữ và tiền tệ')} title={t('Ngôn ngữ & tiền tệ')}>
          <Icon name="globe" size={18} />
        </button>

        <div className="menu-anchor">
          <button className="account-btn" aria-haspopup="true" aria-expanded={state.menu === 'account'}
                  onClick={() => openMenu('account')}>
            <span className="ic"><Icon name="menu" size={17} /></span>
            <UnreadBadge />
            {state.user
              ? <Avatar url={state.user.avatarUrl} initials={state.user.initials} />
              : <span className="avatar is-anon" aria-hidden="true"><Icon name="heart" size={15} /></span>}
          </button>
          {state.menu === 'account' && <AccountMenu />}
        </div>
      </div>
    </div>

    <SearchBar wide={discovery} onSubmit={submitSearch} onQueryInput={onQueryInput} />

    {/*
      * The chip row stays mounted while browsing and collapses to nothing on the
      * landing page, rather than appearing out of nowhere. Its 52px arriving in
      * one frame was half the jolt when a rail heading turned the landing page
      * into a set of results.
      */}
    {onBrowse && (
      <div className={`quick-slot ${discovery ? 'is-collapsed' : ''}`} aria-hidden={discovery}>
        <QuickBar />
      </div>
    )}
  </>;
}

function UnreadBadge() {
  const state = useStore();
  const unread = state.user?.unreadMessages ?? 0;
  if (unread > 0) return <span className="fav-count" title={t('Tin nhắn chưa đọc')}>{unread}</span>;
  if (state.favCount > 0) return <span className="fav-count" title={t('Chỗ nghỉ đã lưu')}>{state.favCount}</span>;
  return null;
}

/**
 * Below this the calendar has nowhere to hang: two months need more room than a
 * phone has, so those screens keep the modal.
 */
const DROPDOWN_FROM = '(min-width: 900px)';

/*
 * And below this the bar itself has nowhere to stand. Three segments plus the
 * button need ~500px; a phone gives ~358px, which left the destination field
 * 45px wide with its placeholder cut mid-word — the one field a search starts
 * from was the one nobody could read. Under this width the bar is a single
 * summary that opens the search sheet.
 */
const MINI_BELOW = '(max-width: 719px)';



/**
 * The landing page keeps the tall "Địa điểm · Ngày · Khách" pill; every other
 * route gets the compact summary bar, the same way airbnb.com collapses it.
 */
function SearchBar({ wide, onSubmit, onQueryInput }) {
  const state = useStore();

  // Which segment is open, the way airbnb.com does it: the bar goes grey and the
  // one you are filling in lifts out of it in white.
  const [openSeg, setOpenSeg] = useState(null);
  const barRef = useRef(null);
  const [roomy, setRoomy] = useState(() => window.matchMedia(DROPDOWN_FROM).matches);
  const mini = useMedia(MINI_BELOW);

  useEffect(() => {
    const mq = window.matchMedia(DROPDOWN_FROM);
    const onChange = e => { setRoomy(e.matches); if (!e.matches) setOpenSeg(null); };
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);

  // A click anywhere else, or Escape, puts the bar back. Pointerdown rather than
  // click so the bar closes before whatever was clicked does its own work.
  useEffect(() => {
    if (!openSeg) return undefined;

    // Clicking away and pressing Escape close the bar just as "Xong" does, so
    // they have to commit a half-picked stay the same way — otherwise the pill
    // keeps reading "25-08 – ?" over a search that is using something else.
    const away = e => {
      if (!barRef.current?.contains(e.target)) closeBar();
    };
    const key = e => { if (e.key === 'Escape') closeBar(); };

    document.addEventListener('pointerdown', away);
    document.addEventListener('keydown', key);
    return () => {
      document.removeEventListener('pointerdown', away);
      document.removeEventListener('keydown', key);
    };
  }, [openSeg]);

  // Opening the date panel should show the dates it is about, wherever the
  // calendar was last paged to.
  const toggle = seg => {
    // The suggestion list belongs to the destination field; moving to another
    // segment should take it off the screen with it.
    set({ suggestOpen: false });

    if (!roomy) { openOverlay(seg === 'when' ? 'dates' : 'guests'); return; }
    if (seg === 'when' && openSeg !== 'when') resetCalendarView();
    setOpenSeg(seg);
  };

  const closeBar = () => { setOpenSeg(null); settleDates(); set({ suggestOpen: false }); };

  /*
   * The panel's box is worked out in pixels rather than left by CSS to one of
   * three anchorings, because `left: 0` cannot animate into `right: 0`. With
   * both edges and the height as numbers, the browser can tween all three and
   * the box glides from under one segment to under the next.
   */
  const popRef = useRef(null);
  const popContentRef = useRef(null);
  const [popBox, setPopBox] = useState(null);

  useLayoutEffect(() => {
    const anchor = barRef.current?.querySelector('.search-anchor');
    const content = popContentRef.current;
    if (!openSeg || !anchor || !content) { setPopBox(null); return undefined; }

    const measure = () => {
      const room = anchor.getBoundingClientRect().width;
      const wanted = { where: 440, when: 700, who: 400 }[openSeg];
      const width = Math.min(wanted, room);

      // Each panel sits under the thing it belongs to: the destination at the
      // left edge, the guests at the right, the calendar in the middle.
      const left = openSeg === 'where' ? 0 : openSeg === 'who' ? room - width : (room - width) / 2;

      setPopBox({ left, width, height: content.offsetHeight });
    };

    measure();

    // The calendar changes height when its tab changes, and the counters when
    // the panel is first filled; the box has to follow rather than clip them.
    const observer = new ResizeObserver(measure);
    observer.observe(content);
    observer.observe(anchor);
    return () => observer.disconnect();
    // The suggestion list arrives from the server after the panel is already
    // open, so its length has to be a reason to measure again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSeg, state.suggestOpen, state.suggestions?.length]);

  const placeholder = wide
    ? t('Tìm điểm đến')
    : state.q.trim()
      ? `${t('Chỗ ở')} · ${state.q.trim()}`
      : state.category !== 'all'
        ? t(state.meta?.categories.find(c => c.key === state.category)?.label ?? 'Chỗ ở')
        : t('Mọi nơi');

  // The per-segment class carries the widths, so it belongs on both bars. Hanging
  // it on `wide` alone left the compact bar's three fields at their natural size,
  // bunched against the left edge with a third of the pill empty behind them.
  const segClass = seg =>
    `seg seg-${seg} ${openSeg === seg ? 'is-active' : ''}`;

  const field = (
    // The destination is a segment like the other two: putting the caret in it
    // greys the bar and lifts this one, so all three behave the same way.
    <label className={segClass('where')}>
      {wide && <span className="seg-cap">{t('Địa điểm')}</span>}
      <input id="q" type="text" value={state.q} placeholder={placeholder} autoComplete="off"
             onChange={e => onQueryInput(e.target.value)}
             onFocus={() => {
               setOpenSeg('where');
               set({ suggestOpen: true });
               loadSuggestions();
             }}
             aria-autocomplete="list" aria-expanded={state.suggestOpen} />
    </label>
  );

  if (mini) {
    return (
      <div className="search-row is-mini">
        <button type="button" className="searchbar is-mini" onClick={() => openOverlay('search')}>
          <span className="seg-lead" aria-hidden="true"><Icon name="search" size={17} /></span>
          <span className="mini-tx">
            <b>{state.q.trim() || t('Mọi nơi')}</b>
            <span>{dateRangeLabel(state.checkIn, state.checkOut)} · {guestLabel()}</span>
          </span>
        </button>
      </div>
    );
  }

  return (
    <div className={`search-row ${wide ? 'is-wide' : ''}`} ref={barRef}>
      {/* The panels hang off this rather than off the row, so they line up with
          the bar's edges instead of the window's. */}
      <div className="search-anchor">
      <form className={`searchbar ${wide ? '' : 'is-compact'} ${openSeg ? 'is-open' : ''}`}
            onSubmit={onSubmit} role="search">
        {!wide && <span className="seg-lead" aria-hidden="true"><Icon name="house" size={20} /></span>}
        {field}
        <span className="seg-div" />
        <button type="button" className={segClass('when')} onClick={() => toggle('when')}
                aria-expanded={openSeg === 'when'}>
          {wide && <span className="seg-cap">{t('Ngày')}</span>}
          <span className="seg-val">
            {state.pickingFrom
              ? `${shortDate(state.pickingFrom)} – ?`
              : dateRangeLabel(state.checkIn, state.checkOut)}
          </span>
        </button>
        <span className="seg-div" />
        <button type="button" className={segClass('who')} onClick={() => toggle('who')}
                aria-expanded={openSeg === 'who'}>
          {wide && <span className="seg-cap">{t('Khách')}</span>}
          <span className="seg-val">{guestLabel()}</span>
        </button>
        <button type="submit" className="search-go" aria-label={t('Tìm kiếm')}>
          <Icon name="search" size={wide ? 17 : 15} />
        </button>
      </form>

      {/*
        * One panel, not three. Moving between segments slides and resizes this
        * box rather than tearing one down and building another — which is the
        * difference between airbnb.com's search bar and a set of dropdowns that
        * happen to share a row.
        */}
      {openSeg && (
        <div className={`search-pop is-${openSeg}`} ref={popRef}
             style={popBox ? { left: popBox.left, width: popBox.width, height: popBox.height } : undefined}>
          <div className="search-pop-inner" ref={popContentRef} key={openSeg}>
            {openSeg === 'where' && <Suggestions onDone={closeBar} />}

            {openSeg === 'when' && <>
              <DateFields />
              <div className="search-pop-foot">
                <button type="button" className="text-btn"
                        onClick={() => { clearDates(); applySearch(); }}>{t('Xoá ngày')}</button>
                <button type="button" className="btn btn-dark btn-sm" onClick={closeBar}>{t('Xong')}</button>
              </div>
            </>}

            {openSeg === 'who' && <>
              <GuestFields />
              <div className="search-pop-foot">
                <span style={{ fontSize: 13, color: 'var(--ink-muted)' }}>{t('Tổng')} {totalGuests()} {t('khách')}</span>
                <button type="button" className="btn btn-dark btn-sm" onClick={closeBar}>{t('Xong')}</button>
              </div>
            </>}
          </div>
        </div>
      )}
      </div>
    </div>
  );
}


function BellMenu() {
  const state = useStore();
  const navigate = useNavigate();
  const feed = state.notifications;
  const items = feed?.items ?? [];

  const open = async n => {
    try { await api.readNotification(n.id); } catch { /* non-blocking */ }
    set({ menu: null });
    await loadNotifications();
    if (n.link) navigate(n.link);
  };

  return (
    <div className="menu bell-menu" role="menu">
      <div className="bell-head">
        <b>{t('Thông báo')}</b>
        {!!feed?.unread && (
          <button className="link-btn" onClick={async e => {
            e.stopPropagation();
            try { await api.readAllNotifications(); await loadNotifications(); }
            catch (err) { toast(err.message); }
          }}>{t('Đánh dấu đã đọc')}</button>
        )}
      </div>
      {items.length
        ? items.map(n => (
            <button className={`bell-row ${n.unread ? 'is-unread' : ''}`} role="menuitem" key={n.id}
                    onClick={() => open(n)}>
              <b>{n.title}</b>
              <span>{n.body}</span>
            </button>))
        : <p className="bell-empty">{t('Chưa có thông báo nào.')}</p>}
    </div>
  );
}

function AccountMenu() {
  const state = useStore();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const u = state.user;

  /*
   * The menu is sixteen entries long and said nothing about where you already
   * were, so opening it left you to work that out from the page behind it.
   * aria-current is the right way to say "this one" — one attribute drives both
   * the marker and what a screen reader announces. The trailing slash matters:
   * without it /host would claim /hosting and /trips would claim /trip-plans.
   */
  const here = path => (pathname === path || pathname.startsWith(`${path}/`) ? 'page' : undefined);

  const goTo = href => { set({ menu: null }); navigate(href); };
  const auth = mode => set({ authMode: mode, authError: null, overlay: 'login', menu: null });

  if (!u) {
    return (
      <div className="menu" role="menu">
        <button className="bold" role="menuitem" onClick={() => auth('register')}>{t('Đăng ký')}</button>
        <button className="bold" role="menuitem" onClick={() => auth('login')}>{t('Đăng nhập')}</button>
        <hr />
        <button role="menuitem" aria-current={here('/wishlists')} onClick={() => goTo('/wishlists')}>{t('Danh sách yêu thích')} ({state.favCount})</button>
        <button role="menuitem" aria-current={here('/trips')} onClick={() => goTo('/trips')}>{t('Chuyến đi của tôi')}</button>
        {/* docs/01 QT-08 — a real rollout switch on a real P2 feature, so the
            console's percentages move something. Unknown to the server means on. */}
        {/* docs/02 H1 — the reviews screen, next to the trips it follows from. */}
        <button role="menuitem" aria-current={here('/danh-gia')} onClick={() => goTo('/danh-gia')}>{t('Đánh giá')}</button>
        {featureOn('trip-plans') && (
          <button role="menuitem" aria-current={here('/trip-plans')} onClick={() => goTo('/trip-plans')}>{t('Lịch trình chuyến đi')}</button>
        )}
        <button role="menuitem" aria-current={here('/friends')} onClick={() => goTo('/friends')}>{t('Bạn bè')}</button>
        <button role="menuitem" aria-current={here('/host')} onClick={() => goTo('/host')}>{t('Cho thuê nhà trên Staylio')}</button>
        <hr />
        <button role="menuitem" onClick={() => openOverlay('language')}>{t('Ngôn ngữ & tiền tệ')}</button>
        <button role="menuitem" aria-current={here('/help')} onClick={() => goTo('/help')}>{t('Trung tâm trợ giúp')}</button>
      </div>
    );
  }

  const startHosting = async () => {
    set({ menu: null });
    if (!await becomeHost()) return;
    toast('Bạn đã sẵn sàng cho thuê nhà.');
    navigate('/hosting');
  };

  return (
    <div className="menu" role="menu">
      <div className="menu-user">
        <Avatar url={u.avatarUrl} initials={u.initials} />
        <div style={{ minWidth: 0 }}>
          <b>{u.displayName || u.fullName}</b>
          <span>{u.email}</span>
        </div>
      </div>
      <hr />
      <button className="bold" role="menuitem" aria-current={here('/messages')} onClick={() => goTo('/messages')}>
        {t('Tin nhắn')} {!!u.unreadMessages && <span className="fav-count" style={{ marginLeft: 'auto' }}>{u.unreadMessages}</span>}
      </button>
      <button role="menuitem" aria-current={here('/trips')} onClick={() => goTo('/trips')}>{t('Chuyến đi của tôi')}</button>
      <button role="menuitem" aria-current={here('/wishlists')} onClick={() => goTo('/wishlists')}>{t('Danh sách yêu thích')} ({state.favCount})</button>
      <button role="menuitem" aria-current={here('/experiences/bookings')} onClick={() => goTo('/experiences/bookings')}>{t('Vé trải nghiệm')}</button>
      <button role="menuitem" aria-current={here('/services/bookings')} onClick={() => goTo('/services/bookings')}>{t('Dịch vụ đã đặt')}</button>
      <button role="menuitem" aria-current={here('/wallet')} onClick={() => goTo('/wallet')}>{t('Số dư & thẻ quà tặng')}</button>
      <button role="menuitem" aria-current={here('/resolutions')} onClick={() => goTo('/resolutions')}>{t('Trung tâm giải quyết')}</button>
      <button role="menuitem" aria-current={here('/shield')} onClick={() => goTo('/shield')}>Staylio Shield</button>
      <hr />
      {u.isHost
        ? <button className="bold" role="menuitem" aria-current={here('/hosting')} onClick={() => goTo('/hosting')}>{t('Trang chủ nhà')} ({u.listingCount})</button>
        : <button className="bold" role="menuitem" onClick={startHosting}>{t('Cho thuê nhà trên Staylio')}</button>}
      {u.role === 'Admin' && <button className="bold" role="menuitem" aria-current={here('/admin')} onClick={() => goTo('/admin')}>{t('Trang quản trị')}</button>}
      <button role="menuitem" aria-current={here('/cai-dat')} onClick={() => goTo('/cai-dat')}>{t('Cài đặt')}</button>
      <button role="menuitem" aria-current={here(`/users/${u.id}`)} onClick={() => goTo(`/users/${u.id}`)}>{t('Hồ sơ công khai')}</button>
      <hr />
      <button role="menuitem" onClick={() => openOverlay('language')}>{t('Ngôn ngữ & tiền tệ')}</button>
      <button role="menuitem" aria-current={here('/help')} onClick={() => goTo('/help')}>{t('Trung tâm trợ giúp')}</button>
      <button role="menuitem" onClick={async () => { set({ menu: null }); await logout(); navigate('/'); }}>{t('Đăng xuất')}</button>
    </div>
  );
}

/**
 * airbnb.com's /s/ page has no category strip — a "Filters" button followed by
 * one-tap chips, then the total-price switch on the right.
 */
function QuickBar() {
  const state = useStore();
  const filters = activeFilterCount();
  const amenities = state.meta?.amenities ?? [];

  // Same running order airbnb.com uses: booking perks first, then amenities.
  const chips = ['kitchen', 'pool', 'wifi', 'ac', 'washer', 'parking', 'tv', 'workspace', 'gym', 'pet']
    .map(key => amenities.find(a => a.key === key))
    .filter(Boolean);

  const flag = key => { set({ [key]: !state[key] }); applySearch(); };

  return (
    <div className="catbar-outer">
      <div className="quickbar">
        <button className="quick-chip is-lead" onClick={() => openOverlay('filters')}>
          <Icon name="filter" size={15} /> {t('Bộ lọc')}{!!filters && <span className="count">{filters}</span>}
        </button>
        <span className="quick-div" />

        <div className="quick-scroll" id="cat-scroll">
          <button className={`quick-chip ${state.instantBookOnly ? 'is-on' : ''}`} onClick={() => flag('instantBookOnly')}>{t('Đặt ngay')}</button>
          <button className={`quick-chip ${state.freeCancellationOnly ? 'is-on' : ''}`} onClick={() => flag('freeCancellationOnly')}>{t('Huỷ miễn phí')}</button>
          <button className={`quick-chip ${state.dealsOnly ? 'is-on' : ''}`} onClick={() => flag('dealsOnly')}>{t('Ưu đãi')}</button>
          <button className={`quick-chip ${state.guestFavoriteOnly ? 'is-on' : ''}`} onClick={() => flag('guestFavoriteOnly')}>{t('Khách yêu thích')}</button>
          <button className={`quick-chip ${state.superhostOnly ? 'is-on' : ''}`} onClick={() => flag('superhostOnly')}>{t('Siêu chủ nhà')}</button>
          {chips.map(a => (
            <button key={a.key} className={`quick-chip ${state.amenities.includes(a.key) ? 'is-on' : ''}`}
                    aria-pressed={state.amenities.includes(a.key)}
                    onClick={() => { toggleAmenity(a.key); applySearch(); }}>{t(a.label)}</button>
          ))}
        </div>

        <button className={`total-toggle ${state.showTotalPrice ? 'is-on' : ''}`}
                aria-pressed={state.showTotalPrice}
                title={t('Hiện giá mỗi đêm đã gồm phí dịch vụ, phí dọn dẹp và thuế')}
                onClick={() => set({ showTotalPrice: !state.showTotalPrice })}>
          <span className="switch" aria-hidden="true" /> {t('Giá đã gồm thuế và phí')}
        </button>
      </div>
    </div>
  );
}
