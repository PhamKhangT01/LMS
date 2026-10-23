import { CourseDetail, session } from './api';

export type ChatMode = 'learning' | 'guide';
export type ChatSource = { id: string; title: string; kind: 'lesson' | 'guide'; courseId?: string | null; lessonId?: string | null; view?: string | null };
export type ChatMessage = { role: 'user' | 'assistant'; content: string; sources?: ChatSource[] };
export type ChatContext = { available: boolean; message: string | null; courses: { id: string; code: string; title: string }[] };
export type LessonChatContext = { courseId: string; lessonId: string; title: string };
const base = import.meta.env.VITE_API_URL || 'http://localhost:5155/api';
const lessonEvent = 'lms-open-lesson-chat';

async function chatRequest<T>(path: string, signal: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch(base + path, {
    method: body === undefined ? 'GET' : 'POST', signal,
    headers: { 'Content-Type': 'application/json', ...(session.token ? { Authorization: `Bearer ${session.token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  }).catch((error: unknown) => {
    if (signal.aborted) throw error;
    throw new Error('Không thể kết nối LMS. Hãy kiểm tra kết nối mạng và thử lại.');
  });
  if (response.status === 401) { session.clear(); window.dispatchEvent(new Event('lms-session-expired')); }
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(result.detail || result.message || result.title || (response.status === 429 ? 'Bạn đã gửi nhiều câu hỏi. Vui lòng chờ một phút rồi thử lại.' : 'Không thể kết nối trợ lý. Vui lòng thử lại.'));
  return result as T;
}

export const chatApi = {
  context: (signal: AbortSignal) => chatRequest<ChatContext>('/chat/context', signal),
  send: (input: { mode: ChatMode; message: string; courseId: string | null; lessonId: string | null; history: ChatMessage[] }, signal: AbortSignal) =>
    chatRequest<{ answer: string; sources: ChatSource[] }>('/chat/messages', signal, { ...input, history: boundedHistory(input.history) }),
  course: (courseId: string, signal: AbortSignal) => chatRequest<CourseDetail>(`/courses/${encodeURIComponent(courseId)}`, signal),
};

function boundedHistory(messages: ChatMessage[]) {
  const selected: { role: 'user' | 'assistant'; content: string }[] = [];
  let remaining = 16000;
  for (const message of messages.slice(-12).reverse()) {
    const content = message.content.slice(0, 4000);
    if (content.length > remaining) break;
    selected.unshift({ role: message.role, content }); remaining -= content.length;
  }
  return selected;
}

export function openLessonChat(context: LessonChatContext) { window.dispatchEvent(new CustomEvent<LessonChatContext>(lessonEvent, { detail: context })); }
export function onLessonChat(listener: (context: LessonChatContext) => void) {
  const handler = (event: Event) => listener((event as CustomEvent<LessonChatContext>).detail);
  window.addEventListener(lessonEvent, handler);
  return () => window.removeEventListener(lessonEvent, handler);
}
