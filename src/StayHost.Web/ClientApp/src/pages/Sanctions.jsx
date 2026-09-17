import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../lib/api.js';
import { toast } from '../lib/store.js';
import { dateTime, longDate } from '../lib/format.js';
import { t } from '../lib/i18n.js';

/**
 * docs/08 §8, seen from the other side — what was decided about you, why, and
 * the one appeal the section promises.
 *
 * The sanction notice tells people they may appeal within 30 days. Until this
 * page existed that was a sentence with nowhere to go.
 */
export function MySanctions() {
  const [rows, setRows] = useState(null);
  const [busy, setBusy] = useState(false);
  const [drafting, setDrafting] = useState(null);
  const [argument, setArgument] = useState('');

  const load = () => api.mySanctions().then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, []);

  const send = async id => {
    setBusy(true);
    try {
      const res = await api.fileAppeal(id, { argument });
      toast(res.message);
      setDrafting(null);
      setArgument('');
      await load();
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  if (rows === null) return <div className="shell" style={{ padding: '40px 0' }}><p>{t('Đang tải…')}</p></div>;

  return (
    <div className="shell" style={{ paddingBlock: '34px 90px', maxWidth: 760 }}>
      <h1 className="section-title" style={{ fontSize: 24 }}>{t('Quyết định về tài khoản của bạn')}</h1>
      <p className="section-sub">
        {t('Mỗi quyết định được khiếu nại một lần trong 30 ngày. Người xét lại luôn là người khác với người đã ra quyết định, và trả lời trong 7 ngày làm việc.')}
      </p>

      {rows.length === 0 && (
        <div className="empty-state" style={{ marginTop: 24 }}>
          <h3>{t('Không có quyết định nào')}</h3>
          <p>{t('Tài khoản của bạn chưa từng bị cảnh cáo, hạn chế hay khoá.')}</p>
        </div>
      )}

      <div style={{ display: 'grid', gap: 14, marginTop: 20 }}>
        {rows.map(s => (
          <div key={s.id} className="stat" style={{ padding: 18 }}>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
              <b style={{ fontSize: 16 }}>{t(s.levelLabel)}</b>
              {!!s.restrictionLabel && <span className="badge">{t(s.restrictionLabel)}</span>}
              {s.overturnedOnAppeal && <span className="badge confirmed">{t('Đã gỡ theo khiếu nại')}</span>}
              {!!s.liftedAt && !s.overturnedOnAppeal && <span className="badge confirmed">{t('Đã được gỡ')}</span>}
            </div>

            <p style={{ margin: '10px 0 4px', fontSize: 14.5 }}>{s.reason}</p>
            <p className="field-note" style={{ margin: 0 }}>
              {s.policy ? `${t('Chính sách:')} ${s.policy} · ` : ''}{t('Ngày')} {dateTime(s.createdAt)}
              {s.expiresAt ? ` · ${t('có hiệu lực tới')} ${dateTime(s.expiresAt)}` : ''}
            </p>
            {!!s.liftedWhen && (
              <p className="field-note" style={{ margin: '4px 0 0' }}>{t('Được gỡ khi:')} {s.liftedWhen}</p>
            )}

            {!!s.appealStatusLabel && (
              <div className="book-alert" style={{ marginTop: 12 }}>
                <b>{t('Khiếu nại:')} {s.appealStatusLabel}</b>
                {!!s.appealDueBy && <span>{t('Hạn trả lời:')} {longDate(s.appealDueBy)}</span>}
                {!!s.appealOutcome && <span>{s.appealOutcome}</span>}
              </div>
            )}

            {s.mayAppeal && drafting !== s.id && (
              <button className="btn btn-outline btn-sm" style={{ marginTop: 12 }}
                      onClick={() => { setDrafting(s.id); setArgument(''); }}>
                {t('Khiếu nại quyết định này')}
              </button>
            )}

            {!s.mayAppeal && !s.appealStatusLabel && !!s.whyNotAppeal && (
              <p className="field-note" style={{ marginTop: 10 }}>{s.whyNotAppeal}</p>
            )}

            {drafting === s.id && (
              <div style={{ marginTop: 12 }}>
                <label className="form-field">
                  <span className="cap">{t('Bạn cho rằng quyết định này sai ở chỗ nào?')}</span>
                  <textarea rows={4} value={argument} onChange={e => setArgument(e.target.value)}
                            placeholder={t('Nêu rõ sự việc và bằng chứng nếu có (ít nhất 20 ký tự).')} />
                </label>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => send(s.id)}>
                    {t('Gửi khiếu nại')}
                  </button>
                  <button className="btn btn-outline btn-sm" onClick={() => setDrafting(null)}>{t('Huỷ')}</button>
                </div>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * docs/08 §8 for somebody who can no longer sign in. A suspension takes the
 * password away; it does not take away the right to argue, so the notice email
 * carries a token and it lands here.
 */
export function AppealByToken() {
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const [argument, setArgument] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(null);

  const send = async () => {
    setBusy(true);
    try {
      const res = await api.appealByToken({ token, argument });
      setDone(res.message);
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <div className="shell" style={{ paddingBlock: '34px 90px', maxWidth: 640 }}>
      <h1 className="section-title" style={{ fontSize: 24 }}>{t('Khiếu nại quyết định')}</h1>

      {!token && (
        <div className="empty-state" style={{ marginTop: 20 }}>
          <h3>{t('Thiếu liên kết khiếu nại')}</h3>
          <p>{t('Hãy mở đúng liên kết trong thư Staylio đã gửi cho bạn.')}</p>
        </div>
      )}

      {!!token && !done && (
        <>
          <p className="section-sub">
            {t('Một người khác với người đã ra quyết định sẽ đọc và trả lời bạn trong 7 ngày làm việc. Mỗi quyết định chỉ được khiếu nại một lần.')}
          </p>
          <label className="form-field" style={{ marginTop: 16 }}>
            <span className="cap">{t('Bạn cho rằng quyết định này sai ở chỗ nào?')}</span>
            <textarea rows={6} value={argument} onChange={e => setArgument(e.target.value)}
                      placeholder={t('Nêu rõ sự việc và bằng chứng nếu có (ít nhất 20 ký tự).')} />
          </label>
          <button className="btn btn-primary" disabled={busy} onClick={send}>{t('Gửi khiếu nại')}</button>
        </>
      )}

      {!!done && (
        <div className="book-alert" style={{ marginTop: 20 }}>
          <b>{t('Đã nhận khiếu nại của bạn')}</b>
          <span>{done}</span>
        </div>
      )}
    </div>
  );
}
