using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Services;

public interface IClassService
{
    Task<List<ClassDto>>  GetAllAsync();
    Task<ClassDto?>       GetByIdAsync(int id);
    Task<ClassDto>        CreateAsync(CreateClassRequest req);
    Task<ClassDto?>       UpdateAsync(int id, UpdateClassRequest req);
    Task<bool>            DeleteAsync(int id);

    Task<List<StudentDto>>  GetStudentsAsync(int classId);
    Task<StudentDto>        AddStudentAsync(int classId, CreateStudentRequest req);
    Task<StudentDto?>       UpdateStudentAsync(int classId, int studentId, UpdateStudentRequest req);
    Task<bool>              DeleteStudentAsync(int classId, int studentId);
    Task<StudentDto?>       MoveStudentAsync(int classId, int studentId, int targetClassId);
    Task<BulkCreateResult>  BulkAddStudentsAsync(int classId, BulkCreateStudentsRequest req);
}

public class ClassService(AppDbContext db, IWebHostEnvironment env) : IClassService
{
    // ── Classes ──────────────────────────────────────────────────────────────

    public async Task<List<ClassDto>> GetAllAsync() =>
        await db.Classes
            .Include(c => c.Students)
            .OrderBy(c => c.Name)
            .Select(c => ToClassDto(c))
            .ToListAsync();

    public async Task<ClassDto?> GetByIdAsync(int id)
    {
        var c = await db.Classes
            .Include(c => c.Students)
            .FirstOrDefaultAsync(c => c.Id == id);
        return c is null ? null : ToClassDto(c);
    }

    public async Task<ClassDto> CreateAsync(CreateClassRequest req)
    {
        var classe = new Class { Name = req.Name.Trim(), AcademicYear = req.AcademicYear?.Trim() };
        db.Classes.Add(classe);
        await db.SaveChangesAsync();
        return ToClassDto(classe);
    }

    public async Task<ClassDto?> UpdateAsync(int id, UpdateClassRequest req)
    {
        var c = await db.Classes.Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == id);
        if (c is null) return null;
        c.Name = req.Name.Trim();
        c.AcademicYear = req.AcademicYear?.Trim();
        await db.SaveChangesAsync();
        return ToClassDto(c);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var c = await db.Classes.FindAsync(id);
        if (c is null) return false;
        db.Classes.Remove(c);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Alumnes ──────────────────────────────────────────────────────────────

    public async Task<List<StudentDto>> GetStudentsAsync(int classId)
    {
        var list = await db.Students.Where(s => s.ClassId == classId)
            .OrderBy(s => s.NumLlista)
            .ToListAsync();
        return list.Select(ToStudentDto).ToList();
    }

    public async Task<StudentDto> AddStudentAsync(int classId, CreateStudentRequest req)
    {
        var student = new Student
        {
            ClassId   = classId,
            NumLlista = req.NumLlista,
            Nom       = req.Nom.Trim(),
            Cognoms   = req.Cognoms.Trim(),
            Email     = req.Email.Trim().ToLower()
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return ToStudentDto(student);
    }

    public async Task<StudentDto?> UpdateStudentAsync(int classId, int studentId, UpdateStudentRequest req)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId);
        if (student is null) return null;
        student.Nom      = req.Nom.Trim();
        student.Cognoms  = req.Cognoms.Trim();
        student.NumLlista = req.NumLlista;
        student.Email    = req.Email.Trim().ToLower();
        await db.SaveChangesAsync();
        return ToStudentDto(student);
    }

    public async Task<StudentDto?> MoveStudentAsync(int classId, int studentId, int targetClassId)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId);
        if (student is null) return null;

        var targetExists = await db.Classes.AnyAsync(c => c.Id == targetClassId);
        if (!targetExists) return null;

        student.ClassId = targetClassId;
        await db.SaveChangesAsync();
        return ToStudentDto(student);
    }

    public async Task<bool> DeleteStudentAsync(int classId, int studentId)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId);
        if (student is null) return false;

        db.Students.Remove(student);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<BulkCreateResult> BulkAddStudentsAsync(int classId, BulkCreateStudentsRequest req)
    {
        int created = 0, skipped = 0;
        var errors  = new List<string>();
        var batchEmails = new HashSet<string>();

        foreach (var s in req.Students)
        {
            if (string.IsNullOrWhiteSpace(s.Nom) || string.IsNullOrWhiteSpace(s.Cognoms) ||
                string.IsNullOrWhiteSpace(s.Email))
            {
                errors.Add($"Alumne #{s.NumLlista}: camps obligatoris buits.");
                skipped++; continue;
            }

            var emailNorm = s.Email.Trim().ToLower();

            if (!System.Text.RegularExpressions.Regex.IsMatch(emailNorm, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                errors.Add($"Alumne #{s.NumLlista}: format de correu invàlid ({emailNorm}).");
                skipped++; continue;
            }

            // Comprova duplicats dins el CSV i a la BD
            if (batchEmails.Contains(emailNorm) || await db.Students.AnyAsync(x => x.Email == emailNorm))
            {
                errors.Add($"Alumne #{s.NumLlista}: correu duplicat omès ({emailNorm}).");
                skipped++; continue;
            }
            batchEmails.Add(emailNorm);

            var student  = new Student
            {
                ClassId   = classId,
                NumLlista = s.NumLlista,
                Nom       = s.Nom.Trim(),
                Cognoms   = s.Cognoms.Trim(),
                Email     = emailNorm
            };
            db.Students.Add(student);
            created++;
        }

        if (created > 0)
        {
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("IX_Students_Email") == true)
            {
                errors.Add("Error en guardar: algun correu ja existia a la base de dades.");
                return new BulkCreateResult(0, skipped + created, errors);
            }

        }

        return new BulkCreateResult(created, skipped, errors);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClassDto ToClassDto(Class c) => new(
        c.Id, c.Name, c.AcademicYear, c.CreatedAt, c.Students.Count);

    private StudentDto ToStudentDto(Student s) => new(
        s.Id, s.ClassId, s.Nom, s.Cognoms, s.NomComplet, s.NumLlista, s.Email, s.CreatedAt,
        FotoUrlSiExisteix(s.Id));

    private string? FotoUrlSiExisteix(int studentId)
    {
        var path = Path.Combine(env.WebRootPath, "fotos", "alumnes", $"{studentId}.jpg");
        return File.Exists(path) ? $"/fotos/alumnes/{studentId}.jpg" : null;
    }

}
