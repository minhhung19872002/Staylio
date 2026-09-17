import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useStore } from '../../lib/useStore.js';
import {
  set, login, register, loadMe, saveProfile, submitReview,
  closeOverlay, toast, loadSessions, loadBookings, loadTrip, loadSpokenLanguages,
  submitTwoFactor, resendTwoFactor, state as store
} from '../../lib/store.js';
import { api } from '../../lib/api.js';
import { money, longDate } from '../../lib/format.js';
import { Avatar } from '../Avatar.jsx';
import { Modal } from './Modal.jsx';
import { t } from '../../lib/i18n.js';

import { externalConfig, mountGoogleButton, signInWithApple, signInWithFacebook } from '../../lib/externalLogin.js';

export function AuthModal() {
  const state = useStore();
  if (state.authMode === 'forgot') return <ForgotModal />;
  if (state.authMode === 'reset') return <ResetModal />;
  if (state.authMode === 'twoFactor') return <TwoFactorModal />;

  const isRegister = state.authMode === 'register';

  // docs/01 TK-01 — one field takes either an email or a phone number, so
  // nobody has to decide which kind of account they are making first.
  const submit = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    const typed = f.identifier.value.trim();
    const looksLikePhone = /^[+\d][\d\s.]{7,}$/.test(typed);

    const body = isRegister
      ? {
          email: looksLikePhone ? null : typed,
          phone: looksLikePhone ? typed : (f.phone?.value?.trim() || null),
          password: f.password.value,
          fullName: f.fullName?.value?.trim() ?? '',
          dateOfBirth: f.dateOfBirth?.value || null,
          // docs/01 TC-10 — the invitation email says "đăng ký bằng mã RF…",
          // and without a field for it only an exact email match was rewarded.
          referralCode: f.referralCode?.value?.trim() || null
        }
      : { email: typed, password: f.password.value };

    const ok = await (isRegister ? register(body) : login(body));

    // docs/01 TK-08 — a challenge means the password step passed and the login
    // has not finished. The overlay stays open and swaps to the code step;
    // closing it here would strand somebody halfway in with no way back.
    if (ok && !store.twoFactor) { await loadMe(); closeOverlay(); }
  };

  return (
    <Modal title={isRegister ? t('Đăng ký') : t('Đăng nhập')} size="narrow">
      <h3 style={{ margin: '0 0 4px', fontSize: 20, fontWeight: 800 }}>{t('Chào mừng đến Staylio')}</h3>
      <p style={{ margin: '0 0 18px', fontSize: 13.5, color: 'var(--ink-muted)' }}>
        {isRegister ? t('Tạo tài khoản để đặt chỗ, lưu yêu thích và cho thuê nhà.') : t('Đăng nhập để tiếp tục.')}
      </p>

      <ProviderButtons />

      <form onSubmit={submit} noValidate id="auth-form">
        {isRegister && (
          <label className="form-field">
            <span className="cap">{t('Họ và tên')}</span>
            <input type="text" name="fullName" autoComplete="name" placeholder={t('Nguyễn Văn A')} required />
          </label>
        )}
        <label className="form-field">
          <span className="cap">{t('Email hoặc số điện thoại')}</span>
          <input type="text" name="identifier" autoComplete="username"
                 placeholder={t('ban@email.com hoặc 0912 345 678')} required />
        </label>
        <label className="form-field">
          <span className="cap">{t('Mật khẩu')}</span>
          <input type="password" name="password" autoComplete={isRegister ? 'new-password' : 'current-password'}
                 placeholder={t('Tối thiểu 8 ký tự')} required />
        </label>
        {isRegister && <>
          <label className="form-field">
            <span className="cap">{t('Số điện thoại')} <span style={{ fontWeight: 400 }}>{t('(nếu đăng ký bằng email)')}</span></span>
            <input type="tel" name="phone" autoComplete="tel" placeholder="0912 345 678" />
          </label>
          <label className="form-field">
            <span className="cap">{t('Ngày sinh')}</span>
            <input type="date" name="dateOfBirth" required />
          </label>
          <p style={{ margin: '-4px 0 12px', fontSize: 12.5, color: 'var(--ink-muted)' }}>
            {t('Bạn cần đủ 18 tuổi để tạo tài khoản.')}
          </p>
          <label className="form-field">
            <span className="cap">{t('Mã giới thiệu')} <span style={{ fontWeight: 400 }}>{t('(nếu có)')}</span></span>
            <input name="referralCode" autoComplete="off" placeholder="RF…"
                   defaultValue={new URLSearchParams(window.location.search).get('ma-gioi-thieu') ?? ''} />
          </label>
        </>}

        {state.authError && <div className="form-error">{state.authError}</div>}

        <button type="submit" className="btn btn-primary btn-block" style={{ marginTop: 6 }} disabled={state.authBusy}>
          {state.authBusy ? t('Đang xử lý…') : isRegister ? t('Tạo tài khoản') : t('Đăng nhập')}
        </button>
      </form>
      {!isRegister && (
        <p style={{ textAlign: 'right', margin: '-6px 0 0' }}>
          <button className="link-btn" style={{ fontWeight: 600, fontSize: 13 }}
                  onClick={() => set({ authMode: 'forgot', authError: null })}>{t('Quên mật khẩu?')}</button>
        </p>
      )}

      <p style={{ textAlign: 'center', fontSize: 13.5, color: 'var(--ink-muted)', margin: '18px 0 0' }}>
        {isRegister ? t('Đã có tài khoản?') : t('Chưa có tài khoản?')}{' '}
        <button className="link-btn" onClick={() => set({ authMode: isRegister ? 'login' : 'register', authError: null })}>
          {isRegister ? t('Đăng nhập') : t('Đăng ký ngay')}
        </button>
      </p>

      <div style={{ marginTop: 22, padding: 14, background: 'var(--surface-soft)', borderRadius: 12 }}>
        <b style={{ fontSize: 12.5 }}>{t('Tài khoản dùng thử')}</b>
        <div style={{ fontSize: 12.5, color: 'var(--ink-muted)', marginTop: 6, lineHeight: 1.6 }}>
          {t('Khách:')} <code>guest@staylio.vn</code><br />
          {t('Chủ nhà:')} <code>host1@staylio.vn</code><br />
          {t('Mật khẩu:')} <code>stayhost123</code>
        </div>
        {/* Tài khoản quản trị cố tình KHÔNG nằm ở đây. Hộp này hiện cho mọi khách
            vào trang, mà trang quản trị mở ra hồ sơ, giấy tờ tuỳ thân và tiền của
            người dùng thật — mã 6 số của docs/08 §3 là thứ duy nhất chặn lại, và
            nó có thể đang bị tắt trên một máy chủ chưa gửi được email. */}
        <button className="btn btn-outline btn-sm btn-block" style={{ marginTop: 10 }} onClick={() => {
          const form = document.getElementById('auth-form');
          if (!form) return;
          form.email.value = 'host1@staylio.vn';
          form.password.value = 'stayhost123';
        }}>{t('Điền tài khoản chủ nhà')}</button>
      </div>
    </Modal>
  );
}

