using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using LawLms.Domain;
using LawLms.Application;
using System.Security.Cryptography;
using LawLms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Api.Controllers;

[ApiController, Route("api/students")]
public sealed class StudentsController(LmsDbContext db) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet("import-template")]
    public IActionResult DownloadImportTemplate()
    {
        var content = StudentXlsxTemplate.Create();
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "mau-danh-sach-hoc-vien.xlsx");
    }

    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [HttpPost("preview-xlsx")]
    public async Task<IActionResult> Preview([FromForm] IFormFile? file,CancellationToken ct)
    {
        if(file is null || file.Length==0 || !string.Equals(Path.GetExtension(file.FileName),".xlsx",StringComparison.OrdinalIgnoreCase))return ValidationProblem("Chọn tệp XLSX hợp lệ.");
        IReadOnlyList<StudentImportRow> rows;
        try{await using var stream=file.OpenReadStream();rows=StudentXlsxReader.Read(stream);}catch(Exception){return ValidationProblem("Không thể đọc tệp XLSX.");}
        if(rows.Count is <1 or >5000)return ValidationProblem("Mỗi tệp cần từ 1 đến 5000 học viên.");
        var emails=rows.Select(x=>x.Email.Trim().ToLowerInvariant()).ToHashSet();
        var existing=await db.Users.AsNoTracking().Where(x=>emails.Contains(x.Email)).ToDictionaryAsync(x=>x.Email,x=>x,ct);var seen=new HashSet<string>();var result=new List<object>();
        foreach(var row in rows)
        {
            var email=row.Email.Trim().ToLowerInvariant();var error="";
            if(string.IsNullOrWhiteSpace(row.FullName)||string.IsNullOrWhiteSpace(row.ClassCode)||!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)||email.Length>256||row.FullName.Length>200||row.ClassCode.Length>30)error="Thiếu dữ liệu, email sai hoặc vượt độ dài.";
            else if(!seen.Add(email))error="Email trùng trong tệp.";
            else if(existing.TryGetValue(email,out var person)&&person.Role!=UserRole.Student)error="Email thuộc tài khoản không phải học viên.";
            result.Add(new{row=row.Row,fullName=row.FullName,email,classCode=row.ClassCode.Trim().ToUpperInvariant(),action=error.Length>0?"Lỗi":existing.ContainsKey(email)?"Cập nhật":"Tạo mới",error});
        }
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [HttpPost("import-xlsx")]
    public async Task<ActionResult<StudentImportResult>> ImportXlsx([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return ValidationProblem("Vui lòng chọn tệp XLSX.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return ValidationProblem("Chỉ hỗ trợ tệp XLSX.");
        IReadOnlyList<StudentImportRow> rows;
        try { await using var stream = file.OpenReadStream(); rows = StudentXlsxReader.Read(stream); }
        catch (Exception) { return ValidationProblem("Tệp XLSX không hợp lệ hoặc không đọc được."); }
        if (rows.Count > 5000) return ValidationProblem("Mỗi tệp tối đa 5000 học viên.");
        if (rows.Count == 0) return ValidationProblem("Không tìm thấy dữ liệu. Mẫu gồm 3 cột: Họ và tên, Email, Mã lớp.");

        var credentials = new List<InitialCredential>();
        var seen = new HashSet<string>();
        var errors = new List<string>(); var created = 0; var updated = 0;
        var hasher = new PasswordHasher<AppUser>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.FullName) || string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.ClassCode)) { errors.Add($"Dòng {row.Row}: thiếu Họ và tên, Email hoặc Mã lớp."); continue; }
            var email = row.Email.Trim().ToLowerInvariant();
            if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)) { errors.Add($"Dòng {row.Row}: email không hợp lệ."); continue; }
            if (!seen.Add(email)) { errors.Add($"Dòng {row.Row}: email bị trùng trong tệp."); continue; }
            if (row.FullName.Length > 200 || row.ClassCode.Length > 30 || email.Length > 256) { errors.Add($"Dòng {row.Row}: dữ liệu vượt độ dài cho phép."); continue; }
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
            if (user is null)
            {
                user = new AppUser { FullName = row.FullName.Trim(), Email = email, Role = UserRole.Student, ClassCode = row.ClassCode.Trim().ToUpperInvariant() };
                var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
                user.MustChangePassword = true; credentials.Add(new(email, password));
                user.PasswordHash = hasher.HashPassword(user, password); db.Users.Add(user); created++;
            }
            else if (user.Role != UserRole.Student) errors.Add($"Dòng {row.Row}: email đang thuộc tài khoản không phải học viên.");
            else { user.FullName = row.FullName.Trim(); user.ClassCode = row.ClassCode.Trim().ToUpperInvariant(); updated++; }
        }
        db.AuditEntries.Add(new() { ActorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), EntityId = Guid.Empty, Action = "students.import", Detail = System.Text.Json.JsonSerializer.Serialize(new { created, updated, failed = errors.Count }) });
        await db.SaveChangesAsync(ct);
        return Ok(new StudentImportResult(created, updated, errors.Count, errors, credentials));
    }
}

