import { useEffect, useState } from 'react';
import { useStore } from '../lib/useStore.js';
import { api } from '../lib/api.js';
import { toast, requireAuth } from '../lib/store.js';
import { longDate } from '../lib/format.js';
import { t } from '../lib/i18n.js';
import { TranslatedText } from './TranslatedText.jsx';

/**
 * Public questions on a listing. Answered ones are for everybody; the asker
 * also sees their own that are still waiting. Written by guests and hosts, so
 * the text goes through TranslatedText rather than the dictionary.
 */
export function GuestQuestions({ listingId, isOwnListing }) {
  const state = useStore();
  const [rows, setRows] = useState(null);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  const load = () => api.listingQuestions(listingId).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, [listingId, state.user?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const ask = async e => {
    e.preventDefault();
    if (!requireAuth()) return;
    setBusy(true);
    try {
      await api.askQuestion(listingId, text.trim());
      setText('');
      await load();
      toast(t('Đã gửi câu hỏi. Câu trả lời sẽ hiện ở đây khi chủ nhà trả lời.'));
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  if (!rows) return null;

  return (
    <section className="detail-section" id="section-questions">
      <h2>{t('Hỏi đáp của khách')}</h2>
      {!rows.length && <p className="meta">{t('Chưa có câu hỏi nào. Hãy là người đầu tiên hỏi chủ nhà.')}</p>}
      <div style={{ display: 'grid', gap: 14 }}>
        {rows.map(q => (
          <article key={q.id} className="review">
            <b>{t('Hỏi:')}</b> <TranslatedText as="span" text={q.question} notice={false} />
            {q.answer && (
              <div className="review-reply" style={{ marginTop: 8 }}>
                <b>{t('Chủ nhà trả lời')}</b>
                <TranslatedText as="p" text={q.answer} />
                <span className="review-when">{longDate(q.answeredAt.slice(0, 10))}</span>
              </div>
            )}
            {q.myStatus && <div className="meta" style={{ marginTop: 6 }}>{t(q.myStatus)}</div>}
          </article>
        ))}
      </div>
      {!isOwnListing && (
        <form onSubmit={ask} style={{ marginTop: 18 }}>
          <textarea className="field" rows={2} maxLength={500} style={{ width: '100%' }}
                    placeholder={t('Hỏi chủ nhà về chỗ nghỉ này — câu trả lời sẽ được đăng công khai.')}
                    value={text} onChange={e => setText(e.target.value)} />
          <button className="btn btn-outline btn-sm" style={{ marginTop: 8 }}
                  disabled={busy || text.trim().length < 10}>{t('Gửi câu hỏi')}</button>
        </form>
      )}
    </section>
  );
}

/** The host's side: questions waiting first, answered ones under them. */
export function HostQuestions() {
  const [rows, setRows] = useState(null);
  const [draft, setDraft] = useState({});
  const [busyId, setBusyId] = useState(null);

  const load = () => api.hostQuestions().then(setRows).catch(err => toast(err.message));
  useEffect(() => { load(); }, []);

  const act = async (q, answer) => {
    setBusyId(q.id);
    try {
      if (answer) await api.answerQuestion(q.id, (draft[q.id] ?? '').trim());
      else await api.dismissQuestion(q.id);
      setDraft(d => ({ ...d, [q.id]: '' }));
      await load();
      toast(answer ? t('Đã đăng câu trả lời.') : t('Đã ẩn câu hỏi.'));
    } catch (err) { toast(err.message); }
    finally { setBusyId(null); }
  };

  if (!rows?.length) return null;
  const waiting = rows.filter(q => !q.answer).length;

  return (
    <div style={{ marginTop: 24 }}>
      <h2 className="section-title" style={{ fontSize: 20 }}>{t('Câu hỏi của khách')}</h2>
      <p className="section-sub">{t('{} câu hỏi chờ trả lời').replace('{}', waiting)}</p>
      <div style={{ marginTop: 16, display: 'grid', gap: 12 }}>
        {rows.map(q => (
          <article className="host-booking" key={q.id}>
            <div style={{ minWidth: 0, flexBasis: '100%' }}>
              <h3>{q.askerName}</h3>
              <div className="meta">{q.listingTitle} · {longDate(q.createdAt.slice(0, 10))}</div>
              <p style={{ margin: '10px 0 0', fontSize: 14.5, lineHeight: 1.6 }}>{q.question}</p>
              {q.answer && (
                <div className="review-reply" style={{ marginLeft: 0, marginTop: 12 }}>
                  <b>{t('Câu trả lời công khai')}</b>
                  <p>{q.answer}</p>
                </div>
              )}
              <div style={{ marginTop: 12 }}>
                <textarea className="field" rows={2} maxLength={1000}
                          placeholder={q.answer ? t('Sửa câu trả lời') : t('Trả lời công khai — mọi khách xem chỗ nghỉ đều đọc được.')}
                          value={draft[q.id] ?? ''}
                          onChange={e => setDraft(d => ({ ...d, [q.id]: e.target.value }))} />
                <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
                  <button className="btn btn-primary btn-sm"
                          disabled={busyId === q.id || (draft[q.id] ?? '').trim().length < 10}
                          onClick={() => act(q, true)}>{q.answer ? t('Cập nhật') : t('Trả lời')}</button>
                  {!q.answer && (
                    <button className="btn btn-outline btn-sm" disabled={busyId === q.id}
                            onClick={() => act(q, false)}>{t('Không đăng')}</button>
                  )}
                </div>
              </div>
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}
