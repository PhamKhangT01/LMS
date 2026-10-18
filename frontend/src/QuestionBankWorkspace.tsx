import { FormEvent, useEffect, useState } from 'react';
import { Archive, Check, ChevronLeft, ChevronRight, FileText, Plus, RotateCcw, Search, Shuffle, Upload, X } from 'lucide-react';
import { User } from './api';
import { BankCourse, BankPage, BankQuestion, bankRequest, difficulties, questionTypes } from './questionBankApi';
import './question-bank.css';

type Props={user:User;fixedCourseId?:string;onPick?:(rows:BankQuestion[])=>void;excludedIds?:string[];close?:()=>void};
export default function QuestionBankWorkspace({user,fixedCourseId,onPick,excludedIds=[],close}:Props){
  const [courses,setCourses]=useState<BankCourse[]>([]);
  const [courseId,setCourseId]=useState(fixedCourseId||'');
  const [filters,setFilters]=useState({query:'',topic:'',type:'',difficulty:'',includeArchived:false});
  const [page,setPage]=useState(1);
  const [data,setData]=useState<BankPage>({items:[],total:0,page:1,pageSize:20});
  const [loading,setLoading]=useState(false);
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('');
  const [reload,setReload]=useState(0);
  const [editor,setEditor]=useState<BankQuestion|'new'|null>(null);
  const [selected,setSelected]=useState<Map<string,BankQuestion>>(new Map());
  const [count,setCount]=useState(3);
  const canWrite=user.role==='Lecturer'&&!onPick;
  useEffect(()=>{let live=true;bankRequest<BankCourse[]>('/courses').then(rows=>{if(live){setCourses(rows);setCourseId(fixedCourseId||rows[0]?.id||'');}}).catch(e=>{if(live)setMessage(e.message);});return()=>{live=false;};},[fixedCourseId]);
  useEffect(()=>{
    if(!courseId)return;
    let live=true;
    const timer=setTimeout(()=>{
      setLoading(true);
      const query=new URLSearchParams({courseId,page:String(page),pageSize:'20',query:filters.query,topic:filters.topic,includeArchived:String(!onPick&&filters.includeArchived)});
      if(filters.type!=='')query.set('type',filters.type);if(filters.difficulty!=='')query.set('difficulty',filters.difficulty);
      bankRequest<BankPage>('?'+query).then(result=>{if(live){setData(result);const last=Math.max(1,Math.ceil(result.total/result.pageSize));if(page>last)setPage(last);}}).catch(e=>{if(live)setMessage(e.message);}).finally(()=>{if(live)setLoading(false);});
    },250);
    return()=>{live=false;clearTimeout(timer);};
  },[courseId,filters,page,reload,onPick]);
  const act=async(work:()=>Promise<unknown>,success:string)=>{setBusy(true);setMessage('');try{await work();setReload(x=>x+1);setMessage(success);}catch(e){setMessage(e instanceof Error?e.message:'Không thể xử lý.');}finally{setBusy(false);}};
  const changeFilter=(patch:Partial<typeof filters>)=>{setFilters(current=>({...current,...patch}));setPage(1);};
  const importFile=async(file:File|null)=>{
    if(!file||!courseId)return;
    const body=new FormData();body.append('file',file);body.append('courseId',courseId);body.append('topic',filters.topic);body.append('difficulty',filters.difficulty||'1');
    setBusy(true);setMessage('');
    try{const result=await bankRequest<{added:number;skipped:number}>('/import-docx','POST',body);setMessage(`Đã nhập ${result.added} câu; bỏ qua ${result.skipped} câu trùng.`);setReload(x=>x+1);setPage(1);}catch(e){setMessage(e instanceof Error?e.message:'Không thể nhập DOCX.');}finally{setBusy(false);}
  };
  const draw=async()=>{
    if(selected.size+count>200)return setMessage('Mỗi đề chọn tối đa 200 câu hỏi.');
    setBusy(true);setMessage('');
    try{
      const rows=await bankRequest<BankQuestion[]>('/draw','POST',{courseId,count,topic:filters.topic,type:filters.type===''?null:Number(filters.type),difficulty:filters.difficulty===''?null:Number(filters.difficulty),excludeIds:[...new Set([...excludedIds,...selected.keys()])]});
      setSelected(current=>new Map([...current,...rows.map(row=>[row.id,row] as [string,BankQuestion])]));setMessage(`Đã chọn ngẫu nhiên ${rows.length} câu theo bộ lọc dạng câu, chủ đề và độ khó.`);
    }catch(e){setMessage(e instanceof Error?e.message:'Không thể chọn câu hỏi.');}finally{setBusy(false);}
  };
  const toggle=(row:BankQuestion)=>setSelected(current=>{const next=new Map(current);if(next.has(row.id))next.delete(row.id);else if(next.size<200)next.set(row.id,row);return next;});
  const panel=<section className="question-bank" aria-labelledby={onPick?'bank-picker-title':'bank-title'}>
    <header className="bank-heading"><div><h2 id={onPick?'bank-picker-title':'bank-title'}>{onPick?'Chọn câu hỏi từ ngân hàng':'Ngân hàng câu hỏi'}</h2><p>{onPick?'Câu hỏi được sao chép vào đề; chỉnh sửa ngân hàng không làm thay đổi đề đã lưu.':'Lưu, phân loại và tái sử dụng câu hỏi trong các học phần được phân công.'}</p></div><div className="bank-actions">{canWrite&&<><button type="button" disabled={!courseId||busy} onClick={()=>setEditor('new')}><Plus size={17}/>Thêm tự luận</button><label className={`bank-import ${busy?'is-busy':''}`}><Upload size={17}/>Nhập DOCX<input aria-label="Nhập trắc nghiệm vào ngân hàng từ DOCX" type="file" accept=".docx" disabled={!courseId||busy} onChange={e=>{void importFile(e.target.files?.[0]||null);e.target.value='';}}/></label></>}{close&&<button type="button" className="bank-secondary" onClick={close}><X size={17}/>Đóng</button>}</div></header>
    <div className="bank-filters"><label>Học phần<select value={courseId} disabled={!!fixedCourseId||busy} onChange={e=>{setCourseId(e.target.value);setPage(1);setSelected(new Map());}}><option value="">Chọn học phần</option>{courses.map(c=><option key={c.id} value={c.id}>{c.code} · {c.title}</option>)}</select></label><label>Tìm câu hỏi<div className="bank-search"><Search size={17}/><input value={filters.query} onChange={e=>changeFilter({query:e.target.value})} placeholder="Nội dung hoặc từ khóa"/></div></label><label>Chủ đề<input maxLength={200} value={filters.topic} onChange={e=>changeFilter({topic:e.target.value})} placeholder="Tất cả chủ đề"/></label><label>Dạng câu<select value={filters.type} onChange={e=>changeFilter({type:e.target.value})}><option value="">Tất cả</option>{questionTypes.map((name,i)=><option key={i} value={i}>{name}</option>)}</select></label><label>Độ khó<select value={filters.difficulty} onChange={e=>changeFilter({difficulty:e.target.value})}><option value="">Tất cả</option>{difficulties.map((name,i)=><option key={i} value={i}>{name}</option>)}</select></label></div>
    {canWrite&&<p className="bank-hint">DOCX dùng mẫu Câu 1:, A., B., … và Đáp án: A. Câu nhập nhận chủ đề và độ khó đang chọn; mặc định là Vận dụng.</p>}
    {!onPick&&<div className="bank-toolbar"><span>{data.total} câu hỏi phù hợp</span><label className="bank-check"><input type="checkbox" checked={filters.includeArchived} onChange={e=>changeFilter({includeArchived:e.target.checked})}/>Hiện cả câu đã lưu trữ</label></div>}
    {onPick&&<div className="bank-draw"><div><Shuffle size={20}/><span>Chọn ngẫu nhiên theo bộ lọc</span></div><label>Số câu<input aria-label="Số câu chọn ngẫu nhiên" type="number" min={1} max={200} value={count} onChange={e=>setCount(Math.max(1,Math.min(200,Number(e.target.value)||1)))}/></label><button type="button" disabled={!courseId||busy} onClick={()=>void draw()}>Chọn ngẫu nhiên</button><strong>Đã chọn {selected.size} câu</strong></div>}
    {message&&<p className="bank-message" role="status">{message}</p>}
    {loading?<p role="status">Đang tải câu hỏi…</p>:!courseId?<p className="bank-empty">Chưa có học phần được phân công.</p>:!data.items.length?<p className="bank-empty">Chưa có câu hỏi phù hợp. {canWrite?'Thêm tự luận, nhập DOCX hoặc lưu câu hỏi từ một đề có sẵn.':''}</p>:<div className="bank-list">{data.items.map(row=>{
      const already=excludedIds.includes(row.id);
      return <article className={`bank-question ${row.isArchived?'is-archived':''}`} key={row.id}><header><div className="bank-tags">{onPick&&<label className="bank-check"><input aria-label={`Chọn câu hỏi ${row.content.slice(0,80)}`} type="checkbox" disabled={already||(!selected.has(row.id)&&selected.size>=200)} checked={already||selected.has(row.id)} onChange={()=>toggle(row)}/>{already?'Đã có trong đề':'Chọn'}</label>}<span>{questionTypes[row.type]}</span><span>{difficulties[row.difficulty]}</span><span>{row.points} điểm</span>{row.isArchived&&<span>Đã lưu trữ</span>}</div>{canWrite&&row.createdById===user.id&&<div className="bank-actions">{!row.isArchived&&<button type="button" className="bank-secondary" disabled={busy} onClick={()=>setEditor(row)}>Chỉnh sửa</button>}<button type="button" className="bank-secondary" disabled={busy} onClick={()=>void act(()=>bankRequest(`/${row.id}/archive`,'PUT',{archived:!row.isArchived,revision:row.revision}),row.isArchived?'Đã khôi phục câu hỏi.':'Đã lưu trữ câu hỏi; các đề đã tạo giữ nguyên nội dung.')} >{row.isArchived?<RotateCcw size={15}/>:<Archive size={15}/>} {row.isArchived?'Khôi phục':'Lưu trữ'}</button></div>}</header><p className="bank-content">{row.content}</p><small>{row.topic||'Chưa phân chủ đề'} · {row.authorName} · {['Tự luận thủ công','Nhập DOCX','Lưu từ đề'][row.source]}</small><details><summary>Xem đầy đủ và hướng dẫn chấm</summary><p className="bank-full-content">{row.content}</p>{row.options.length>0&&<ul className="bank-options">{row.options.map((option,i)=><li key={i}>{String.fromCharCode(65+i)}. {option}</li>)}</ul>}<p><b>{row.type===3?'Tiêu chí chấm':'Đáp án đúng'}:</b> {row.correctAnswer||'Chưa nhập'}</p></details></article>;
    })}</div>}
    {courseId&&<footer className="bank-pagination"><button type="button" className="bank-secondary" disabled={page<=1||loading} onClick={()=>setPage(x=>x-1)}><ChevronLeft size={17}/>Trước</button><span>Trang {page} / {Math.max(1,Math.ceil(data.total/20))}</span><button type="button" className="bank-secondary" disabled={page*20>=data.total||loading} onClick={()=>setPage(x=>x+1)}>Sau<ChevronRight size={17}/></button></footer>}
    {onPick&&<div className="bank-selection-footer"><button type="button" className="bank-secondary" disabled={selected.size===0} onClick={()=>setSelected(new Map())}>Bỏ chọn tất cả</button><button type="button" disabled={selected.size===0||busy} onClick={()=>onPick([...selected.values()])}><Check size={17}/>Thêm {selected.size} câu vào đề</button></div>}
    {editor&&<BankEditor courseId={courseId} row={editor==='new'?null:editor} close={()=>setEditor(null)} saved={()=>{setEditor(null);setReload(x=>x+1);setMessage('Đã lưu câu hỏi vào ngân hàng.');}}/>}
  </section>;
  return onPick?<div className="modal-backdrop bank-picker-backdrop"><div className="bank-picker-dialog" role="dialog" aria-modal="true" aria-labelledby="bank-picker-title">{panel}</div></div>:panel;
}

