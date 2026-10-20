import { ReactNode, useMemo, useState } from 'react';
import { AlertCircle, CheckCircle2, Info } from 'lucide-react';

type FeedbackTone = 'info' | 'success' | 'warning' | 'error';
export function useFeedback() {
  const [feedback, setFeedback] = useState<{ message: string; tone: FeedbackTone }>({ message: '', tone: 'info' });
  const notify = useMemo(() => Object.assign(
    (message: string) => setFeedback({ message, tone: 'info' }),
    { error: (message: string) => setFeedback({ message, tone: 'error' }), success: (message: string) => setFeedback({ message, tone: 'success' }) },
  ), []);
  return [feedback.message, notify, feedback.tone] as const;
}

export function UiNotice({ children, tone = 'info', action, className = '' }: { children: ReactNode; tone?: 'info' | 'success' | 'warning' | 'error'; action?: ReactNode; className?: string }) {
  const Icon = tone === 'success' ? CheckCircle2 : tone === 'info' ? Info : AlertCircle;
  return <div className={'ui-notice ' + className} data-tone={tone} role={tone === 'error' ? 'alert' : 'status'}><Icon size={20} aria-hidden="true"/><div>{children}</div>{action && <div className="ui-notice-action">{action}</div>}</div>;
}

export function UiPageHeading({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return <header className="ui-page-heading"><div><h1>{title}</h1>{description && <p>{description}</p>}</div>{actions && <div className="ui-page-actions">{actions}</div>}</header>;
}

export function UiEmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <div className="ui-empty-state"><Info size={26} aria-hidden="true"/><strong>{title}</strong>{description && <p>{description}</p>}{action}</div>;
}

export function UiStatus({ children, tone = 'neutral' }: { children: ReactNode; tone?: 'neutral' | 'success' | 'warning' | 'error' }) {
  return <span className="ui-status" data-tone={tone}>{children}</span>;
}
