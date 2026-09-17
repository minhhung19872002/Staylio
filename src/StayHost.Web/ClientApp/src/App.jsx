import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Navigate, Route, Routes, useLocation, useNavigate, useNavigationType } from 'react-router-dom';
import { useStore } from './lib/useStore.js';
import {
  loadMeta, loadMe, loadFeatures, loadFavorites, loadNotifications, set, state as store
} from './lib/store.js';
import { queryToSearch } from './lib/urlState.js';
import { setNavigator } from './lib/nav.js';
import { applyCanonical, resetPageMeta, setStructuredData, siteJsonLd } from './lib/seo.js';

import { Header } from './components/Header.jsx';
import { Footer } from './components/Footer.jsx';
import { ImpersonationBanner } from './components/ImpersonationBanner.jsx';
import { Overlay } from './components/modals/Overlay.jsx';
import { Toasts } from './components/Toasts.jsx';

import { Browse } from './pages/Browse.jsx';
import { Detail } from './pages/Detail.jsx';
import { Wishlists } from './pages/Wishlists.jsx';
import { Trips } from './pages/Trips.jsx';
import { Reviews } from './pages/Reviews.jsx';
import { FindBooking } from './pages/FindBooking.jsx';
import { Trip } from './pages/Trip.jsx';
import { Host } from './pages/Host.jsx';
import { Hosting } from './pages/Hosting.jsx';
import { Messages } from './pages/Messages.jsx';
import { Admin } from './pages/Admin.jsx';
import { Help } from './pages/Help.jsx';
import { Split } from './pages/Split.jsx';
import { Transfer } from './pages/Transfer.jsx';
import { PaymentResult } from './pages/PaymentResult.jsx';
import { Experiences, ExperienceBookings, ExperienceCheckout } from './pages/Experiences.jsx';
import { Services, ServiceBookings, ServiceCheckout } from './pages/Services.jsx';
import { Wallet } from './pages/Wallet.jsx';
import { Shield, ShieldTerms } from './pages/Shield.jsx';
import { Resolutions } from './pages/Resolutions.jsx';
import { UserProfile } from './pages/UserProfile.jsx';
import { City } from './pages/City.jsx';
import { SharedWishlist } from './pages/SharedWishlist.jsx';
import { MySanctions, AppealByToken } from './pages/Sanctions.jsx';
import { Neighbors } from './pages/Neighbors.jsx';
import { Friends } from './pages/Friends.jsx';
import { TripPlans } from './pages/TripPlans.jsx';
import { Settings } from './pages/Settings.jsx';
import { NotFound } from './pages/NotFound.jsx';

