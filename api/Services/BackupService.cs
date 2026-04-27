using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntornExamen.Api.Services;

public interface IBackupService
{
    Task<BackupDto>              ExportAsync();
    Task<ImportResult>           ImportAsync(BackupDto backup);
    Task<List<BackupFileInfoDto>> ListFilesAsync();
    Task<BackupFileInfoDto>      CreateFileAsync();
    Task<(byte[] Data, string Name)?> DownloadFileAsync(string name);
    Task<bool>                   DeleteFileAsync(string name);
    Task<ImportResult>           RestoreFileAsync(string name);
}

public class BackupService(AppDbContext db, IConfiguration cfg, ILogger<BackupService> logger) : IBackupService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented             = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition    = JsonIgnoreCondition.WhenWritingNull
    };

    private string BackupsDir => cfg["Backup:Path"] ?? "/app/backups";

    private void EnsureDir() => Directory.CreateDirectory(BackupsDir);

    // ── Export ────────────────────────────────────────────────────────────────
    public async Task<BackupDto> ExportAsync()
    {
        var professors = await db.Professors.AsNoTracking().ToListAsync();
        var classes    = await db.Classes.AsNoTracking().ToListAsync();
        var students   = await db.Students.AsNoTracking().ToListAsync();

        var classDtos = classes.Select(c => new ClassBackupDto(
            c.Id, c.Name, c.AcademicYear, c.CreatedAt,
            students.Where(s => s.ClassId == c.Id)
                .Select(s => new StudentBackupDto(
                    s.Id, s.Nom, s.Cognoms, s.NumLlista, s.Email, s.Dni, s.CreatedAt))
                .ToList()
        )).ToList();

        return new BackupDto("2.0", DateTime.UtcNow,
            professors.Select(p => new ProfessorBackupDto(
                p.Id, p.Email, p.Nom, p.Cognoms, p.IsAdmin, p.PasswordHash, p.CreatedAt)).ToList(),
            classDtos);
    }

    // ── Import (substitueix totes les dades) ──────────────────────────────────
    public async Task<ImportResult> ImportAsync(BackupDto bk)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Esborrar en ordre de dependències FK
            await db.RegistresConnexio.ExecuteDeleteAsync();
            await db.SessionsExamen.ExecuteDeleteAsync();
            await db.AlumneMacs.ExecuteDeleteAsync();
            await db.Students.ExecuteDeleteAsync();
            await db.Classes.ExecuteDeleteAsync();
            await db.ProfessorLogins.ExecuteDeleteAsync();
            await db.Professors.ExecuteDeleteAsync();

            // ── Professors ────────────────────────────────────────────────────
            var profEnts = bk.Professors.Select(p => new Professor
            {
                Email = p.Email, Nom = p.Nom, Cognoms = p.Cognoms,
                IsAdmin = p.IsAdmin, PasswordHash = p.PasswordHash ?? "", CreatedAt = p.CreatedAt
            }).ToList();
            db.Professors.AddRange(profEnts);
            await db.SaveChangesAsync();

            // ── Classes ───────────────────────────────────────────────────────
            var classEnts = bk.Classes.Select(c => new Class
                { Name = c.Name, AcademicYear = c.AcademicYear, CreatedAt = c.CreatedAt }).ToList();
            db.Classes.AddRange(classEnts);
            await db.SaveChangesAsync();

            // ── Students ──────────────────────────────────────────────────────
            int totalStudents = 0;
            for (int ci = 0; ci < bk.Classes.Count; ci++)
            {
                foreach (var s in bk.Classes[ci].Students)
                {
                    db.Students.Add(new Student
                    {
                        ClassId   = classEnts[ci].Id, Nom = s.Nom, Cognoms = s.Cognoms,
                        NumLlista = s.NumLlista, Email = s.Email, Dni = s.Dni,
                        CreatedAt = s.CreatedAt
                    });
                    totalStudents++;
                }
            }
            await db.SaveChangesAsync();

            await tx.CommitAsync();

            return new ImportResult(true, null,
                bk.Professors.Count, bk.Classes.Count, totalStudents);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Error important backup");
            return new ImportResult(false, "Error intern en importar el backup.", 0, 0, 0);
        }
    }

    // ── Fitxers de còpia ──────────────────────────────────────────────────────
    public Task<List<BackupFileInfoDto>> ListFilesAsync()
    {
        EnsureDir();
        var files = new DirectoryInfo(BackupsDir)
            .GetFiles("backup_*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupFileInfoDto(f.Name, f.LastWriteTimeUtc, f.Length))
            .ToList();
        return Task.FromResult(files);
    }

    public async Task<BackupFileInfoDto> CreateFileAsync()
    {
        EnsureDir();
        var backup = await ExportAsync();
        var name   = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var path   = Path.Combine(BackupsDir, name);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup, _json));
        var info   = new FileInfo(path);
        return new BackupFileInfoDto(name, info.LastWriteTimeUtc, info.Length);
    }

    public async Task<(byte[] Data, string Name)?> DownloadFileAsync(string name)
    {
        var path = SafePath(name);
        if (path is null || !File.Exists(path)) return null;
        return (await File.ReadAllBytesAsync(path), name);
    }

    public Task<bool> DeleteFileAsync(string name)
    {
        var path = SafePath(name);
        if (path is null || !File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public async Task<ImportResult> RestoreFileAsync(string name)
    {
        var path = SafePath(name);
        if (path is null || !File.Exists(path))
            return new ImportResult(false, "Fitxer no trobat", 0, 0, 0);
        var json   = await File.ReadAllTextAsync(path);
        var backup = JsonSerializer.Deserialize<BackupDto>(json, _json);
        if (backup is null)
            return new ImportResult(false, "Format invàlid", 0, 0, 0);
        return await ImportAsync(backup);
    }

    private string? SafePath(string name)
    {
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..")) return null;
        if (!name.StartsWith("backup_") || !name.EndsWith(".json")) return null;
        return Path.Combine(BackupsDir, name);
    }
}
