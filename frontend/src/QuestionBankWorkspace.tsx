import { FormEvent, ReactNode, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { AlertCircle, Archive, ArrowLeft, Check, CheckCircle2, ChevronLeft, ChevronRight, FileText, Filter, LoaderCircle, Plus, RotateCcw, Search, Shuffle, Upload, X } from 'lucide-react';
import { User } from './api';
import { BankCourse, BankPage, BankQuestion, bankRequest, difficulties, questionTypes } from './questionBankApi';
import './question-bank.css';

type Props = { user: User; fixedCourseId?: string; onPick?: (rows: BankQuestion[]) => void; excludedIds?: string[]; close?: () => void; createQuiz?: () => void };
type Filters = { query: string; topic: string; type: string; difficulty: string; includeArchived: boolean };
const clearFilters: Filters = { query: '', topic: '', type: '', difficulty: '', includeArchived: false };
const errorText = (error: unknown) => error instanceof Error ? error.message : 'Không thể xử lý. Vui lòng thử lại.';
const pointsText = (value: number) => value.toLocaleString('vi-VN', { maximumFractionDigits: 2 });

function BankDialog({ titleId, close, children, compact = false }: { titleId: string; close: () => void; children: ReactNode; compact?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null);
  const onClose = useRef(close);
  onClose.current = close;
  useEffect(() => {
    const dialog = ref.current!;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    dialog.showModal();
    return () => { dialog.close(); document.body.style.overflow = previousOverflow; previousFocus?.focus(); };
  }, []);
  return createPortal(<dialog ref={ref} className={'bank-modal' + (compact ? ' bank-modal-compact' : '')} aria-labelledby={titleId} onCancel={event => { event.preventDefault(); event.stopPropagation(); onClose.current(); }} onSubmit={event => event.stopPropagation()}>{children}</dialog>, document.body);
}

function DiscardDialog({ selection = false, cancel, discard }: { selection?: boolean; cancel: () => void; discard: () => void }) {
  return <BankDialog titleId="bank-discard-title" close={cancel} compact><div className="bank-discard"><AlertCircle size={30}/><h2 id="bank-discard-title">{selection ? 'Bỏ danh sách đang chọn?' : 'Bạn có thay đổi chưa lưu'}</h2><p>{selection ? 'Các câu đang chọn chưa được thêm vào đề. Bạn có thể tiếp tục chọn hoặc đóng bộ chọn.' : 'Nội dung vừa chỉnh sửa sẽ bị mất nếu đóng. Bạn có thể quay lại để kiểm tra và lưu câu hỏi.'}</p><div className="bank-actions"><button type="button" autoFocus onClick={cancel}>{selection ? 'Tiếp tục chọn' : 'Tiếp tục chỉnh sửa'}</button><button type="button" className="bank-secondary" onClick={discard}>{selection ? 'Bỏ lựa chọn & đóng' : 'Bỏ thay đổi'}</button></div></div></BankDialog>;
}

export default function QuestionBankWorkspace({ user, fixedCourseId, onPick, excludedIds = [], close, createQuiz }: Props) {
  const [courses, setCourses] = useState<BankCourse[]>([]);
  const [coursesLoading, setCoursesLoading] = useState(true);
  const [coursesRetry, setCoursesRetry] = useState(0);
  const [courseId, setCourseId] = useState(fixedCourseId || '');
  const [filters, setFilters] = useState<Filters>(clearFilters);
  const [advancedOpen, setAdvancedOpen] = useState(() => !window.matchMedia('(max-width:700px)').matches);
  const [page, setPage] = useState(1);
  const [data, setData] = useState<BankPage>({ items: [], total: 0, page: 1, pageSize: 20 });
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [loadError, setLoadError] = useState('');
  const [reload, setReload] = useState(0);
  const [editor, setEditor] = useState<BankQuestion | 'new' | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [selected, setSelected] = useState<Map<string, BankQuestion>>(new Map());
  const [reviewSelected, setReviewSelected] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);
  const [count, setCount] = useState(3);
  const [undo, setUndo] = useState<{ row: BankQuestion; archived: boolean } | null>(null);
  const bodyRef = useRef<HTMLDivElement>(null);
  const picker = !!onPick;
  const canWrite = user.role === 'Lecturer' && !picker;
  const excluded = new Set(excludedIds);
  const capacity = Math.max(0, 200 - excluded.size);
  const hasFilters = Object.values(filters).some(Boolean);
  const course = courses.find(row => row.id === courseId);
  const selectedPoints = [...selected.values()].reduce((sum, row) => sum + row.points, 0);
  const activeFilters = [filters.query && ['query', 'Từ khóa: ' + filters.query], filters.topic && ['topic', 'Chủ đề: ' + filters.topic], filters.type !== '' && ['type', questionTypes[Number(filters.type)]], filters.difficulty !== '' && ['difficulty', difficulties[Number(filters.difficulty)]], filters.includeArchived && ['includeArchived', 'Gồm câu lưu trữ']].filter(Boolean) as [keyof Filters, string][];

  useEffect(() => {
    let live = true;
    setCoursesLoading(true);
    bankRequest<BankCourse[]>('/courses').then(rows => {
      if (live) { setCourses(rows); setCourseId(current => fixedCourseId || (rows.some(row => row.id === current) ? current : rows[0]?.id || '')); setError(''); }
    }).catch(reason => { if (live) setError(errorText(reason)); }).finally(() => { if (live) setCoursesLoading(false); });
    return () => { live = false; };
  }, [fixedCourseId, coursesRetry]);

  useEffect(() => {
    if (!courseId) return;
    let live = true;
    setLoading(true); setLoadError('');
    const timer = setTimeout(() => {
      const query = new URLSearchParams({ courseId, page: String(page), pageSize: '20', query: filters.query, topic: filters.topic, includeArchived: String(!picker && filters.includeArchived) });
      if (filters.type !== '') query.set('type', filters.type);
      if (filters.difficulty !== '') query.set('difficulty', filters.difficulty);
      bankRequest<BankPage>('?' + query).then(result => {
        if (live) { setData(result); const last = Math.max(1, Math.ceil(result.total / result.pageSize)); if (page > last) setPage(last); }
      }).catch(reason => { if (live) setLoadError(errorText(reason)); }).finally(() => { if (live) setLoading(false); });
    }, 250);
    return () => { live = false; clearTimeout(timer); };
  }, [courseId, filters, page, reload, picker]);

  useEffect(() => {
    const media = window.matchMedia('(max-width:700px)');
    const resize = () => setAdvancedOpen(!media.matches);
    media.addEventListener('change', resize);
    return () => media.removeEventListener('change', resize);
  }, []);

  const changeFilter = (patch: Partial<Filters>) => { setFilters(current => ({ ...current, ...patch })); setPage(1); };
  const toggle = (row: BankQuestion) => setSelected(current => {
    const next = new Map(current);
    if (next.has(row.id)) next.delete(row.id); else if (next.size < capacity) next.set(row.id, row);
    return next;
  });
  const visibleSelectable = data.items.filter(row => !excluded.has(row.id));
  const allVisibleSelected = visibleSelectable.length > 0 && visibleSelectable.every(row => selected.has(row.id));
  const selectPage = () => setSelected(current => {
    const next = new Map(current);
    if (allVisibleSelected) visibleSelectable.forEach(row => next.delete(row.id));
    else visibleSelectable.forEach(row => { if (next.size < capacity) next.set(row.id, row); });
    return next;
  });
  const changeArchive = async (row: BankQuestion, archived: boolean, isUndo = false) => {
    setBusy(true); setError(''); setNotice('');
    try {
      await bankRequest('/' + row.id + '/archive', 'PUT', { archived, revision: row.revision });
      setUndo(isUndo ? null : { row: { ...row, revision: row.revision + 1, isArchived: archived }, archived: !archived });
      setNotice(archived ? 'Đã lưu trữ câu hỏi. Các đề đã tạo vẫn giữ nguyên.' : 'Đã khôi phục câu hỏi vào ngân hàng.'); setReload(value => value + 1);
    } catch (reason) { setError(errorText(reason)); } finally { setBusy(false); }
  };
  const draw = async () => {
    if (selected.size + count > capacity) { setError('Đề tối đa 200 câu, bao gồm ' + excluded.size + ' câu đã có và ' + selected.size + ' câu đang chọn.'); return; }
    setBusy(true); setNotice(''); setError('');
    try {
      const rows = await bankRequest<BankQuestion[]>('/draw', 'POST', { courseId, count, query: filters.query, topic: filters.topic, type: filters.type === '' ? null : Number(filters.type), difficulty: filters.difficulty === '' ? null : Number(filters.difficulty), excludeIds: [...new Set([...excludedIds, ...selected.keys()])] });
      setSelected(current => new Map([...current, ...rows.map(row => [row.id, row] as [string, BankQuestion])]));
      setNotice('Đã chọn thêm ' + rows.length + ' câu theo bộ lọc. Bạn có thể kiểm tra trong mục Đã chọn.');
    } catch (reason) { setError(errorText(reason)); } finally { setBusy(false); }
  };
  const showReview = (review: boolean) => { setReviewSelected(review); bodyRef.current?.scrollTo({ top: 0 }); };
  const closePicker = () => { if (!busy) { if (selected.size) setDiscardOpen(true); else close?.(); } };
  const currentRows = reviewSelected ? [...selected.values()] : data.items;
  const titleId = picker ? 'bank-picker-title' : 'bank-title';

  const panel = <section className={'question-bank' + (picker ? ' bank-picker' : '')} aria-labelledby={titleId}>
    <header className="bank-heading">
      <div><p className="bank-eyebrow">{picker ? 'SOẠN ĐỀ / CHỌN CÂU HỎI' : 'KHÔNG GIAN GIẢNG DẠY / HỌC LIỆU ĐÁNH GIÁ'}</p>{picker ? <h2 id={titleId}>Chọn câu hỏi cho đề</h2> : <h1 id={titleId}>Ngân hàng câu hỏi</h1>}<p>{picker ? 'Chọn từng câu hoặc rút ngẫu nhiên. Câu hỏi được sao chép độc lập vào đề.' : 'Tổ chức câu hỏi theo học phần. Tìm lại nhanh, tái sử dụng cho những đề tiếp theo.'}</p></div>
      <div className="bank-actions">{canWrite && <>{createQuiz && <button type="button" className="bank-secondary" onClick={createQuiz}><FileText size={17}/>Tạo bài kiểm tra</button>}<button type="button" className="bank-secondary" disabled={!courseId || busy || coursesLoading} onClick={() => setImportOpen(true)}><Upload size={17}/>Nhập trắc nghiệm DOCX</button><button type="button" disabled={!courseId || busy || coursesLoading} onClick={() => setEditor('new')}><Plus size={18}/>Thêm tự luận</button></>}{close && <button type="button" className="bank-secondary" disabled={busy} onClick={closePicker}><X size={18}/>Đóng</button>}</div>
    </header>
    {picker && <div className="bank-view-switch" aria-label="Danh sách câu hỏi"><button type="button" aria-pressed={!reviewSelected} onClick={() => showReview(false)}>Ngân hàng</button><button type="button" aria-pressed={reviewSelected} onClick={() => showReview(true)}>Đã chọn <span>{selected.size}</span></button><span className="bank-course-context">{course?.code} · {course?.title}</span></div>}
    <div className="bank-workspace-body" ref={bodyRef}>
      {!reviewSelected && <div className="bank-filter-panel">
        <div className="bank-primary-filters"><label>Học phần<select value={courseId} disabled={!!fixedCourseId || busy || coursesLoading} onChange={event => { setCourseId(event.target.value); setPage(1); setSelected(new Map()); setNotice(''); setError(''); setUndo(null); }}><option value="">{coursesLoading ? 'Đang tải học phần…' : 'Chọn học phần'}</option>{courses.map(row => <option key={row.id} value={row.id}>{row.code} · {row.title}</option>)}</select></label><label>Tìm câu hỏi<div className="bank-search"><Search size={18}/><input type="search" value={filters.query} onChange={event => changeFilter({ query: event.target.value })} placeholder="Nhập nội dung hoặc từ khóa…"/>{filters.query && <button type="button" className="bank-icon-button" aria-label="Xóa từ khóa tìm kiếm" onClick={() => changeFilter({ query: '' })}><X size={16}/></button>}</div></label></div>
        <details className="bank-advanced-filters" open={advancedOpen} onToggle={event => setAdvancedOpen(event.currentTarget.open)}><summary><Filter size={15}/>Bộ lọc chi tiết<span>{activeFilters.filter(([key]) => key !== 'query').length || ''}</span></summary><div className="bank-secondary-filters"><label>Chủ đề<input maxLength={200} value={filters.topic} onChange={event => changeFilter({ topic: event.target.value })} placeholder="Tất cả chủ đề"/></label><label>Dạng câu<select value={filters.type} onChange={event => changeFilter({ type: event.target.value })}><option value="">Tất cả dạng câu</option>{questionTypes.map((name, index) => <option key={index} value={index}>{name}</option>)}</select></label><label>Độ khó<select value={filters.difficulty} onChange={event => changeFilter({ difficulty: event.target.value })}><option value="">Tất cả độ khó</option>{difficulties.map((name, index) => <option key={index} value={index}>{name}</option>)}</select></label>{!picker && <label className="bank-check bank-archive-filter"><input type="checkbox" checked={filters.includeArchived} onChange={event => changeFilter({ includeArchived: event.target.checked })}/>Gồm câu đã lưu trữ</label>}</div></details>
        {hasFilters && <div className="bank-active-filters" aria-label="Bộ lọc đang áp dụng">{activeFilters.map(([key, label]) => <button type="button" className="bank-filter-chip" key={key} onClick={() => changeFilter({ [key]: key === 'includeArchived' ? false : '' })}>{label}<X size={13}/><span className="bank-sr-only">Bỏ bộ lọc</span></button>)}<button type="button" className="bank-text-button" onClick={() => { setFilters(clearFilters); setPage(1); }}>Xóa bộ lọc</button></div>}
      </div>}
      {picker && !reviewSelected && <div className="bank-draw"><div><span className="bank-draw-icon"><Shuffle size={20}/></span><div><strong>Chọn ngẫu nhiên</strong><p>Áp dụng bộ lọc phía trên, bỏ qua câu đã chọn và câu đã có trong đề.</p></div></div><div className="bank-draw-controls"><label>Số câu<input aria-label="Số câu chọn ngẫu nhiên" type="number" min={1} max={Math.max(1, capacity - selected.size)} value={count} onChange={event => setCount(Math.max(1, Math.min(200, Number(event.target.value) || 1)))}/></label><button type="button" disabled={!courseId || busy || selected.size >= capacity} onClick={() => void draw()}>{busy ? <LoaderCircle size={17} className="bank-spin"/> : <Shuffle size={17}/>}Chọn thêm</button></div></div>}
      {notice && <div className="bank-feedback bank-success" role="status"><CheckCircle2 size={19}/><span>{notice}</span>{undo && <button type="button" disabled={busy} className="bank-text-button" onClick={() => void changeArchive(undo.row, undo.archived, true)}>Hoàn tác</button>}<button type="button" className="bank-icon-button" aria-label="Đóng thông báo" onClick={() => { setNotice(''); setUndo(null); }}><X size={16}/></button></div>}
      {error && <div className="bank-feedback bank-error" role="alert"><AlertCircle size={19}/><span>{error}</span>{!courses.length && <button type="button" className="bank-text-button" onClick={() => setCoursesRetry(value => value + 1)}>Thử lại</button>}<button type="button" className="bank-icon-button" aria-label="Đóng lỗi thao tác" onClick={() => setError('')}><X size={16}/></button></div>}
      <div className="bank-toolbar"><div><h3>{reviewSelected ? 'Câu hỏi đã chọn' : 'Danh sách câu hỏi'}</h3><span role="status">{!reviewSelected && (loading || coursesLoading) ? 'Đang cập nhật…' : !courseId && error ? 'Chưa tải được dữ liệu' : reviewSelected ? selected.size + ' câu · ' + pointsText(selectedPoints) + ' điểm' : data.total + ' câu phù hợp'}</span></div>{picker && !reviewSelected && !loading && !loadError && visibleSelectable.length > 0 && <label className="bank-check"><input type="checkbox" checked={allVisibleSelected} disabled={busy || (!allVisibleSelected && selected.size >= capacity)} onChange={selectPage}/>{allVisibleSelected ? 'Bỏ chọn trang này' : 'Chọn cả trang'}</label>}{picker && reviewSelected && selected.size > 0 && <button type="button" className="bank-text-button" disabled={busy} onClick={() => setSelected(new Map())}>Bỏ chọn tất cả</button>}</div>
      {!courseId && error ? null : !reviewSelected && loadError ? <div className="bank-empty bank-load-error" role="alert"><AlertCircle size={30}/><h3>Chưa tải được câu hỏi</h3><p>{loadError}</p><button type="button" onClick={() => setReload(value => value + 1)}><RotateCcw size={16}/>Tải lại</button></div> : !reviewSelected && (loading || coursesLoading) ? <div className="bank-loading" role="status"><LoaderCircle size={24} className="bank-spin"/><span>Đang tải câu hỏi…</span></div> : !courseId || !currentRows.length ? <div className="bank-empty"><span className="bank-empty-icon">{hasFilters && !reviewSelected ? <Filter size={28}/> : <FileText size={28}/>}</span><h3>{reviewSelected ? 'Chưa chọn câu hỏi nào' : !courseId ? 'Chưa có học phần' : hasFilters ? 'Không tìm thấy câu hỏi phù hợp' : 'Ngân hàng đang trống'}</h3><p>{reviewSelected ? 'Quay lại ngân hàng để chọn từng câu hoặc chọn ngẫu nhiên.' : !courseId ? 'Các học phần sẽ xuất hiện khi bạn được phân công giảng dạy.' : hasFilters ? 'Thử từ khóa khác hoặc bỏ bớt bộ lọc để xem thêm câu hỏi.' : canWrite ? 'Bắt đầu bằng câu tự luận hoặc nhập trắc nghiệm từ tệp DOCX.' : 'Giảng viên có thể thêm câu hỏi trong học phần này.'}</p>{reviewSelected ? <button type="button" onClick={() => showReview(false)}><ArrowLeft size={16}/>Về ngân hàng</button> : hasFilters ? <button type="button" className="bank-secondary" onClick={() => { setFilters(clearFilters); setPage(1); }}>Xóa bộ lọc</button> : canWrite && courseId && <button type="button" onClick={() => setEditor('new')}><Plus size={17}/>Thêm câu tự luận đầu tiên</button>}</div> : <div className="bank-list">{currentRows.map((row, index) => <QuestionCard key={row.id} row={row} number={reviewSelected ? index + 1 : (page - 1) * 20 + index + 1} picker={picker} selected={selected.has(row.id)} already={excluded.has(row.id)} disabled={busy || (!selected.has(row.id) && selected.size >= capacity)} canEdit={canWrite && row.createdById === user.id} toggle={() => toggle(row)} edit={() => setEditor(row)} archive={() => void changeArchive(row, !row.isArchived)}/>)}</div>}
      {!reviewSelected && courseId && data.total > 0 && !loadError && <nav className="bank-pagination" aria-label="Phân trang câu hỏi"><span>{loading ? 'Đang tải…' : 'Hiển thị ' + ((page - 1) * 20 + 1) + '–' + Math.min(page * 20, data.total) + ' / ' + data.total}</span><div><button type="button" className="bank-secondary" disabled={page <= 1 || loading} onClick={() => setPage(value => value - 1)}><ChevronLeft size={17}/>Trước</button><span>Trang {page} / {Math.max(1, Math.ceil(data.total / 20))}</span><button type="button" className="bank-secondary" disabled={page * 20 >= data.total || loading} onClick={() => setPage(value => value + 1)}>Sau<ChevronRight size={17}/></button></div></nav>}
      {!picker && canWrite && <p className="bank-bottom-hint"><FileText size={16}/>Tự luận được soạn trực tiếp. Trắc nghiệm được nhập từ DOCX hoặc lưu từ bài kiểm tra đã có.</p>}
    </div>
    {picker && <div className="bank-selection-footer"><div><strong>{selected.size} câu đang chọn <span>· {pointsText(selectedPoints)} điểm</span></strong><small>{excluded.size ? excluded.size + ' câu đã có trong đề · ' : ''}Còn có thể thêm {Math.max(0, capacity - selected.size)} câu</small></div><div className="bank-actions"><button type="button" className="bank-secondary" onClick={() => showReview(!reviewSelected)}>{reviewSelected ? 'Tiếp tục chọn' : 'Xem đã chọn'}</button><button type="button" disabled={selected.size === 0 || busy} onClick={() => onPick?.([...selected.values()])}><Check size={18}/>Thêm {selected.size} câu vào đề</button></div></div>}
    {editor && <BankEditor courseName={course?.title || ''} courseId={courseId} row={editor === 'new' ? null : editor} close={() => setEditor(null)} saved={() => { setEditor(null); setReload(value => value + 1); setNotice('Đã lưu câu hỏi vào ngân hàng.'); setUndo(null); }}/>}
    {importOpen && <BankImport courseId={courseId} courseName={course?.title || ''} close={() => setImportOpen(false)} saved={result => { setImportOpen(false); setReload(value => value + 1); setPage(1); setFilters(clearFilters); setNotice('Đã nhập ' + result.added + ' câu; bỏ qua ' + result.skipped + ' câu trùng.'); setUndo(null); }}/>}
  </section>;
  return picker ? <><BankDialog titleId={titleId} close={closePicker}>{panel}</BankDialog>{discardOpen && <DiscardDialog selection cancel={() => setDiscardOpen(false)} discard={() => close?.()}/>}</> : panel;
}

