import { session } from './api';
export type BankQuestion = {id:string;courseId:string;createdById:string;authorName:string;content:string;type:number;options:string[];correctAnswer:string;points:number;topic:string;difficulty:number;source:number;isArchived:boolean;revision:number};
export type BankCourse = {id:string;code:string;title:string};
export type BankPage = {items:BankQuestion[];total:number;page:number;pageSize:number};
const base=import.meta.env.VITE_API_URL||'http://localhost:5155/api';
export async function bankRequest<T>(path='',method='GET',body?:unknown):Promise<T>{
  const form=body instanceof FormData;
  const r=await fetch(base+'/question-bank'+path,{method,headers:{Authorization:`Bearer ${session.token}`,...(form?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:form?body:JSON.stringify(body)}).catch(()=>{throw new Error('Không kết nối được máy chủ. Vui lòng kiểm tra kết nối rồi thử lại.');});
  if(r.status===401){session.clear();window.dispatchEvent(new Event('lms-session-expired'));}
  if(r.status===204)return undefined as T;
  const data=await r.json().catch(()=>({}));
  if(!r.ok)throw new Error(data.detail||Object.values(data.errors||{}).flat().join(' ')||data.title||'Không thể xử lý ngân hàng câu hỏi.');
  return data as T;
}
export const questionTypes=['Một đáp án','Nhiều đáp án','Đúng / Sai','Tự luận'];
export const difficulties=['Cơ bản','Vận dụng','Nâng cao'];
