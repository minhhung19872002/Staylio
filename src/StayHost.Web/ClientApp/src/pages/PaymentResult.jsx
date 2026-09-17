import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../lib/api.js';
import { money } from '../lib/format.js';
import { t } from '../lib/i18n.js';

/**
 * docs/07 §13 — where a guest lands coming back from VNPay, MoMo or ZaloPay.
 *
 * The word in the query string is a hint, not a verdict. docs/07 §5 is explicit
 * that the platform must not believe which page a guest returned to, so this one
 * says nothing on its own authority: it reads the booking from the server and
 * reports what the booking says. The two can genuinely disagree for a few
 * seconds — the gateway's own confirmation and the guest's browser race each
 * other home — so a booking still unconfirmed is not called a failure until the
 * platform has had time to ask.
 */
export function PaymentResult() {
  const [params] = useSearchParams();
  const navigate = useNavigate();

  const hint = params.get('ket-qua') ?? 'pending';
  const bookingId = Number(params.get('don')) || null;
  const orderRef = params.get('ma');
  // docs/07 §13 — what this visit paid for: a stay (default), the rest of a
  // part-paid stay, an experience ticket or a service.
  const kind = params.get('loai') ?? 'stay';
  const ticketId = Number(params.get('ve')) || null;
  const subjectId = kind === 'xp' || kind === 'svc' ? ticketId : bookingId;

  const [booking, setBooking] = useState(null);
  const [error, setError] = useState(null);
  const [asking, setAsking] = useState(true);

  useEffect(() => {
    if (!subjectId) { setAsking(false); return; }

    let alive = true;
    let tries = 0;
    let timer = null;

    const ask = async () => {
      try {
        const b = await readSubject(kind, subjectId);
        if (!alive) return;
        setBooking(b);

        // Settled, or gone for good — nothing left to wait for.
        if (!stillWaiting(kind, b)) { setAsking(false); return; }
      } catch (err) {
        if (!alive) return;
        setError(t(err.message));
        setAsking(false);
        return;
      }

      // Still pending. The sweep of docs/07 §5 asks the gateway once a minute,
      // and the IPN may be on its way, so the page waits rather than declaring
      // a failure it cannot see. Twelve tries is a little over a minute.
      if (++tries >= 12) { setAsking(false); return; }
      timer = setTimeout(ask, 5_000);
    };

    ask();
    return () => { alive = false; if (timer) clearTimeout(timer); };
  }, [kind, subjectId]);

  const paid = isPaid(kind, booking);
  const waiting = asking && !paid;

  return (
    <div className="shell" style={{ paddingBlock: '32px 90px', maxWidth: 640 }}>
      {paid && (
        <div className="book-alert is-ok">
          <b>{t('Thanh toán thành công')}</b>
          <span>{t('Đơn')} {booking.reference} {t('đã được xác nhận.')}</span>
        </div>
      )}

      {!paid && waiting && (
        <div className="book-alert">
          <b>{t('Đang xác nhận với cổng thanh toán…')}</b>
          <span>{t('Tiền có thể đã được trừ. Đừng thanh toán lại — Staylio đang đối chiếu và sẽ xác nhận đơn ngay khi có kết quả.')}</span>
        </div>
      )}

      {!paid && !waiting && hint === 'cancelled' && (
        <div className="book-alert">
          <b>{t('Bạn đã huỷ ở trang thanh toán')}</b>
          <span>{t('Chưa có khoản nào bị trừ. Chỗ vẫn đang được giữ trong ít phút — bạn có thể thử lại.')}</span>
        </div>
      )}

      {!paid && !waiting && hint !== 'cancelled' && (
        <div className="book-alert is-error">
          <b>{t('Chưa thanh toán được')}</b>
          <span>{t('Nếu tài khoản của bạn đã bị trừ tiền, đừng trả lại lần nữa — hãy liên hệ hỗ trợ kèm mã bên dưới.')}</span>
        </div>
      )}

      {error && (
        <div className="book-alert is-error" style={{ marginTop: 16 }}>
          <b>{t('Không đọc được đơn')}</b>
          <span>{error}</span>
        </div>
      )}

      <div className="table-wrap" style={{ marginTop: 20 }}>
        <table className="admin-table">
          <tbody>
            {booking && (
              <tr>
                <td style={{ color: 'var(--ink-muted)' }}>{t('Đơn')}</td>
                <td><b>{booking.reference}</b></td>
              </tr>
            )}
            {booking && (
              <tr>
                <td style={{ color: 'var(--ink-muted)' }}>{t('Số tiền')}</td>
                <td><b>{money(booking.total)}</b></td>
              </tr>
            )}
            {orderRef && (
              <tr>
                <td style={{ color: 'var(--ink-muted)' }}>{t('Mã giao dịch')}</td>
                <td><b>{orderRef}</b></td>
              </tr>
            )}
            {booking && (
              <tr>
                <td style={{ color: 'var(--ink-muted)' }}>{t('Trạng thái')}</td>
                <td><b>{t(booking.statusLabel)}</b></td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div style={{ display: 'flex', gap: 10, marginTop: 20, flexWrap: 'wrap' }}>
        <button className="btn btn-primary"
                onClick={() => navigate(destination(kind, bookingId))}>
          {paid ? t('Xem đơn của bạn') : t('Xem chuyến của tôi')}
        </button>
        <button className="btn btn-outline" onClick={() => navigate('/')}>{t('Về trang chủ')}</button>
      </div>
    </div>
  );
}

async function readSubject(kind, id) {
  if (kind === 'xp') return (await api.experienceBookings()).find(b => b.id === id) ?? Promise.reject(new Error('Không tìm thấy vé này.'));
  if (kind === 'svc') return (await api.serviceBookings()).find(b => b.id === id) ?? Promise.reject(new Error('Không tìm thấy đơn dịch vụ này.'));
  return api.booking(id);
}

function isPaid(kind, b) {
  if (!b) return false;
  if (kind === 'balance') return b.balanceStatus === 'Paid' || b.balanceDue === 0;
  return b.status === 'Confirmed';
}

function stillWaiting(kind, b) {
  if (kind === 'balance') return !isPaid(kind, b) && ['Confirmed', 'InProgress'].includes(b.status);
  if (kind === 'xp' || kind === 'svc') return b.status === 'AwaitingPayment';
  return b.status === 'PendingPayment';
}

function destination(kind, bookingId) {
  if (kind === 'xp') return '/experiences/bookings';
  if (kind === 'svc') return '/services/bookings';
  return bookingId ? `/trips/${bookingId}` : '/trips';
}