function QuestionCard({ row, number, picker, selected, already, disabled, canEdit, toggle, edit, archive }: { row: BankQuestion; number: number; picker: boolean; selected: boolean; already: boolean; disabled: boolean; canEdit: boolean; toggle: () => void; edit: () => void; archive: () => void }) {
  let answers: string[] = [];
  if (row.type === 1) { try { const parsed: unknown = JSON.parse(row.correctAnswer); if (Array.isArray(parsed)) answers = parsed.filter((value): value is string => typeof value === 'string'); } catch { /* Older data is shown verbatim below. */ } }
  else if (row.type !== 3) answers = [row.correctAnswer];
  return <article className={['bank-question', selected ? 'is-selected' : '', row.isArchived ? 'is-archived' : '', already ? 'is-in-quiz' : ''].join(' ')}>
    <header><div className="bank-tags">{picker ? <label className="bank-check"><input type="checkbox" aria-label={'Chọn câu hỏi ' + row.content.slice(0, 80)} disabled={already || disabled} checked={already || selected} onChange={toggle}/>{already ? 'Đã có trong đề' : selected ? 'Đã chọn' : 'Chọn câu'}</label> : <span className="bank-question-index">{String(number).padStart(2, '0')}</span>}<span className={'bank-type bank-type-' + row.type}>{questionTypes[row.type]}</span><span>{difficulties[row.difficulty]}</span><span>{pointsText(row.points)} điểm</span>{row.isArchived && <span>Đã lưu trữ</span>}</div>{canEdit && <div className="bank-actions">{!row.isArchived && <button type="button" className="bank-text-button" disabled={disabled} onClick={edit}>Chỉnh sửa</button>}<button type="button" className="bank-icon-button" disabled={disabled} aria-label={(row.isArchived ? 'Khôi phục câu hỏi ' : 'Lưu trữ câu hỏi ') + number} title={row.isArchived ? 'Khôi phục câu hỏi' : 'Lưu trữ câu hỏi'} onClick={archive}>{row.isArchived ? <RotateCcw size={17}/> : <Archive size={17}/>}</button></div>}</header>
    <p className="bank-content">{row.content}</p><div className="bank-question-meta"><span>{row.topic || 'Chưa phân chủ đề'}</span><span>{row.authorName}</span><span>{['Soạn thủ công', 'Nhập DOCX', 'Lưu từ đề'][row.source]}</span></div>
    <details className="bank-question-details"><summary>Xem câu hỏi & {row.type === 3 ? 'hướng dẫn chấm' : 'đáp án'}</summary><div><p className="bank-full-content">{row.content}</p>{row.options.length > 0 && <ul className="bank-options">{row.options.map((option, index) => <li key={index} className={answers.includes(option) ? 'is-correct' : ''}><span>{String.fromCharCode(65 + index)}</span><p>{option}</p>{answers.includes(option) && <CheckCircle2 size={17} aria-label="Đáp án đúng"/>}</li>)}</ul>}{row.type === 3 ? <div className="bank-grading-guide"><b>Hướng dẫn chấm</b><p>{row.correctAnswer || 'Chưa bổ sung hướng dẫn chấm.'}</p></div> : <p className="bank-answer-caption">{answers.length > 0 ? 'Đáp án đúng: ' + row.options.flatMap((option, index) => answers.includes(option) ? [String.fromCharCode(65 + index)] : []).join(', ') : row.correctAnswer || 'Chưa nhập đáp án'}</p>}</div></details>
  </article>;
}

