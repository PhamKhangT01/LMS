import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle } from 'lucide-react';
import { AuthoringDialog } from './AuthoringDialog';

type Request = { title: string; description: string; confirmLabel?: string; requireReason?: boolean };
function Confirmation({ request, resolve }: { request: Request; resolve: (result: string | false) => void }) {
  const [reason, setReason] = useState('');
  return <AuthoringDialog titleId="ui-confirm-title" close={() => resolve(false)} compact><form className="bank-discard" onSubmit={event => { event.preventDefault(); if (!request.requireReason || reason.trim()) resolve(request.requireReason ? reason.trim() : 'confirmed'); }}><AlertCircle size={30}/><h2 id="ui-confirm-title">{request.title}</h2><p>{request.description}</p>{request.requireReason && <label className="ui-confirm-reason">Lý do *<textarea required maxLength={2000} rows={4} value={reason} onChange={event => setReason(event.target.value)}/></label>}<div className="bank-actions"><button type="button" autoFocus onClick={() => resolve(false)}>Hủy</button><button type="submit" className="bank-secondary" disabled={request.requireReason && !reason.trim()}>{request.confirmLabel || 'Xác nhận'}</button></div></form></AuthoringDialog>;
}

export function useUiConfirmation() {
  const [request, setRequest] = useState<Request | null>(null);
  const pending = useRef<((result: string | false) => void) | null>(null);
  const ask = useCallback((next: Request) => new Promise<string | false>(resolve => {
    pending.current?.(false);
    pending.current = resolve;
    setRequest(next);
  }), []);
  const resolve = (result: string | false) => { pending.current?.(result); pending.current = null; setRequest(null); };
  useEffect(() => () => { pending.current?.(false); }, []);
  return { ask, dialog: request ? <Confirmation request={request} resolve={resolve}/> : null };
}
