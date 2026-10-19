import { ReactNode, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { AlertCircle } from 'lucide-react';

let openDialogs = 0;
let originalOverflow = '';

export function AuthoringDialog({ titleId, label, close, children, compact = false, className = 'bank-modal' }: { titleId?: string; label?: string; close: () => void; children: ReactNode; compact?: boolean; className?: string }) {
  const ref = useRef<HTMLDialogElement>(null);
  const onClose = useRef(close);
  onClose.current = close;
  useEffect(() => {
    const dialog = ref.current!;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (openDialogs === 0) originalOverflow = document.body.style.overflow;
    openDialogs++;
    document.body.style.overflow = 'hidden';
    dialog.showModal();
    return () => {
      dialog.close();
      if (--openDialogs === 0) document.body.style.overflow = originalOverflow;
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, []);
  return createPortal(<dialog ref={ref} className={className + (compact ? ' bank-modal-compact' : '')} aria-labelledby={titleId} aria-label={label} onCancel={event => { event.preventDefault(); event.stopPropagation(); onClose.current(); }} onSubmit={event => event.stopPropagation()}>{children}</dialog>, document.body);
}

export function AuthoringConfirmation({ title, description, confirmLabel, cancelLabel = 'Tiếp tục chỉnh sửa', cancel, confirm }: { title: string; description: string; confirmLabel: string; cancelLabel?: string; cancel: () => void; confirm: () => void }) {
  return <AuthoringDialog titleId="authoring-confirm-title" close={cancel} compact><div className="bank-discard"><AlertCircle size={30}/><h2 id="authoring-confirm-title">{title}</h2><p>{description}</p><div className="bank-actions"><button type="button" autoFocus onClick={cancel}>{cancelLabel}</button><button type="button" className="bank-secondary" onClick={confirm}>{confirmLabel}</button></div></div></AuthoringDialog>;
}