function ForgotModal() {
  const state = useStore();

  const submit = async e => {
    e.preventDefault();
    const email = e.currentTarget.email.value.trim();
    set({ authBusy: true, authError: null });
    try {
      const res = await api.forgotPassword(email);
      const token = res.resetLink ? new URLSearchParams(res.resetLink.split('?')[1]).get('token') : null;
      set({
        resetLink: res.resetLink,
        resetToken: token,
        authError: res.resetLink ? null : 'Không tìm thấy tài khoản với email này.'
      });
    } catch (err) {
      set({ authError: err.message });
    } finally {
      set({ authBusy: false });
    }
  };

  return (
    <Modal title={t('Quên mật khẩu')} size="narrow">
      <p style={{ margin: '0 0 18px', fontSize: 14, color: 'var(--ink-muted)', lineHeight: 1.6 }}>
        {t('Nhập email của bạn, chúng tôi sẽ gửi liên kết đặt lại mật khẩu.')}
      </p>
      <form onSubmit={submit}>
        <label className="form-field">
          <span className="cap">{t('Email')}</span>
          <input type="email" name="email" autoComplete="email" placeholder={t('ban@email.com')} required />
        </label>
        {state.authError && <div className="form-error">{state.authError}</div>}
        {state.resetLink ? <>
          <div className="book-alert">
            <b>{t('Liên kết đặt lại của bạn')}</b>
            <span>{t('Bản demo không gửi email thật — bấm nút dưới để tiếp tục.')}</span>
          </div>
          <button type="button" className="btn btn-dark btn-block" style={{ marginTop: 12 }}
                  onClick={() => set({ authMode: 'reset', authError: null })}>
            {t('Mở trang đặt lại mật khẩu')}
          </button>
        </> : (
          <button type="submit" className="btn btn-primary btn-block" disabled={state.authBusy}>
            {state.authBusy ? t('Đang gửi…') : t('Gửi liên kết')}
          </button>
        )}
      </form>
      <p style={{ textAlign: 'center', margin: '18px 0 0' }}>
        <button className="link-btn" onClick={() => set({ authMode: 'login', authError: null, resetLink: null })}>
          {t('Quay lại đăng nhập')}
        </button>
      </p>
    </Modal>
  );
}

function ResetModal() {
  const state = useStore();

  const submit = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    if (f.newPassword.value !== f.confirmPassword.value) {
      set({ authError: 'Hai mật khẩu không khớp.' });
      return;
    }
    set({ authBusy: true, authError: null });
    try {
      const user = await api.resetPassword({ token: state.resetToken, newPassword: f.newPassword.value });
      // docs/01 TK-08 — the new password is set, but the code is still owed.
      if (user?.challenge) {
        set({ twoFactor: user, authMode: 'twoFactor', overlay: 'login', resetLink: null, resetToken: null });
        toast('Đã đổi mật khẩu. Nhập mã xác thực để đăng nhập.');
        return;
      }
      set({ user, overlay: null, resetLink: null, resetToken: null });
      toast('Đã đổi mật khẩu và đăng nhập lại.');
    } catch (err) {
      set({ authError: err.message });
    } finally {
      set({ authBusy: false });
    }
  };

  return (
    <Modal title={t('Đặt mật khẩu mới')} size="narrow">
      <form onSubmit={submit}>
        <label className="form-field">
          <span className="cap">{t('Mật khẩu mới')}</span>
          <input type="password" name="newPassword" autoComplete="new-password" placeholder={t('Tối thiểu 8 ký tự')} required />
        </label>
        <label className="form-field">
          <span className="cap">{t('Nhập lại mật khẩu')}</span>
          <input type="password" name="confirmPassword" autoComplete="new-password" required />
        </label>
        {state.authError && <div className="form-error">{state.authError}</div>}
        <button type="submit" className="btn btn-primary btn-block" disabled={state.authBusy}>
          {state.authBusy ? t('Đang lưu…') : t('Đặt mật khẩu mới')}
        </button>
      </form>
    </Modal>
  );
}

/**
 * docs/01 TK-08 — the second step of a login. The account is not signed in yet:
 * everything this screen knows is a challenge token and a masked address.
 */
function TwoFactorModal() {
  const state = useStore();
  const tf = state.twoFactor;
  const [code, setCode] = useState('');

  if (!tf) return null;

  const submit = async e => {
    e.preventDefault();
    const ok = await submitTwoFactor(code.trim());
    if (ok && store.user) closeOverlay();
  };

  return (
    <Modal title={t('Xác minh đăng nhập')}>
      <p style={{ margin: '0 0 16px', fontSize: 14.5, lineHeight: 1.6, color: 'var(--ink-body)' }}>
        {t('Chúng tôi đã gửi mã')} {tf.codeLength} {t('số tới')} <b>{tf.sentTo}</b>. {t('Nhập mã để hoàn tất đăng nhập.')}
      </p>

      {/* Same escape hatch as sign-up: no SMS provider behind this build. */}
      {tf.devCode && <div className="form-note">{t('Mã thử nghiệm:')} <code>{tf.devCode}</code></div>}

      <form onSubmit={submit}>
        <label className="form-field"><span className="cap">{t('Mã xác minh')}</span>
          <input inputMode="numeric" autoComplete="one-time-code" maxLength={tf.codeLength}
                 value={code} onChange={e => setCode(e.target.value.replace(/\D/g, ''))}
                 style={{ letterSpacing: 6, fontSize: 20, textAlign: 'center' }} autoFocus /></label>

        {state.authError && <div className="form-error">{state.authError}</div>}

        <button type="submit" className="btn btn-primary btn-block" disabled={state.authBusy}>
          {state.authBusy ? t('Đang kiểm tra…') : t('Xác minh')}
        </button>
      </form>

      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 12 }}>
        <button className="link-btn" onClick={resendTwoFactor}>{t('Gửi lại mã')}</button>
        <button className="link-btn" onClick={() => set({ authMode: 'login', twoFactor: null, authError: null })}>
          {t('Đăng nhập bằng tài khoản khác')}
        </button>
      </div>
    </Modal>
  );
}

/**
 * docs/01 TK-02. Each provider opens its own window — the Google account chooser,
 * the Apple sheet, the Facebook dialog — and hands back a signed token that only
 * the server is allowed to believe. A provider with no credentials configured is
 * left off the modal entirely rather than shown as a button that cannot work.
 */
