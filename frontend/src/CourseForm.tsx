import { useFeedback, UiNotice } from './ui';
import { BusinessFormDialog } from './BusinessFormDialog';
import { FormEvent, useState } from 'react';
import {api,Course} from './api';

export function CreateCourseModal({course,close,saved}:{course?:Course;close:()=>void;saved:()=>void}){
 const [form,setForm]=useState({code:course?.code||'',title:course?.title||'',description:course?.description||'',category:course?.category||'Luật',color:course?.color||'#355C42'});
 const [initial] = useState(() => JSON.stringify(form));
 const [busy,setBusy]=useState(false);const [message, setMessage, messageTone] = useFeedback();
 const submit=async(e:FormEvent)=>{e.preventDefault();setBusy(true);try{if(course)await api.updateCourse(course.id,form);else await api.createCourse(form);saved();}catch(error){setMessage.error((error as Error).message);}finally{setBusy(false);}};
 return <BusinessFormDialog title={course?'Chỉnh sửa học phần':'Tạo học phần'} description="Học phần được lưu thành bản nháp. Thêm bài học và gửi duyệt trước khi công bố." close={close} busy={busy} dirty={JSON.stringify(form)!==initial} submit={submit} submitLabel="Lưu bản nháp"><label>Mã học phần<input required maxLength={30} value={form.code} onChange={e=>setForm({...form,code:e.target.value})}/></label><label>Tên học phần<input required maxLength={300} value={form.title} onChange={e=>setForm({...form,title:e.target.value})}/></label><label>Mô tả<textarea required maxLength={4000} value={form.description} onChange={e=>setForm({...form,description:e.target.value})}/></label><label>Nhóm học phần<input required maxLength={100} value={form.category} onChange={e=>setForm({...form,category:e.target.value})}/></label><label>Màu nhận diện<input type="color" value={form.color} onChange={e=>setForm({...form,color:e.target.value})}/></label>{message&&<UiNotice tone="error">{message}</UiNotice>}</BusinessFormDialog>;
}
