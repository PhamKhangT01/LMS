using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using LawLms.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LawLms.Api.Controllers;

public sealed partial class QuestionBankController
{
    [HttpPost("import-preview"), Authorize(Roles="Lecturer"), RequestSizeLimit(11*1024*1024)]
    public async Task<IActionResult> PreviewDocument([FromForm] Guid courseId,[FromForm] IFormFile? file,CancellationToken ct)
    {
        var format=Path.GetExtension(file?.FileName??"").ToLowerInvariant();
        if(file is null || file.Length is <22 or >10*1024*1024 || format is not (".docx" or ".pdf"))return ValidationProblem("Chọn DOCX hoặc PDF có nội dung, tối đa 10 MB.");
        var name=Path.GetFileName(file.FileName.Replace('\\','/'));
        await service.CheckImportAsync(new(courseId,name,format[1..],[]),Actor,ct);
        var lines=new List<DocumentLine>();var warnings=new List<string>();
        try
        {
            await using var source=file.OpenReadStream();
            if(format==".docx")
            {
                using var zip=new ZipArchive(source,ZipArchiveMode.Read);
                if(zip.Entries.Count>2000||zip.Entries.Sum(x=>x.Length)>50*1024*1024)throw new InvalidDataException();
                var entry=zip.GetEntry("word/document.xml")??throw new InvalidDataException();
                if(entry.Length>20*1024*1024)throw new InvalidDataException();
                await using var xml=entry.Open();using var reader=XmlReader.Create(xml,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024});
                var root=XDocument.Load(reader);XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var page=1;
                foreach(var p in root.Descendants(w+"p"))
                {
                    var text=string.Concat(p.Descendants().Select(e=>e.Name==w+"t"?e.Value:e.Name==w+"tab"?"\t":e.Name==w+"br"?"\n":""));
                    foreach(var line in text.Split('\n'))lines.Add(new(page,line));
                    page+=p.Descendants(w+"br").Count(e=>(string?)e.Attribute(w+"type")=="page")+p.Descendants(w+"lastRenderedPageBreak").Count();
                }
                if(root.Descendants(w+"drawing").Any()||root.Descendants(w+"pict").Any())warnings.Add("Tệp có hình ảnh. Phần xem trước chỉ lấy chữ; kiểm tra hình trong tệp gốc trước khi nhập.");
                warnings.Add("Vị trí DOCX được suy ra từ ngắt trang, có thể khác trang hiển thị trong Word.");
            }
            else
            {
                using var memory=new MemoryStream();await source.CopyToAsync(memory,ct);using var pdf=PdfDocument.Open(memory.ToArray());
                if(pdf.NumberOfPages>100)return ValidationProblem("PDF tối đa 100 trang. Hãy chia tệp.");
                foreach(var page in pdf.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    var text=ContentOrderTextExtractor.GetText(page);
                    if(string.IsNullOrWhiteSpace(text))warnings.Add($"Trang {page.Number} không có lớp chữ. PDF scan cần OCR trước khi nhập.");
                    foreach(var line in text.Split('\n'))lines.Add(new(page.Number,line));
                    if(lines.Sum(x=>x.Text.Length)>1_000_000)throw new InvalidDataException();
                }
                warnings.Add("PDF chỉ trích xuất lớp chữ, không nhận hình/bảng hay suy đoán đáp án gạch chân. Đối chiếu bản gốc trước khi nhập.");
            }
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex) when(ex is not LawLms.Application.UnauthorizedAppException)
        {return ValidationProblem("Không đọc được tệp. Kiểm tra đúng định dạng, tệp không mã hóa/hỏng và không vượt giới hạn giải nén.");}
        var questions=QuestionDocumentParser.Parse(lines);
        if(questions.Count==0)return ValidationProblem(warnings.Any(x=>x.Contains("OCR"))?"PDF không có câu hỏi đọc được từ lớp chữ. Hãy OCR hoặc chuyển sang DOCX trước khi nhập.":"Không nhận diện được câu hỏi. Dùng đánh số Câu 1:, Question 1: và phương án A., B., …");
        return Ok(new{fileName=name,format=format[1..],questions,warnings,text=string.Join("\n",lines.Select(x=>$"[Trang {x.Page}] {x.Text}"))});
    }
    [HttpPost("import-check"),Authorize(Roles="Lecturer")]
    public async Task<IActionResult> CheckDocumentQuestions(BankDocumentCommand command,CancellationToken ct)=>Ok(await service.CheckImportAsync(command,Actor,ct));
    [HttpPost("import-confirm"),Authorize(Roles="Lecturer")]
    public async Task<IActionResult> ConfirmDocumentQuestions(BankDocumentCommand command,CancellationToken ct)=>Ok(await service.ImportDocumentAsync(command,Actor,ct));
}
