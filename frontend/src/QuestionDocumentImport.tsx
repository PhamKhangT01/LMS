import { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, LoaderCircle, Upload, X } from 'lucide-react';
import { AuthoringDialog, AuthoringConfirmation } from './AuthoringDialog';
import { bankRequest, questionTypes } from './questionBankApi';
import './question-document-import.css';

type Candidate = { number: number; page: number; content: string; type: number; options: string[]; correctAnswer: string; points: number; warnings: string[]; selected: boolean };
type Preview = { fileName: string; format: string; questions: Candidate[]; warnings: string[]; text: string };
type Check = { index: number; duplicateId: string | null; duplicateInFile: boolean; errors: string[] };
const message = (e: unknown) => e instanceof Error ? e.message : 'Không thể xử lý tệp. Hãy thử lại.';
function errors(q: Candidate) {
  const result: string[] = [];
  if (!q.content.trim() || q.content.length > 5000) result.push('Nội dung cần có 1–5000 ký tự.');
  if (!Number.isFinite(q.points) || q.points <= 0 || q.points > 100000) result.push('Điểm phải lớn hơn 0, tối đa 100000.');
  if (q.type !== 3) {
    if (q.options.length < 2 || q.options.length > 20 || q.options.some(o => !o.trim() || o.length > 2000) || new Set(q.options.map(o => o.trim())).size !== q.options.length) result.push('Cần 2–20 phương án có nội dung và khác nhau.');
    if (q.type === 1) { try { const a: unknown = JSON.parse(q.correctAnswer); if (!Array.isArray(a) || !a.length || a.some(o => !q.options.includes(o))) result.push('Chọn đáp án đúng.'); } catch { result.push('Chọn đáp án đúng.'); } }
    else if (!q.correctAnswer || !q.options.includes(q.correctAnswer)) result.push('Chọn một đáp án đúng.');
  }
  return result;
}
function answers(q: Candidate): string[] { if (q.type !== 1) return q.correctAnswer ? [q.correctAnswer] : []; try { const a: unknown = JSON.parse(q.correctAnswer); return Array.isArray(a) ? a.filter((s): s is string => typeof s === 'string') : []; } catch { return []; } }

