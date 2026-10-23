import { FormEvent, useEffect, useRef, useState } from 'react';
import { ArrowLeft, ArrowUp, BookOpen, ChevronRight, CircleHelp, MessageCircle, Plus, RefreshCw, X } from 'lucide-react';
import { AuthoringDialog } from './AuthoringDialog';
import { User } from './api';
import { ChatContext, ChatMessage, ChatMode, ChatSource, LessonChatContext, chatApi, onLessonChat } from './chatApi';
import { WorkspaceView } from './WorkspaceNavigation';
import './chat-assistant.css';

type Conversation = { messages: ChatMessage[]; draft: string; error: string };
type SourcePreview = { title: string; course: string; content: string };
const emptyConversation: Conversation = { messages: [], draft: '', error: '' };
const isAbort = (error: unknown) => error instanceof DOMException && error.name === 'AbortError';

export function ChatAssistant({ user, navigate }: { user: User; navigate: (view: WorkspaceView) => void }) {
  const [open, setOpen] = useState(false);
  const [otherDialog, setOtherDialog] = useState(false);
  const [mode, setMode] = useState<ChatMode>('guide');
  const [courseId, setCourseId] = useState('');
  const [lesson, setLesson] = useState<LessonChatContext | null>(null);
  const [context, setContext] = useState<ChatContext | null>(null);
  const [loading, setLoading] = useState(false);
  const [contextError, setContextError] = useState('');
  const [conversations, setConversations] = useState<Record<string, Conversation>>({});
  const [pending, setPending] = useState<string | null>(null);
  const [preview, setPreview] = useState<SourcePreview | null>(null);
  const [sourceLoading, setSourceLoading] = useState(false);
  const [sourceError, setSourceError] = useState('');
  const contextRequest = useRef<AbortController | null>(null);
  const messageRequest = useRef<AbortController | null>(null);
  const sourceRequest = useRef<AbortController | null>(null);
  const transcript = useRef<HTMLDivElement | null>(null);
  const textarea = useRef<HTMLTextAreaElement | null>(null);
  const scope = mode === 'guide' ? 'guide' : `learning:${courseId}:${lesson?.lessonId ?? ''}`;
  const conversation = conversations[scope] ?? emptyConversation;
  const allowedViews: WorkspaceView[] = ['overview', 'courses', 'resources', 'operations', 'schedule', 'forum', 'password', ...(user.role === 'Student' ? ['student-report' as const] : ['reports' as const, 'question-bank' as const]), ...(user.role === 'Admin' ? ['accounts' as const] : [])];

  function cancelPending() {
    messageRequest.current?.abort(); messageRequest.current = null;
    sourceRequest.current?.abort(); sourceRequest.current = null;
    setPending(null); setSourceLoading(false); setSourceError(''); setPreview(null);
  }
  function close() { cancelPending(); setOpen(false); }
  function changeScope(next: () => void) { cancelPending(); next(); }
  function updateConversation(update: Partial<Conversation>) {
    setConversations(previous => ({ ...previous, [scope]: { ...(previous[scope] ?? emptyConversation), ...update } }));
  }
  async function loadContext() {
    contextRequest.current?.abort();
    const controller = new AbortController(); contextRequest.current = controller;
    setLoading(true); setContextError('');
    try {
      const result = await chatApi.context(controller.signal);
      if (!controller.signal.aborted) setContext(result);
    } catch (error) {
      if (!controller.signal.aborted) { setContext(null); setContextError(error instanceof Error ? error.message : 'Không thể tải thông tin trợ lý.'); }
    } finally { if (!controller.signal.aborted) setLoading(false); }
  }

  useEffect(() => {
    const check = () => setOtherDialog(!!document.querySelector('dialog[open]:not(.chat-panel)'));
    check();
    const observer = new MutationObserver(check);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['open'] });
    return () => observer.disconnect();
  }, []);
  useEffect(() => onLessonChat(next => {
    if (document.querySelector('dialog[open]:not(.chat-panel)')) return;
    cancelPending(); setMode('learning'); setCourseId(next.courseId); setLesson(next); setOpen(true);
  }), []);
  useEffect(() => {
    if (open) void loadContext();
    return () => { contextRequest.current?.abort(); };
  }, [open]);
  useEffect(() => () => { contextRequest.current?.abort(); messageRequest.current?.abort(); sourceRequest.current?.abort(); }, []);
  useEffect(() => {
    if (open) transcript.current?.scrollTo({ top: transcript.current.scrollHeight, behavior: 'smooth' });
  }, [open, scope, conversation.messages.length, pending, sourceError]);

  async function send(event?: FormEvent) {
    event?.preventDefault();
    const message = conversation.draft.trim();
    if (messageRequest.current || !message || message.length > 2000 || !context?.available || loading || (mode === 'learning' && !courseId)) return;
    const controller = new AbortController(); messageRequest.current = controller;
    const requestScope = scope;
    setPending(message); updateConversation({ error: '' });
    try {
      const result = await chatApi.send({ mode, message, courseId: mode === 'learning' ? courseId : null, lessonId: mode === 'learning' ? lesson?.lessonId ?? null : null, history: conversation.messages }, controller.signal);
      if (controller.signal.aborted) return;
      setConversations(previous => ({ ...previous, [requestScope]: {
        messages: [...(previous[requestScope]?.messages ?? []), { role: 'user', content: message }, { role: 'assistant', content: result.answer, sources: result.sources }].slice(-40) as ChatMessage[], draft: '', error: '',
      } }));
      requestAnimationFrame(() => textarea.current?.focus());
    } catch (error) {
      if (!controller.signal.aborted && !isAbort(error)) setConversations(previous => ({ ...previous, [requestScope]: { ...(previous[requestScope] ?? emptyConversation), error: error instanceof Error ? error.message : 'Chưa nhận được câu trả lời. Bạn có thể gửi lại câu hỏi.' } }));
    } finally {
      if (messageRequest.current === controller) { messageRequest.current = null; setPending(null); }
    }
  }
  async function openSource(source: ChatSource) {
    setSourceError('');
    if (source.kind === 'guide') {
      if (source.view && allowedViews.includes(source.view as WorkspaceView)) { close(); navigate(source.view as WorkspaceView); }
      else setSourceError('Mục hướng dẫn này chưa có liên kết phù hợp với tài khoản của bạn.');
      return;
    }
    if (!source.courseId || !source.lessonId) { setSourceError('Nguồn bài học chưa có đủ thông tin để mở.'); return; }
    sourceRequest.current?.abort();
    const controller = new AbortController(); sourceRequest.current = controller; setSourceLoading(true);
    try {
      const detail = await chatApi.course(source.courseId, controller.signal);
      const citedLesson = detail.lessons.find(item => item.id === source.lessonId);
      if (!citedLesson) throw new Error('Bài học được dẫn đã thay đổi hoặc không còn tồn tại.');
      if (!controller.signal.aborted) setPreview({ title: citedLesson.title, course: `${detail.code} · ${detail.title}`, content: citedLesson.content });
    } catch (error) {
      if (!controller.signal.aborted) setSourceError(error instanceof Error ? error.message : 'Không thể mở bài học được dẫn.');
    } finally { if (sourceRequest.current === controller) { sourceRequest.current = null; setSourceLoading(false); } }
  }
  const suggestions = mode === 'learning'
    ? ['Tóm tắt những ý chính của bài học.', 'Giải thích các khái niệm chính kèm ví dụ.', 'Gợi ý câu hỏi để tôi tự ôn tập.']
    : user.role === 'Student'
      ? ['Làm thế nào để học bài và ghi nhận hoàn thành?', 'Tôi xem điểm và gửi phúc khảo ở đâu?', 'Làm thế nào để tìm và tải học liệu?']
      : user.role === 'Lecturer'
        ? ['Tôi cập nhật nội dung bài học như thế nào?', 'Quy trình tạo và gửi duyệt bài kiểm tra?', 'Tôi chấm bài và công bố điểm như thế nào?']
        : ['Quy trình mở một lớp học phần?', 'Tôi duyệt học phần và bài kiểm tra ở đâu?', 'Làm thế nào để duyệt và công bố điểm?'];
  const unavailable = contextError || context?.message || 'Trợ lý chưa sẵn sàng. Vui lòng thử lại sau.';
  const blocked = loading || !context?.available || (mode === 'learning' && (!courseId || !context.courses.some(course => course.id === courseId)));

  return <>
    {!open && !otherDialog && <button type="button" className="chat-launcher" onClick={() => setOpen(true)} aria-haspopup="dialog"><MessageCircle size={21} aria-hidden="true"/><span>Trợ lý LMS</span></button>}
    {open && <AuthoringDialog className="chat-panel" titleId="chat-title" close={close}>
      <div className="chat-shell">
        <header className="chat-header"><span className="chat-brand-icon"><MessageCircle size={22} aria-hidden="true"/></span><div><h2 id="chat-title">Trợ lý LMS</h2><p>Học cùng bạn · Hướng dẫn sử dụng</p></div><button type="button" className="chat-icon-button" onClick={close} aria-label="Đóng trợ lý"><X size={20}/></button></header>
        {preview ? <>
          <div className="chat-source-heading"><button type="button" autoFocus onClick={() => setPreview(null)}><ArrowLeft size={16}/>Quay lại hội thoại</button><p>{preview.course}</p><h3>{preview.title}</h3></div>
          <article className="chat-source-content" aria-label="Nội dung bài học được dẫn">{preview.content}</article>
        </> : <>
          <div className="chat-context">
            <div className="chat-mode-switch" role="group" aria-label="Chọn chức năng trợ lý"><button type="button" aria-pressed={mode === 'guide'} onClick={() => changeScope(() => setMode('guide'))}><CircleHelp size={16}/>Hướng dẫn LMS</button><button type="button" aria-pressed={mode === 'learning'} onClick={() => changeScope(() => setMode('learning'))}><BookOpen size={16}/>Hỏi bài học</button></div>
            {mode === 'learning' && <label className="chat-course-label">Học phần<select aria-label="Học phần để hỏi trợ lý" value={courseId} disabled={loading || !context?.courses.length} onChange={event => changeScope(() => { setCourseId(event.target.value); setLesson(null); })}><option value="">Chọn học phần của bạn</option>{context?.courses.map(course => <option key={course.id} value={course.id}>{course.code} · {course.title}</option>)}</select></label>}
            {mode === 'learning' && lesson && <div className="chat-lesson-context"><BookOpen size={14}/><span>Đang hỏi: {lesson.title}</span><button type="button" onClick={() => changeScope(() => setLesson(null))} aria-label="Chuyển sang hỏi toàn học phần"><X size={14}/></button></div>}
          </div>
          <div className="chat-transcript" ref={transcript}>
            {loading && <div className="chat-loading" role="status"><RefreshCw size={17} className="chat-spin"/>Đang tải thông tin trợ lý…</div>}
            {!loading && !context?.available && <div className="chat-service-notice" role="status"><CircleHelp size={23}/><strong>Trợ lý chưa sẵn sàng</strong><p>{unavailable}</p><button type="button" onClick={() => void loadContext()}><RefreshCw size={15}/>Kiểm tra lại</button></div>}
            {!loading && context?.available && !conversation.messages.length && !pending && <div className="chat-welcome"><span className="chat-welcome-icon">{mode === 'learning' ? <BookOpen size={27}/> : <CircleHelp size={27}/>}</span><h3>{mode === 'learning' ? 'Cùng làm rõ bài học' : 'Bạn cần hỗ trợ gì?'}</h3><p>{mode === 'learning' ? 'Hỏi về nội dung học phần. Câu trả lời đi kèm nguồn bài học để bạn đối chiếu.' : 'Tìm các bước thao tác phù hợp với vai trò của bạn trong LMS.'}</p>{mode === 'learning' && !context.courses.length ? <p className="chat-empty-courses">Bạn chưa có học phần có thể truy cập. Bạn vẫn có thể dùng mục Hướng dẫn LMS.</p> : <div className="chat-suggestions">{suggestions.map(suggestion => <button type="button" key={suggestion} onClick={() => { updateConversation({ draft: suggestion, error: '' }); textarea.current?.focus(); }}><span>{suggestion}</span><ChevronRight size={16}/></button>)}</div>}</div>}
            <div role="log" aria-label="Hội thoại với trợ lý" aria-live="polite" aria-relevant="additions">
              {conversation.messages.map((message, index) => <article className={`chat-message chat-message-${message.role}`} key={index}><span className="chat-message-label">{message.role === 'user' ? 'Bạn' : 'Trợ lý LMS'}</span><div className="chat-message-content">{message.content}</div>{message.sources && message.sources.length > 0 && <div className="chat-sources"><span>Nguồn tham khảo</span>{message.sources.map(source => <button type="button" key={source.id} disabled={sourceLoading} onClick={() => void openSource(source)}><BookOpen size={13}/><span>{source.title}</span><ChevronRight size={13}/></button>)}</div>}</article>)}
              {pending && <><article className="chat-message chat-message-user"><span className="chat-message-label">Bạn</span><div className="chat-message-content">{pending}</div></article><div className="chat-answering" role="status"><span className="chat-typing"><i/><i/><i/></span>Đang tìm câu trả lời có nguồn…</div></>}
            </div>
            {sourceLoading && <p className="chat-loading" role="status">Đang mở bài học được dẫn…</p>}
            {sourceError && <p className="chat-error" role="alert">{sourceError}</p>}
            {conversation.error && <div className="chat-error" role="alert"><p>{conversation.error}</p><span>Câu hỏi vẫn được giữ bên dưới.</span><button type="button" disabled={blocked || !!pending} onClick={() => void send()}><RefreshCw size={14}/>Gửi lại câu hỏi</button></div>}
          </div>
          <form className="chat-composer" onSubmit={event => void send(event)}>
            <div className="chat-composer-top"><span>{mode === 'learning' ? 'Câu trả lời từ bài học của bạn' : 'Hướng dẫn dành cho bạn'}</span><button type="button" onClick={() => { cancelPending(); updateConversation(emptyConversation); textarea.current?.focus(); }} disabled={!conversation.messages.length && !conversation.draft} title="Bắt đầu hội thoại mới"><Plus size={14}/>Hội thoại mới</button></div>
            <label htmlFor="chat-question" className="chat-sr-only">Câu hỏi cho trợ lý LMS</label>
            <div className="chat-input-wrap"><textarea id="chat-question" ref={textarea} rows={2} maxLength={2000} value={conversation.draft} disabled={blocked || !!pending} placeholder={mode === 'learning' && !courseId ? 'Chọn học phần để bắt đầu…' : 'Nhập câu hỏi của bạn…'} onChange={event => updateConversation({ draft: event.target.value, error: '' })} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing && event.keyCode !== 229) { event.preventDefault(); void send(); } }}/><button type="submit" disabled={blocked || !!pending || !conversation.draft.trim()} aria-label="Gửi câu hỏi"><ArrowUp size={20}/></button></div>
            <div className="chat-composer-note"><span>AI có thể nhầm. Hãy đối chiếu nguồn.</span><span>{conversation.draft.length}/2000</span></div>
          </form>
        </>}
      </div>
    </AuthoringDialog>}
  </>;
}