export function ProviderButtons() {
  const [config, setConfig] = useState(null);
  const [busy, setBusy] = useState(null);
  const googleSlot = useRef(null);

  useEffect(() => { externalConfig().then(setConfig); }, []);

  // Signing in is the same three steps whichever button was pressed; only the
  // way the token is obtained differs.
  const finish = async (provider, credential, label) => {
    if (!credential) return;                       // the window was closed
    setBusy(provider);
    try {
      const res = await api.externalSignIn(provider, credential);
      // docs/01 TK-08 — the provider vouched for who this is; the account's own
      // second factor is still owed before there is a session.
      if (res?.challenge) {
        set({ twoFactor: res, authMode: 'twoFactor' });
        return;
      }
      await loadMe();
      closeOverlay();
      toast(`Đã đăng nhập bằng ${label}.`);
    } catch (err) {
      set({ authError: err.message });
    } finally { setBusy(null); }
  };

  useEffect(() => {
    if (!config?.googleClientId || !googleSlot.current) return;

    mountGoogleButton(
      googleSlot.current, config.googleClientId,
      credential => finish('google', credential, 'Google'),
      err => set({ authError: err.message })
    ).catch(err => set({ authError: err.message }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config?.googleClientId]);

  const run = async (provider, label, start) => {
    setBusy(provider);
    try {
      const credential = await start();
      setBusy(null);
      await finish(provider, credential, label);
    } catch (err) {
      setBusy(null);
      set({ authError: err.message });
    }
  };

  if (!config) return null;
  if (!config.googleClientId && !config.appleServicesId && !config.facebookAppId) return null;

  return (
    <>
    <div className="auth-providers">
      {config.googleClientId && <div className="auth-provider-slot" ref={googleSlot} />}

      {config.appleServicesId && (
        <button className="auth-provider" disabled={busy !== null}
                onClick={() => run('apple', 'Apple', () => signInWithApple({
                  servicesId: config.appleServicesId, redirectUri: config.appleRedirectUri
                }))}>
          {busy === 'apple' ? t('Đang mở…') : t('Tiếp tục với Apple')}
        </button>
      )}

      {config.facebookAppId && (
        <button className="auth-provider" disabled={busy !== null}
                onClick={() => run('facebook', 'Facebook',
                  () => signInWithFacebook({ appId: config.facebookAppId }))}>
          {busy === 'facebook' ? t('Đang mở…') : t('Tiếp tục với Facebook')}
        </button>
      )}
    </div>
    {/* The divider belongs to the buttons: with none on screen it says nothing. */}
    <div className="auth-or"><span>{t('hoặc')}</span></div>
    </>
  );
}


/*
 * docs/02 F1 — the eight-tab ProfileModal used to live here. It is gone on
 * purpose, not lost: /cai-dat renders the same exported panels, and two live
 * doors onto one set of forms is how a fix lands on only one of them. The
 * pieces it carried inline — the password form and the device list — were
 * lifted out below so the page could use them without copying.
 */

/** docs/01 TK-08 — the password half of "Đăng nhập & bảo mật". */
export function ChangePasswordForm() {
  const state = useStore();

  const changePassword = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    if (f.newPassword.value !== f.confirmPassword.value) {
      set({ authError: 'Hai mật khẩu mới không khớp.' });
      return;
    }
    try {
      await api.changePassword({ currentPassword: f.currentPassword.value, newPassword: f.newPassword.value });
      // On a page there is no overlay to close, so the form itself has to say
      // what happened: clear the fields and toast.
      f.reset();
      set({ authError: null });
      toast('Đã đổi mật khẩu.');
    } catch (err) { set({ authError: err.message }); }
  };

  return (
    <form onSubmit={changePassword}>
      <label className="form-field"><span className="cap">{t('Mật khẩu hiện tại')}</span>
        <input type="password" name="currentPassword" autoComplete="current-password" required /></label>
      <label className="form-field"><span className="cap">{t('Mật khẩu mới')}</span>
        <input type="password" name="newPassword" autoComplete="new-password" placeholder={t('Tối thiểu 8 ký tự')} required /></label>
      <label className="form-field"><span className="cap">{t('Nhập lại mật khẩu mới')}</span>
        <input type="password" name="confirmPassword" autoComplete="new-password" required /></label>
      {state.authError && <div className="form-error">{state.authError}</div>}
      <p style={{ fontSize: 12.5, color: 'var(--ink-muted)', lineHeight: 1.5, margin: '0 0 12px' }}>
        {t('Đổi mật khẩu sẽ đăng xuất mọi thiết bị khác.')}
      </p>
      <button type="submit" className="btn btn-primary btn-block">{t('Đổi mật khẩu')}</button>
    </form>
  );
}

/** docs/01 TK-08 — every signed-in device, with a per-session sign-out. */
export function DeviceList() {
  const state = useStore();
  useEffect(() => { loadSessions(); }, []);

  return (
    <div style={{ display: 'grid', gap: 10 }}>
      {(state.sessions ?? []).length ? state.sessions.map(s => (
        <div className="cal-row" key={s.id}>
          <div style={{ flex: 1, minWidth: 0 }}>
            <b style={{ fontSize: 14 }}>{s.device.split(' · ').map(part => t(part)).join(' · ')}</b>
            {s.isCurrent && <span className="badge confirmed" style={{ marginLeft: 8 }}>{t('Thiết bị này')}</span>}
            <div style={{ fontSize: 12.5, color: 'var(--ink-muted)' }}>
              {t('Đăng nhập')} {longDate(s.createdAt.slice(0, 10))}
            </div>
          </div>
          {!s.isCurrent && (
            <button className="text-btn" onClick={async () => {
              try { await api.revokeSession(s.id); await loadSessions(); toast('Đã đăng xuất thiết bị đó.'); }
              catch (err) { toast(err.message); }
            }}>{t('Đăng xuất')}</button>
          )}
        </div>
      )) : <p style={{ fontSize: 14, color: 'var(--ink-muted)' }}>{t('Đang tải phiên đăng nhập…')}</p>}
    </div>
  );
}

/**
 * docs/01 TK-04 — the whole profile in one form: the photo, the name other
 * people see, and the four things the spec asks for beyond a bio.
 *
 * The photo, the languages and the interests are held in React state because
 * each is edited by clicking rather than typing; everything else stays an
 * uncontrolled field, the way the rest of the modals here work.
 */
export function ProfileForm() {
  const state = useStore();
  const u = state.user;
  const navigate = useNavigate();

  const [avatar, setAvatar] = useState(u.avatarUrl ?? null);
  const [languages, setLanguages] = useState(() => [...(u.languages ?? [])]);
  const [interests, setInterests] = useState(() => [...(u.interests ?? [])]);
  const [draft, setDraft] = useState('');
  const [uploading, setUploading] = useState(false);
  const [saving, setSaving] = useState(false);
  const photoRef = useRef(null);

  useEffect(() => { loadSpokenLanguages(); }, []);

  const options = state.spokenLanguages ?? [];

  const pickPhoto = async e => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;

    setUploading(true);
    try {
      const body = new FormData();
      body.append('files', file);
      const res = await fetch('/api/uploads/images', { method: 'POST', body, credentials: 'same-origin' });
      const payload = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(payload.message ?? 'Không tải được ảnh.');
      setAvatar(payload.urls[0]);
    } catch (err) {
      toast(err.message);
    } finally {
      setUploading(false);
    }
  };

  const toggleLanguage = code =>
    setLanguages(list => list.includes(code) ? list.filter(c => c !== code) : [...list, code]);

  const addInterest = () => {
    const value = draft.trim();
    if (!value) return;
    setInterests(list => list.some(i => i.toLowerCase() === value.toLowerCase()) ? list : [...list, value]);
    setDraft('');
  };

  const submit = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    setSaving(true);
    await saveProfile({
      fullName: f.fullName.value.trim(),
      displayName: f.displayName.value.trim() || null,
      phone: f.phone.value.trim() || null,
      bio: f.bio.value.trim() || null,
      avatarUrl: avatar,
      languages,
      location: f.location.value.trim() || null,
      occupation: f.occupation.value.trim() || null,
      interests,
      // docs/01 TK-13 — emergency contact for trip incidents.
      emergencyContactName: f.emergencyContactName.value.trim() || null,
      emergencyContactPhone: f.emergencyContactPhone.value.trim() || null,
      emergencyContactRelation: f.emergencyContactRelation.value.trim() || null
    });
    setSaving(false);
  };

  return (
    <form onSubmit={submit}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 16 }}>
        <Avatar url={avatar} initials={u.initials} size={64} />
        <div style={{ display: 'grid', gap: 6 }}>
          <button type="button" className="btn btn-sm" disabled={uploading}
                  onClick={() => photoRef.current?.click()}>
            {uploading ? t('Đang tải…') : avatar ? t('Đổi ảnh') : t('Tải ảnh lên')}
          </button>
          {avatar && (
            <button type="button" className="text-btn" style={{ fontSize: 12.5 }}
                    onClick={() => setAvatar(null)}>{t('Gỡ ảnh')}</button>
          )}
        </div>
        <input ref={photoRef} type="file" accept="image/*" hidden onChange={pickPhoto} />
      </div>

      <label className="form-field"><span className="cap">{t('Họ và tên')}</span>
        <input type="text" name="fullName" defaultValue={u.fullName} required /></label>

      <label className="form-field">
        <span className="cap">{t('Tên hiển thị')}</span>
        <input type="text" name="displayName" defaultValue={u.displayName ?? ''}
               placeholder={u.fullName} maxLength={80} />
      </label>
      <p className="field-note">{t('Đây là tên người khác nhìn thấy. Bỏ trống thì dùng họ tên ở trên.')}</p>

      <label className="form-field"><span className="cap">{t('Số điện thoại')}</span>
        <input type="tel" name="phone" defaultValue={u.phone ?? ''} /></label>

      <label className="form-field"><span className="cap">{t('Nơi ở')}</span>
        <input type="text" name="location" defaultValue={u.location ?? ''}
               placeholder="Đà Nẵng, Việt Nam" maxLength={80} /></label>

      <label className="form-field"><span className="cap">{t('Nghề nghiệp')}</span>
        <input type="text" name="occupation" defaultValue={u.occupation ?? ''}
               placeholder={t('Kiến trúc sư')} maxLength={80} /></label>

      <div className="form-field">
        <span className="cap">{t('Ngôn ngữ nói được')}</span>
        <div className="chip-wrap">
          {options.map(l => (
            <button type="button" key={l.code}
                    className={`quick-chip ${languages.includes(l.code) ? 'is-on' : ''}`}
                    aria-pressed={languages.includes(l.code)}
                    onClick={() => toggleLanguage(l.code)}>{t(l.label)}</button>
          ))}
        </div>
      </div>

      <div className="form-field">
        <span className="cap">{t('Sở thích')}</span>
        {!!interests.length && (
          <div className="chip-wrap" style={{ marginBottom: 8 }}>
            {interests.map(i => (
              <span className="quick-chip is-on" key={i}>
                {i}
                <button type="button" aria-label={`${t('Bỏ')} ${i}`} className="chip-x"
                        onClick={() => setInterests(list => list.filter(x => x !== i))}>×</button>
              </span>
            ))}
          </div>
        )}
        {/* Not a nested form — Enter has to add a tag, not save the profile. */}
        <div className="chip-add">
          <input type="text" value={draft} maxLength={40} placeholder={t('Nấu ăn, leo núi, nhiếp ảnh…')}
                 onChange={e => setDraft(e.target.value)}
                 onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); addInterest(); } }} />
          <button type="button" className="btn btn-sm" onClick={addInterest}>{t('Thêm')}</button>
        </div>
      </div>

      <label className="form-field"><span className="cap">{t('Giới thiệu')}</span>
        <textarea name="bio" rows={4} defaultValue={u.bio ?? ''} maxLength={700}
          style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} /></label>

      {/* docs/01 TK-13 — liên hệ khẩn cấp, dùng khi có sự cố trong chuyến đi. */}
      <div className="form-field" style={{ marginTop: 4 }}>
        <span className="cap">{t('Liên hệ khẩn cấp')}</span>
        <p className="field-note" style={{ marginTop: 0 }}>{t('Chỉ dùng khi có sự cố trong chuyến đi.')}</p>
      </div>
      <div className="field-grid">
        <label className="form-field"><span className="cap">{t('Tên người liên hệ')}</span>
          <input type="text" name="emergencyContactName" defaultValue={u.emergencyContactName ?? ''}
                 maxLength={80} placeholder={t('Nguyễn Văn A')} /></label>
        <label className="form-field"><span className="cap">{t('Số điện thoại')}</span>
          <input type="tel" name="emergencyContactPhone" defaultValue={u.emergencyContactPhone ?? ''}
                 maxLength={80} placeholder="09xx xxx xxx" /></label>
      </div>
      <label className="form-field"><span className="cap">{t('Quan hệ')}</span>
        <input type="text" name="emergencyContactRelation" defaultValue={u.emergencyContactRelation ?? ''}
               maxLength={80} placeholder={t('Người thân, bạn bè…')} /></label>

      <button type="submit" className="btn btn-primary btn-block" disabled={saving}>
        {saving ? t('Đang lưu…') : t('Lưu thay đổi')}
      </button>

      <button type="button" className="btn btn-block" style={{ marginTop: 8 }}
              onClick={() => { closeOverlay(); navigate(`/users/${u.id}`); }}>
        {t('Xem hồ sơ công khai')}
      </button>
    </form>
  );
}

