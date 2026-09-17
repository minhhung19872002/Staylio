// One mutable state object plus the actions that touch it, exposed to React
// through useSyncExternalStore. Keeping the store outside React means the
// business logic stays plain JS and testable, while React handles the DOM.

import { api } from './api.js';
import { todayIso, isoOf, parseIso, setCurrency, setLocale, setTimeZone } from './format.js';
import { t } from './i18n.js';

const listeners = new Set();
let version = 0;

export const state = {
  // search criteria
  q: '',
  checkIn: todayIso(9),
  checkOut: todayIso(12),
  // docs/01 TM-06 & TM-07 — 'exact' | 'weekend' | 'week' | 'month' | 'months'
  stay: 'exact',
  flexDays: 0,
  stayMonths: 1,
  startMonths: [],
  guests: { adults: 2, children: 0, infants: 0, pets: 0 },
  category: 'all',
  minPrice: 0,
  maxPrice: 0,
  amenities: [],
  sort: 'reco',
  roomType: 'any',
  bedrooms: 0,
  beds: 0,
  bathrooms: 0,
  superhostOnly: false,
  guestFavoriteOnly: false,
  instantBookOnly: false,
  freeCancellationOnly: false,
  hostLanguages: [],   // docs/01 TM-18
  minRating: 0,            // 4.5 / 4 / 3.5, 0 = any
  payAtPropertyOnly: false, // "không cần trả trước"
  maxCentreKm: 0,          // 1 / 3 / 5, 0 = any

  // reference data
  meta: null,
  metaError: null,
  /**
   * docs/01 QT-08 — which features are on for whoever is asking, already
   * bucketed by the server for this identity. Empty until the boot pass
   * answers, and `featureOn` treats an unknown key as on: a flag the server
   * has never heard of must not take a working screen away.
   */
  features: {},
  currency: { code: 'VND', label: 'Việt Nam Đồng', symbol: '₫', rateFromVnd: 1 },
  language: { code: 'vi', label: 'Tiếng Việt', region: 'Việt Nam' },
  // docs/01 TK-09 — display timezone; null is the device's own clock.
  timeZone: null,

  // catalogue
  home: null,
  homeLoading: true,
  results: { total: 0, items: [], page: 1, pageSize: 24 },
  loading: true,
  loadingMore: false,
  detail: null,
  detailLoading: false,
  // Told apart from "not loaded yet": a listing that is gone renders a 404
  // page, while a null detail with nothing else known renders the skeleton.
  // Until this existed an unknown slug left the skeleton on screen forever,
  // which is what a crawler saw as a blank page answering 200.
  detailMissing: false,
  quote: null,
  suggestions: [],

  // account
  user: null,
  authError: null,
  authBusy: false,
  sessions: [],

  // guest data
  favorites: [],
  favCount: 0,
  wishlists: [],
  activeWishlist: null,
  bookings: [],
  trip: null,
  tripLoading: false,
  bookingResult: null,
  bookingError: null,
  /** The booking currently holding dates while the guest is at checkout. */
  held: null,

  // hosting + ops
  hosting: null,
  hostingLoading: false,
  hostCalendar: null,
  threads: [],
  inboxFilter: 'all',
  activeThread: null,
  notifications: { unread: 0, items: [] },
  admin: null,

  // ui — chrome that React renders but that lives outside component state so
  // any handler can flip it without prop-drilling.
  hideMap: false,
  showTotalPrice: false,
  /**
   * docs/01 TM-24 — a bump asks the map to start the draw tool. The button that
   * does it lives in the filter sheet, which is a different component tree; a
   * counter rather than a boolean so a second request after the first has been
   * served still registers.
   *
   * TM-12 ("tìm khi di chuyển bản đồ") used to be a `searchOnMapMove` flag with
   * a checkbox on the map. It is now simply how the map behaves — see Maps.jsx.
   */
  drawRequest: 0,
  /** The map rectangle the current results were searched in, if any. */
  searchArea: null,
  /** docs/01 TM-24 — a hand-drawn search area, [{lat,lng}], if any. */
  searchPolygon: null,
  tab: 'homes',
  menu: null,            // 'account' | 'bell' | null
  overlay: null,         // key into the overlay registry
  suggestOpen: false,
  inspirationTab: null,
  photoIndex: null,
  // docs/01 TĐ-18 — what the share dialog is currently offering.
  share: null,
  /**
   * The arrival chosen on the first click, before a check-out exists.
   *
   * While it is set the committed checkIn/checkOut are left exactly as they
   * were — a search or a price quote running in the background keeps working on
   * the last real range — and the calendar paints this one day and nothing else.
   * One click used to light up two days: the day tapped and the night after it.
   */
  pickingFrom: null,

  // Which month the calendar is looking at. Its own state, because paging must
  // not touch the chosen dates: a check-out three months out is reached by
  // paging there, and rewriting the dates on the way makes that impossible.
  // Null means "wherever the check-in is".
  calendarMonth: null,

  // auth / profile modals
  authMode: 'login',     // login | register | forgot | reset | twoFactor
  // docs/01 TK-08 — the half-finished login: a challenge token, never a session.
  twoFactor: null,
  resetLink: null,
  resetToken: null,
  // docs/01 TK-04 / TK-05 — the language picker, and somebody else's profile.
  spokenLanguages: [],
  publicProfile: null,
  publicProfileLoading: false,

  // checkout
  checkoutStep: 0,
  payMethod: 'card',
  /* docs/07 §4 — which saved card, when the guest picked one. */
  payCardId: null,
  payCardLast4: null,
  // docs/01 ĐP-06 — take a deposit now instead of the whole amount.
  payDeposit: false,
  // docs/06 — the booking a Staylio Shield case is being opened for.
  shieldBooking: null,
  shieldSide: 'guest',
  // Spend the guest's balance on this booking.
  useCredit: false,
  // docs/01 ĐP-09 — the promo code the guest typed at checkout.
  couponCode: '',
  // docs/01 ĐP-17 — the private offer being booked, if the guest came from one.
  offerId: null,
  // docs/01 ĐP-10 — the guest ticked "I agree to the house rules".
  agreedToRules: false,
  // docs/01 MR-09 — the room type chosen on a hotel listing.
  roomTypeId: null,
  // docs/01 ĐP-07 — let other people pay their share instead of paying it all.
  splitBill: false,
  splitEmails: '',
  checkoutName: '',
  checkoutEmail: '',
  /** docs/07 §2.5 — how a host reaches somebody who booked with no account. */
  checkoutPhone: '',
  checkoutNote: '',
  /** Arrival hour, who is staying, work trip, special requests — told to the host. */
  checkoutDetails: {
    estimatedArrivalHour: null, forSomeoneElse: false, stayingGuestName: '',
    isBusinessTrip: false, specialRequests: []
  },
  cancelPreview: null,

  // hosting
  hostingTab: 'today',
  editingListing: null,
  // docs/09 §2.1 MR-E-01 — the experience open in the host's editor, if any.
  editingExperience: null,
  // docs/09 §3.2 MR-S-01 — the service open in the provider's editor, if any.
  editingService: null,
  // docs/01 QL-13 — the warning shown before a host cancels a guest's stay.
  hostCancel: null,
  uploading: false,
  hostMonthOffset: 0,
  hostCalcNights: 20,
  hostCalcRate: 1_500_000,
  guestReviewBooking: null,
  guestReviewDraft: { rating: 5, wouldHostAgain: true },

  // reviews
  reviewBooking: null,
  reviewDraft: null,
  /** docs/01 ĐG-08 — the review modal is opened over an existing review. */
  reviewEditing: false,
  reviewQuery: '',
  reviewSort: 'recent',
  /** docs/01 TĐ-11 — 'all', or a language code present in this listing's reviews. */
  reviewLanguage: 'all'
};

