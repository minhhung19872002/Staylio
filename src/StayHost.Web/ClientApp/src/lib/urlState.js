// The browse URL is the single source of truth for search criteria, so a link
// pasted into a new tab reproduces the same result set. These two helpers move
// state in and out of the query string.

import { state } from './store.js';
import { totalGuests } from './store.js';

export function searchToQuery() {
  const usp = new URLSearchParams();
  if (state.q.trim()) usp.set('q', state.q.trim());
  if (state.category !== 'all') usp.set('category', state.category);
  if (state.amenities.length) usp.set('amenities', state.amenities.join(','));
  if (state.roomType !== 'any') usp.set('roomType', state.roomType);
  if (state.meta && state.maxPrice < state.meta.maxPrice) usp.set('maxPrice', String(state.maxPrice));
  if (state.meta && state.minPrice > state.meta.minPrice) usp.set('minPrice', String(state.minPrice));
  if (state.bedrooms) usp.set('bedrooms', String(state.bedrooms));
  if (state.beds) usp.set('beds', String(state.beds));
  if (state.bathrooms) usp.set('bathrooms', String(state.bathrooms));
  if (state.sort !== 'reco') usp.set('sort', state.sort);
  if (state.superhostOnly) usp.set('superhost', '1');
  if (state.guestFavoriteOnly) usp.set('guestFavorite', '1');
  if (state.instantBookOnly) usp.set('instantBook', '1');
  if (state.freeCancellationOnly) usp.set('freeCancellation', '1');
  if (state.minRating) usp.set('minRating', String(state.minRating));
  if (state.payAtPropertyOnly) usp.set('payAtProperty', '1');
  if (state.maxCentreKm) usp.set('maxCentreKm', String(state.maxCentreKm));
  if (state.stars.length) usp.set('stars', state.stars.join(','));
  if (state.breakfastOnly) usp.set('breakfast', '1');
  if (state.dealsOnly) usp.set('deals', '1');
  if (state.stay !== 'exact') usp.set('stay', state.stay);
  if (state.flexDays) usp.set('flex', String(state.flexDays));
  if (state.stay === 'months') usp.set('months', String(state.stayMonths));
  if (state.stay === 'months' && state.startMonths.length) usp.set('startMonths', state.startMonths.join(','));
  usp.set('checkIn', state.checkIn);
  usp.set('checkOut', state.checkOut);
  usp.set('guests', String(totalGuests()));
  return usp.toString();
}

/** Mutates the store in place; the caller decides when to notify. */
export function queryToSearch(search) {
  const usp = new URLSearchParams(search);
  state.q = usp.get('q') ?? '';
  state.category = usp.get('category') ?? 'all';
  state.amenities = (usp.get('amenities') ?? '').split(',').filter(Boolean);
  state.roomType = usp.get('roomType') ?? 'any';
  state.bedrooms = Number(usp.get('bedrooms')) || 0;
  state.beds = Number(usp.get('beds')) || 0;
  state.bathrooms = Number(usp.get('bathrooms')) || 0;
  state.sort = usp.get('sort') ?? 'reco';
  state.superhostOnly = usp.get('superhost') === '1';
  state.guestFavoriteOnly = usp.get('guestFavorite') === '1';
  state.instantBookOnly = usp.get('instantBook') === '1';
  state.freeCancellationOnly = usp.get('freeCancellation') === '1';
  state.minRating = Number(usp.get('minRating')) || 0;
  state.payAtPropertyOnly = usp.get('payAtProperty') === '1';
  state.maxCentreKm = Number(usp.get('maxCentreKm')) || 0;
  state.stars = (usp.get('stars') ?? '').split(',').map(Number).filter(n => n >= 1 && n <= 5);
  state.breakfastOnly = usp.get('breakfast') === '1';
  state.dealsOnly = usp.get('deals') === '1';

  if (state.meta) {
    state.minPrice = Number(usp.get('minPrice')) || state.meta.minPrice;
    state.maxPrice = Number(usp.get('maxPrice')) || state.meta.maxPrice;
  }

  state.stay = usp.get('stay') ?? 'exact';
  state.flexDays = Number(usp.get('flex')) || 0;
  state.stayMonths = Number(usp.get('months')) || 1;
  state.startMonths = (usp.get('startMonths') ?? '').split(',').filter(Boolean);

  if (usp.get('checkIn')) state.checkIn = usp.get('checkIn');
  if (usp.get('checkOut')) state.checkOut = usp.get('checkOut');

  const g = Number(usp.get('guests'));
  if (g > 0) { state.guests = { ...state.guests, adults: g, children: 0 }; }
}