/**
 * docs/01 TK-08 — the second factor. Switching it on takes a code so nobody
 * points it at an address they cannot read; switching it off takes the
 * password, so an unlocked screen is not enough to strip it.
 */
export function TwoFactorPanel() {
  const state = useStore();
  const [tf, setTf] = useState(null);
  const [stage, setStage] = useState('idle');   // idle | code | off
  const [kind, setKind] = useState('Email');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [devCode, setDevCode] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.twoFactorState().then(setTf).catch(() => setTf(null)); }, []);

  if (!tf) return null;

  const run = async fn => {
    setBusy(true);
    try { await fn(); } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const sendCode = () => run(async () => {
    const res = await api.enableTwoFactor({ kind, code: null });
    setDevCode(res.devCode ?? null);
    setStage('code');
    toast(res.message);
  });

  const confirm = () => run(async () => {
    setTf(await api.enableTwoFactor({ kind, code: code.trim() }));
    setStage('idle');
    setCode('');
    toast('Đã bật bảo mật 2 lớp.');
  });

  const turnOff = () => run(async () => {
    setTf(await api.disableTwoFactor(password));
    setStage('idle');
    setPassword('');
    toast('Đã tắt bảo mật 2 lớp.');
  });

  return (
    <section className="modal-section" style={{ paddingTop: 0 }}>
      <h3>{t('Bảo mật 2 lớp')}</h3>
      <span className="hint">
        {t('Sau khi nhập mật khẩu, chúng tôi hỏi thêm một mã 6 số gửi tới')} {kind === 'Phone' ? t('điện thoại') : t('email')} {t('của bạn.')}
      </span>

      <div className="count-row">
        <div className="tx">
          <b>{tf.enabled ? t('Đang bật') : t('Đang tắt')}</b>
          <span>{tf.enabled ? `${t('Mã gửi tới')} ${tf.sentTo}` : t('Chỉ cần mật khẩu để đăng nhập.')}</span>
        </div>
        {tf.enabled
          ? <button type="button" className="pill" onClick={() => setStage(stage === 'off' ? 'idle' : 'off')}>{t('Tắt')}</button>
          : <button type="button" className="pill is-on" onClick={sendCode} disabled={busy}>{t('Bật')}</button>}
      </div>

      {!tf.enabled && stage === 'idle' && (
        <label className="form-field"><span className="cap">{t('Gửi mã tới')}</span>
          <select value={kind} onChange={e => setKind(e.target.value)}>
            <option value="Email">{t('Email')} {state.user?.email ? `(${state.user.email})` : ''}</option>
            <option value="Phone" disabled={!state.user?.phone}>
              {t('Số điện thoại')} {state.user?.phone ? `(${state.user.phone})` : t('— chưa có')}
            </option>
          </select>
        </label>
      )}

      {stage === 'code' && <>
        {devCode && <div className="form-note">{t('Mã thử nghiệm:')} <code>{devCode}</code></div>}
        <label className="form-field"><span className="cap">{t('Nhập mã vừa gửi')}</span>
          <input inputMode="numeric" maxLength={6} value={code}
                 onChange={e => setCode(e.target.value.replace(/\D/g, ''))} /></label>
        <button type="button" className="btn btn-primary btn-block" onClick={confirm} disabled={busy}>
          {t('Xác nhận bật')}
        </button>
      </>}

      {stage === 'off' && <>
        <label className="form-field"><span className="cap">{t('Mật khẩu hiện tại')}</span>
          <input type="password" value={password} autoComplete="current-password"
                 onChange={e => setPassword(e.target.value)} /></label>
        <button type="button" className="btn btn-block" onClick={turnOff} disabled={busy}>
          {t('Xác nhận tắt')}
        </button>
      </>}
    </section>
  );
}

/**
 * docs/01 TK-06 — proving who somebody is. Two photos of a document and one of
 * their face, reviewed by a person; the badge on the public profile is what
 * comes out the other end.
 */
