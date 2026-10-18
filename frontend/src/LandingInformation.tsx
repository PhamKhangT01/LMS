import { ArrowRight } from 'lucide-react';
import './landing-information.css';

export function LandingInformation({ openLogin }: { openLogin: () => void }) {
  return <div className="landing-information">
    <section className="library-introduction" id="library" aria-labelledby="library-title">
      <div className="library-copy"><h2 id="library-title">Kho học liệu pháp luật</h2><p>Tìm tài liệu cho buổi học hôm nay và những chủ đề bạn muốn nghiên cứu sâu hơn.</p><button type="button" className="landing-text-action" onClick={openLogin}>Đăng nhập để tra cứu<ArrowRight size={18} aria-hidden="true"/></button><p className="library-access-note">Sử dụng tài khoản được cấp để tìm kiếm và tải học liệu.</p></div>
      <ul className="library-types">
        {[
          { title: 'Bài giảng', description: 'Chuẩn bị bài, ôn tập và hệ thống kiến thức trên lớp.' },
          { title: 'Văn bản pháp luật', description: 'Tra cứu và đối chiếu văn bản trong quá trình học.' },
          { title: 'Đề cương', description: 'Nắm nội dung, mục tiêu và phạm vi của học phần.' },
          { title: 'Tài liệu tham khảo', description: 'Mở rộng góc nhìn, bổ sung kiến thức chuyên ngành.' },
        ].map(item => <li key={item.title}><button type="button" className="library-entry" onClick={openLogin} aria-label={`Đăng nhập để tra cứu ${item.title.toLowerCase()}`}><span className="library-entry-title">{item.title}</span><span className="library-entry-description">{item.description}</span><ArrowRight size={20} aria-hidden="true"/></button></li>)}
      </ul>
    </section>

    <section className="landing-help" id="help" aria-labelledby="help-title"><div className="help-heading"><h2 id="help-title">Hướng dẫn bắt đầu</h2><p>Thông tin về tài khoản, học phần và kết quả học tập để bạn sẵn sàng vào lớp.</p></div><div className="landing-faq">
      <details><summary>Tôi nhận tài khoản đăng nhập ở đâu?</summary><p>Tài khoản do đơn vị quản lý hoặc quản trị viên cấp. Học viên được thêm theo danh sách lớp; hệ thống không mở đăng ký tài khoản công khai. Nếu chưa nhận được thông tin đăng nhập, hãy liên hệ cán bộ quản lý lớp.</p></details>
      <details><summary>Vì sao tôi chưa thấy học phần của mình?</summary><p>Học phần xuất hiện theo ghi danh và phân công lớp. Kiểm tra lại tài khoản đang đăng nhập và liên hệ cán bộ quản lý lớp để đối soát nếu danh sách học phần chưa đúng.</p></details>
      <details><summary>Tôi cần làm gì khi đăng nhập lần đầu?</summary><p>Sử dụng thông tin tài khoản được cấp. Nếu hệ thống yêu cầu đổi mật khẩu lần đầu, hãy hoàn tất bước này trước khi truy cập các chức năng học tập.</p></details>
      <details><summary>Khi nào tôi có thể xem điểm và nhận xét?</summary><p>Điểm và nhận xét cá nhân được hiển thị sau khi kết quả đã hoàn tất quy trình duyệt và công bố. Bạn có thể theo dõi kết quả trong lớp học phần và báo cáo học tập.</p></details>
      <details><summary>Tôi phải làm gì nếu quên mật khẩu?</summary><p>Liên hệ quản trị viên hoặc cán bộ quản lý lớp để được hỗ trợ cấp lại mật khẩu. Không gửi mật khẩu hiện tại cho người khác.</p></details>
    </div></section>

    <section className="landing-final-cta"><div><h2>Sẵn sàng vào lớp học?</h2><p>Đăng nhập để tiếp tục học tập, giảng dạy và quản lý đào tạo.</p></div><button type="button" className="hero-login" onClick={openLogin}>Đăng nhập hệ thống<ArrowRight size={18} aria-hidden="true"/></button></section>
  </div>;
}
