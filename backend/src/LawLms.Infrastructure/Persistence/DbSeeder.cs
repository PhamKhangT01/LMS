using LawLms.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(LmsDbContext db, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;
        var hasher = new PasswordHasher<AppUser>();
        var lecturer = new AppUser { FullName = "TS. Nguyễn Minh Anh", Email = "giangvien@lmsluat.edu.vn", Role = UserRole.Lecturer };
        lecturer.PasswordHash = hasher.HashPassword(lecturer, "123456");
        var student = new AppUser { FullName = "Nguyễn Hoài Nam", Email = "sinhvien@lmsluat.edu.vn", Role = UserRole.Student };
        student.PasswordHash = hasher.HashPassword(student, "123456");
        var admin = new AppUser { FullName = "Quản trị hệ thống", Email = "admin@lmsluat.edu.vn", Role = UserRole.Admin };
        admin.PasswordHash = hasher.HashPassword(admin, "123456");
        var departmentHead = new AppUser { FullName = "Trưởng khoa Luật", Email = "truongkhoa@lmsluat.edu.vn", Role = UserRole.DepartmentHead };
        departmentHead.PasswordHash = hasher.HashPassword(departmentHead, "123456");
        var assistantOne = new AppUser { FullName = "ThS. Lê Thu Hà", Email = "lethuha@lmsluat.edu.vn", Role = UserRole.Lecturer }; assistantOne.PasswordHash = hasher.HashPassword(assistantOne, "123456");
        var assistantTwo = new AppUser { FullName = "ThS. Trần Quang Huy", Email = "tranquanghuy@lmsluat.edu.vn", Role = UserRole.Lecturer }; assistantTwo.PasswordHash = hasher.HashPassword(assistantTwo, "123456");
        db.AddRange(lecturer, student, admin, departmentHead, assistantOne, assistantTwo);
        await db.SaveChangesAsync(ct);
    }
}
