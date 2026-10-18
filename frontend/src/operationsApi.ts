import { session, User, CourseDetail } from './api';
export type Offering = { id:string;courseId:string;code:string;name:string;courseTitle:string;academicYear:string;term:string;status:number;startsAt:string|null;endsAt:string|null;passingGrade:number;requiredProgress:number;appealDays:number;gradeStatus:number;gradesPublishedAt:string|null;revision:number;studentCount:number;progress:number;completedLessonIds:string[];canSubmitGrades:boolean };
export type ManagedQuiz = {id:string;teachingClassId:string;title:string;status:number;weight:number;multipleChoiceWeight:number;essayWeight:number;revision:number;opensAt:string|null;closesAt:string|null;questions:number;shuffleQuestions:boolean;shuffleOptions:boolean};
export type Answer = {questionId:string;answer:string};
export type Attempt = {id:string;quizId:string;startedAt:string;expiresAt:string;serverNow:string;submittedAt:string|null;status:number;revision:number;questions:{id:string;content:string;type:number;options:string[];points:number;position:number}[];answers:Answer[]};
export type GradeInput = {questionId:string;score:number;feedback:string};
export type GradingAttempt = {id:string;quizId:string;studentId:string;studentName:string;quizTitle:string;status:number;revision:number;finalScore:number|null;answers:{questionId:string;content:string;type:number;maxPoints:number;answer:string;score:number|null;feedback:string;gradingGuide:string}[]};
export type Gradebook = {offeringId:string;status:number;revision:number;visible:boolean;publishedAt:string|null;rows:{studentId:string;studentName:string;progress:number;grade:number;passed:boolean;assessments:{quizId:string;title:string;weight:number;attemptId:string|null;score:number|null;maximum:number;pending:boolean}[]}[]};
export type Appeal = {id:string;studentId:string;studentName:string;attemptId:string;reason:string;resolution:string;reviewerId:string|null;status:number;revision:number;proposedGradesJson:string};
export type Roster = {id:string;userId:string;fullName:string;email:string;status:number;progress:number};
export type Account = {id:string;fullName:string;email:string;role:number;isActive:boolean;mustChangePassword:boolean};
const base=import.meta.env.VITE_API_URL||'http://localhost:5155/api';
export async function operation<T>(path:string,method='GET',body?:unknown):Promise<T>{const r=await fetch(base+'/operations/'+path,{method,headers:{'Content-Type':'application/json',Authorization:`Bearer ${session.token}`},body:body===undefined?undefined:JSON.stringify(body)});if(r.status===401){session.clear();window.dispatchEvent(new Event('lms-session-expired'));}if(r.status===204)return undefined as T;const data=await r.json().catch(()=>({}));if(!r.ok)throw new Error(data.detail||data.title||'Không thể xử lý yêu cầu.');return data as T;}
export async function downloadRecord(id:string){const r=await fetch(base+`/operations/offerings/${id}/export`,{headers:{Authorization:`Bearer ${session.token}`}});if(!r.ok)throw new Error('Không thể xuất hồ sơ.');download(await r.blob(),`ho-so-lop-${id}.json`);}
export function download(data:Blob,name:string){const url=URL.createObjectURL(data);const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
export const ops={
 offerings:()=>operation<Offering[]>('offerings'),
 quizzes:(id:string)=>operation<ManagedQuiz[]>(`offerings/${id}/quizzes`),
 roster:(id:string)=>operation<Roster[]>(`offerings/${id}/roster`),
 grading:(id:string)=>operation<GradingAttempt[]>(`offerings/${id}/grading`),
 grades:(id:string)=>operation<Gradebook>(`offerings/${id}/grades`),
 appeals:(id:string)=>operation<Appeal[]>(`offerings/${id}/appeals`),
 start:(id:string)=>operation<Attempt>(`quizzes/${id}/start`,'POST'),
 save:(a:Attempt,answers:Answer[],submit=false)=>operation<Attempt>(`attempts/${a.id}/${submit?'submit':'draft'}`,submit?'POST':'PUT',{revision:a.revision,answers}),
 changePassword:(currentPassword:string,newPassword:string)=>operation<{token:string;user:User}>('change-password','POST',{currentPassword,newPassword}),
};
export type { CourseDetail };
