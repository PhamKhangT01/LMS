import { X } from 'lucide-react';
import { AuthoringDialog } from './AuthoringDialog';
import { useUiConfirmation } from './useUiConfirmation';
import { useFeedback, UiNotice } from './ui';
import { FormEvent, useEffect, useState } from 'react';
import { api, CourseDetail, User } from './api';

import './workflow.css';

export function CourseEditor({ id, user, close, changed }: { id: string; user: User; close: () => void; changed: () => void }) {
  const [course, setCourse] = useState<CourseDetail | null>(null);
  const [message, setMessage, messageTone] = useFeedback();
  const [busy, setBusy] = useState(false);
  const [newCode,setNewCode]=useState('');
  const [reason, setReason] = useState('');
  const [editingLessonId,setEditingLessonId]=useState<string|null>(null);
  const [lesson, setLesson] = useState({ title: '', content: '', durationMinutes: 45 });
  const { ask, dialog: confirmationDialog } = useUiConfirmation();
  const originalLesson = course?.lessons.find(item => item.id === editingLessonId);
  const dirty = !!(reason.trim() || newCode.trim() || (originalLesson ? lesson.title !== originalLesson.title || lesson.content !== originalLesson.content || lesson.durationMinutes !== originalLesson.durationMinutes : lesson.title || lesson.content || lesson.durationMinutes !== 45));
  const dismiss = async () => { if(busy)return; if(dirty && !await ask({title:'Bạn có thay đổi chưa lưu',description:'Nội dung đang chỉnh sửa sẽ bị mất nếu đóng.',confirmLabel:'Bỏ thay đổi và đóng'}))return;close(); };
  const manager = user.role === 'Admin' || user.role === 'DepartmentHead';
  const load = async () => { const result = await api.courseDetail(id); setCourse(result); };
  useEffect(() => { load().catch(error => setMessage.error(error.message)); }, [id]);
  const perform = async (operation: () => Promise<unknown>, success: string) => {
    setBusy(true); setMessage('');
    try { await operation(); await load(); changed(); setReason(''); setMessage.success(success); }
    catch (error) { setMessage.error(error instanceof Error ? error.message : 'Không thể lưu thay đổi.'); await load().catch(() => undefined); }
    finally { setBusy(false); }
  };
  const add = (event: FormEvent) => {
    event.preventDefault();
    void perform(async () => { if(editingLessonId)await api.updateLesson(id,editingLessonId,{...lesson,revision:course!.revision});else await api.addLesson(id, lesson);setEditingLessonId(null); setLesson({ title: '', content: '', durationMinutes: 45 }); }, 'Đã lưu bài học.');
  };
  return <><AuthoringDialog className="ui-form-dialog" titleId="workflow-title" close={() => {void dismiss();}}><section className="workflow-editor">
    <header><div><p className="section-label">QUẢN LÝ NỘI DUNG</p><h2 id="workflow-title">{course?.title || 'Đang tải học phần…'}</h2></div><button type="button" onClick={()=>void dismiss()} disabled={busy} aria-label="Đóng"><X size={20}/>Đóng</button></header>
    <div className="workflow-body">
      {message && <UiNotice tone={messageTone}>{message}</UiNotice>}
      {course && <><ol className="workflow-steps">{['Soạn bản nháp', 'Chờ duyệt', 'Đã công bố'].map((label, index) => <li key={label} aria-current={course.status === index ? 'step' : undefined} className={course.status === index ? 'current' : ''}>{index + 1}. {label}</li>)}</ol>
      <p>{course.description}</p>
      <div className="workflow-columns"><main><h3>Nội dung học phần · {course.lessons.length} bài</h3>
        {course.lessons.map(item => <details key={item.id}><summary>{item.position}. {item.title} · {item.durationMinutes} phút</summary><p className="workflow-content">{item.content}</p>{course.status===0&&<button className="outline-green" disabled={busy} onClick={()=>{setEditingLessonId(item.id);setLesson({title:item.title,content:item.content,durationMinutes:item.durationMinutes});}}>Chỉnh sửa bài học</button>}</details>)}
        {!course.lessons.length && <p>Chưa có bài học. Thêm nội dung trước khi gửi duyệt.</p>}
        {course.status === 0 && <form onSubmit={add} className="workflow-form"><h3>{editingLessonId?"Chỉnh sửa bài học":"Thêm bài học"}</h3><label>Tiêu đề<input required maxLength={300} value={lesson.title} onChange={e => setLesson({ ...lesson, title: e.target.value })}/></label><label>Nội dung<textarea required maxLength={20000} rows={8} value={lesson.content} onChange={e => setLesson({ ...lesson, content: e.target.value })}/></label><label>Thời lượng (phút)<input type="number" required min={1} max={600} value={lesson.durationMinutes} onChange={e => setLesson({ ...lesson, durationMinutes: Number(e.target.value) })}/></label><button className="outline-green" disabled={busy}>{editingLessonId?"Lưu chỉnh sửa":"Thêm bài học"}</button>{editingLessonId&&<button className="outline-green" type="button" onClick={()=>{setEditingLessonId(null);setLesson({title:"",content:"",durationMinutes:45});}}>Hủy chỉnh sửa</button>}</form>}
      </main><aside><h3>Xử lý học phần</h3>
        {course.status === 0 && <><p>Kiểm tra nội dung và thời lượng từng bài trước khi gửi duyệt.</p><button className="hero-login" disabled={busy || !course.lessons.length} onClick={() => perform(() => api.reviewCourse(id, 'submit', '', course.revision), 'Đã gửi học phần để duyệt.')}>Gửi duyệt</button></>}
        {course.status === 1 && (manager ? <div className="workflow-form"><p>Kiểm tra nội dung trước khi công bố cho học viên đã ghi danh.</p><label>Nhận xét / lý do trả lại<textarea maxLength={2000} value={reason} onChange={e => setReason(e.target.value)}/></label><button className="hero-login" disabled={busy} onClick={() => perform(() => api.reviewCourse(id, 'publish', reason, course.revision), 'Đã duyệt và công bố học phần.')}>Duyệt & công bố</button><button className="outline-green" disabled={busy || !reason.trim()} onClick={() => perform(() => api.reviewCourse(id, 'reject', reason, course.revision), 'Đã trả lại bản nháp.')}>Yêu cầu chỉnh sửa</button></div> : <p>Đang chờ Trưởng khoa duyệt. Nội dung được khóa trong thời gian xét duyệt.</p>)}
        {course.status === 2 && <p>Đã công bố cho học viên được ghi danh. Nội dung được khóa để giữ nguyên bản đã duyệt.</p>}
        {course.status===2&&manager&&<form className="workflow-form" onSubmit={e=>{e.preventDefault();void perform(async()=>{await api.copyCourse(id,newCode);setNewCode('');},'Đã tạo bản nháp phiên bản mới trong danh sách học phần. Lớp cũ giữ nguyên nội dung.');}}><h3>Phiên bản học phần mới</h3><p>Sao chép nội dung sang mã mới để chỉnh sửa và duyệt lại. Dữ liệu lớp cũ được giữ nguyên.</p><label>Mã học phần mới<input required maxLength={30} value={newCode} onChange={e=>setNewCode(e.target.value)}/></label><button className="outline-green" disabled={busy}>Tạo phiên bản mới</button></form>}
        <h3>Lịch sử xử lý</h3>{course.reviewHistory.length ? <ul className="workflow-history">{course.reviewHistory.map((entry, index) => <li key={index}><b>{entry.action === 'submit' ? 'Gửi duyệt' : entry.action === 'publish' ? 'Duyệt & công bố' : entry.action === 'copy' ? 'Tạo phiên bản học phần' : 'Yêu cầu chỉnh sửa'}</b><time>{new Date(entry.at).toLocaleString('vi-VN')}</time>{entry.reason && <p>{entry.reason}</p>}<small>Người xử lý: {entry.actorId}</small></li>)}</ul> : <p>Chưa có thao tác duyệt được ghi nhận.</p>}
      </aside></div></>}
    </div>
  </section></AuthoringDialog>{confirmationDialog}</>;
}
