import { FormEvent, ReactNode, useEffect, useState } from 'react';
import { Save, X } from 'lucide-react';
import { AuthoringConfirmation, AuthoringDialog } from './AuthoringDialog';

export function BusinessFormDialog({ title, description, children, busy = false, dirty = false, close, submit, submitLabel = 'Lưu thay đổi' }: { title: string; description?: string; children: ReactNode; busy?: boolean; dirty?: boolean; close: () => void; submit: (event: FormEvent) => void; submitLabel?: string }) {
  const [confirmClose, setConfirmClose] = useState(false);
  const dismiss = () => { if (!busy) { if (dirty) setConfirmClose(true); else close(); } };
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);
  return <><AuthoringDialog className="ui-form-dialog" titleId="business-form-title" close={dismiss}><form className="ui-business-form" onSubmit={event => { event.preventDefault(); if (!busy) submit(event); }} aria-busy={busy}><header className="ui-form-header"><div><h2 id="business-form-title">{title}</h2>{description && <p>{description}</p>}</div><div className="ui-form-actions"><button type="button" onClick={dismiss} disabled={busy}><X size={18}/>Hủy</button><button type="submit" className="ui-primary-button" disabled={busy}><Save size={18}/>{busy ? 'Đang xử lý…' : submitLabel}</button></div></header><div className="ui-form-body"><div className="ui-form-fields" inert={busy}>{children}</div></div></form></AuthoringDialog>{confirmClose && <AuthoringConfirmation title="Bạn có thay đổi chưa lưu" description="Nội dung vừa nhập sẽ bị mất nếu đóng. Bạn có thể tiếp tục chỉnh sửa và lưu khi sẵn sàng." confirmLabel="Bỏ thay đổi và đóng" cancel={() => setConfirmClose(false)} confirm={close}/>}</>;
}
