using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController,Authorize(Roles="Lecturer,DepartmentHead,Admin"),Route("api/question-bank")]
public sealed class QuestionBankController(IQuestionBankService service) : ControllerBase
{
    private Actor Actor=>new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!));
    [HttpGet("courses")] public async Task<IActionResult> Courses(CancellationToken ct)=>Ok(await service.CoursesAsync(Actor,ct));
    [HttpGet] public async Task<IActionResult> Search([FromQuery] BankFilter filter,CancellationToken ct)=>Ok(await service.SearchAsync(filter,Actor,ct));
    [HttpPost("essay")] public async Task<IActionResult> Create(BankEssay command,CancellationToken ct)=>Ok(new{id=await service.CreateEssayAsync(command,Actor,ct)});
    [HttpPut("{id:guid}")] public async Task<IActionResult> Edit(Guid id,BankEdit command,CancellationToken ct){await service.EditAsync(id,command,Actor,ct);return NoContent();}
    [HttpPut("{id:guid}/archive")] public async Task<IActionResult> Archive(Guid id,BankArchive command,CancellationToken ct){await service.ArchiveAsync(id,command,Actor,ct);return NoContent();}
    [HttpPost("draw")] public async Task<IActionResult> Draw(BankDraw command,CancellationToken ct)=>Ok(await service.DrawAsync(command,Actor,ct));
    [HttpPost("from-quiz/{id:guid}")] public async Task<IActionResult> FromQuiz(Guid id,BankFromQuiz command,CancellationToken ct)=>Ok(await service.FromQuizAsync(id,command,Actor,ct));
    [HttpPost("import-docx"),Authorize(Roles="Lecturer"),RequestSizeLimit(10*1024*1024)]
    public async Task<IActionResult> Import([FromForm] Guid courseId,[FromForm] string? topic,[FromForm] QuestionDifficulty difficulty,[FromForm] IFormFile? file,CancellationToken ct)
    {
        if(file is null||file.Length<22||file.Length>10*1024*1024||!string.Equals(Path.GetExtension(file.FileName),".docx",StringComparison.OrdinalIgnoreCase))return ValidationProblem("Chọn tệp DOCX, tối đa 10 MB.");
        IReadOnlyList<ImportedQuizQuestionDto> questions;
        try
        {
            await using var source=file.OpenReadStream();using var zip=new ZipArchive(source,ZipArchiveMode.Read);
            if(zip.Entries.Count>2000||zip.Entries.Sum(x=>x.Length)>50*1024*1024)throw new InvalidDataException();
            var entry=zip.GetEntry("word/document.xml")??throw new InvalidDataException();
            if(entry.Length>20*1024*1024)throw new InvalidDataException();
            await using var xml=entry.Open();var root=XDocument.Load(xml);XNamespace word="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var lines=root.Descendants(word+"p").Select(p=>string.Concat(p.Descendants(word+"t").Select(t=>t.Value)).Trim()).Where(x=>x.Length>0).ToList();
            questions=QuizDocxParser.Parse(lines);
            var declared=lines.Count(line=>System.Text.RegularExpressions.Regex.IsMatch(line,@"^(Câu|Question)\s*\d+\s*[:.\-]",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            if(questions.Count!=declared)return ValidationProblem("Có câu hỏi thiếu phương án hoặc đáp án. Kiểm tra toàn bộ tệp trước khi nhập lại.");
        }
        catch(Exception error) when(error is InvalidDataException or System.Xml.XmlException or ArgumentOutOfRangeException or EndOfStreamException){return ValidationProblem("Nội dung DOCX không hợp lệ hoặc vượt giới hạn giải nén.");}
        if(questions.Count==0)return ValidationProblem("Không đọc được câu hỏi. Dùng mẫu Câu 1:, A., B., … và Đáp án: A.");
        return Ok(await service.ImportAsync(courseId,topic??"",difficulty,questions.Select(x=>new CreateQuizQuestionCommand(x.Content,QuestionType.SingleChoice,x.Options,x.CorrectAnswer,1)).ToList(),BankQuestionSource.Docx,Actor,ct));
    }
}
