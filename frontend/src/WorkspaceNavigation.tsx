import { useState } from 'react';
import { BookOpen, CalendarDays, ChevronDown, Clock3, FileText, FolderOpen, LayoutDashboard, Menu, MessageCircle, ShieldCheck, UsersRound } from 'lucide-react';
import { User } from './api';

export type WorkspaceView = 'overview' | 'courses' | 'resources' | 'course' | 'operations' | 'schedule' | 'forum' | 'student-report' | 'accounts' | 'password' | 'reports' | 'question-bank';

export function WorkspaceNavigation({ user, view, select, importStudents }: { user: User; view: WorkspaceView; select: (view: WorkspaceView) => void; importStudents: () => void }) {
  const [expanded, setExpanded] = useState(false);
  const manager = user.role === 'Admin' || user.role === 'DepartmentHead';
  const items = [
    { view: 'overview' as const, label: 'Tổng quan', icon: LayoutDashboard },
    { view: 'courses' as const, label: user.role === 'Student' ? 'Học phần của tôi' : manager ? 'Học phần & phân công' : 'Học phần được phân công', icon: BookOpen },
    { view: 'operations' as const, label: 'Lớp học phần & kết quả', icon: Clock3 },
    ...(user.role !== 'Student' ? [{ view: 'question-bank' as const, label: 'Ngân hàng câu hỏi', icon: FileText }] : []),
    { view: 'resources' as const, label: 'Kho học liệu', icon: FolderOpen },
    { view: 'schedule' as const, label: 'Thời khóa biểu', icon: CalendarDays },
    { view: 'forum' as const, label: 'Diễn đàn lớp học', icon: MessageCircle },
    ...(user.role === 'Student' ? [{ view: 'student-report' as const, label: 'Báo cáo học tập', icon: FileText }] : [{ view: 'reports' as const, label: 'Báo cáo học vụ', icon: FileText }]),
    ...(user.role === 'Admin' ? [{ view: 'accounts' as const, label: 'Quản lý tài khoản', icon: UsersRound }] : []),
    { view: 'password' as const, label: 'Đổi mật khẩu', icon: ShieldCheck },
  ];
  const current = items.find(item => item.view === (view === 'course' ? 'courses' : view));
  return <aside className="ui-sidebar"><nav aria-label="Điều hướng hệ thống"><button type="button" className="ui-navigation-toggle" aria-expanded={expanded} aria-controls="workspace-navigation" onClick={() => setExpanded(value => !value)}><Menu size={20}/><span>{current?.label || 'Điều hướng'}</span><ChevronDown size={18}/></button><div id="workspace-navigation" className={'ui-navigation-items' + (expanded ? ' is-expanded' : '')}><p>Không gian cá nhân</p>{items.map(item => <button type="button" key={item.view} aria-current={(view === 'course' ? 'courses' : view) === item.view ? 'page' : undefined} onClick={() => { select(item.view); setExpanded(false); }}><item.icon size={19}/><span>{item.label}</span></button>)}{user.role === 'Admin' && <button type="button" onClick={() => { importStudents(); setExpanded(false); }}><UsersRound size={19}/><span>Nhập danh sách học viên</span></button>}</div></nav></aside>;
}