public sealed record StudentImportResult(int Created, int Updated, int Failed, IReadOnlyList<string> Errors, IReadOnlyList<InitialCredential> InitialCredentials);
file sealed record StudentImportRow(int Row, string FullName, string Email, string ClassCode);

file static class StudentXlsxReader
{
    public static IReadOnlyList<StudentImportRow> Read(Stream source)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read);
        if (zip.Entries.Count > 5000 || zip.Entries.Sum(x => x.Length) > 50 * 1024 * 1024) throw new InvalidDataException();
        var shared = ReadSharedStrings(zip);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException();
        using var xml = sheet.Open(); var doc = XDocument.Load(xml); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var result = new List<StudentImportRow>();
        foreach (var row in doc.Descendants(ns + "row").Skip(1))
        {
            var values = new Dictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? string.Empty; var column = ColumnIndex(reference);
                var raw = cell.Element(ns + "v")?.Value ?? cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? string.Empty;
                values[column] = (string?)cell.Attribute("t") == "s" && int.TryParse(raw, out var index) && index < shared.Count ? shared[index] : raw;
            }
            var number = (int?)row.Attribute("r") ?? result.Count + 2;
            var fullName = values.GetValueOrDefault(1, string.Empty); var email = values.GetValueOrDefault(2, string.Empty); var classCode = values.GetValueOrDefault(3, string.Empty);
            if (!string.IsNullOrWhiteSpace(fullName) || !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(classCode)) result.Add(new StudentImportRow(number, fullName, email, classCode));
        }
        return result;
    }
    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml"); if (entry is null) return [];
        using var xml = entry.Open(); var doc = XDocument.Load(xml); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return doc.Descendants(ns + "si").Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value))).ToList();
    }
    private static int ColumnIndex(string reference) { var letters = new string(reference.TakeWhile(char.IsLetter).ToArray()); var value = 0; foreach (var c in letters) value = value * 26 + c - 'A' + 1; return value; }
}

file static class StudentXlsxTemplate
{
    public static byte[] Create()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Write(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Write(zip, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Danh sách học viên" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Write(zip, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Write(zip, "xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF1F603D"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="center"/></xf></cellXfs></styleSheet>""");
            Write(zip, "xl/worksheets/sheet1.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols><col min="1" max="1" width="28" customWidth="1"/><col min="2" max="2" width="34" customWidth="1"/><col min="3" max="3" width="18" customWidth="1"/></cols><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetData><row r="1" ht="24" customHeight="1"><c r="A1" t="inlineStr" s="1"><is><t>Họ và tên</t></is></c><c r="B1" t="inlineStr" s="1"><is><t>Email</t></is></c><c r="C1" t="inlineStr" s="1"><is><t>Mã lớp</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>Nguyễn Văn A</t></is></c><c r="B2" t="inlineStr"><is><t>nguyenvana@example.edu.vn</t></is></c><c r="C2" t="inlineStr"><is><t>D01</t></is></c></row></sheetData><autoFilter ref="A1:C2"/></worksheet>""");
        }
        return output.ToArray();
    }
    private static void Write(ZipArchive zip, string path, string content) { var entry = zip.CreateEntry(path); using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)); writer.Write(content); }
}