export default function QuestionDocumentImport({ courseId, courseName, close, saved }: { courseId: string; courseName: string; close: () => void; saved: (result: { added: number; skipped: number }) => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [rows, setRows] = useState<Candidate[]>([]);
  const [checks, setChecks] = useState<Check[]>([]);
  const [checked, setChecked] = useState('');
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [discard, setDiscard] = useState(false);
  const [replaceFile, setReplaceFile] = useState(false);
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState('all');
  const dirty = !!preview;
  useEffect(() => { if (!dirty) return; const warn = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ''; }; window.addEventListener('beforeunload', warn); return () => window.removeEventListener('beforeunload', warn); }, [dirty]);
  const dismiss = () => { if (!busy) { if (dirty) { setReplaceFile(false); setDiscard(true); } else close(); } };
  const payload = (items = rows) => ({ courseId, fileName: preview?.fileName || '', format: preview?.format || '', questions: items.map(({ content, type, options, correctAnswer, points, page: sourcePage, number }) => ({ content, type, options, correctAnswer, points, page: sourcePage, number })) });
  const signature = JSON.stringify(payload());
  const fresh = !!checked && checked === signature;
  const duplicate = (i: number) => fresh && !!(checks[i]?.duplicateId || checks[i]?.duplicateInFile);
  const valid = (q: Candidate, i: number) => errors(q).length === 0 && !duplicate(i) && (!fresh || !checks[i]?.errors.length);
  const selected = rows.filter((q, i) => q.selected && valid(q, i));
  const change = (index: number, patch: Partial<Candidate>) => { setRows(current => current.map((q, i) => i === index ? { ...q, ...patch } : q)); setError(''); };
  const selectFile = (next: File | null) => {
    if (!next) return;
    if (!/\.(docx|pdf)$/i.test(next.name) || !next.size || next.size > 10 * 1024 * 1024) { setError('Chọn DOCX/PDF có nội dung, tối đa 10 MB.'); return; }
    setFile(next); setError('');
  };
  const parse = async () => {
    if (!file || busy) return; setBusy('Đang đọc tệp…'); setError('');
    const body = new FormData(); body.append('file', file); body.append('courseId', courseId);
    try { const p = await bankRequest<Preview>('/import-preview', 'POST', body); setPreview(p); setRows(p.questions.map(q => ({ ...q, points: 1, selected: false }))); setChecks([]); setChecked(''); setPage(1); setFilter('all'); } catch (e) { setError(message(e)); } finally { setBusy(''); }
  };
  const checkDuplicates = async () => {
    if (busy) return; setBusy('Đang kiểm tra trùng…'); setError('');
    try { const data = await bankRequest<Check[]>('/import-check', 'POST', payload()); setChecks(data); setChecked(signature); setRows(current => current.map((q, i) => data[i]?.duplicateId || data[i]?.duplicateInFile || data[i]?.errors.length ? { ...q, selected: false } : q)); } catch (e) { setError(message(e)); } finally { setBusy(''); }
  };
  const confirm = async () => {
    if (!fresh || !selected.length || busy) return; setBusy('Đang nhập câu hỏi…'); setError('');
    try { saved(await bankRequest<{ added: number; skipped: number }>('/import-confirm', 'POST', payload(selected))); } catch (e) { setError(message(e)); } finally { setBusy(''); }
  };
  const optionEdit = (index: number, option: number, text: string) => {
    const q = rows[index], old = q.options[option], correct = answers(q).map(a => a === old ? text : a);
    change(index, { options: q.options.map((o, i) => i === option ? text : o), correctAnswer: q.type === 1 ? JSON.stringify(correct) : correct[0] || '' });
  };
  const visible = rows.map((q, i) => ({ q, i })).filter(({ q, i }) => filter === 'all' || filter === 'errors' && errors(q).length > 0 || filter === 'duplicates' && duplicate(i) || filter === 'selected' && q.selected);
  const pages = Math.max(1, Math.ceil(visible.length / 10));
  const shown = visible.slice((Math.min(page, pages) - 1) * 10, Math.min(page, pages) * 10);
  return <><AuthoringDialog titleId="document-import-title" close={dismiss}><div className="document-import" aria-busy={!!busy}>
    <header className="bank-dialog-heading"><div><p className="bank-eyebrow">NGÂN HÀNG / {courseName}</p><h2 id="document-import-title">Nhập câu hỏi DOCX / PDF</h2></div><button type="button" className="bank-icon-button" onClick={dismiss} disabled={!!busy} aria-label="Đóng nhập câu hỏi"><X size={20}/></button></header>
    <div className="document-import-body">
      <ol className="document-import-steps"><li className={preview ? 'done' : 'active'}>1. Tải tệp</li><li className={preview && !fresh ? 'active' : ''}>2. Xem trước và sửa</li><li className={fresh ? 'active' : ''}>3. Kiểm tra trùng và chọn</li></ol>
      {!preview ? <section className="document-upload"><p>Học phần đã chọn: <strong>{courseName}</strong>. Không cần khai báo nguồn đề.</p><label className="bank-file-drop" onDragOver={e => e.preventDefault()} onDrop={e => { e.preventDefault(); if (!busy) selectFile(e.dataTransfer.files[0] || null); }}><Upload size={28}/><strong>{file?.name || 'Chọn hoặc kéo thả DOCX / PDF'}</strong><span>Tối đa 10 MB · PDF cần có lớp chữ · Tối đa 200 câu</span><input type="file" accept=".docx,.pdf" aria-label="Chọn tệp DOCX hoặc PDF" disabled={!!busy} onChange={e => { selectFile(e.target.files?.[0] || null); e.target.value = ''; }}/></label><details className="bank-format-help"><summary>Cách nhận diện nội dung</summary><p>Đánh số “Câu 1:” hoặc “Question 1:”, phương án A., B., … Đáp án có thể ghi “Đáp án: A” sau câu hoặc bảng đơn giản “1 A” cuối tệp. Câu thiếu đáp án vẫn được giữ để bạn sửa. PDF scan cần OCR trước.</p></details></section> : <>
        <div className="document-file-summary"><strong>{preview.fileName}</strong><span>{rows.length} câu nhận diện · {rows.filter(q => errors(q).length).length} câu cần sửa · {fresh ? checks.filter(c => c.duplicateId || c.duplicateInFile).length + ' câu trùng' : 'Chưa kiểm tra trùng sau thay đổi'}</span><button type="button" className="bank-secondary" disabled={!!busy} onClick={() => { setReplaceFile(true); setDiscard(true); }}>Đổi tệp</button></div>
        <details className="document-source"><summary>Đối chiếu văn bản và lưu ý nhận diện</summary>{preview.warnings.map((w, i) => <p key={i}>{w}</p>)}<pre>{preview.text}</pre></details>
        <div className="document-review-toolbar"><label>Hiển thị<select value={filter} disabled={!!busy} onChange={e => { setFilter(e.target.value); setPage(1); }}><option value="all">Tất cả câu hỏi</option><option value="errors">Cần sửa</option><option value="duplicates">Trùng</option><option value="selected">Đã chọn</option></select></label><button type="button" className="bank-secondary" disabled={!!busy} onClick={checkDuplicates}>{fresh ? 'Kiểm tra trùng lại' : 'Kiểm tra trùng'}</button><button type="button" className="bank-secondary" disabled={!fresh || !!busy} onClick={() => setRows(current => current.map((q, i) => valid(q, i) ? { ...q, selected: true } : q))}>Chọn tất cả câu hợp lệ</button><button type="button" className="bank-secondary" disabled={!!busy} onClick={() => setRows(current => current.map(q => ({ ...q, selected: false })))}>Bỏ chọn</button></div>
        {!fresh && <p className="bank-feedback">Sửa nội dung, sau đó bấm Kiểm tra trùng trước khi chọn câu để nhập.</p>}
        {shown.map(({ q, i }) => <article key={i} className="document-question">
          <div className="document-question-heading"><label><input type="checkbox" aria-label={'Chọn câu ' + q.number} checked={q.selected && valid(q, i)} disabled={!fresh || !valid(q, i) || !!busy} onChange={e => change(i, { selected: e.target.checked })}/>Câu {q.number}</label><span>Trang {q.page}{fresh && duplicate(i) ? ' · Trùng, sẽ bỏ qua' : ''}</span></div>
          <fieldset disabled={!!busy}><div className="document-question-fields"><label>Dạng câu<select value={q.type} onChange={e => { const type = Number(e.target.value); change(i, { type, options: type === 3 ? [] : type === 2 ? ['Đúng', 'Sai'] : q.options.length ? q.options : ['', ''], correctAnswer: '' }); }} >{questionTypes.map((name, type) => <option value={type} key={type}>{name}</option>)}</select></label><label>Điểm<input type="number" min="0.01" max="100000" step="0.01" value={q.points || ''} onChange={e => change(i, { points: Number(e.target.value) })}/></label></div>
          <label>Nội dung câu hỏi<textarea rows={3} maxLength={5000} value={q.content} onChange={e => change(i, { content: e.target.value })}/></label>
          {q.type === 3 ? <label>Hướng dẫn chấm (không bắt buộc)<textarea rows={2} maxLength={5000} value={q.correctAnswer} onChange={e => change(i, { correctAnswer: e.target.value })}/></label> : <div className="document-options"><p>Chọn {q.type === 1 ? 'các' : 'một'} đáp án đúng:</p>{q.options.map((option, oi) => <div key={oi}><input type={q.type === 1 ? 'checkbox' : 'radio'} name={'answer-' + i} aria-label={'Đáp án ' + String.fromCharCode(65 + oi) + ' câu ' + q.number} checked={!!option && answers(q).includes(option)} onChange={e => { const correct = answers(q); change(i, { correctAnswer: q.type === 1 ? JSON.stringify(e.target.checked ? [...correct, option] : correct.filter(a => a !== option)) : option }); }}/><label><span>{String.fromCharCode(65 + oi)}.</span><input aria-label={'Phương án ' + String.fromCharCode(65 + oi) + ' câu ' + q.number} value={option} maxLength={2000} onChange={e => optionEdit(i, oi, e.target.value)}/></label>{q.type !== 2 && <button type="button" className="bank-icon-button" aria-label={'Xóa phương án ' + String.fromCharCode(65 + oi) + ' câu ' + q.number} disabled={q.options.length <= 2} onClick={() => { const correct = answers(q).filter(a => a !== option); change(i, { options: q.options.filter((_, n) => n !== oi), correctAnswer: q.type === 1 ? JSON.stringify(correct) : correct[0] || '' }); }}><X size={15}/></button>}</div>)}{q.type !== 2 && <button type="button" className="bank-secondary" disabled={q.options.length >= 20} onClick={() => change(i, { options: [...q.options, ''] })}>Thêm phương án</button>}</div>}
          </fieldset>
          {q.warnings.length > 0 && <details><summary>Lưu ý từ tệp gốc</summary>{q.warnings.map((w, n) => <p key={n}>{w}</p>)}</details>}
          {errors(q).map((e, n) => <p key={n} className="bank-error">{e}</p>)}{fresh && checks[i]?.errors.map((e, n) => <p key={n} className="bank-error">{e}</p>)}
          {fresh && !duplicate(i) && !errors(q).length && <p className="document-valid"><CheckCircle2 size={16}/>Hợp lệ để chọn</p>}
        </article>)}
        {visible.length === 0 && <p>Không có câu hỏi trong bộ lọc này.</p>}
        <nav className="document-pages" aria-label="Trang xem trước"><button type="button" className="bank-secondary" disabled={page <= 1 || !!busy} onClick={() => setPage(p => p - 1)}>Trước</button><span>Trang {Math.min(page, pages)} / {pages}</span><button type="button" className="bank-secondary" disabled={page >= pages || !!busy} onClick={() => setPage(p => p + 1)}>Sau</button></nav>
      </>}
      {error && <p className="bank-feedback bank-error" role="alert"><AlertCircle size={18}/>{error}</p>}
    </div>
    <footer className="bank-dialog-footer"><span role="status">{busy || (preview ? selected.length + ' câu được chọn' : 'Chưa lưu câu hỏi nào')}</span><button type="button" className="bank-secondary" disabled={!!busy} onClick={dismiss}>Hủy</button>{preview ? <button type="button" disabled={!fresh || !selected.length || !!busy} onClick={confirm}>{busy ? <LoaderCircle className="bank-spin" size={17}/> : <Upload size={17}/>}Nhập {selected.length} câu vào ngân hàng</button> : <button type="button" disabled={!file || !!busy} onClick={parse}>{busy ? <LoaderCircle className="bank-spin" size={17}/> : <Upload size={17}/>}Xem trước câu hỏi</button>}</footer>
  </div></AuthoringDialog>{discard && <AuthoringConfirmation title="Bỏ bản xem trước?" description="Các câu đang sửa chưa được lưu vào ngân hàng." confirmLabel="Bỏ bản xem trước" cancel={() => setDiscard(false)} confirm={() => { setDiscard(false); if (replaceFile) { setPreview(null); setRows([]); setFile(null); setChecks([]); setChecked(''); setError(''); } else close(); }}/>}</>;
}