export function IdentityPanel() {
  const [check, setCheck] = useState(undefined);
  const [document, setDocument] = useState('NationalId');
  const [shots, setShots] = useState({ front: null, back: null, selfie: null });
  const [number, setNumber] = useState('');
  const [busy, setBusy] = useState(false);

  const load = () => api.identityStatus().then(setCheck).catch(() => setCheck(null));
  useEffect(() => { load(); }, []);

  const needsBack = document !== 'Passport';

  const pick = async (slot, file) => {
    if (!file) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append('files', file);
      // Giấy tờ tuỳ thân đi đường riêng: file không nằm trong thư mục công khai,
      // chỉ chủ ảnh và admin đã ghi lý do xem mới mở được (docs/08 §4).
      const res = await fetch('/api/uploads/identity', { method: 'POST', body, credentials: 'same-origin' });
      const payload = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(payload.message ?? 'Không tải được ảnh.');
      setShots(s => ({ ...s, [slot]: payload.urls[0] }));
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  const submit = async e => {
    e.preventDefault();
    setBusy(true);
    try {
      setCheck(await api.submitIdentity({
        document,
        documentNumber: number.trim() || null,
        frontImageUrl: shots.front,
        backImageUrl: needsBack ? shots.back : null,
        selfieImageUrl: shots.selfie
      }));
      toast('Đã gửi hồ sơ. Chúng tôi sẽ phản hồi sớm.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  if (check === undefined) return <p className="field-note">{t('Đang tải…')}</p>;

  if (check && check.status !== 'Rejected') {
    return (
      <div style={{ display: 'grid', gap: 12 }}>
        <div>
          <span className={`badge ${check.badgeClass}`}>{check.statusLabel}</span>
        </div>
        <p className="field-note" style={{ margin: 0 }}>
          {check.documentLabel}{check.documentLast4 ? ` ••••${check.documentLast4}` : ''} · {t('gửi ngày')}{' '}
          {longDate(check.submittedAt.slice(0, 10))}
        </p>
        {check.status === 'Pending' && (
          <p className="field-note" style={{ margin: 0 }}>
            {t('Hồ sơ đang chờ duyệt. Bạn sẽ nhận được thông báo khi có kết quả.')}
          </p>
        )}
      </div>
    );
  }

  return (
    <form onSubmit={submit}>
      {check?.status === 'Rejected' && (
        <div className="book-alert">
          <b>{t('Hồ sơ trước bị từ chối')}</b>
          <span>{check.note}</span>
        </div>
      )}

      <p className="field-note" style={{ marginTop: 10 }}>
        {t('Ảnh giấy tờ và ảnh chân dung chỉ dùng để xác minh, không hiển thị công khai.')}
      </p>

      <label className="form-field"><span className="cap">{t('Loại giấy tờ')}</span>
        <select value={document} onChange={e => setDocument(e.target.value)}>
          <option value="NationalId">{t('Căn cước công dân')}</option>
          <option value="Passport">{t('Hộ chiếu')}</option>
          <option value="DriverLicence">{t('Giấy phép lái xe')}</option>
        </select>
      </label>

      <label className="form-field"><span className="cap">{t('Số giấy tờ')} <span style={{ fontWeight: 400 }}>{t('(chỉ lưu 4 số cuối)')}</span></span>
        <input value={number} maxLength={30} onChange={e => setNumber(e.target.value)} /></label>

      <div className="shot-row">
        <Shot label="Mặt trước" url={shots.front} onPick={f => pick('front', f)} />
        {needsBack && <Shot label="Mặt sau" url={shots.back} onPick={f => pick('back', f)} />}
        <Shot label="Ảnh chân dung" url={shots.selfie} onPick={f => pick('selfie', f)} />
      </div>

      <button type="submit" className="btn btn-primary btn-block" style={{ marginTop: 14 }} disabled={busy}>
        {busy ? t('Đang xử lý…') : t('Gửi hồ sơ xác minh')}
      </button>
    </form>
  );
}

function Shot({ label, url, onPick }) {
  const ref = useRef(null);
  return (
    <div className="shot">
      <button type="button" className={`shot-box ${url ? 'is-set' : ''}`} onClick={() => ref.current?.click()}>
        {url ? <img src={url} alt="" /> : <span>+</span>}
      </button>
      <span>{t(label)}</span>
      <input ref={ref} type="file" accept="image/*" hidden
             onChange={e => { onPick(e.target.files?.[0]); e.target.value = ''; }} />
    </div>
  );
}

/**
 * docs/01 TK-10 — the matrix. docs/03 §11 locks the transactional rows, and the
 * server is what enforces that; a locked switch here just says so out loud.
 */
/**
 * docs/07 §4 — the cards a guest has kept. The number is typed once and never
 * comes back: everything on this screen is brand, last four and expiry, which is
 * all §4 allows to exist.
 */
export function SavedCardsPanel() {
  const [cards, setCards] = useState(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => { api.savedCards().then(setCards).catch(e => toast(e.message)); }, []);

  if (!cards) return <div className="stat skeleton" style={{ height: 140, border: 0 }} />;

  const run = async (fn) => {
    setBusy(true);
    setError(null);
    try { setCards(await fn()); }
    catch (err) { setError(t(err.message)); }
    finally { setBusy(false); }
  };

  const add = async e => {
    e.preventDefault();
    const f = e.currentTarget;
    const body = {
      number: f.number.value,
      expiryMonth: Number(f.expiryMonth.value),
      expiryYear: Number(f.expiryYear.value),
      nickname: f.nickname.value || null,
      makeDefault: cards.length === 0
    };
    setBusy(true);
    setError(null);
    try {
      setCards(await api.addCard(body));
      setAdding(false);
      toast('Đã lưu thẻ.');
    } catch (err) { setError(t(err.message)); }
    finally { setBusy(false); }
  };

  return (
    <div>
      <p className="section-sub" style={{ marginTop: 0 }}>
        {t('Staylio chỉ lưu thương hiệu, 4 số cuối và tháng/năm hết hạn. Số thẻ đầy đủ và mã CVV không bao giờ được lưu.')}
      </p>

      {!!error && <p className="notice notice-warn">{error}</p>}

      <div style={{ display: 'grid', gap: 10, margin: '16px 0' }}>
        {cards.map(c => (
          <div className="cal-row" key={c.id}>
            <div style={{ flex: 1, minWidth: 0 }}>
              <b style={{ fontSize: 14 }}>{c.brandLabel} •••• {c.last4}</b>
              <span style={{ color: 'var(--ink-muted)', fontSize: 13 }}> · {c.expiry}</span>
              {!!c.nickname && <span style={{ color: 'var(--ink-muted)', fontSize: 13 }}> · {c.nickname}</span>}
              <div style={{ marginTop: 4, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {c.isDefault && <span className="badge confirmed">{t('Mặc định')}</span>}
                {c.isExpired && <span className="badge cancelled">{t('Đã hết hạn')}</span>}
                {c.expiringSoon && !c.isExpired && <span className="badge pending">{t('Sắp hết hạn')}</span>}
                {c.hasScheduledCharge && <span className="badge pending">{t('Còn lịch thu tự động')}</span>}
              </div>
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              {!c.isDefault && (
                <button className="link-btn" disabled={busy}
                        onClick={() => run(() => api.makeCardDefault(c.id))}>{t('Đặt mặc định')}</button>
              )}
              <button className="link-btn" disabled={busy}
                      onClick={() => run(() => api.removeCard(c.id))}>{t('Xoá')}</button>
            </div>
          </div>
        ))}
        {!cards.length && (
          <p style={{ fontSize: 13.5, color: 'var(--ink-muted)' }}>{t('Chưa có thẻ nào được lưu.')}</p>
        )}
      </div>

      {adding ? (
        <form onSubmit={add}>
          <label className="form-field"><span className="cap">{t('Số thẻ')}</span>
            <input name="number" inputMode="numeric" placeholder="4111 1111 1111 1111" required /></label>
          <div className="field-grid">
            <label className="form-field"><span className="cap">{t('Tháng hết hạn')}</span>
              <input name="expiryMonth" inputMode="numeric" placeholder="08" required /></label>
            <label className="form-field"><span className="cap">{t('Năm hết hạn')}</span>
              <input name="expiryYear" inputMode="numeric" placeholder="2029" required /></label>
          </div>
          <label className="form-field"><span className="cap">{t('Tên gọi')} {t('(không bắt buộc)')}</span>
            <input name="nickname" placeholder={t('Thẻ công ty')} /></label>
          <div style={{ display: 'flex', gap: 10 }}>
            <button type="submit" className="btn btn-primary" disabled={busy}>
              {busy ? t('Đang lưu…') : t('Lưu thẻ')}
            </button>
            <button type="button" className="btn" onClick={() => { setAdding(false); setError(null); }}>{t('Huỷ')}</button>
          </div>
        </form>
      ) : (
        <button className="btn btn-primary" onClick={() => setAdding(true)}>{t('+ Thêm thẻ')}</button>
      )}
    </div>
  );
}

export function NotificationMatrix() {
  const [prefs, setPrefs] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.notificationPrefs().then(setPrefs).catch(err => toast(err.message)); }, []);

  if (!prefs) return <p className="field-note">{t('Đang tải…')}</p>;

  const toggle = async (topic, cell) => {
    if (cell.locked || busy) return;
    setBusy(true);
    try {
      setPrefs(await api.setNotificationPref({ topic, channel: cell.channel, on: !cell.on }));
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <div className="matrix-wrap">
      <table className="matrix">
        <thead>
          <tr>
            <th />
            {prefs.channelLabels.map(l => <th key={l}>{t(l)}</th>)}
          </tr>
        </thead>
        <tbody>
          {prefs.rows.map(row => (
            <tr key={row.topic}>
              <th scope="row">
                <b>{t(row.label)}</b>
                <span>{t(row.note)}</span>
              </th>
              {row.cells.map(cell => (
                <td key={cell.channel}>
                  <button type="button"
                          className={`switch-btn ${cell.on ? 'is-on' : ''} ${cell.locked ? 'is-locked' : ''}`}
                          aria-pressed={cell.on} disabled={cell.locked}
                          title={cell.locked ? t('Thông báo giao dịch luôn được gửi') : undefined}
                          onClick={() => toggle(row.topic, cell)}>
                    <span />
                  </button>
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <p className="field-note" style={{ marginTop: 12 }}>
        {t('Thông báo về đơn đặt và thanh toán luôn được gửi — đó là bằng chứng về tiền của bạn.')}
      </p>
    </div>
  );
}

/**
 * docs/01 TK-11 — one file, downloaded now, with everything held about them —
 * and docs/08 §9, the formal requests that go to a person: a copy delivered by
 * a time-limited link, or the account itself erased.
 */
/**
 * docs/01 TK-12 — stepping away without deleting anything.
 *
 * Deliberately not the same thing as the suspensions of docs/08 §5: those are
 * something the platform does to somebody, with a policy and an appeal. This is
 * somebody's own choice, and it ends the moment they sign back in.
 */
export function PauseAccount() {
  const [state, setState] = useState(null);
  const [busy, setBusy] = useState(false);

  const load = () => api.pauseState().then(setState).catch(() => setState(null));
  useEffect(() => { load(); }, []);

  if (!state) return null;

  const run = async (fn, done) => {
    setBusy(true);
    try { setState(await fn()); toast(done); await loadMe(); }
    catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  if (state.isPaused) {
    return (
      <div className="notice" style={{ display: 'grid', gap: 10 }}>
        <b>{t('Tài khoản đang tạm dừng')}</b>
        <span style={{ fontSize: 13.5, lineHeight: 1.55, color: 'var(--ink-body)' }}>{state.notice}</span>
        {state.hiddenListings > 0 && (
          <span className="field-note" style={{ margin: 0 }}>
            {t('{} tin đăng đang được ẩn và sẽ hiện lại khi bạn quay lại.').replace('{}', state.hiddenListings)}
          </span>
        )}
        <div>
          <button className="btn btn-primary btn-sm" disabled={busy}
                  onClick={() => run(api.resumeAccount, 'Đã mở lại tài khoản.')}>
            {t('Mở lại tài khoản')}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div style={{ display: 'grid', gap: 10 }}>
      <p className="field-note" style={{ margin: 0 }}>
        {t('Tạm dừng là bước nhẹ hơn xoá: tin đăng được ẩn, không ai đặt hay nhắn tin được, và hồ sơ công khai của bạn không hiện. Không có gì bị xoá, đăng nhập lại là mở lại.')}
      </p>
      {state.canPause ? (
        <div>
          <button className="btn btn-outline btn-sm" disabled={busy}
                  onClick={() => {
                    if (!confirm(t('Tạm dừng tài khoản? Đăng nhập lại bất cứ lúc nào để mở lại.'))) return;
                    run(api.pauseAccount, 'Đã tạm dừng tài khoản.');
                  }}>
            {t('Tạm dừng tài khoản')}
          </button>
        </div>
      ) : (
        <p className="notice notice-warn" style={{ margin: 0 }}>{state.blocker}</p>
      )}
    </div>
  );
}

export function DataPanel() {
  const [requests, setRequests] = useState([]);
  const [busy, setBusy] = useState(false);

  const load = () => api.myDataRequests().then(setRequests).catch(() => setRequests([]));
  useEffect(() => { load(); }, []);

  const ask = async kind => {
    setBusy(true);
    try {
      await api.askDataRequest(kind);
      await load();
      toast(kind === 'Erase'
        ? 'Đã gửi yêu cầu xoá tài khoản. Chúng tôi sẽ xử lý trong 30 ngày.'
        : 'Đã gửi yêu cầu. Chúng tôi sẽ gửi liên kết tải trong 30 ngày.');
    } catch (err) { toast(err.message); }
    finally { setBusy(false); }
  };

  return (
    <div style={{ display: 'grid', gap: 12 }}>
      <p style={{ margin: 0, fontSize: 14.5, lineHeight: 1.6, color: 'var(--ink-body)' }}>
        {t('Tải về bản sao dữ liệu cá nhân của bạn: hồ sơ, đơn đặt, hoá đơn, đánh giá, tin nhắn, số dư, hồ sơ Staylio Shield, thông báo và lịch sử đăng nhập.')}
      </p>
      <a className="btn btn-primary btn-block" href="/api/account/data/export" download>
        {t('Tải dữ liệu của tôi (.json)')}
      </a>
      <p className="field-note" style={{ margin: 0 }}>
        {t('Tệp gồm cả dữ liệu giao dịch mà sàn phải giữ cho nghĩa vụ kế toán.')}
      </p>

      <hr style={{ border: 0, borderTop: '1px solid var(--line)', margin: '4px 0' }} />

      {/* docs/01 TK-12 — "tạm vô hiệu hoá hoặc xoá". Only the erase half was
          ever built, and the code counted as done because one clause of an
          "hoặc" was there. */}
      <PauseAccount />

      <hr style={{ border: 0, borderTop: '1px solid var(--line)', margin: '4px 0' }} />

      <p className="field-note" style={{ margin: 0 }}>
        {t('Bạn cũng có thể gửi yêu cầu chính thức. Xoá tài khoản là')} <b>{t('ẩn danh hoá')}</b>{t(': tên, ảnh, email, số điện thoại và giấy tờ bị xoá; đơn đặt, giao dịch và sổ ghi tiền được giữ lại theo nghĩa vụ kế toán và pháp lý. Đánh giá bạn đã viết không bị xoá, chỉ ẩn tên người viết.')}
      </p>

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <button className="btn btn-outline btn-sm" disabled={busy} onClick={() => ask('Export')}>
          {t('Yêu cầu bản sao qua liên kết')}
        </button>
        <button className="btn btn-outline btn-sm" disabled={busy} onClick={() => ask('Erase')}>
          {t('Yêu cầu xoá tài khoản')}
        </button>
      </div>

      {!!requests.length && (
        <div style={{ display: 'grid', gap: 8, marginTop: 4 }}>
          {requests.map(r => (
            <div key={r.id} style={{ fontSize: 13 }}>
              <b>{r.kindLabel}</b> · {r.statusLabel} · {t('hạn')} {longDate(r.dueBy)}
              {!!r.note && <div className="field-note" style={{ margin: 0 }}>{r.note}</div>}
              {!!r.downloadUrl && (
                <a className="link-btn" href={r.downloadUrl} download>{t('Tải bản sao (liên kết có hạn)')}</a>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const REVIEW_FIELDS = [
  ['cleanliness', 'Mức độ sạch sẽ'], ['accuracy', 'Độ chính xác'], ['checkIn', 'Nhận phòng'],
  ['communication', 'Giao tiếp'], ['location', 'Vị trí'], ['value', 'Giá trị']
];

const BLANK_REVIEW = {
  rating: 5, cleanliness: 5, accuracy: 5, checkIn: 5,
  communication: 5, location: 5, value: 5, text: ''
};

/**
 * docs/01 TK-01 — proving the phone or the email with a six-digit code, and
 * docs/01 TK-02 — what is attached to this account.
 */
export function Verification() {
  const [v, setV] = useState(null);
  const [sending, setSending] = useState(null);
  const [codes, setCodes] = useState({ email: '', phone: '' });

  const load = () => api.verification().then(setV).catch(e => toast(e.message));
  useEffect(() => { load(); }, []);

  if (!v) return <div className="stat skeleton" style={{ height: 160, border: 0, marginTop: 16 }} />;

  const send = async kind => {
    setSending(kind);
    try {
      const res = await api.sendCode(kind);
      // No SMS provider in this build, so development hands the code back
      // rather than leaving the flow impossible to finish.
      if (res.devCode) setCodes(c => ({ ...c, [kind]: res.devCode }));
      toast(res.devCode ? `${res.message} Mã thử nghiệm: ${res.devCode}` : res.message);
    } catch (err) { toast(err.message); } finally { setSending(null); }
  };

  const confirm = async kind => {
    try {
      await api.confirmCode(kind, codes[kind]);
      setCodes(c => ({ ...c, [kind]: '' }));
      await loadMe();
      load();
      toast('Đã xác thực.');
    } catch (err) { toast(err.message); }
  };

  const unlink = async provider => {
    try { await api.unlinkProvider(provider); load(); toast('Đã bỏ liên kết.'); }
    catch (err) { toast(err.message); }
  };

  const row = (kind, value, confirmed) => !value ? null : (
    <div className="verify-row" key={kind}>
      <div style={{ minWidth: 0, flex: 1 }}>
        <b>{value}</b>
        <div className="meta">
          {confirmed
            ? t('Đã xác thực')
            : `${t('Chưa xác thực · mã gồm')} ${v.codeLength} ${t('chữ số, hiệu lực')} ${v.codeMinutes} ${t('phút')}`}
        </div>
      </div>

      {confirmed
        ? <span className="badge confirmed">{t('Xong')}</span>
        : <>
            <input className="verify-code" inputMode="numeric" maxLength={v.codeLength}
                   placeholder="000000" value={codes[kind]}
                   onChange={e => setCodes(c => ({ ...c, [kind]: e.target.value.replace(/\D/g, '') }))} />
            <button className="btn btn-outline btn-sm" disabled={sending === kind}
                    onClick={() => send(kind)}>{sending === kind ? t('Đang gửi…') : t('Gửi mã')}</button>
            <button className="btn btn-primary btn-sm"
                    disabled={codes[kind].length !== v.codeLength}
                    onClick={() => confirm(kind)}>{t('Xác nhận')}</button>
          </>}
    </div>
  );

  return (
    <div style={{ marginTop: 8 }}>
      {row('email', v.email, v.emailConfirmed)}
      {row('phone', v.phone, v.phoneConfirmed)}
      {!v.email && !v.phone && <p className="section-sub">{t('Tài khoản chưa có email hay số điện thoại nào.')}</p>}

      <h4 style={{ margin: '22px 0 4px', fontSize: 14.5, fontWeight: 800 }}>{t('Email công ty')}</h4>
      <p className="section-sub" style={{ marginTop: 0 }}>
        {t('Dành cho công tác. Dùng tên miền tổ chức, không dùng email cá nhân.')}
      </p>
      <WorkEmailPanel codeLength={v.codeLength} />

      <h4 style={{ margin: '22px 0 4px', fontSize: 14.5, fontWeight: 800 }}>{t('Tài khoản đã liên kết')}</h4>
      {v.linked.length ? v.linked.map(l => (
        <div className="verify-row" key={l.provider}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <b>{l.label}</b>
            <div className="meta">{l.email ?? t('Không có email')}</div>
          </div>
          <button className="btn btn-outline btn-sm" onClick={() => unlink(l.provider)}>{t('Bỏ liên kết')}</button>
        </div>
      )) : <p className="section-sub">{t('Chưa liên kết Google, Apple hay Facebook nào.')}</p>}
    </div>
  );
}

/** docs/01 TM-23 — the searches this account is being alerted about. */
export function SavedSearchesPanel() {
  const [rows, setRows] = useState(null);
  const load = () => api.savedSearches().then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, []);

  const remove = async id => {
    try { await api.deleteSavedSearch(id); load(); toast('Đã xoá tìm kiếm.'); }
    catch (err) { toast(err.message); }
  };

  if (!rows) return null;

  return (
    <div style={{ marginTop: 28 }}>
      <h4 style={{ margin: '0 0 4px', fontSize: 14.5, fontWeight: 800 }}>{t('Tìm kiếm đã lưu')}</h4>
      <p className="section-sub" style={{ marginTop: 0 }}>
        {t('Chúng tôi báo cho bạn khi có chỗ mới phù hợp. Lưu từ nút “Lưu tìm kiếm” trong bộ lọc.')}
      </p>
      {rows.length === 0
        ? <p className="section-sub">{t('Bạn chưa lưu tìm kiếm nào.')}</p>
        : rows.map(s => (
            <div className="verify-row" key={s.id}>
              <div style={{ minWidth: 0, flex: 1 }}>
                <b>{s.label}</b>
                <div className="meta">{s.summary}</div>
              </div>
              <button className="btn btn-outline btn-sm" onClick={() => remove(s.id)}>{t('Xoá')}</button>
            </div>
          ))}
    </div>
  );
}

/** docs/01 TK-07 — set and verify a company email, earning the work-verified badge. */
export function WorkEmailPanel({ codeLength }) {
  const state = useStore();
  const u = state.user;
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [stage, setStage] = useState('idle');   // idle | code
  const [busy, setBusy] = useState(false);

  if (!u) return null;

  const send = async () => {
    setBusy(true);
    try {
      const res = await api.setWorkEmail(email.trim());
      if (res.devCode) setCode(res.devCode);
      setStage('code');
      toast(res.devCode ? `${res.message} Mã thử nghiệm: ${res.devCode}` : res.message);
    } catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  const confirm = async () => {
    setBusy(true);
    try { await api.confirmWorkEmail(code); await loadMe(); setStage('idle'); setCode(''); toast('Đã xác thực email công ty.'); }
    catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  const remove = async () => {
    setBusy(true);
    try { await api.removeWorkEmail(); await loadMe(); toast('Đã gỡ email công ty.'); }
    catch (err) { toast(err.message); } finally { setBusy(false); }
  };

  if (u.workEmail && u.workEmailConfirmed) {
    return (
      <div className="verify-row">
        <div style={{ minWidth: 0, flex: 1 }}>
          <b>{u.workEmail}</b>
          <div className="meta">{t('Đã xác thực · huy hiệu công tác')}</div>
        </div>
        <span className="badge confirmed">{t('Xong')}</span>
        <button className="btn btn-outline btn-sm" disabled={busy} onClick={remove}>{t('Gỡ')}</button>
      </div>
    );
  }

  return (
    <div className="verify-row" style={{ flexWrap: 'wrap', gap: 8 }}>
      <input className="form-field" style={{ flex: 1, minWidth: 180 }} type="email"
             placeholder={t('ban@congty.com.vn')} value={email}
             onChange={e => setEmail(e.target.value)} />
      {stage === 'code' && (
        <input className="verify-code" inputMode="numeric" maxLength={codeLength}
               placeholder="000000" value={code}
               onChange={e => setCode(e.target.value.replace(/\D/g, ''))} />
      )}
      <button className="btn btn-outline btn-sm" disabled={busy || !email.trim()} onClick={send}>
        {stage === 'code' ? t('Gửi lại') : t('Gửi mã')}
      </button>
      {stage === 'code' && (
        <button className="btn btn-primary btn-sm" disabled={busy || code.length !== codeLength} onClick={confirm}>
          {t('Xác nhận')}
        </button>
      )}
    </div>
  );
}

export function ReviewModal() {
  const state = useStore();
  const b = state.reviewBooking;
  const [draft, setDraft] = useState(() => state.reviewDraft ?? BLANK_REVIEW);
  // docs/01 ĐG-08 — opened over a review the guest already wrote, this is the
  // correction form: the same fields, filled in, saved with PUT instead of POST.
  const [existing, setExisting] = useState(null);
  const [loading, setLoading] = useState(!!state.reviewEditing);

  useEffect(() => {
    if (!state.reviewEditing || !b) return;
    let live = true;
    api.myReview(b.id)
      .then(r => {
        if (!live) return;
        setExisting(r);
        setDraft({
          rating: r.rating, cleanliness: r.cleanliness, accuracy: r.accuracy,
          checkIn: r.checkIn, communication: r.communication,
          location: r.location, value: r.value, text: r.text
        });
      })
      .catch(err => { if (live) { toast(err.message); closeOverlay(); } })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [state.reviewEditing, b?.id]);

  if (!b) return null;
  const editing = !!state.reviewEditing;

  const stars = (field, small) => (
    <div className="star-row" data-field={field}>
      {[1, 2, 3, 4, 5].map(n => (
        <button type="button" key={n} aria-label={`${n} ${t('sao')}`}
                className={`star ${small ? 'sm' : ''} ${n <= draft[field] ? 'is-on' : ''}`}
                onClick={() => setDraft(d => ({ ...d, [field]: n }))}>★</button>
      ))}
    </div>
  );

  const submit = async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const text = form.text.value.trim();
    const privateNote = form.privateNote.value.trim() || null;
    const ok = await submitReview(b.id, { bookingId: b.id, ...draft, text, privateNote }, editing);
    if (ok) closeOverlay();
  };

  if (loading) {
    return <Modal title={t('Sửa đánh giá')}><p>{t('Đang tải…')}</p></Modal>;
  }

  // The server refuses a correction once the review is public or the 48 hours
  // are up; saying so here beats offering a form that cannot be saved.
  if (editing && existing && !existing.canEdit) {
    return (
      <Modal title={t('Sửa đánh giá')} size="narrow">
        <p style={{ fontSize: 14.5, lineHeight: 1.6 }}>
          {existing.isPublic
            ? t('Đánh giá đã công khai nên không sửa được nữa.')
            : t('Đã quá 48 giờ kể từ khi gửi nên không sửa được nữa.')}
        </p>
        <div className="review-reply" style={{ marginLeft: 0, marginTop: 14 }}>
          <b>★ {existing.rating.toFixed(1)}</b>
          <p>{existing.text}</p>
        </div>
      </Modal>
    );
  }

  return (
    <Modal title={editing ? t('Sửa đánh giá') : t('Đánh giá chuyến đi')}>
      <div style={{ display: 'flex', gap: 14, alignItems: 'center', paddingBottom: 18, borderBottom: '1px solid var(--divider)' }}>
        <img src={b.listingImage} alt="" style={{ width: 88, height: 66, objectFit: 'cover', borderRadius: 12 }} />
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 700 }}>{b.listingTitle}</div>
          <div style={{ fontSize: 13, color: 'var(--ink-muted)' }}>{b.listingCity} · {b.nights} {t('đêm')}</div>
        </div>
      </div>

      <form onSubmit={submit}>
        <div style={{ padding: '20px 0', borderBottom: '1px solid var(--divider)' }}>
          <b style={{ fontSize: 15 }}>{t('Điểm tổng thể')}</b>
          <div style={{ marginTop: 10 }}>{stars('rating', false)}</div>
        </div>

        {REVIEW_FIELDS.map(([key, label]) => (
          <div className="count-row" key={key}>
            <div className="tx"><b>{t(label)}</b></div>
            {stars(key, true)}
          </div>
        ))}

        <label className="form-field" style={{ marginTop: 20 }}>
          <span className="cap">{t('Cảm nhận của bạn')} <span style={{ fontWeight: 400 }}>{t('(công khai)')}</span></span>
          <textarea name="text" rows={5} required minLength={10} defaultValue={draft.text}
                    placeholder={t('Chỗ nghỉ thế nào? Chủ nhà hỗ trợ ra sao?')}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>

        <label className="form-field">
          <span className="cap">{t('Góp ý riêng cho chủ nhà')} <span style={{ fontWeight: 400 }}>{t('(không công khai)')}</span></span>
          <textarea name="privateNote" rows={3} defaultValue={existing?.privateNote ?? ''}
                    placeholder={t('Điều gì có thể tốt hơn cho khách sau?')}
                    style={{ width: '100%', padding: '12px 14px', border: '1px solid var(--line)', borderRadius: 12, fontSize: 14 }} />
        </label>

        <p style={{ fontSize: 12.5, color: 'var(--ink-muted)', lineHeight: 1.5, margin: '0 0 12px' }}>
          {t('Đánh giá của bạn và của chủ nhà chỉ hiện khi cả hai đã gửi, hoặc sau 14 ngày. Không được ghi số điện thoại, email hay đường liên kết.')}
        </p>

        <button type="submit" className="btn btn-primary btn-block">
          {editing ? t('Lưu thay đổi') : t('Gửi đánh giá')}
        </button>
      </form>
    </Modal>
  );
}

export function CancelTripModal() {
  const state = useStore();
  const preview = state.cancelPreview;
  if (!preview) return null;

  // docs/07 §2.5 — the booking this preview is for, when it is one on screen.
  // The dialog is opened from both the trips list and the trip page, so it looks
  // in both rather than assuming either is loaded.
  const booking = state.trip?.id === preview.bookingId
    ? state.trip
    : state.bookings.find(b => b.id === preview.bookingId);
  const paidAtProperty = !!booking?.paidAtProperty && !booking?.cashCollectedAt;

  const confirm = async () => {
    try {
      const result = await api.cancelBooking(preview.bookingId);
      set({ overlay: null, cancelPreview: null });
      toast(`Đã huỷ. Hoàn lại ${money(result.refund)}.`);
      await loadBookings();
      if (store.trip?.id === preview.bookingId) await loadTrip(preview.bookingId);
    } catch (err) { toast(err.message); }
  };

  return (
    <Modal title={t('Huỷ chuyến đi')} size="narrow" foot={<>
      <button className="text-btn" onClick={closeOverlay}>{t('Giữ chuyến đi')}</button>
      <button className="btn btn-primary btn-sm" onClick={confirm}>{t('Xác nhận huỷ')}</button>
    </>}>
      <p style={{ margin: '0 0 18px', fontSize: 14.5, lineHeight: 1.6, color: 'var(--ink-body)' }}>
        {preview.explanation}
      </p>
      <div className="book-lines">
        {/* docs/07 §2.5 — "Đã thanh toán" is a lie on a booking the guest has not
            paid for. The figure is the same, the claim about it is not. */}
        <div className="book-line">
          <span>{paidAtProperty ? t('Tổng tiền của đơn') : t('Đã thanh toán')}</span>
          <span>{money(preview.total)}</span>
        </div>
        <div className="book-line" style={{ color: 'var(--brand-dark)' }}>
          <span>{t('Sẽ hoàn lại')}</span><span>{money(preview.refund)}</span>
        </div>
        <div className="book-rule" />
        <div className="book-total"><span>{t('Không hoàn')}</span><span>{money(preview.penalty)}</span></div>
      </div>
    </Modal>
  );
}