/* ------------------------------------------------------------ react bridge */

export function subscribe(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export const getSnapshot = () => version;

export function notify() {
  version++;
  listeners.forEach(fn => fn());
}

/** Applies a patch and notifies in one step. */
export function set(patch) {
  Object.assign(state, patch);
  notify();
}

/* ----------------------------------------------------------------- helpers */

export const totalGuests = () => state.guests.adults + state.guests.children;

export function guestLabel() {
  const g = state.guests;
  const parts = [`${g.adults + g.children} ${t('khách')}`];
  if (g.infants) parts.push(`${g.infants} ${t('em bé')}`);
  if (g.pets) parts.push(`${g.pets} ${t('thú cưng')}`);
  return parts.join(', ');
}

/**
 * True while nothing is narrowed down. Airbnb shows curated carousels in that
 * state and switches to the flat result grid once you search.
 */
export function isDiscovery() {
  return !state.q.trim()
    && state.category === 'all'
    && state.amenities.length === 0
    && state.roomType === 'any'
    && !state.bedrooms && !state.beds && !state.bathrooms
    && !state.superhostOnly && !state.guestFavoriteOnly
    && !state.instantBookOnly && !state.freeCancellationOnly
    && state.hostLanguages.length === 0
    && !state.minRating && !state.payAtPropertyOnly && !state.maxCentreKm
    && (!state.meta || (state.minPrice <= state.meta.minPrice && state.maxPrice >= state.meta.maxPrice));
}

export function activeFilterCount() {
  const meta = state.meta;
  let n = state.amenities.length;
  if (state.category !== 'all') n++;
  if (meta && state.maxPrice < meta.maxPrice) n++;
  if (meta && state.minPrice > meta.minPrice) n++;
  if (state.roomType !== 'any') n++;
  if (state.bedrooms) n++;
  if (state.beds) n++;
  if (state.bathrooms) n++;
  if (state.superhostOnly) n++;
  if (state.guestFavoriteOnly) n++;
  if (state.instantBookOnly) n++;
  if (state.freeCancellationOnly) n++;
  if (state.hostLanguages.length) n++;
  if (state.minRating) n++;
  if (state.payAtPropertyOnly) n++;
  if (state.maxCentreKm) n++;
  return n;
}

export function resetFilters() {
  const meta = state.meta;
  Object.assign(state, {
    category: 'all',
    amenities: [],
    roomType: 'any',
    bedrooms: 0,
    beds: 0,
    bathrooms: 0,
    superhostOnly: false,
    guestFavoriteOnly: false,
    instantBookOnly: false,
    freeCancellationOnly: false,
    hostLanguages: [],
    minRating: 0,
    payAtPropertyOnly: false,
    maxCentreKm: 0,
    minPrice: meta ? meta.minPrice : 0,
    maxPrice: meta ? meta.maxPrice : 0
  });
}

/* ------------------------------------------------------------------ toasts */

let toastId = 0;
export const toasts = { items: [] };

/**
 * The one place every transient message passes through, and so the only place
 * worth translating them.
 *
 * Two kinds arrive here and neither went through the dictionary before: 177
 * calls pass `err.message`, which the server composes in Vietnamese, and 72
 * more pass a Vietnamese literal written at the call site. Both reached a
 * reader of one of the other seven languages exactly as typed. Wrapping them
 * here covers both without touching 249 call sites, and t() hands back a string
 * it has no entry for untouched — so a message nobody has translated yet still
 * reads the way it does today.
 */
export function toast(message) {
  const id = ++toastId;
  toasts.items = [...toasts.items, { id, message: t(message) }];
  notify();
  setTimeout(() => {
    // Named `item`, not `t`: that is the translate function, and a parameter
    // shadowing it makes any t('…') in the same scope silently wrong rather
    // than an error — the trap CLAUDE.md §4 records twice.
    toasts.items = toasts.items.filter(item => item.id !== id);
    notify();
  }, 2800);
}

/**
 * Sharing a listing, from the page header and from the photo viewer. Both offer
 * it, so both had better do the same thing.
 */
/**
 * docs/01 TĐ-18 — "qua link, mạng xã hội, email". The sheet the device offers is
 * still the best answer on a phone, so it stays first; the dialog behind it is
 * what gives a desktop browser somewhere to go, since navigator.share is absent
 * on most of them and the button did nothing but copy.
 */
export function shareListing(card) {
  set({ share: { title: card.title, url: location.href }, overlay: 'share', menu: null });
}

export async function shareViaDevice(share) {
  if (!navigator.share) return;
  // A dismissal is not a failure — the dialog stays open behind it either way.
  try { await navigator.share({ title: share.title, url: share.url }); } catch { /* dismissed */ }
}

export async function copyShareLink(url) {
  try { await navigator.clipboard.writeText(url); toast('Đã sao chép liên kết chỗ nghỉ'); }
  catch { toast(url); }
}

/* --------------------------------------------------------------- bootstrap */

export async function loadMeta() {
  try {
    const meta = await api.meta();
    state.meta = meta;
    if (!state.maxPrice) state.maxPrice = meta.maxPrice;
    if (!state.minPrice) state.minPrice = meta.minPrice;

    const savedCurrency = meta.currencies.find(c => c.code === localStorage.getItem('sh_currency'));
    if (savedCurrency) { state.currency = savedCurrency; setCurrency(savedCurrency); }

    const savedLang = meta.languages.find(l => l.code === localStorage.getItem('sh_language'));
    if (savedLang) { state.language = savedLang; setLocale(savedLang.code); }

    // docs/01 TK-09 — the third of "ngôn ngữ, tiền tệ, múi giờ". Null means the
    // device's clock; setTimeZone validates and drops an id Intl rejects.
    const savedZone = localStorage.getItem('sh_timezone');
    if (savedZone) { state.timeZone = savedZone; setTimeZone(savedZone); }
  } catch (err) {
    state.metaError = err.message;
  }
  notify();
}

/* ------------------------------------------------------------------ search */

export function searchParams(page = 1) {
  const meta = state.meta;
  const area = state.searchArea;
  return {
    ...(area ?? {}),
    // docs/01 TM-24 — the hand-drawn area, as "lat,lng;lat,lng;…".
    polygon: state.searchPolygon && state.searchPolygon.length >= 3
      ? state.searchPolygon.map(p => `${p.lat},${p.lng}`).join(';') : undefined,
    // Dates go to the server so it can price every card with the same engine
    // checkout uses (docs/00 §6.8).
    checkIn: state.checkIn,
    checkOut: state.checkOut,
    // A loose wish instead of two firm dates (docs/01 TM-06, TM-07).
    stay: state.stay !== 'exact' ? state.stay : undefined,
    flex: state.flexDays || undefined,
    months: state.stay === 'months' ? state.stayMonths : undefined,
    startMonths: state.stay === 'months' && state.startMonths.length
      ? state.startMonths.join(',') : undefined,
    q: state.q.trim() || undefined,
    category: state.category !== 'all' ? state.category : undefined,
    minPrice: meta && state.minPrice > meta.minPrice ? state.minPrice : undefined,
    maxPrice: meta && state.maxPrice < meta.maxPrice ? state.maxPrice : undefined,
    guests: totalGuests(),
    // totalGuests() is adults + children, so a pet never reached the server and
    // every card quoted a stay without the pet fee while the booking panel -
    // which asks /api/quote with the whole party - quoted it with (docs/00 §6.8).
    infants: state.guests.infants || undefined,
    pets: state.guests.pets || undefined,
    amenities: state.amenities.length ? state.amenities : undefined,
    sort: state.sort,
    roomType: state.roomType !== 'any' ? state.roomType : undefined,
    bedrooms: state.bedrooms || undefined,
    beds: state.beds || undefined,
    bathrooms: state.bathrooms || undefined,
    superhost: state.superhostOnly || undefined,
    guestFavorite: state.guestFavoriteOnly || undefined,
    instantBook: state.instantBookOnly || undefined,
    freeCancellation: state.freeCancellationOnly || undefined,
    hostLanguages: state.hostLanguages.length ? state.hostLanguages : undefined,
    minRating: state.minRating || undefined,
    payAtProperty: state.payAtPropertyOnly || undefined,
    maxCentreKm: state.maxCentreKm || undefined,
    page,
    pageSize: 24
  };
}

let searchToken = 0;

export async function runSearch({ page = 1 } = {}) {
  const token = ++searchToken;
  state.loading = true;
  notify();

  try {
    const data = await api.search(searchParams(page));
    if (token !== searchToken) return;
    state.results = data;
  } catch (err) {
    if (token !== searchToken) return;
    state.results = { total: 0, items: [], page: 1, pageSize: 24 };
    toast(err.message);
  } finally {
    if (token === searchToken) {
      state.loading = false;
      notify();
    }
  }
}

/** Cached per date range, since the rails carry a priced total for those nights. */
let homeKey = '';

export async function loadHome() {
  // Infants and pets are sent too, and pets are priced (docs/03 §1), so they
  // belong in the key: without them the rails kept the old prices.
  const key = `${state.checkIn}|${state.checkOut}|${totalGuests()}|${state.guests.infants || 0}|${state.guests.pets || 0}`;
  if (state.home && key === homeKey) { notify(); return; }

  state.homeLoading = true;
  notify();
  try {
    state.home = await api.home({
      checkIn: state.checkIn,
      checkOut: state.checkOut,
      guests: totalGuests(),
      infants: state.guests.infants || undefined,
      pets: state.guests.pets || undefined
    });
    homeKey = key;
  } catch (err) {
    toast(err.message);
  } finally {
    state.homeLoading = false;
    notify();
  }
}

export async function loadSuggestions() {
  try {
    state.suggestions = await api.suggest(state.q);
    notify();
  } catch { /* suggestions never block typing */ }
}

/* ----------------------------------------------------------------- account */

/**
 * docs/01 QT-08 — the rollout, read once at boot. It was computed server-side
 * from the first day and never asked for, so every flag an admin set moved
 * nothing at all.
 */
export async function loadFeatures() {
  try { state.features = await api.featureFlags() ?? {}; }
  catch { state.features = {}; }
  notify();
}

/** True unless the server has explicitly said this feature is off for them. */
export const featureOn = key => state.features[key] !== false;

export async function loadMe() {
  try {
    state.user = await api.me();
    adoptAccountPreferences(state.user);
  } catch {
    state.user = null;
  }
  notify();
}

/*
 * docs/01 TK-09 — the account's saved "ngôn ngữ, tiền tệ, múi giờ" win over
 * this browser's localStorage: that is the whole point of putting them on the
 * account, so a guest who chose 한국어 on their laptop gets 한국어 on their
 * phone the moment they sign in. Null on the account means "never chosen" and
 * the local choice stands untouched.
 */
function adoptAccountPreferences(me) {
  if (!me) return;

  if (me.language && me.language !== state.language.code) {
    const lang = state.meta?.languages.find(l => l.code === me.language);
    if (lang) { state.language = lang; setLocale(lang.code); localStorage.setItem('sh_language', lang.code); }
  }
  if (me.currency && me.currency !== state.currency.code) {
    const cur = state.meta?.currencies.find(c => c.code === me.currency);
    if (cur) { state.currency = cur; setCurrency(cur); localStorage.setItem('sh_currency', cur.code); }
  }
  if (me.timeZoneId && me.timeZoneId !== state.timeZone) {
    state.timeZone = me.timeZoneId;
    setTimeZone(me.timeZoneId);
    localStorage.setItem('sh_timezone', me.timeZoneId);
  }

  // The other adoption: an account that has never chosen, signed into by a
  // browser that has. Without this, the guest who browsed in 한국어 and then
  // registered is silently reset to Vietnamese by their own new account the
  // next time the account's null wins somewhere.
  if (!me.language && !me.currency && !me.timeZoneId
      && (state.language.code !== 'vi' || state.currency.code !== 'VND' || state.timeZone)) {
    syncPreferences();
  }
}

/*
 * The other direction: a picker change made while signed in is written to the
 * account. Fire-and-forget with a swallow — a preference save failing must
 * never break the picker that just worked locally.
 */
function syncPreferences() {
  if (!state.user) return;
  api.savePreferences({
    language: state.language.code,
    currency: state.currency.code,
    timeZoneId: state.timeZone,
  }).catch(() => {});
}

async function runAuth(fn) {
  state.authBusy = true;
  state.authError = null;
  notify();
  try {
    const result = await fn();

    // docs/01 TK-08 — the password was right but a code is still owed. There is
    // no session yet, so nothing about the account may be loaded here.
    if (result?.challenge) {
      state.twoFactor = result;
      state.authMode = 'twoFactor';
      return true;
    }

    state.user = result;
    state.twoFactor = null;
    toast(`Xin chào ${state.user.fullName}!`);
    await Promise.all([loadFavorites(), loadBookings(), loadNotifications()]);
    return true;
  } catch (err) {
    state.authError = err.message;
    return false;
  } finally {
    state.authBusy = false;
    notify();
  }
}

export const login = body => runAuth(() => api.login(body));

/** docs/01 TK-08 — the second step: the code that finishes a login. */
export const submitTwoFactor = code =>
  runAuth(() => api.twoFactorVerify({ challenge: state.twoFactor?.challenge, code }));

export async function resendTwoFactor() {
  try {
    state.twoFactor = await api.twoFactorResend(state.twoFactor?.challenge);
    toast('Đã gửi lại mã.');
  } catch (err) {
    state.authError = err.message;
  }
  notify();
}
export const register = body => runAuth(() => api.register(body));

export async function logout() {
  try { await api.logout(); } catch { /* the cookie goes either way */ }
  Object.assign(state, {
    user: null, hosting: null, threads: [], favorites: [], favCount: 0,
    bookings: [], admin: null, wishlists: [], notifications: { unread: 0, items: [] }
  });
  toast('Đã đăng xuất.');
  notify();
}

export async function saveProfile(body) {
  try {
    state.user = await api.updateProfile(body);
    toast('Đã lưu hồ sơ.');
    return true;
  } catch (err) {
    toast(err.message);
    return false;
  } finally {
    notify();
  }
}

/** docs/01 TK-04 — fetched once; the list only changes when the server does. */
export async function loadSpokenLanguages() {
  if (state.spokenLanguages.length) return;
  try {
    state.spokenLanguages = await api.profileOptions();
  } catch { /* the editor falls back to whatever the profile already holds */ }
  notify();
}

/** docs/01 TK-05 — somebody else's public profile. */
export async function loadPublicProfile(id) {
  state.publicProfileLoading = true;
  state.publicProfile = null;
  notify();
  try {
    state.publicProfile = await api.publicProfile(id);
  } catch (err) {
    toast(err.message);
  } finally {
    state.publicProfileLoading = false;
    notify();
  }
}

/**
 * docs/01 CN-01 — a guest becomes a host. Lives here rather than in the account
 * menu because the "Cho thuê nhà" landing page needs the same three lines, and
 * having only the menu know how left that page's own button with nowhere to go.
 */
export async function becomeHost() {
  try {
    await api.becomeHost();
    await loadMe();
    return true;
  } catch (err) {
    toast(err.message);
    return false;
  }
}

export async function loadSessions() {
  try {
    state.sessions = await api.sessions();
  } catch (err) { toast(err.message); }
  notify();
}

/* --------------------------------------------------------------- favorites */

export async function loadFavorites() {
  try {
    const favorites = await api.favorites();
    state.favorites = favorites;
    state.favCount = favorites.length;
  } catch { /* wishlist is non-critical on first paint */ }
  notify();
}

export async function toggleFavorite(id) {
  const flip = card => card.id === id ? { ...card, isFavorite: !card.isFavorite } : card;
  const flipAll = () => {
    state.results = { ...state.results, items: state.results.items.map(flip) };
    if (state.home) {
      state.home = { ...state.home, sections: state.home.sections.map(s => ({ ...s, items: s.items.map(flip) })) };
    }
    if (state.detail?.card.id === id) {
      state.detail = { ...state.detail, card: flip(state.detail.card) };
    }
    if (state.activeWishlist) {
      // A list holds { card, note } entries, not cards: flipping the entry
      // itself matched nothing and the heart stayed red.
      state.activeWishlist = {
        ...state.activeWishlist,
        items: state.activeWishlist.items.map(e => e?.card ? { ...e, card: flip(e.card) } : flip(e))
      };
    }
    notify();
  };

  flipAll();

  try {
    const res = await api.toggleFavorite(id);
    state.favCount = res.count;
    toast(res.isFavorite ? 'Đã lưu vào danh sách yêu thích' : 'Đã bỏ khỏi danh sách yêu thích');
    notify();
  } catch (err) {
    flipAll();
    toast(err.message);
  }
}

export async function loadWishlists() {
  try {
    state.wishlists = await api.wishlists();
  } catch (err) { toast(err.message); }
  notify();
}

export async function openWishlist(id) {
  try {
    state.activeWishlist = await api.wishlist(id);
  } catch (err) { toast(err.message); }
  notify();
}

/* ------------------------------------------------------------------ detail */

export async function loadDetail(idOrSlug) {
  state.detailLoading = true;
  state.detail = null;
  state.detailMissing = false;
  state.bookingResult = null;
  state.bookingError = null;
  notify();

  try {
    state.detail = await api.listing(idOrSlug, {
      checkIn: state.checkIn,
      checkOut: state.checkOut,
      guests: totalGuests(),
      // Same reason as searchParams: the header price has to agree with the
      // panel underneath it.
      infants: state.guests.infants || undefined,
      pets: state.guests.pets || undefined
    });
    await refreshQuote();
  } catch (err) {
    state.detail = null;
    // A listing that is not there is not an error to apologise for in a toast —
    // the page itself says so. Anything else (offline, a 500) still is.
    state.detailMissing = err.status === 404;
    if (!state.detailMissing) toast(err.message);
  } finally {
    state.detailLoading = false;
    notify();
  }
}

export async function refreshQuote() {
  if (!state.detail) return;
  try {
    state.quote = await api.quote({
      listingId: state.detail.card.id,
      checkIn: state.checkIn,
      checkOut: state.checkOut,
      adults: state.guests.adults,
      children: state.guests.children,
      infants: state.guests.infants,
      pets: state.guests.pets,
      roomTypeId: state.roomTypeId,
      // docs/01 ĐP-09 — the code is priced server-side so the guest sees the
      // discount, or the reason it did not apply, before committing.
      couponCode: state.couponCode || undefined
    });
  } catch {
    state.quote = null;
  }
  notify();
}

/** docs/01 ĐP-09 — apply or clear a promo code, then re-price. */
export function applyCoupon(code) {
  state.couponCode = (code ?? '').trim();
  notify();
  return refreshQuote();
}

/** docs/01 MR-09 — picking a room re-prices the panel against that room. */
export function setRoom(roomTypeId) {
  state.roomTypeId = roomTypeId;
  notify();
  refreshQuote();
}

/**
 * docs/01 ĐP-02 — entering checkout takes the dates off the market for 15
 * minutes. Nothing is charged here; `pay` does that.
 */
export async function holdDates(extra = {}) {
  if (!state.detail) return null;
  set({ bookingError: null });

  try {
    const held = await api.hold({
      listingId: state.detail.card.id,
      checkIn: state.checkIn,
      checkOut: state.checkOut,
      guests: totalGuests(),
      adults: state.guests.adults,
      children: state.guests.children,
      infants: state.guests.infants,
      pets: state.guests.pets,
      // docs/01 MR-09 — a hotel booking carries the room the guest picked.
      roomTypeId: state.roomTypeId,
      useCredit: state.useCredit,
      // docs/01 ĐP-09 — the code committed at the hold, re-checked at payment.
      couponCode: state.couponCode || undefined,
      // docs/01 ĐP-17 — the private offer whose price this booking is taking.
      offerId: state.offerId || undefined,
      // docs/01 ĐP-10 — the house-rules agreement.
      agreedToRules: state.agreedToRules,
      // docs/07 §2.5 — required when there is no account behind the booking.
      guestPhone: state.checkoutPhone || undefined,
      // docs/07 §6 — the currency this screen is rendering prices in. The rate
      // is stamped server-side; until 05/09/2026 neither field was ever sent,
      // which is why every booking's snapshot was null.
      displayCurrency: state.currency.code,
      estimatedArrivalHour: state.checkoutDetails.estimatedArrivalHour,
      stayingGuestName: state.checkoutDetails.forSomeoneElse
        ? (state.checkoutDetails.stayingGuestName.trim() || null) : null,
      isBusinessTrip: state.checkoutDetails.isBusinessTrip,
      specialRequests: state.checkoutDetails.specialRequests,
      ...extra
    });
    set({ held });
    return held;
  } catch (err) {
    set({ bookingError: err.message });
    return null;
  }
}

/** Charges a held booking. The server re-prices and refuses on a mismatch. */
export async function payHeld(extra = {}) {
  if (!state.held) return null;
  set({ bookingError: null });

  try {
    state.bookingResult = await api.pay(state.held.id, extra);
    state.held = null;

    // docs/07 §2.3 — paying by transfer leaves the booking where it was: held,
    // unpaid, waiting for money. The caller sends the guest to the QR instead,
    // and nothing here may congratulate them on a booking they have not paid.
    if (state.bookingResult.status !== 'PendingPayment')
      toast(`Đặt chỗ thành công — mã ${state.bookingResult.reference}`);

    await loadBookings();
    return state.bookingResult;
  } catch (err) {
    state.bookingError = err.message;
    return null;
  } finally {
    notify();
  }
}

/**
 * docs/01 ĐP-07 — turns the held booking into a split. Nobody is charged here;
 * everyone gets a link, and the dates are held for a day rather than 15 minutes.
 */
export async function openSplit(emails) {
  if (!state.held) return null;
  set({ bookingError: null });

  try {
    const split = await api.openSplit(state.held.id, emails);
    state.split = split;
    state.held = null;
    state.splitBill = false;
    state.splitEmails = '';
    toast(`Đã gửi liên kết cho ${split.shares.length - 1} người. Đơn giữ chỗ trong 24 giờ.`);
    await loadBookings();
    return split;
  } catch (err) {
    state.bookingError = err.message;
    toast(err.message);
    return null;
  } finally {
    notify();
  }
}

/** docs/01 ĐP-06 — the guest settles the rest before its date comes round. */
export async function payBalance(bookingId) {
  try {
    const updated = await api.payBalance(bookingId);
    // docs/07 §13 — the deposit went through a gateway, so the rest does too.
    if (updated.gatewayRedirectUrl) {
      window.location.assign(updated.gatewayRedirectUrl);
      return updated;
    }
    state.trip = updated;
    toast('Đã thanh toán phần còn lại.');
    await loadBookings();
    return updated;
  } catch (err) {
    toast(err.message);
    return null;
  } finally {
    notify();
  }
}

/** Leaving checkout without paying puts the dates straight back on sale. */
export async function releaseHold() {
  const held = state.held;
  if (!held) return;
  set({ held: null });
  try { await api.release(held.id); } catch { /* the sweep will expire it anyway */ }
}

/* ------------------------------------------------------------------- trips */

export async function loadBookings() {
  try {
    state.bookings = await api.bookings();
  } catch { /* trips need an account; silence is fine when anonymous */ }
  notify();
}

export async function loadTrip(id) {
  state.tripLoading = true;
  state.trip = null;
  notify();
  try {
    state.trip = await api.booking(id);
  } catch (err) {
    toast(err.message);
  } finally {
    state.tripLoading = false;
    notify();
  }
}

export async function submitReview(bookingId, body, editing = false) {
  try {
    // docs/03 §7 — the server says whether it went public straight away or is
    // waiting on the host, and that is what the guest needs to hear.
    // docs/01 ĐG-08 — the correction path answers 204, so there is no message
    // to read back and the confirmation is written here instead.
    // docs/01 TĐ-11 — the language the writer is reading the site in. Nothing
    // on the server knows it: the choice lives in this browser.
    const withLanguage = { ...body, language: state.language?.code ?? null };

    const result = editing
      ? (await api.editReview(bookingId, withLanguage), { message: 'Đã sửa đánh giá.' })
      : await api.review(bookingId, withLanguage);
    toast(result?.message ?? 'Cảm ơn bạn đã đánh giá!');
    await loadBookings();
    if (state.trip?.id === bookingId) await loadTrip(bookingId);
    return true;
  } catch (err) {
    toast(err.message);
    return false;
  }
}

/* ----------------------------------------------------------------- hosting */

export async function loadHosting() {
  if (!state.user) return;
  state.hostingLoading = true;
  notify();
  try {
    state.hosting = await api.hostDashboard();
  } catch (err) {
    toast(err.message);
  } finally {
    state.hostingLoading = false;
    notify();
  }
}

export async function saveListing(id, body) {
  try {
    const saved = id ? await api.updateListing(id, body) : await api.createListing(body);
    toast(id ? 'Đã cập nhật chỗ nghỉ.' : 'Đã đăng chỗ nghỉ mới.');
    await Promise.all([loadHosting(), loadMe()]);
    return saved;
  } catch (err) {
    toast(err.message);
    return null;
  }
}

export async function removeListing(id) {
  try {
    await api.deleteListing(id);
    toast('Đã xoá chỗ nghỉ.');
    await loadHosting();
  } catch (err) { toast(err.message); }
}

export async function loadHostCalendar(listingId) {
  try {
    state.hostCalendar = { listingId, ...(await api.hostCalendar(listingId)) };
  } catch (err) {
    toast(err.message);
    state.hostCalendar = null;
  }
  notify();
}

export async function respondBooking(id, action) {
  try {
    await api.respondBooking(id, action);
    toast(action === 'confirm' ? 'Đã xác nhận đặt chỗ.' : 'Đã từ chối đặt chỗ.');
    await loadHosting();
  } catch (err) { toast(err.message); }
}

/** docs/01 CĐ-06 — the host accepts or rejects a guest's change request. */
export async function respondChange(bookingId, reqId, accept) {
  try {
    await api.respondChange(bookingId, reqId, accept);
    toast(accept ? 'Đã đổi lịch cho khách.' : 'Đã từ chối yêu cầu đổi lịch.');
    await loadHosting();
  } catch (err) { toast(err.message); }
}

/**
 * docs/01 QL-13 — a host cancelling a confirmed stay is shown what follows
 * before they confirm, not after: the refund, the automatic Staylio Shield case
 * inside 30 days, and what it does to their Superhost cancellation rate.
 */
export async function previewHostCancel(id) {
  try {
    // The id travels with the preview: the server's payload names the booking
    // by reference, and the cancel call needs the number.
    state.hostCancel = { ...await api.hostCancelPreview(id), id };
    state.overlay = 'host-cancel';
  } catch (err) { toast(err.message); }
  notify();
}

export async function confirmHostCancel(id, reason) {
  try {
    await api.hostCancelBooking(id, reason);
    state.hostCancel = null;
    state.overlay = null;
    toast('Đã huỷ đơn và hoàn tiền cho khách.');
    await loadHosting();
  } catch (err) { toast(err.message); }
  notify();
}

/* --------------------------------------------------------------- messaging */

export async function loadThreads() {
  if (!state.user) return;
  try {
    state.threads = await api.threads(state.inboxFilter);
  } catch (err) { toast(err.message); }
  notify();
}

/** docs/01 TN-05 — switch the inbox filter and reload. */
export function setInboxFilter(filter) {
  state.inboxFilter = filter;
  notify();
  return loadThreads();
}

/** docs/01 TN-05 — archive or restore a thread for the viewer, then refresh. */
export async function archiveThread(id, on) {
  try {
    await api.archiveThread(id, on);
    await loadThreads();
  } catch (err) { toast(err.message); }
}

export async function openThread(id) {
  try {
    state.activeThread = await api.thread(id);
    await loadThreads();
  } catch (err) { toast(err.message); }
  notify();
}

export async function sendMessage(body) {
  try {
    state.activeThread = await api.sendMessage(body);
    await loadThreads();
    return state.activeThread;
  } catch (err) {
    toast(err.message);
    return null;
  } finally {
    notify();
  }
}

/** docs/01 QL-14 — the host sends a private offer, then the thread reloads with it. */
export async function sendOffer(threadId, body) {
  state.activeThread = await api.sendOffer(threadId, body);
  await loadThreads();
  notify();
}

/** docs/01 ĐP-17 — the host withdraws a still-pending offer. */
export async function withdrawOffer(offerId) {
  try {
    await api.withdrawOffer(offerId);
    if (state.activeThread) await openThread(state.activeThread.summary.id);
  } catch (err) { toast(err.message); }
}

/**
 * docs/01 ĐP-17 — the guest books a private offer. It carries the offer's dates,
 * guests and id into the normal checkout, so the one booking path prices it,
 * holds it and takes the money exactly as any other stay.
 */
export async function bookOffer(thread, offer) {
  state.checkIn = offer.checkIn;
  state.checkOut = offer.checkOut;
  state.guests = { adults: Math.max(1, offer.guests), children: 0, infants: 0, pets: 0 };
  state.offerId = offer.id;
  await loadDetail(thread.summary.listingSlug);
  set({ overlay: 'checkout', checkoutStep: 0, menu: null });
}

/* ----------------------------------------------------------- notifications */

export async function loadNotifications() {
  if (!state.user) return;
  try {
    state.notifications = await api.notifications();
  } catch { /* the bell is optional chrome */ }
  notify();
}

/* ------------------------------------------------------------------- admin */

export async function loadAdmin() {
  try {
    state.admin = await api.adminOverview();
  } catch (err) {
    toast(err.message);
    state.admin = null;
  }
  notify();
}

/* ------------------------------------------------------------ preferences */

export function applyCurrency(code) {
  const c = state.meta?.currencies.find(x => x.code === code);
  if (!c) return;
  state.currency = c;
  setCurrency(c);
  localStorage.setItem('sh_currency', code);
  syncPreferences();
  notify();
}

export function applyTimeZone(id) {
  // docs/01 TK-09 — deadlines and timestamps in the clock the reader asked
  // for. Dates (check-in, check-out) stay on the device clock on purpose: a
  // date is not an instant, and shifting it westward moves the stay by a day.
  const zone = id || null;
  state.timeZone = zone;
  setTimeZone(zone);
  if (zone) localStorage.setItem('sh_timezone', zone);
  else localStorage.removeItem('sh_timezone');
  syncPreferences();
  notify();
}

export function applyLanguage(code) {
  const l = state.meta?.languages.find(x => x.code === code);
  if (!l) return;
  state.language = l;
  // Dates and numbers belong to the language too: switching to 한국어 and still
  // reading "19 tháng 8, 2026" says the switch did not really take.
  setLocale(code);
  localStorage.setItem('sh_language', code);
  syncPreferences();
  notify();
}

/* ------------------------------------------------------------- ui actions */

export const openOverlay = kind =>
  // A picker opened afresh should be looking at the dates it is showing, not at
  // wherever it was last paged to.
  set({ overlay: kind, menu: null, ...(kind === 'dates' ? { calendarMonth: null } : {}) });
/**
 * docs/01 AT-02 — one dialog serves four subjects, so it has to be told which one
 * it is looking at. Subject and overlay are set together: opened any other way the
 * dialog would come up pointing at whatever was reported last.
 */
export const openReport = (target, subjectId, title) =>
  set({ report: { target, subjectId, title }, overlay: 'report', menu: null });

export const closeOverlay = () =>
  set({ overlay: null, photoIndex: null, report: null, share: null, couponCode: '', offerId: null, agreedToRules: false });
export const openMenu = kind => set({ menu: state.menu === kind ? null : kind });

/** Signed-out guests get the login modal instead of the action they asked for. */
export function requireAuth() {
  if (state.user) return true;
  set({ authMode: 'login', authError: null, overlay: 'login', menu: null });
  return false;
}

/* ------------------------------------------------------- dates and guests */

const blockedNights = () => new Set(state.detail?.unavailableDates ?? []);

function rangeHasBlockedNight(fromIso, toIso) {
  const blocked = blockedNights();
  if (!blocked.size) return false;
  for (const d = parseIso(fromIso); isoOf(d) < toIso; d.setDate(d.getDate() + 1)) {
    if (blocked.has(isoOf(d))) return true;
  }
  return false;
}

/**
 * Two-click range picking: the first click sets check-in with a one-night
 * default, the second closes the range, any later click starts over.
 */
/**
 * One click, one date. The first picks the arrival and nothing else; the second
 * completes the stay. A second click on or before the arrival starts again
 * rather than making a stay of zero nights.
 */
export function pickDate(iso) {
  // Pin the view before anything moves. The anchor falls back to checkIn, so
  // completing a stay on the right-hand month used to slide that month over to
  // the left under the cursor — the calendar walking away mid-click.
  if (state.calendarMonth === null) state.calendarMonth = isoOf(calendarAnchor());

  const from = state.pickingFrom;

  if (from === null || iso <= from) {
    state.pickingFrom = iso;
    notify();
    return;
  }

  const previous = { checkIn: state.checkIn, checkOut: state.checkOut };
  state.checkIn = from;
  state.checkOut = iso;
  state.pickingFrom = null;

  if (rangeHasBlockedNight(state.checkIn, state.checkOut)) {
    Object.assign(state, previous);
    toast('Khoảng ngày này đã có người đặt. Chọn ngày khác nhé.');
    notify();
    return;
  }

  normaliseDates();
}

/**
 * Closing the picker with only an arrival chosen commits it as a one-night stay.
 * Leaving it half-picked would mean the bar shows one date while the search runs
 * on the range from before — the bar lying about the search.
 */
export function settleDates() {
  const from = state.pickingFrom;
  if (from === null) return;

  state.pickingFrom = null;
  state.checkIn = from;

  const out = parseIso(from);
  out.setDate(out.getDate() + 1);
  state.checkOut = isoOf(out);

  normaliseDates();
}

/** The first of whichever month the calendar is showing on its left panel. */
export function calendarAnchor() {
  const from = parseIso(state.calendarMonth ?? state.checkIn);
  return new Date(from.getFullYear(), from.getMonth(), 1, 12);
}

/**
 * Pages the calendar. It moves the view and nothing else — this used to move the
 * check-in date with it, so paging forward to find a check-out three months away
 * silently rewrote the dates you had just chosen.
 */
export function shiftCalendar(dir) {
  const d = calendarAnchor();
  d.setMonth(d.getMonth() + dir);

  // Nobody can book the past, so there is nothing to see behind this month.
  const floor = parseIso(todayIso());
  if (d < new Date(floor.getFullYear(), floor.getMonth(), 1, 12)) return;

  state.calendarMonth = isoOf(d);
  notify();
}

/** Puts the view back on the chosen dates, for when the picker is opened afresh. */
export function resetCalendarView() {
  state.calendarMonth = null;
}

export function applyDatePreset(key) {
  const start = parseIso(todayIso());
  if (key === 'weekend') {
    const daysToFriday = (5 - start.getDay() + 7) % 7 || 7;
    start.setDate(start.getDate() + daysToFriday);
    state.checkIn = isoOf(start);
    start.setDate(start.getDate() + 2);
    state.checkOut = isoOf(start);
  } else {
    const spans = { week: 7, fortnight: 14, month: 30 };
    start.setDate(start.getDate() + 7);
    state.checkIn = isoOf(start);
    start.setDate(start.getDate() + (spans[key] ?? 3));
    state.checkOut = isoOf(start);
  }

  // A preset chooses both ends at once, so nothing is half-picked afterwards,
  // and the view follows the dates it just set.
  state.pickingFrom = null;
  state.calendarMonth = null;
  normaliseDates();
}

export function clearDates() {
  state.checkIn = todayIso(9);
  state.checkOut = todayIso(12);
  state.stay = 'exact';
  state.flexDays = 0;
  state.startMonths = [];
  state.pickingFrom = null;
  // Dates that jump somewhere else take the view with them, or the picker sits
  // on a month with nothing selected in it.
  state.calendarMonth = null;
  normaliseDates();
}

/** docs/01 TM-06 & TM-07 — how loose the guest is willing to be about dates. */
export function setStayShape(patch) {
  Object.assign(state, patch);
  if (state.stay === 'exact' && state.flexDays === 0) normaliseDates();
  else notify();
}

/** Keeps check-out after check-in, then re-prices whatever screen is open. */
export function normaliseDates() {
  if (state.checkOut <= state.checkIn) {
    const out = parseIso(state.checkIn);
    out.setDate(out.getDate() + 1);
    state.checkOut = isoOf(out);
  }
  notify();
  if (state.detail) refreshQuote();
}

export function bumpGuest(key, delta) {
  const min = key === 'adults' ? 1 : 0;
  state.guests = { ...state.guests, [key]: Math.max(min, Math.min(16, state.guests[key] + delta)) };
  notify();
  if (state.detail) refreshQuote();
}

/** The book panel's single +/- pair, which only moves adults and children. */
export function bumpTotalGuests(delta) {
  const max = state.detail?.card.maxGuests ?? 16;
  const next = Math.min(max, Math.max(1, totalGuests() + delta));
  const diff = next - totalGuests();
  if (!diff) return;

  const g = { ...state.guests };
  if (diff > 0) g.adults += diff;
  else if (g.children > 0) g.children = Math.max(0, g.children + diff);
  else g.adults = Math.max(1, g.adults + diff);

  state.guests = g;
  notify();
  if (state.detail) refreshQuote();
}

/* ------------------------------------------------------------- filter ops */

export function toggleAmenity(key) {
  state.amenities = state.amenities.includes(key)
    ? state.amenities.filter(a => a !== key)
    : [...state.amenities, key];
  notify();
}

export function bumpCount(key, delta) {
  state[key] = Math.max(0, Math.min(8, (state[key] || 0) + delta));
  notify();
}

// docs/01 TM-23 — save the current search so new matches raise an alert.
export async function saveCurrentSearch() {
  const meta = state.meta;
  const label = (state.q.trim() || (state.category !== 'all' ? state.category : 'Tìm kiếm')) + ' — đã lưu';
  try {
    await api.saveSearch({
      label,
      q: state.q.trim() || null,
      category: state.category !== 'all' ? state.category : null,
      minPrice: meta && state.minPrice > meta.minPrice ? state.minPrice : null,
      maxPrice: meta && state.maxPrice < meta.maxPrice ? state.maxPrice : null,
      guests: totalGuests(),
      amenities: state.amenities.length ? state.amenities : null,
      roomType: state.roomType !== 'any' ? state.roomType : null,
      bedrooms: state.bedrooms || 0,
      superhostOnly: state.superhostOnly,
      instantBookOnly: state.instantBookOnly,
      hostLanguages: state.hostLanguages.length ? state.hostLanguages : null
    });
    toast('Đã lưu tìm kiếm. Sẽ báo khi có chỗ mới phù hợp.');
  } catch (err) { toast(err.message); }
}

// docs/01 TM-18 — toggle a host-language filter code.
export function toggleHostLanguage(code) {
  state.hostLanguages = state.hostLanguages.includes(code)
    ? state.hostLanguages.filter(c => c !== code)
    : [...state.hostLanguages, code];
  notify();
}