function BankImport({ courseId, courseName, close, saved }: { courseId: string; courseName: string; close: () => void; saved: (result: { added: number; skipped: number }) => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [topic, setTopic] = useState('');
  const [difficulty, setDifficulty] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [dragging, setDragging] = useState(false);
  const selectFile = (next: File | null) => {
    setError('');
    if (next && (!next.name.toLowerCase().endsWith('.docx') || next.size > 10 * 1024 * 1024 || next.size === 0)) { setFile(null); setError('Chọn tệp DOCX có nội dung, tối đa 10 MB.'); return; }
    setFile(next);
  };
  const dismiss = () => { if (!busy) close(); };
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (!file || busy) return;
    setBusy(true); setError('');
    const body = new FormData(); body.append('courseId', courseId); body.append('file', file); body.append('topic', topic.trim()); body.append('difficulty', String(difficulty));
    try { saved(await bankRequest<{ added: number; skipped: number }>('/import-docx', 'POST', body)); } catch (reason) { setError(errorText(reason)); } finally { setBusy(false); }
  };
  return <BankDialog titleId="bank-import-title" close={dismiss} compact><form className="bank-import-form" onSubmit={submit} aria-busy={busy}><header className="bank-dialog-heading"><div><p className="bank-eyebrow">THÊM CÂU HỎI / {courseName}</p><h2 id="bank-import-title">Nhập trắc nghiệm từ DOCX</h2></div><button type="button" className="bank-icon-button" aria-label="Đóng nhập DOCX" disabled={busy} onClick={dismiss}><X size={20}/></button></header><div className="bank-import-body"><p>Chọn tệp và phân loại câu hỏi trước khi nhập. Các câu trùng được tự động bỏ qua.</p><label className={'bank-file-drop' + (dragging ? ' is-dragging' : '')} onDragOver={event => { event.preventDefault(); if (!busy) setDragging(true); }} onDragLeave={() => setDragging(false)} onDrop={event => { event.preventDefault(); setDragging(false); if (!busy) selectFile(event.dataTransfer.files[0] || null); }}><Upload size={28}/><strong>{file ? file.name : 'Chọn tệp hoặc kéo thả vào đây'}</strong><span>{file ? (file.size / 1024).toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + ' KB · Bấm để chọn tệp khác' : 'Định dạng .docx · Tối đa 10 MB'}</span><input type="file" accept=".docx" disabled={busy} aria-label="Chọn tệp câu hỏi DOCX" onChange={event => { selectFile(event.target.files?.[0] || null); event.target.value = ''; }}/></label><div className="bank-import-meta"><label>Chủ đề<input value={topic} maxLength={200} disabled={busy} onChange={event => setTopic(event.target.value)} placeholder="Ví dụ: Giao dịch dân sự"/></label><label>Độ khó<select value={difficulty} disabled={busy} onChange={event => setDifficulty(Number(event.target.value))}>{difficulties.map((name, index) => <option key={index} value={index}>{name}</option>)}</select></label></div><details className="bank-format-help"><summary>Cách trình bày câu hỏi trong DOCX</summary><pre>{'Câu 1: Nội dung câu hỏi\nA. Phương án thứ nhất\nB. Phương án thứ hai\nC. Phương án thứ ba\nD. Phương án thứ tư\nĐáp án: A'}</pre><p>Mỗi câu cần ít nhất 2 phương án và một đáp án đúng. Tệp có câu thiếu dữ liệu sẽ được báo lỗi để bạn sửa trước khi nhập.</p></details>{error && <p className="bank-feedback bank-error" role="alert"><AlertCircle size={18}/>{error}</p>}</div><footer className="bank-dialog-footer"><button type="button" className="bank-secondary" disabled={busy} onClick={dismiss}>Hủy</button><button type="submit" disabled={!file || busy}>{busy ? <LoaderCircle size={17} className="bank-spin"/> : <Upload size={17}/ >}{busy ? 'Đang nhập…' : 'Nhập câu hỏi'}</button></footer></form></BankDialog>;
}