function BankEditor({courseId,row,close,saved}:{courseId:string;row:BankQuestion|null;close:()=>void;saved:()=>void}){
  const [form,setForm]=useState({content:row?.content||'',options:row?.options||[],correctAnswer:row?.correctAnswer||'',points:row?.points||1,topic:row?.topic||'',difficulty:row?.difficulty??1});
  const [busy,setBusy]=useState(false);const [error,setError]=useState('');
  const type=row?.type??3;
  const submit=async(e:FormEvent)=>{e.preventDefault();setBusy(true);setError('');try{if(row)await bankRequest(`/${row.id}`,'PUT',{...form,revision:row.revision});else await bankRequest('/essay','POST',{courseId,...form,gradingGuide:form.correctAnswer});saved();}catch(e){setError(e instanceof Error?e.message:'Không thể lưu câu hỏi.');}finally{setBusy(false);}};
  const changeOption=(index:number,value:string)=>setForm(current=>{const old=current.options[index];let answer=current.correctAnswer;if(type===1){try{answer=JSON.stringify((JSON.parse(answer) as string[]).map(x=>x===old?value:x));}catch{answer='[]';}}else if(answer===old)answer=value;return {...current,options:current.options.map((x,i)=>i===index?value:x),correctAnswer:answer};});
  let correct:string[]=[];if(type===1){try{correct=JSON.parse(form.correctAnswer);}catch{}}
  return <div className="modal-backdrop bank-editor-backdrop"><form className="bank-editor" role="dialog" aria-modal="true" aria-labelledby="bank-editor-title" onSubmit={submit}><header><div><FileText size={24}/><h2 id="bank-editor-title">{row?'Chỉnh sửa câu hỏi':'Thêm câu hỏi tự luận'}</h2></div><div className="bank-actions"><button type="button" className="bank-secondary" onClick={close}>Hủy</button><button disabled={busy}>{busy?'Đang lưu…':'Lưu câu hỏi'}</button></div></header><div className="bank-editor-fields"><div className="bank-editor-meta"><label>Chủ đề<input value={form.topic} maxLength={200} onChange={e=>setForm({...form,topic:e.target.value})}/></label><label>Độ khó<select value={form.difficulty} onChange={e=>setForm({...form,difficulty:Number(e.target.value)})}>{difficulties.map((x,i)=><option key={i} value={i}>{x}</option>)}</select></label><label>Điểm mặc định<input type="number" min="0.25" max="100000" step="0.25" required value={form.points} onChange={e=>setForm({...form,points:Number(e.target.value)})}/></label></div><label>Nội dung câu hỏi<textarea rows={7} required maxLength={5000} value={form.content} onChange={e=>setForm({...form,content:e.target.value})}/></label>{type===3?<label>Hướng dẫn / tiêu chí chấm<textarea rows={6} maxLength={5000} value={form.correctAnswer} onChange={e=>setForm({...form,correctAnswer:e.target.value})}/></label>:<><div className="bank-editor-options">{form.options.map((option,i)=><label key={i}>Phương án {String.fromCharCode(65+i)}<input required maxLength={2000} value={option} onChange={e=>changeOption(i,e.target.value)}/></label>)}</div>{type===1?<fieldset><legend>Đáp án đúng</legend>{form.options.map((option,i)=><label className="bank-check" key={i}><input type="checkbox" checked={correct.includes(option)} onChange={()=>setForm({...form,correctAnswer:JSON.stringify(correct.includes(option)?correct.filter(x=>x!==option):[...correct,option])})}/>{option}</label>)}</fieldset>:<label>Đáp án đúng<select required value={form.correctAnswer} onChange={e=>setForm({...form,correctAnswer:e.target.value})}><option value="">Chọn đáp án</option>{form.options.map((option,i)=><option key={i} value={option}>{option}</option>)}</select></label>}</>}<p className="bank-hint">Câu hỏi đã sao chép vào đề được giữ độc lập với những chỉnh sửa tại đây.</p>{error&&<p className="bank-message" role="alert">{error}</p>}</div></form></div>;
}
