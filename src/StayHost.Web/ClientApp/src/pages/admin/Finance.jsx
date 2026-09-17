import { useEffect, useState } from 'react';
import { api } from '../../lib/api.js';
import { toast } from '../../lib/store.js';
import { money, longDate } from '../../lib/format.js';
import { t } from '../../lib/i18n.js';
import { PayoutReconcilePanel } from './PayoutReconcile.jsx';

/**
 * docs/07 §13 — the transfers Staylio owes hosts, and the file that pays them.
 *
 * This screen exists because option A has no API behind it: a licensed gateway
 * settles every guest's payment into the platform's own account and the split
 * between hosts is a file somebody puts through internet banking. So the honest
 * shape is three columns of work — decided, downloaded, confirmed — and only the
 * last one posts anything to the ledger.
 */
export function PayoutBatchPanel() {
  const [d, setD] = useState(null);
  const [busy, setBusy] = useState(false);

  const load = () => api.payoutBatches().then(setD).catch(() => setD(null));
  useEffect(() => { load(); }, []);

  if (!d) return null;

  const decide = async (row, settled) => {
    const note = window.prompt(settled
      ? t('Ngân hàng đã chuyển khoản này. Ghi lại mã giao dịch hoặc ghi chú:')
      : t('Ngân hàng từ chối khoản này. Ghi rõ lý do:'));

    if (!note?.trim()) return;

    setBusy(true);
    try {
      await (settled ? api.settlePayoutBatch(row.id, { note }) : api.failPayoutBatch(row.id, { note }));
      toast(settled ? t('Đã ghi nhận ngân hàng chuyển thành công.') : t('Đã ghi nhận ngân hàng từ chối.'));
      await load();
    } catch (err) {
      toast(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Chuyển tiền cho chủ nhà')}</h2>
      <p className="section-sub">
        {t('Cổng thanh toán trả toàn bộ tiền đơn về tài khoản Staylio. Phần của chủ nhà phải chuyển đi bằng lệnh hàng loạt, và chỉ được ghi sổ khi ngân hàng đã thực hiện.')}
      </p>

      {/* The one thing that must not be silent: with no encryption key the sweep
          cannot even write these rows, so an empty table would read as "nothing
          to pay" when it means "cannot pay anybody". */}
      {d.blocked && (
        <div className="book-alert is-error" style={{ marginTop: 14 }}>
          <b>{t('Chưa tạo được lệnh chuyển tiền')}</b>
          <span>{d.blocked}</span>
        </div>
      )}

      <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginTop: 16, flexWrap: 'wrap' }}>
        <div>
          <div style={{ fontSize: 18, fontWeight: 800 }}>{money(d.waitingAmount)}</div>
          <div style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>
            {d.waiting} {t('lệnh đang chờ ngân hàng')}
          </div>
        </div>
        {d.waiting > 0 && (
          <a className="btn btn-primary btn-sm" href={api.payoutFileUrl} onClick={() => setTimeout(load, 1500)}>
            {t('Tải file chuyển tiền (.csv)')}
          </a>
        )}
      </div>

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="admin-table">
          <thead>
            <tr>
              <th>{t('Mã chuyển')}</th>
              <th>{t('Chủ nhà')}</th>
              <th>{t('Tài khoản')}</th>
              <th>{t('Số tiền')}</th>
              <th>{t('Số đơn')}</th>
              <th>{t('Trạng thái')}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {d.batches.map(row => (
              <tr key={row.id}>
                <td><b>{row.reference}</b></td>
                <td>{row.hostName}</td>
                <td>
                  {row.accountName}<br />
                  <span style={{ color: 'var(--ink-muted)', fontSize: 12.5 }}>
                    {row.bankName} · {row.accountMasked}
                  </span>
                </td>
                <td><b>{money(row.amount)}</b></td>
                <td>{row.bookingCount}</td>
                <td>
                  {t(row.statusLabel)}
                  {row.note && (
                    <div style={{ color: 'var(--ink-muted)', fontSize: 12.5 }}>{row.note}</div>
                  )}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {(row.status === 'Pending' || row.status === 'Exported') && (
                    <>
                      <button className="btn btn-primary btn-sm" disabled={busy}
                              onClick={() => decide(row, true)}>{t('Đã chuyển')}</button>{' '}
                      <button className="btn btn-outline btn-sm" disabled={busy}
                              onClick={() => decide(row, false)}>{t('Bị từ chối')}</button>
                    </>
                  )}
                </td>
              </tr>
            ))}
            {!d.batches.length && (
              <tr><td colSpan={7} style={{ color: 'var(--ink-muted)' }}>
                {t('Chưa có lệnh chuyển nào.')}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>

      <PayoutReconcilePanel />
    </section>
  );
}

/** docs/07 TC-A-04 — fee revenue, money held for others, tax owed, losses. */
export function FinancePanel() {
  const [d, setD] = useState(null);

  useEffect(() => { api.financeReport().then(setD).catch(() => setD(null)); }, []);
  if (!d) return null;

  const groups = [...new Set(d.lines.map(l => l.group))];

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Báo cáo tài chính')}</h2>
      <p className="section-sub">
        {t('Doanh thu tính từ')} {longDate(d.from)} {t('đến')} {longDate(d.to)}{t('; các khoản giữ hộ là số dư hiện tại.')}
      </p>

      <div className="stat-grid" style={{ marginTop: 16 }}>
        <Figure label={t('Doanh thu phí')} value={money(d.feeRevenue)} note={t('Phí khách + phí chủ nhà')} />
        <Figure label={t('Đang giữ hộ')} value={money(d.heldForOthers)} note={t('Tiền của người khác')} />
        <Figure label={t('Thuế phải nộp')} value={money(d.taxPayable)} note={t('Thu hộ cơ quan thuế')} />
        <Figure label={t('Thất thoát')} value={money(d.losses)} note={t('Chi phí, nợ khó đòi, thua khiếu nại')} />
      </div>

      {/* A report off an unbalanced ledger is a report of nothing. */}
      {d.ledgerDifference !== 0 && (
        <div className="book-alert is-error" style={{ marginTop: 12 }}>
          <b>{t('Sổ sách không cân: lệch')} {money(d.ledgerDifference)}</b>
          <span>{t('Mọi con số trên đây đều không đáng tin cho tới khi tìm ra chỗ lệch.')}</span>
        </div>
      )}

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="admin-table">
          <thead><tr><th>{t('Khoản')}</th><th>{t('Nhóm')}</th><th>{t('Số tiền')}</th></tr></thead>
          <tbody>
            {groups.flatMap(g => d.lines.filter(l => l.group === g).map(l => (
              <tr key={l.key}>
                <td><b>{l.label}</b></td>
                <td>{l.group}</td>
                <td>{money(l.amount)}</td>
              </tr>
            )))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function Figure({ label, value, note }) {
  return (
    <div className="stat">
      <span className="cap">{label}</span>
      <b style={{ display: 'block', fontSize: 22, margin: '6px 0 2px' }}>{value}</b>
      <span style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>{note}</span>
    </div>
  );
}

/** docs/07 §7 — one line out of place against the gateway is the alarm. */
export function ReconciliationPanel() {
  const [day, setDay] = useState(() => new Date().toISOString().slice(0, 10));
  const [d, setD] = useState(null);

  const load = on => api.reconciliation(on).then(setD).catch(e => toast(e.message));
  useEffect(() => { load(day); }, []);

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Đối soát với cổng thanh toán')}</h2>
      <p className="section-sub">{t('Chạy mỗi ngày. Lệch một giao dịch là báo động, không được bỏ qua.')}</p>

      <div style={{ display: 'flex', gap: 10, marginTop: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <label className="form-field" style={{ margin: 0, maxWidth: 200 }}>
          <span className="cap">{t('Ngày')}</span>
          <input type="date" value={day} onChange={e => setDay(e.target.value)} />
        </label>
        <button className="btn btn-outline btn-sm" onClick={() => load(day)}>{t('Đối soát')}</button>
      </div>

      {!!d && (
        <>
          <div className={`book-alert ${d.balanced ? '' : 'is-error'}`} style={{ marginTop: 14 }}>
            <b>{d.summary}</b>
            <span>
              {t('Sàn')} {d.oursCount} {t('giao dịch')} · {money(d.oursTotal)} {t('— cổng')} {d.theirsCount} {t('giao dịch')} · {money(d.theirsTotal)}
            </span>
          </div>

          {!!d.discrepancies.length && (
            <div className="table-wrap" style={{ marginTop: 16 }}>
              <table className="admin-table">
                <thead><tr><th>{t('Mã')}</th><th>{t('Loại lệch')}</th><th>{t('Sàn')}</th><th>{t('Cổng')}</th><th>{t('Chênh')}</th></tr></thead>
                <tbody>
                  {d.discrepancies.map(x => (
                    <tr key={x.reference}>
                      <td><b>{x.reference}</b></td>
                      <td>{x.kindLabel}</td>
                      <td>{money(x.ours)}</td>
                      <td>{money(x.theirs)}</td>
                      <td>{money(x.difference)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </section>
  );
}

/** docs/07 TC-A-02 — tra cứu giao dịch, hoàn tiền thủ công, điều chỉnh khoản chuyển. */
export function TransactionsPanel() {
  const [q, setQ] = useState('');
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);

  const load = async term => {
    try { setRows(await api.adminTransactions(term)); }
    catch (err) { toast(err.message); }
  };
  useEffect(() => { load(''); }, []);

  const refund = async t => {
    const raw = prompt(`Hoàn bao nhiêu cho đơn ${t.bookingReference}? Tối đa ${money(t.amount - t.refunded)}`);
    if (!raw) return;
    const reason = prompt('Lý do hoàn tiền thủ công (bắt buộc)');
    if (!reason) return;
    setBusy(true);
    try {
      await api.adminRefund(t.bookingId, { amount: Number(raw.replace(/\D/g, '')), reason });
      await load(q);
      toast('Đã hoàn tiền.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  // docs/03 §4 and docs/06 §8 — the endpoint existed with nothing to press, so
  // the whole force-majeure branch, and the host's Q-A behind it, never ran.
  const forceMajeure = async tx => {
    const reason = prompt(t('Sự kiện bất khả kháng (bão, lũ, lệnh phong toả…) — tối thiểu 8 ký tự'));
    if (!reason) return;
    if (!confirm(`${t('Huỷ đơn do bất khả kháng: khách được hoàn toàn bộ, chủ nhà nhận hỗ trợ từ quỹ. Đơn')} ${tx.bookingReference}?`)) return;
    setBusy(true);
    try {
      await api.adminForceMajeure(tx.bookingId, reason);
      await load(q);
      toast('Đã huỷ đơn do bất khả kháng.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const adjust = async (t, release) => {
    const reason = prompt(release ? 'Lý do mở lại khoản chuyển' : 'Lý do tạm giữ khoản chuyển');
    if (!reason) return;
    setBusy(true);
    try {
      await api.adminAdjustPayout(t.bookingId, { release, reason });
      await load(q);
      toast(release ? 'Đã mở lại khoản chuyển.' : 'Đã tạm giữ khoản chuyển.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <section style={{ marginTop: 40 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Tra cứu giao dịch')}</h2>
      <p className="section-sub">{t('Tìm theo mã đơn, mã giao dịch hoặc email khách.')}</p>

      <div style={{ display: 'flex', gap: 10, marginTop: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <label className="form-field" style={{ margin: 0, maxWidth: 320, flex: 1 }}>
          <span className="cap">{t('Từ khoá')}</span>
          <input value={q} onChange={e => setQ(e.target.value)} placeholder="SH1234ABCD" />
        </label>
        <button className="btn btn-outline btn-sm" onClick={() => load(q)}>{t('Tìm')}</button>
      </div>

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="admin-table">
          <thead>
            <tr><th>{t('Đơn')}</th><th>{t('Khách')}</th><th>{t('Số tiền')}</th><th>{t('Trạng thái')}</th><th>{t('Chuyển cho chủ nhà')}</th><th /></tr>
          </thead>
          <tbody>
            {rows.map(tx => (
              <tr key={tx.bookingId}>
                <td><b>{tx.bookingReference}</b><span>{tx.paymentReference} · {tx.listingTitle}</span></td>
                <td>{tx.guestEmail}</td>
                <td>
                  {money(tx.amount)}
                  {tx.refunded > 0 && <span>{t('đã hoàn')} {money(tx.refunded)}</span>}
                  {/* docs/07 §6 — what the guest was reading prices in, frozen
                      at booking time. This is the line that settles "the price
                      I was shown"; it stayed null for 155 bookings because no
                      client ever sent the field. */}
                  {tx.displayCurrency && (
                    <span>{t('khách xem giá bằng')} {tx.displayCurrency} @ {tx.displayRate}</span>
                  )}
                </td>
                <td>{tx.bookingStatusLabel}<span>{tx.paymentStatus}</span></td>
                <td>
                  {tx.payoutStatus === 'Paid' ? t('Đã chuyển') : tx.payoutStatus === 'OnHold' ? t('Tạm giữ') : t('Chờ chuyển')}
                  {!!tx.payoutHoldReason && <span>{tx.payoutHoldReason}</span>}
                  {!!tx.payoutReference && <span>{tx.payoutReference}</span>}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <button className="link-btn" disabled={busy} onClick={() => refund(tx)}>{t('Hoàn tiền')}</button>
                  <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                          onClick={() => forceMajeure(tx)}>{t('Bất khả kháng')}</button>
                  {tx.payoutStatus === 'OnHold' && (
                    <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                            onClick={() => adjust(tx, true)}>{t('Mở lại')}</button>
                  )}
                  {tx.payoutStatus === 'Scheduled' && (
                    <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                            onClick={() => adjust(tx, false)}>{t('Tạm giữ')}</button>
                  )}
                </td>
              </tr>
            ))}
            {!rows.length && <tr><td colSpan={6}>{t('Không có giao dịch nào khớp.')}</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** docs/07 §11 — khách đã báo ngân hàng: giữ tiền, gom bằng chứng, theo kết quả. */
export function ChargebackPanel() {
  const [rows, setRows] = useState([]);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    try { setRows(await api.chargebacks()); }
    catch (err) { toast(err.message); }
  };
  useEffect(() => { load(); }, []);

  const open = async () => {
    const ref = prompt('Mã đơn khách khiếu nại với ngân hàng');
    if (!ref) return;
    const amount = prompt('Số tiền ngân hàng đã thu lại (để trống = toàn bộ đã trả)') ?? '';
    const reason = prompt('Lý do ngân hàng đưa ra') ?? '';
    setBusy(true);
    try {
      setRows(await api.openChargeback({
        bookingReference: ref.trim(),
        amount: Number(amount.replace(/\D/g, '')) || 0,
        reason
      }));
      toast('Đã mở hồ sơ. Khoản chuyển cho chủ nhà bị giữ lại cho tới khi có kết quả.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const contest = async c => {
    const evidence = prompt(`Bằng chứng đã gửi ngân hàng:\n\n${c.checklist.join('\n')}`);
    if (!evidence) return;
    setBusy(true);
    try { setRows(await api.chargebackEvidence(c.id, { evidence })); toast('Đã ghi nhận bằng chứng.'); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const decide = async (c, won) => {
    // docs/07 §11 — the host only wears it when arbitration put it there.
    const hostAtFault = won ? false : confirm('Phân xử có kết luận lỗi thuộc về chủ nhà không?');
    setBusy(true);
    try {
      setRows(await api.decideChargeback(c.id, { won, hostAtFault }));
      toast(won ? 'Đã ghi nhận thắng.' : 'Đã ghi nhận thua.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <section style={{ marginTop: 40 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
        <div>
          <h2 className="section-title" style={{ fontSize: 20 }}>{t('Khiếu nại với ngân hàng')}</h2>
          <p className="section-sub" style={{ margin: 0 }}>
            {t('Bằng chứng phải gửi trong 7 ngày. Chủ nhà chỉ mất tiền khi phân xử kết luận lỗi thuộc về họ.')}
          </p>
        </div>
        <button className="btn btn-outline btn-sm" disabled={busy} onClick={open}>{t('+ Mở hồ sơ')}</button>
      </div>

      <div className="table-wrap" style={{ marginTop: 16 }}>
        <table className="admin-table">
          <thead>
            <tr><th>{t('Đơn')}</th><th>{t('Số tiền')}</th><th>{t('Trạng thái')}</th><th>{t('Hạn gửi bằng chứng')}</th><th /></tr>
          </thead>
          <tbody>
            {rows.map(c => (
              <tr key={c.id}>
                <td><b>{c.bookingReference}</b><span>{c.listingTitle} · {c.reason}</span></td>
                <td>{money(c.amount)}</td>
                <td>
                  <span className={`badge ${c.status === 'Won' ? 'confirmed' : c.status === 'Received' ? 'pending' : 'cancelled'}`}>
                    {c.statusLabel}
                  </span>
                  {c.hostAtFault && <span>{t('Chủ nhà chịu khoản này')}</span>}
                </td>
                <td>
                  {longDate(c.evidenceDueBy)}
                  {c.evidenceOverdue && <span>{t('Đã quá hạn')}</span>}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {c.status === 'Received' && (
                    <button className="link-btn" disabled={busy} onClick={() => contest(c)}>{t('Gửi bằng chứng')}</button>
                  )}
                  {(c.status === 'Received' || c.status === 'Contested') && (
                    <>
                      <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                              onClick={() => decide(c, true)}>{t('Thắng')}</button>
                      <button className="link-btn" style={{ marginLeft: 8 }} disabled={busy}
                              onClick={() => decide(c, false)}>{t('Thua')}</button>
                    </>
                  )}
                </td>
              </tr>
            ))}
            {!rows.length && <tr><td colSpan={5}>{t('Chưa có hồ sơ nào.')}</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  );
}
