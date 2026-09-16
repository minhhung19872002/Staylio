import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import QRCode from 'qrcode';
import { api } from '../lib/api.js';
import { money } from '../lib/format.js';
import { toast } from '../lib/store.js';
import { t } from '../lib/i18n.js';

/**
 * docs/07 §2.3 — the page a guest paying by VietQR waits on.
 *
 * Everything about it is shaped by the fact that the platform cannot see the
 * transfer happen. The guest leaves for a banking app and comes back to a page
 * that has no idea whether they sent anything, so the page does three honest
 * things: shows the code, says how long the booking is held, and keeps asking
 * whether the money has been found.
 *
 * It never claims the booking is paid. The only thing that says so is the
 * server, after somebody has matched a statement line to this reference.
 */
export function Transfer() {
  const { reference } = useParams();
  const navigate = useNavigate();

  const [qr, setQr] = useState(null);
  const [image, setImage] = useState(null);
  const [status, setStatus] = useState(null);
  const [error, setError] = useState(null);
  const [left, setLeft] = useState(null);

  useEffect(() => {
    let alive = true;

    api.bankTransferQr(reference)
      .then(async d => {
        if (!alive) return;
        setQr(d);
        // Rendered in the browser: the payload is the booking's, and turning it
        // into pixels needs nothing from the server.
        setImage(await QRCode.toDataURL(d.payload, { margin: 1, width: 320 }));
      })
      .catch(err => alive && setError(t(err.message)));

    return () => { alive = false; };
  }, [reference]);

  /* The money lands without telling us, so the page asks every ten seconds. */
  useEffect(() => {
    let alive = true;
    let timer = null;

    const ask = async () => {
      try {
        const s = await api.bankTransferStatus(reference);
        if (!alive) return;
        setStatus(s);
        if (s.confirmed || !s.stillWaiting) return;   // nothing left to wait for
        timer = setTimeout(ask, 10_000);
      } catch {
        if (alive) timer = setTimeout(ask, 30_000);
      }
    };

    ask();
    return () => { alive = false; if (timer) clearTimeout(timer); };
  }, [reference]);

  /* The countdown is the honest part: these dates are held, and not forever. */
  useEffect(() => {
    const until = status?.expiresAt ?? qr?.expiresAt;
    if (!until) return;

    const tick = () => {
      const ms = new Date(until).getTime() - Date.now();
      setLeft(ms > 0 ? ms : 0);
    };

    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [status?.expiresAt, qr?.expiresAt]);

  const copy = async text => {
    try {
      await navigator.clipboard.writeText(text);
      toast(t('Đã sao chép'));
    } catch {
      toast(t('Trình duyệt không cho sao chép. Bạn gõ tay giúp nhé.'));
    }
  };

  if (error) {
    return (
      <div className="shell" style={{ paddingBlock: '32px 90px', maxWidth: 640 }}>
        <div className="book-alert is-error">
          <b>{t('Không mở được mã chuyển khoản')}</b>
          <span>{error}</span>
        </div>
      </div>
    );
  }

  if (status?.confirmed) {
    return (
      <div className="shell" style={{ paddingBlock: '32px 90px', maxWidth: 640 }}>
        <div className="book-alert is-ok">
          <b>{t('Đã nhận được tiền')}</b>
          <span>{t('Đơn')} {reference} {t('đã được xác nhận.')}</span>
        </div>
        <button className="btn btn-primary" style={{ marginTop: 20 }}
                onClick={() => navigate(status.nextUrl)}>
          {t('Xem đơn của bạn')}
        </button>
      </div>
    );
  }

  const lapsed = status && !status.stillWaiting && !status.confirmed;

  return (
    <div className="shell" style={{ paddingBlock: '32px 90px', maxWidth: 640 }}>
      <h1 className="section-title" style={{ fontSize: 24 }}>{t('Quét mã để chuyển khoản')}</h1>
      <p className="section-sub">
        {t('Mở ứng dụng ngân hàng, quét mã bên dưới. Số tiền và nội dung đã điền sẵn — giữ nguyên nội dung để hệ thống nhận ra đơn của bạn.')}
      </p>

      {lapsed && (
        <div className="book-alert is-error" style={{ marginTop: 16 }}>
          <b>{t('Đã hết hạn giữ chỗ')}</b>
          <span>{t('Nếu bạn đã chuyển tiền, đừng chuyển thêm lần nữa — hãy liên hệ hỗ trợ với mã đơn này.')}</span>
        </div>
      )}

      {!lapsed && left !== null && (
        <div className={`book-alert ${left < 15 * 60_000 ? 'is-error' : ''}`} style={{ marginTop: 16 }}>
          <b>{t('Còn')} {countdown(left)}</b>
          <span>{t('Quá thời gian này, chỗ sẽ được trả lại cho người khác.')}</span>
        </div>
      )}

      {qr && (
        <>
          <div style={{
            marginTop: 20, padding: 20, background: '#fff', borderRadius: 16,
            border: '1px solid var(--line)', display: 'grid', justifyItems: 'center', gap: 12
          }}>
            {image
              ? <img src={image} alt={t('Mã QR chuyển khoản')} width={280} height={280}
                     style={{ maxWidth: '100%', height: 'auto' }} />
              : <div className="skeleton" style={{ width: 280, height: 280, borderRadius: 8 }} />}
            <div style={{ fontSize: 22, fontWeight: 800 }}>{money(qr.amount)}</div>
          </div>

          {/* docs/07 §2.3 — the same thing in words, for a guest whose phone
              cannot scan or who would rather type it into internet banking. */}
          <div className="table-wrap" style={{ marginTop: 16 }}>
            <table className="admin-table">
              <tbody>
                <Row label={t('Ngân hàng')} value={qr.bankName} />
                <Row label={t('Số tài khoản')} value={qr.accountNumber} onCopy={() => copy(qr.accountNumber)} />
                <Row label={t('Chủ tài khoản')} value={qr.accountName} />
                <Row label={t('Số tiền')} value={money(qr.amount)} onCopy={() => copy(String(qr.amount))} />
                <Row label={t('Nội dung chuyển khoản')} value={qr.memo} onCopy={() => copy(qr.memo)} />
              </tbody>
            </table>
          </div>

          <p className="section-sub" style={{ marginTop: 16 }}>
            {t('Sau khi chuyển, bạn có thể đóng trang này. Đơn sẽ tự được xác nhận khi tiền về, và bạn nhận được email báo.')}
          </p>
        </>
      )}
    </div>
  );
}

function Row({ label, value, onCopy }) {
  return (
    <tr>
      <td style={{ color: 'var(--ink-muted)' }}>{label}</td>
      <td><b>{value}</b></td>
      <td style={{ width: 1 }}>
        {onCopy && (
          <button className="btn btn-outline btn-sm" onClick={onCopy}>{t('Sao chép')}</button>
        )}
      </td>
    </tr>
  );
}

/** Whole minutes while there is time, seconds once it is nearly gone. */
function countdown(ms) {
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;

  if (h > 0) return `${h} ${t('giờ')} ${m} ${t('phút')}`;
  if (m > 0) return `${m} ${t('phút')} ${String(s).padStart(2, '0')} ${t('giây')}`;
  return `${s} ${t('giây')}`;
}
