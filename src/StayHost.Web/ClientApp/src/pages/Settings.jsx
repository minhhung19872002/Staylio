import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useStore } from '../lib/useStore.js';
import { set, toast } from '../lib/store.js';
import { api } from '../lib/api.js';
import { t } from '../lib/i18n.js';
import {
  ProfileForm, Verification, IdentityPanel, SavedCardsPanel, TwoFactorPanel,
  NotificationMatrix, SavedSearchesPanel, DataPanel, WorkEmailPanel,
  ChangePasswordForm, DeviceList
} from '../components/modals/AccountModals.jsx';
import { LanguageChoices, CurrencyChoices } from '../components/modals/SearchModals.jsx';
import { TimeZonePicker } from '../components/TimeZonePicker.jsx';
import { JourneyVisibilityControl } from './Friends.jsx';
import { Payout } from './hosting/Payout.jsx';
import { PaymentHistory } from './settings/PaymentHistory.jsx';
import { NotFound } from './NotFound.jsx';

/*
 * docs/02 F1 — the settings page: "Cửa ngõ dẫn tới các nhóm". The hub is a grid
 * of cards; each group renders the panels that already existed behind the old
 * eight-tab account modal, exported rather than copied, so there is exactly one
 * live version of every form.
 *
 * Two of F1's lines are deliberately absent. "Hoá đơn công ty" has no FR code
 * and no legal shape settled (a real hoá đơn GTGT needs a provider contract and
 * an answer to who the seller is), and "thông tin thuế" has no tax-identity
 * record — a heading that leads nowhere is the same defect as a promise the
 * rules don't keep, so neither is shown until the customer decides.
 */
const GROUPS = [
  ['ho-so', 'Thông tin cá nhân', 'Ảnh, tên hiển thị, xác thực email và số điện thoại, giấy tờ tuỳ thân'],
  ['bao-mat', 'Đăng nhập & bảo mật', 'Mật khẩu, bảo mật 2 lớp, thiết bị đang đăng nhập'],
  ['thanh-toan', 'Thanh toán', 'Thẻ đã lưu và lịch sử trả tiền'],
  ['nhan-tien', 'Nhận tiền', 'Tài khoản nhận tiền và lịch trả cho chủ nhà'],
  ['thong-bao', 'Thông báo', 'Loại thông báo theo từng kênh, tìm kiếm đã lưu'],
  ['quyen-rieng-tu', 'Quyền riêng tư', 'Tải dữ liệu, tạm dừng hoặc xoá tài khoản, hành trình'],
  ['tuy-chinh', 'Tuỳ chỉnh', 'Ngôn ngữ, tiền tệ, múi giờ hiển thị'],
  ['cong-tac', 'Công tác', 'Email công ty cho huy hiệu đi công tác'],
  ['gioi-thieu', 'Giới thiệu bạn bè', 'Link mời và thưởng giới thiệu'],
  ['than-thiet', 'Staylio Thân thiết', 'Cấp thành viên và ưu đãi từ các chủ nhà tham gia'],
];

