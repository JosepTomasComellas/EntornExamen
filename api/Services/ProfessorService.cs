using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Services;

public interface IProfessorService
{
    Task<List<ProfessorDto>> GetAllAsync();
    Task<ProfessorDto?>      GetByIdAsync(int id);
    Task<ProfessorDto>       CreateAsync(CreateProfessorRequest req);
    Task<ProfessorDto?>      UpdateAsync(int id, UpdateProfessorRequest req);
    Task<bool>               DeleteAsync(int id);
    Task<SendCredentialsResult>  SendCredentialsAsync(int professorId);
    Task<SendAllResult>          SendAllCredentialsAsync();
}

public class ProfessorService(AppDbContext db, IEmailService email) : IProfessorService
{
    public async Task<List<ProfessorDto>> GetAllAsync() =>
        await db.Professors.OrderBy(p => p.Cognoms).ThenBy(p => p.Nom)
            .Select(p => ToDto(p)).ToListAsync();

    public async Task<ProfessorDto?> GetByIdAsync(int id)
    {
        var p = await db.Professors.FindAsync(id);
        return p is null ? null : ToDto(p);
    }

    public async Task<ProfessorDto> CreateAsync(CreateProfessorRequest req)
    {
        var password = PasswordHelper.Generate();
        var professor = new Professor
        {
            Email        = req.Email.Trim().ToLower(),
            PasswordHash = PasswordHelper.Hash(password),
            Nom          = req.Nom.Trim(),
            Cognoms      = req.Cognoms.Trim(),
            IsAdmin      = req.IsAdmin
        };
        db.Professors.Add(professor);
        await db.SaveChangesAsync();
        return ToDto(professor);
    }

    public async Task<ProfessorDto?> UpdateAsync(int id, UpdateProfessorRequest req)
    {
        var professor = await db.Professors.FindAsync(id);
        if (professor is null) return null;

        professor.Email   = req.Email.Trim().ToLower();
        professor.Nom     = req.Nom.Trim();
        professor.Cognoms = req.Cognoms.Trim();
        professor.IsAdmin = req.IsAdmin;

        if (!string.IsNullOrWhiteSpace(req.NewPassword))
            professor.PasswordHash = PasswordHelper.Hash(req.NewPassword);

        await db.SaveChangesAsync();
        return ToDto(professor);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var professor = await db.Professors.FindAsync(id);
        if (professor is null) return false;
        if (professor.IsAdmin && await db.Professors.CountAsync(p => p.IsAdmin) <= 1)
            throw new InvalidOperationException("No es pot eliminar l'únic administrador.");
        db.Professors.Remove(professor);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<SendCredentialsResult> SendCredentialsAsync(int professorId)
    {
        var professor = await db.Professors.FindAsync(professorId);
        if (professor is null) return new SendCredentialsResult(false, "Professor no trobat.");
        if (!email.IsEnabled) return new SendCredentialsResult(false, "Correu no configurat.");

        var newPassword = PasswordHelper.Generate();
        professor.PasswordHash = PasswordHelper.Hash(newPassword);
        await db.SaveChangesAsync();

        var sent = await email.SendProfessorCredentialsAsync(professor.Email, professor.NomComplet, newPassword);
        return new SendCredentialsResult(sent, sent ? null : "Error en l'enviament.");
    }

    public async Task<SendAllResult> SendAllCredentialsAsync()
    {
        var professors = await db.Professors.ToListAsync();
        int sent = 0, skipped = 0;
        var details = new List<string>();

        // Genera i desa tots els nous passwords d'un sol cop (evita N+1 SaveChanges)
        var passwords = new Dictionary<int, string>(professors.Count);
        foreach (var p in professors)
        {
            var newPassword = PasswordHelper.Generate();
            p.PasswordHash = PasswordHelper.Hash(newPassword);
            passwords[p.Id] = newPassword;
        }
        await db.SaveChangesAsync();

        foreach (var p in professors)
        {
            if (!email.IsEnabled) { skipped++; continue; }
            var ok = await email.SendProfessorCredentialsAsync(p.Email, p.NomComplet, passwords[p.Id]);
            if (ok) sent++; else { skipped++; details.Add($"{p.NomComplet}: error."); }
        }
        return new SendAllResult(sent, skipped, details);
    }

    private static ProfessorDto ToDto(Professor p) => new(
        p.Id, p.Email, p.Nom, p.Cognoms, p.NomComplet, p.IsAdmin, p.CreatedAt);

}
