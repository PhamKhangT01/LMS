export type User = { id: string; fullName: string; email: string; role: 'Admin' | 'DepartmentHead' | 'Lecturer' | 'Student'; mustChangePassword?: boolean };
export type CourseStatus = 0 | 1 | 2;
export type ReviewEvent = { actorId: string; action: string; reason: string; at: string };
export type Course = { id: string; code: string; title: string; description: string; category: string; color: string; lecturer: string; lessons: number; status: CourseStatus; revision: number };
export type CourseDetail = { status: CourseStatus; revision: number; reviewHistory: ReviewEvent[]; id: string; code: string; title: string; description: string; category: string; color: string; lecturer: string; lessons: { id: string; title: string; content: string; position: number; durationMinutes: number }[] };
export type Learning = { courseId: string; progressPercent: number; title: string; code: string; color: string; lecturer: string };
export type Lecturer = { id: string; fullName: string; email: string };
export type TeachingClass = { id: string; code: string; name: string; courseId: string; courseTitle: string; academicYear: string; term: string; primaryLecturer: Lecturer; assistantLecturerOne: Lecturer; assistantLecturerTwo: Lecturer; assignedAt: string; status:number };
export type LibraryResource = { id: string; title: string; description: string; category: string; originalFileName: string; contentType: string; sizeBytes: number; uploadedBy: string; uploadedAt: string };
export type Quiz = { id: string; courseId: string; classCode: string; title: string; description: string; durationMinutes: number; attemptsAllowed: number; passingScore: number; opensAt: string | null; closesAt: string | null; status: 'Draft' | 'Published'; questionCount: number; createdAt: string };
export type CreateQuiz = { courseId: string; classCode: string; title: string; description: string; durationMinutes: number; attemptsAllowed: number; passingScore: number; opensAt: string | null; closesAt: string | null; questions: { content: string; type: 'SingleChoice' | 'MultipleChoice' | 'TrueFalse' | 'Essay'; options: string[]; correctAnswer: string; points: number }[] };
export type QuizDetail = { quiz: Quiz; questions: { id: string; content: string; type: 'SingleChoice' | 'MultipleChoice' | 'TrueFalse' | 'Essay'; options: string[]; points: number; position: number }[] };

const baseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5155/api';
const tokenKey = 'law_lms_token'; const userKey = 'law_lms_user';
export const session = {
  get token() { return sessionStorage.getItem(tokenKey) ?? localStorage.getItem(tokenKey); },
  get user(): User | null {
    const storage = sessionStorage.getItem(tokenKey) ? sessionStorage : localStorage;
    const raw = storage.getItem(userKey);
    try { return raw ? JSON.parse(raw) as User : null; } catch { return null; }
  },
  save(token: string, user: User, remember?: boolean) {
    const storage = (remember ?? !!localStorage.getItem(tokenKey)) ? localStorage : sessionStorage;
    this.clear();
    storage.setItem(tokenKey, token);
    storage.setItem(userKey, JSON.stringify(user));
  },
  clear() {
    for (const storage of [localStorage, sessionStorage]) { storage.removeItem(tokenKey); storage.removeItem(userKey); }
  },
};

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...(session.token ? { Authorization: `Bearer ${session.token}` } : {}), ...init.headers } });
  if (response.status === 401 && session.token) { session.clear(); window.dispatchEvent(new Event('lms-session-expired')); }
  if (response.status === 204) return undefined as T;
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.detail || body.title || body.message || 'Có lỗi xảy ra. Vui lòng thử lại.');
  return body as T;
}