export function Settings() {
  const state = useStore();
  const navigate = useNavigate();
  const { group } = useParams();

  if (!state.user) {
    return (
      <div className="shell" style={{ paddingBlock: '60px 90px' }}>
        <div className="empty-state">
          <h3>{t('Đăng nhập để vào cài đặt')}</h3>
          <p>{t('Trang này quản lý tài khoản, bảo mật và tuỳ chỉnh của bạn.')}</p>
          <button className="btn btn-primary" style={{ marginTop: 18 }}
                  onClick={() => set({ authMode: 'login', authError: null, overlay: 'login' })}>{t('Đăng nhập')}</button>
        </div>
      </div>
    );
  }

  // The hub. Nhận tiền leads to a host-only endpoint that answers 403 for a
  // guest, and a card that opens an error is worse than no card.
  if (!group) {
    const visible = GROUPS.filter(([slug]) => slug !== 'nhan-tien' || state.user.isHost);
    return (
      <div className="shell" style={{ paddingBlock: '30px 90px', maxWidth: 980 }}>
        <h1 className="section-title">{t('Cài đặt')}</h1>
        <p className="section-sub">{state.user.displayName || state.user.fullName} · {state.user.email}</p>
        <div className="know-grid" style={{ marginTop: 22 }}>
          {visible.map(([slug, title, blurb]) => (
            <a key={slug} className="know-card" href={`/cai-dat/${slug}`}
               style={{ cursor: 'pointer', textDecoration: 'none', color: 'inherit' }}
               onClick={e => {
                 if (e.metaKey || e.ctrlKey || e.shiftKey || e.button === 1) return;
                 e.preventDefault();
                 navigate(`/cai-dat/${slug}`);
               }}>
              <h4 style={{ margin: 0 }}>{t(title)}</h4>
              <p style={{ margin: '6px 0 0', fontSize: 13.5, color: 'var(--ink-muted)', lineHeight: 1.5 }}>{t(blurb)}</p>
            </a>
          ))}
        </div>
      </div>
    );
  }

  const entry = GROUPS.find(([slug]) => slug === group);
  // The server already answered 404 for an invented group (SpaRoutes lists the
  // nine by name); this is the same verdict for a client-side navigation.
  if (!entry) return <NotFound />;

  const [, title] = entry;

  return (
    <div className="shell" style={{ paddingBlock: '30px 90px', maxWidth: 760 }}>
      <button className="back-link" onClick={() => navigate('/cai-dat')}>← {t('Cài đặt')}</button>
      <h1 className="section-title" style={{ marginTop: 10 }}>{t(title)}</h1>
      <div style={{ marginTop: 18 }}>
        {group === 'ho-so' && <>
          <ProfileForm />
          <h3 className="section-title" style={{ fontSize: 17, marginTop: 34 }}>{t('Xác thực & giấy tờ')}</h3>
          <Verification />
          <div style={{ marginTop: 22 }}><IdentityPanel /></div>
        </>}

        {group === 'bao-mat' && <>
          <TwoFactorPanel />
          <h3 className="section-title" style={{ fontSize: 17, margin: '30px 0 12px' }}>{t('Đổi mật khẩu')}</h3>
          <ChangePasswordForm />
          <h3 className="section-title" style={{ fontSize: 17, margin: '30px 0 12px' }}>{t('Thiết bị đang đăng nhập')}</h3>
          <DeviceList />
        </>}

        {group === 'thanh-toan' && <>
          <SavedCardsPanel />
          <h3 className="section-title" style={{ fontSize: 17, margin: '30px 0 0' }}>{t('Lịch sử trả tiền')}</h3>
          <PaymentHistory />
          <p className="section-sub" style={{ marginTop: 14 }}>
            {t('Số dư, thẻ quà tặng và mã giảm giá nằm ở')}{' '}
            <a className="link-btn" href="/wallet"
               onClick={e => { e.preventDefault(); navigate('/wallet'); }}>{t('Số dư & thẻ quà tặng')}</a>.
          </p>
        </>}

        {group === 'nhan-tien' && (state.user.isHost ? <>
          <Payout />
          <p className="section-sub" style={{ marginTop: 14 }}>
            <a className="link-btn" href="/hosting"
               onClick={e => { e.preventDefault(); set({ hostingTab: 'earnings' }); navigate('/hosting'); }}>
              {t('Báo cáo thuế theo năm')}
            </a>
          </p>
        </> : <p className="section-sub">{t('Mục này dành cho chủ nhà.')}</p>)}

        {group === 'thong-bao' && <><NotificationMatrix /><SavedSearchesPanel /></>}

        {group === 'quyen-rieng-tu' && <>
          <JourneyVisibilityControl />
          <div style={{ marginTop: 20 }}><DataPanel /></div>
        </>}

        {group === 'tuy-chinh' && <>
          <h3 className="section-title" style={{ fontSize: 17, margin: '4px 0 0' }}>{t('Ngôn ngữ đề xuất')}</h3>
          <LanguageChoices />
          <h3 className="section-title" style={{ fontSize: 17, margin: '30px 0 0' }}>{t('Chọn loại tiền tệ')}</h3>
          <CurrencyChoices />
          <h3 className="section-title" style={{ fontSize: 17, margin: '30px 0 0' }}>{t('Múi giờ hiển thị')}</h3>
          <p className="section-sub" style={{ marginTop: 4 }}>
            {t('Áp cho giờ và hạn chót. Ngày nhận và trả phòng luôn là ngày tại chỗ nghỉ.')}
          </p>
          <TimeZonePicker />
        </>}

        {group === 'cong-tac' && <WorkEmailSection />}

        {group === 'than-thiet' && <LoyaltyPanel />}

        {group === 'gioi-thieu' && <>
          <p className="section-sub">
            {t('Mời bạn bè và theo dõi thưởng ở')}{' '}
            <a className="link-btn" href="/wallet"
               onClick={e => { e.preventDefault(); navigate('/wallet'); }}>{t('Số dư & thẻ quà tặng')}</a>
            {' '}· {t('Danh sách bạn bè ở')}{' '}
            <a className="link-btn" href="/friends"
               onClick={e => { e.preventDefault(); navigate('/friends'); }}>{t('Bạn bè')}</a>.
          </p>
        </>}
      </div>
    </div>
  );
}

/** Staylio Thân thiết — the level, the progress, and what each level brings. */
function LoyaltyPanel() {
  const [data, setData] = useState(null);
  useEffect(() => { api.loyalty().then(setData).catch(err => toast(err.message)); }, []);
  if (!data) return null;
  return (
    <div style={{ marginTop: 16 }}>
      <div className="kv-grid">
        <div className="kv"><span className="kv-label">{t('Cấp hiện tại')}</span><b>{t(data.label)}</b></div>
        <div className="kv"><span className="kv-label">{t('Chuyến đã hoàn tất (2 năm)')}</span><b>{data.completedStays}</b></div>
      </div>
      {data.staysToNext != null && (
        <p className="section-sub" style={{ marginTop: 14 }}>
          {t('Còn {} chuyến nữa để lên cấp').replace('{}', data.staysToNext)} {t(data.nextLabel)}.
        </p>
      )}
      <ul className="guide-list" style={{ marginTop: 14 }}>
        <li>{t('Thành viên: có tài khoản Staylio.')}</li>
        <li>{t('Thân thiết: từ 3 chuyến — nhận mức giảm của các chỗ nghỉ tham gia.')}</li>
        <li>{t('Thân thiết Vàng: từ 10 chuyến — thêm 5% trên mức giảm đó.')}</li>
      </ul>
    </div>
  );
}

/**
 * docs/01 TK-07 — the work-email panel needs the OTP length, which lives on the
 * verification state; behind the modal it was fed by the surrounding tab. This
 * wrapper fetches the same source rather than hard-coding a 6.
 */
function WorkEmailSection() {
  const [v, setV] = useState(null);
  useEffect(() => { api.verification().then(setV).catch(e => toast(e.message)); }, []);
  if (!v) return <div className="stat skeleton" style={{ height: 120, border: 0 }} />;

  return <>
    <p className="section-sub" style={{ marginTop: 0 }}>
      {t('Dành cho công tác. Dùng tên miền tổ chức, không dùng email cá nhân.')}
    </p>
    <WorkEmailPanel codeLength={v.codeLength} />
  </>;
}