function BankEditor({ courseId, courseName, row, close, saved }: { courseId: string; courseName: string; row: BankQuestion | null; close: () => void; saved: () => void }) {
  const initial = { content: row?.content || '', options: row?.options || [], correctAnswer: row?.correctAnswer || '', points: row?.points || 1, topic: row?.topic || '', difficulty: row?.difficulty ?? 1 };
  const [form, setForm] = useState(initial);
  const initialJson = useRef(JSON.stringify(initial));
  const dirty = JSON.stringify(form) !== initialJson.current;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const type = row?.type ?? 3;
  const [discardOpen, setDiscardOpen] = useState(false);
  const dismiss = () => { if (!busy) { if (dirty) setDiscardOpen(true); else close(); } };
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn); return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (busy) return;
    if (!form.content.trim()) { setError('Vui lòng nhập nội dung câu hỏi.'); return; }
    if (type === 1 && !correct.length) { setError('Chọn ít nhất một đáp án đúng.'); return; }
    setBusy(true); setError('');
    try { if (row) await bankRequest('/' + row.id, 'PUT', { ...form, content: form.content.trim(), revision: row.revision }); else await bankRequest('/essay', 'POST', { courseId, ...form, content: form.content.trim(), gradingGuide: form.correctAnswer }); saved(); } catch (reason) { setError(errorText(reason)); } finally { setBusy(false); }
  };
  const changeOption = (index: number, value: string) => setForm(current => {
    const old = current.options[index]; let answer = current.correctAnswer;
    if (type === 1) { try { answer = JSON.stringify((JSON.parse(answer) as string[]).map(item => item === old ? value : item)); } catch { answer = '[]'; } } else if (answer === old) answer = value;
    return { ...current, options: current.options.map((item, i) => i === index ? value : item), correctAnswer: answer };
  });
  let correct: string[] = []; if (type === 1) { try { const parsed: unknown = JSON.parse(form.correctAnswer); if (Array.isArray(parsed)) correct = parsed.filter((value): value is string => typeof value === 'string'); } catch { /* No valid selection. */ } }
  return <><BankDialog titleId="bank-editor-title" close={dismiss}><form className="bank-editor" onSubmit={submit} aria-busy={busy}><header className="bank-dialog-heading"><div><p className="bank-eyebrow">NGÂN HÀNG CÂU HỎI / {courseName}</p><h2 id="bank-editor-title">{row ? 'Chỉnh sửa câu hỏi' : 'Thêm câu hỏi tự luận'}</h2></div><div className="bank-actions"><span className="bank-save-state">{busy ? 'Đang lưu…' : dirty ? 'Có thay đổi chưa lưu' : 'Chưa có thay đổi'}</span><button type="button" className="bank-secondary" disabled={busy} onClick={dismiss}>Hủy</button><button disabled={busy || (!!row && !dirty)}>{busy ? <LoaderCircle size={17} className="bank-spin"/> : <Check size={17}/ >}{busy ? 'Đang lưu…' : 'Lưu câu hỏi'}</button></div></header><div className="bank-editor-scroll"><fieldset disabled={busy} className="bank-editor-fields"><div className="bank-editor-intro"><span className="bank-type">{questionTypes[type]}</span><p>Các trường có dấu * là bắt buộc. Câu hỏi được lưu trong học phần đang chọn.</p></div><div className="bank-editor-meta"><label>Chủ đề<input value={form.topic} maxLength={200} placeholder="Ví dụ: Giao dịch dân sự" onChange={event => setForm({ ...form, topic: event.target.value })}/></label><label>Độ khó<select value={form.difficulty} onChange={event => setForm({ ...form, difficulty: Number(event.target.value) })}>{difficulties.map((name, index) => <option key={index} value={index}>{name}</option>)}</select></label><label>Điểm mặc định *<input type="number" min="0.25" max="100000" step="0.25" required value={form.points} onChange={event => setForm({ ...form, points: Number(event.target.value) })}/></label></div><label>Nội dung câu hỏi *<textarea rows={7} required maxLength={5000} value={form.content} onChange={event => setForm({ ...form, content: event.target.value })} placeholder="Nhập câu hỏi rõ ràng, đủ dữ kiện để học viên trả lời…"/><small className="bank-field-hint">{form.content.length.toLocaleString('vi-VN')} / 5.000 ký tự</small></label>{type === 3 ? <label>Hướng dẫn / tiêu chí chấm<textarea rows={6} maxLength={5000} value={form.correctAnswer} onChange={event => setForm({ ...form, correctAnswer: event.target.value })} placeholder="Các ý cần đạt và cách phân bổ điểm cho từng ý…"/><small className="bank-field-hint">Chỉ người có quyền soạn đề và chấm bài được xem nội dung này.</small></label> : <><div className="bank-editor-options">{form.options.map((option, index) => <label key={index}>Phương án {String.fromCharCode(65 + index)} *<input required maxLength={2000} value={option} onChange={event => changeOption(index, event.target.value)}/></label>)}</div>{type === 1 ? <fieldset className="bank-correct-options"><legend>Đáp án đúng *</legend>{form.options.map((option, index) => <label className="bank-check" key={index}><input type="checkbox" checked={correct.includes(option)} onChange={() => setForm({ ...form, correctAnswer: JSON.stringify(correct.includes(option) ? correct.filter(item => item !== option) : [...correct, option]) })}/><span>{String.fromCharCode(65 + index)}. {option}</span></label>)}</fieldset> : <label>Đáp án đúng *<select required value={form.correctAnswer} onChange={event => setForm({ ...form, correctAnswer: event.target.value })}><option value="">Chọn đáp án</option>{form.options.map((option, index) => <option key={index} value={option}>{String.fromCharCode(65 + index)}. {option}</option>)}</select></label>}</>}<p className="bank-editor-note"><CheckCircle2 size={18}/>Những đề đã sử dụng câu hỏi này vẫn giữ nguyên nội dung khi bạn chỉnh sửa ngân hàng.</p>{error && <p className="bank-feedback bank-error" role="alert"><AlertCircle size={18}/>{error}</p>}</fieldset></div></form></BankDialog>{discardOpen && <DiscardDialog cancel={() => setDiscardOpen(false)} discard={close}/>}</>;
}