export const api = {
  login: (email: string, password: string) => request<{ token: string; user: User }>('/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  managementCourses: () => request<Course[]>('/courses/management'),
  reviewCourse: (id: string, action: string, reason: string, revision: number) => request<void>(`/courses/${id}/review`, { method: 'POST', body: JSON.stringify({ action, reason, revision }) }),
  copyCourse: (id:string,code:string) => request<CourseDetail>(`/courses/${id}/copy`,{method:"POST",body:JSON.stringify({code})}),
  updateLesson:(id:string,lessonId:string,input:{title:string;content:string;durationMinutes:number;revision:number})=>request<void>(`/courses/${id}/lessons/${lessonId}`,{method:"PUT",body:JSON.stringify(input)}),
  addLesson: (id: string, input: { title: string; content: string; durationMinutes: number }) => request<void>(`/courses/${id}/lessons`, { method: 'POST', body: JSON.stringify(input) }),
  courses: () => request<Course[]>('/courses'),
  courseDetail: (id: string) => request<CourseDetail>(`/courses/${id}`),
  learning: () => request<Learning[]>('/courses/my-learning'),
  teaching: () => request<Course[]>('/courses/teaching'),
  createCourse: (input: Omit<Course, 'id' | 'lecturer' | 'lessons' | 'status' | 'revision'>) => request<Course>('/courses', { method: 'POST', body: JSON.stringify(input) })
  ,updateCourse: (id: string, input: Omit<Course, 'id' | 'lecturer' | 'lessons' | 'status' | 'revision'>) => request<Course>(`/courses/${id}`, { method: 'PUT', body: JSON.stringify(input) })
  ,deleteCourse: (id: string) => request<void>(`/courses/${id}`, { method: 'DELETE' })
  ,lecturers: () => request<Lecturer[]>('/teaching-classes/lecturers')
  ,teachingClasses: () => request<TeachingClass[]>('/teaching-classes')
  ,myClassAssignments: () => request<TeachingClass[]>('/teaching-classes/my-assignments')
  ,assignTeachingClass: (input: { code: string; name: string; courseId: string; academicYear: string; primaryLecturerId: string; assistantLecturerOneId: string; assistantLecturerTwoId: string }) => request<TeachingClass>('/teaching-classes', { method: 'POST', body: JSON.stringify(input) })
  ,updateTeachingClass: (id: string, input: { code: string; name: string; courseId: string; academicYear: string; primaryLecturerId: string; assistantLecturerOneId: string; assistantLecturerTwoId: string }) => request<TeachingClass>(`/teaching-classes/${id}`, { method: 'PUT', body: JSON.stringify(input) })
  ,deleteTeachingClass: (id: string) => request<void>(`/teaching-classes/${id}`, { method: 'DELETE' })
  ,previewStudentsXlsx: async (file:File) => {const form=new FormData();form.append('file',file);const r=await fetch(`${baseUrl}/students/preview-xlsx`,{method:'POST',headers:{Authorization:`Bearer ${session.token}`},body:form});const data=await r.json().catch(()=>({}));if(!r.ok)throw new Error(data.detail||data.title||'Không thể xem trước dữ liệu.');return data as {row:number;fullName:string;email:string;classCode:string;action:string;error:string}[];}
  ,importStudentsXlsx: async (file: File) => { const form = new FormData(); form.append('file', file); const response = await fetch(`${baseUrl}/students/import-xlsx`, { method: 'POST', headers: session.token ? { Authorization: `Bearer ${session.token}` } : {}, body: form }); const body = await response.json().catch(() => ({})); if (!response.ok) throw new Error(body.detail || 'Không thể nhập danh sách học viên.'); return body as { created: number; updated: number; failed: number; errors: string[]; initialCredentials: { email: string; temporaryPassword: string }[] }; }
  ,downloadStudentTemplate: async () => { const response = await fetch(`${baseUrl}/students/import-template`, { headers: session.token ? { Authorization: `Bearer ${session.token}` } : {} }); if (!response.ok) throw new Error('Không thể tải tệp mẫu.'); const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a'); link.href = url; link.download = 'mau-danh-sach-hoc-vien.xlsx'; link.click(); URL.revokeObjectURL(url); }
  ,resources: (query = '') => request<LibraryResource[]>(`/library-resources${query ? `?query=${encodeURIComponent(query)}` : ''}`)
  ,uploadResource: async (input: { title: string; description: string; category: string; file: File }) => { const form = new FormData(); form.append('title', input.title); form.append('description', input.description); form.append('category', input.category); form.append('file', input.file); const response = await fetch(`${baseUrl}/library-resources`, { method: 'POST', headers: session.token ? { Authorization: `Bearer ${session.token}` } : {}, body: form }); const body = await response.json().catch(() => ({})); if (!response.ok) throw new Error(body.detail || body.title || body.message || 'Không thể tải tài liệu lên.'); return body as LibraryResource; }
  ,downloadResource: async (resource: LibraryResource) => { const response = await fetch(`${baseUrl}/library-resources/${resource.id}/download`, { headers: session.token ? { Authorization: `Bearer ${session.token}` } : {} }); if (!response.ok) throw new Error('Không thể tải học liệu.'); const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a'); link.href = url; link.download = resource.originalFileName; link.click(); URL.revokeObjectURL(url); }
  ,quizzes: () => request<Quiz[]>('/quizzes')
  ,createQuiz: (input: CreateQuiz) => request<Quiz>('/quizzes', { method: 'POST', body: JSON.stringify(input) })
  ,publishQuiz: (id: string) => request<void>(`/quizzes/${id}/publish`, { method: 'POST' })
  ,importQuizDocx: async (file: File) => { const form = new FormData(); form.append('file', file); const response = await fetch(`${baseUrl}/quizzes/import-docx`, { method: 'POST', headers: session.token ? { Authorization: `Bearer ${session.token}` } : {}, body: form }); const body = await response.json().catch(() => ({})); if (!response.ok) throw new Error(body.detail || 'Không thể đọc tệp DOCX.'); return body as { content: string; options: string[]; correctAnswer: string }[]; }
  ,quizDetail: (id: string) => request<QuizDetail>(`/quizzes/${id}`)
  ,submitQuiz: (id: string, answers: { questionId: string; answer: string }[]) => request<{ autoScore: number; finalScore: number | null; pendingManualGrading: boolean }>(`/quizzes/${id}/submit`, { method: 'POST', body: JSON.stringify({ answers }) })
};