export function App() {
  const state = useStore();
  const location = useLocation();
  const navigate = useNavigate();
  const [booted, setBooted] = useState(false);

  setNavigator(navigate);

  // One bootstrap pass: reference data first (the URL's price bounds are
  // relative to it), then the signed-in identity and its dependent feeds.
  useEffect(() => {
    (async () => {
      await loadMeta();
      queryToSearch(location.search);
      setBooted(true);
      await loadMe();
      await Promise.all([loadFavorites(), loadNotifications(), loadFeatures()]);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A route change closes whatever chrome was open.
  useEffect(() => {
    set({ overlay: null, menu: null, suggestOpen: false, photoIndex: null });
  }, [location.pathname]);

  /*
   * Going somewhere new starts at the top of it.
   *
   * This used to watch the pathname alone, so the rail headings — which only
   * change the query string — left you a thousand pixels down a page whose
   * contents had just been replaced underneath you.
   *
   * It watches the whole location now, but only acts on a push. Typing in the
   * search box rewrites the query string on every keystroke with replace, and
   * yanking the page to the top each time would be far worse than the bug. A
   * pop is the back button, where the browser restores the old position itself.
   *
   * Layout effect, not effect: after paint the browser has already shown one
   * frame of the new page at the old scroll position, and that frame is the
   * flicker.
   */
  const navigationType = useNavigationType();

  useLayoutEffect(() => {
    if (navigationType !== 'PUSH') return;
    window.scrollTo({ top: 0, behavior: 'instant' });
    // navigationType is deliberately not a dependency: it describes how we
    // arrived at this location, not a value that changes on its own.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.pathname, location.search]);

  // Leaving the room page drops its detail so a stale quote can't leak into
  // the next screen's totals.
  useEffect(() => {
    if (!location.pathname.startsWith('/rooms/') && store.detail) {
      set({ detail: null, quote: null, bookingResult: null, bookingError: null });
    }
  }, [location.pathname]);

  // The document the browser was handed already carries this address's tags;
  // only a route change inside the app needs them put back to the defaults.
  const firstRoute = useRef(true);

  // A route change here never reloads the document, so the canonical and og:url
  // tags would otherwise keep pointing at whatever page was opened first — every
  // room sharing the home page's preview card, and every filtered city address
  // competing with the plain one for the same ranking.
  useEffect(() => {
    applyCanonical(location.pathname, location.search);

    // Back to the defaults on every navigation, then each page sets its own once
    // its data lands. Without the reset a room's title would follow the visitor
    // onto the next page — and a structured-data block describing the previous
    // room is read by Google as a claim about the page it is on, which is how a
    // whole site loses rich results rather than one page.
    //
    // Except on the first run. ShellSeo.cs has already written this address's
    // title, description and share picture into the document that just arrived,
    // and they are correct for every page type — including /experiences/:slug,
    // /services/:slug and /help/:slug, which have no setPageMeta of their own.
    // Resetting here would throw that away: on the pages that do set their own
    // it flickers to the home title until the fetch lands, and on the pages that
    // do not it replaces a correct title with the home page's, permanently.
    if (firstRoute.current) firstRoute.current = false;
    else resetPageMeta();
    setStructuredData(location.pathname === '/' ? siteJsonLd() : null);
    // location.search matters here: ?trang=2 is a different page in a series and
    // has to canonicalise to itself, not to page 1.
  }, [location.pathname, location.search]);

  /*
   * --header-h was a constant: 152px, which is the desktop header and nothing
   * else. Below 720px the real header is nearer 192px, because the search row
   * and the filter chips are stacked rather than beside each other — so every
   * sticky offset and every scroll-margin computed from it was wrong on a phone
   * by the height of a whole row. Measuring costs one ResizeObserver and makes
   * the number true at any width.
   */
  const headerRef = useRef(null);
  useLayoutEffect(() => {
    const el = headerRef.current;
    if (!el) return undefined;
    const apply = () => document.documentElement.style
      .setProperty('--header-h', `${Math.round(el.getBoundingClientRect().height)}px`);
    apply();
    const ro = new ResizeObserver(apply);
    ro.observe(el);
    return () => ro.disconnect();
  }, [state.metaError]);

  if (state.metaError) {
    return (
      <div className="shell" style={{ padding: '60px 0' }}>
        <div className="empty-state">
          <h3>Không kết nối được máy chủ</h3>
          <p>{state.metaError}</p>
        </div>
      </div>
    );
  }

  return <>
    <div id="app">
      {/* docs/08 §7.5 — above everything, for as long as the session lasts. */}
      {state.me?.role === 'Admin' && <ImpersonationBanner />}
      <header className="site-header" ref={headerRef}><Header /></header>
      <main id="main">
        {booted && (
          <Routes>
            <Route path="/" element={<Browse />} />
            <Route path="/rooms/:slug" element={<Detail />} />
            <Route path="/thanh-pho/:city" element={<City />} />
            <Route path="/wishlist/:token" element={<SharedWishlist />} />
            <Route path="/wishlists" element={<Wishlists />} />
            <Route path="/trips" element={<Trips />} />
            {/* docs/02 H1 — cần viết · tôi đã viết · về tôi */}
            <Route path="/danh-gia" element={<Reviews />} />
            {/* docs/07 §2.5 — a booking made without an account, found again */}
            <Route path="/dat-cho" element={<FindBooking />} />
            <Route path="/trips/:id" element={<Trip />} />
            <Route path="/host" element={<Host />} />
            <Route path="/hosting" element={<Hosting />} />
            {/* Links already sent in payout and review emails. */}
            <Route path="/hosting/earnings" element={<Navigate to="/hosting?tab=earnings" replace />} />
            <Route path="/hosting/reviews" element={<Navigate to="/hosting?tab=reviews" replace />} />
            <Route path="/messages" element={<Messages />} />
            <Route path="/messages/:id" element={<Messages />} />
            <Route path="/resolutions" element={<Resolutions />} />
            <Route path="/help" element={<Help />} />
            <Route path="/help/:slug" element={<Help />} />
            <Route path="/experiences" element={<Experiences />} />
            <Route path="/experiences/bookings" element={<ExperienceBookings />} />
            <Route path="/experiences/:slug/thanh-toan" element={<ExperienceCheckout />} />
            <Route path="/experiences/:slug" element={<Experiences />} />
            <Route path="/services" element={<Services />} />
            <Route path="/services/bookings" element={<ServiceBookings />} />
            {/* docs/07 §2 — picking an hour ends on a checkout page of its own,
                not in the dialog that offered it. */}
            <Route path="/services/:slug/thanh-toan" element={<ServiceCheckout />} />
            <Route path="/services/:slug" element={<Services />} />
            <Route path="/wallet" element={<Wallet />} />
            <Route path="/shield" element={<Shield />} />
            <Route path="/shield/terms" element={<ShieldTerms />} />
            <Route path="/shield/:id" element={<Shield />} />
            <Route path="/split/:token" element={<Split />} />
            {/* docs/07 §2.3 — the QR a guest waits on after choosing bank transfer. */}
            <Route path="/chuyen-khoan/:reference" element={<Transfer />} />
            {/* docs/07 §13 — where a licensed gateway sends the guest back to. */}
            <Route path="/thanh-toan/ket-qua" element={<PaymentResult />} />
            <Route path="/users/:id" element={<UserProfile />} />
            {/* docs/08 §8 — quyết định về mình, và cửa khiếu nại cho cả người đã bị khoá */}
            <Route path="/account/sanctions" element={<MySanctions />} />
            <Route path="/appeal" element={<AppealByToken />} />
            <Route path="/neighbors" element={<Neighbors />} />
            <Route path="/friends" element={<Friends />} />
            <Route path="/trip-plans" element={<TripPlans />} />
            {/* docs/02 F1 — the settings hub and its nine groups. */}
            <Route path="/cai-dat" element={<Settings />} />
            <Route path="/cai-dat/:group" element={<Settings />} />
            <Route path="/admin" element={<Admin />} />
            {/* An address no route answers is a 404, not the home page. Rendering
                Browse here meant every typo and every stale link came back as a
                working page — see the soft-404 note in Program.cs. */}
            <Route path="*" element={<NotFound />} />
          </Routes>
        )}
      </main>
      <footer className="site-footer"><Footer /></footer>
    </div>

    <Overlay />
    <Toasts />
  </>;
}
