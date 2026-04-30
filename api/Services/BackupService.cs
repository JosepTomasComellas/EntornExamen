using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
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
    Task<(byte[] Data, string Name)> ExportZipAsync();
    Task<ImportResult>           ImportZipAsync(Stream zipStream);
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
        var recursos   = await db.RecursosExamen.AsNoTracking().OrderBy(r => r.Ordre).ToListAsync();

        var classDtos = classes.Select(c => new ClassBackupDto(
            c.Id, c.Name, c.AcademicYear, c.CreatedAt,
            students.Where(s => s.ClassId == c.Id)
                .Select(s => new StudentBackupDto(
                    s.Id, s.Nom, s.Cognoms, s.NumLlista, s.Email, s.Dni, s.CreatedAt))
                .ToList()
        )).ToList();

        var recursosDtos = recursos.Select(r =>
            new RecursExamenBackupDto(r.Id, r.Icona, r.Etiqueta, r.Url, r.Ordre)).ToList();

        return new BackupDto("2.1", DateTime.UtcNow,
            professors.Select(p => new ProfessorBackupDto(
                p.Id, p.Email, p.Nom, p.Cognoms, p.IsAdmin, p.PasswordHash, p.CreatedAt)).ToList(),
            classDtos,
            recursosDtos);
    }

    // ── Import (substitueix totes les dades) ──────────────────────────────────
    public async Task<ImportResult> ImportAsync(BackupDto bk)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Esborrar en ordre de dependències FK
            await db.RegistresConnexio.ExecuteDeleteAsync();
            await db.SessioExamenRecursos.ExecuteDeleteAsync();
            await db.SessionsExamen.ExecuteDeleteAsync();
            await db.AlumneMacs.ExecuteDeleteAsync();
            await db.Students.ExecuteDeleteAsync();
            await db.Classes.ExecuteDeleteAsync();
            await db.ProfessorLogins.ExecuteDeleteAsync();
            await db.Professors.ExecuteDeleteAsync();
            await db.RecursosExamen.ExecuteDeleteAsync();

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

            // ── Recursos ──────────────────────────────────────────────────────
            if (bk.Recursos is { Count: > 0 })
            {
                db.RecursosExamen.AddRange(bk.Recursos.Select(r => new RecursExamen
                    { Icona = r.Icona, Etiqueta = r.Etiqueta, Url = r.Url, Ordre = r.Ordre }));
                await db.SaveChangesAsync();
            }

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
        var dir = new DirectoryInfo(BackupsDir);
        var files = dir.GetFiles("backup_*.zip")
            .Concat(dir.GetFiles("backup_*.json"))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupFileInfoDto(f.Name, f.LastWriteTimeUtc, f.Length))
            .ToList();
        return Task.FromResult(files);
    }

    public async Task<BackupFileInfoDto> CreateFileAsync()
    {
        EnsureDir();
        var (data, _) = await ExportZipAsync();
        var name = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
        var path = Path.Combine(BackupsDir, name);
        await File.WriteAllBytesAsync(path, data);
        var info = new FileInfo(path);
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

        if (name.EndsWith(".zip"))
        {
            await using var stream = File.OpenRead(path);
            return await ImportZipAsync(stream);
        }

        var json   = await File.ReadAllTextAsync(path);
        var backup = JsonSerializer.Deserialize<BackupDto>(json, _json);
        if (backup is null)
            return new ImportResult(false, "Format invàlid", 0, 0, 0);
        return await ImportAsync(backup);
    }

    // ── ZIP complet (JSON + fotos) ─────────────────────────────────────────────
    public async Task<(byte[] Data, string Name)> ExportZipAsync()
    {
        var backup    = await ExportAsync();
        var json      = JsonSerializer.Serialize(backup, _json);
        var wwwroot   = cfg["Examen:WebWwwrootPath"] ?? "/app/wwwroot";
        var fotosDir  = Path.Combine(wwwroot, "fotos", "alumnes");
        var name      = $"backup_complet_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

        using var ms  = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            {
                var entry = zip.CreateEntry("backup.json", CompressionLevel.Optimal);
                await using var sw = new StreamWriter(entry.Open());
                await sw.WriteAsync(json);
            }

            if (Directory.Exists(fotosDir))
            {
                foreach (var foto in Directory.GetFiles(fotosDir, "*.jpg"))
                {
                    var entryName = $"fotos/alumnes/{Path.GetFileName(foto)}";
                    var fe = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var fs = File.OpenRead(foto);
                    await using var fes = fe.Open();
                    await fs.CopyToAsync(fes);
                }
            }
        }
        return (ms.ToArray(), name);
    }

    public async Task<ImportResult> ImportZipAsync(Stream zipStream)
    {
        var wwwroot  = cfg["Examen:WebWwwrootPath"] ?? "/app/wwwroot";
        var fotosDir = Path.Combine(wwwroot, "fotos", "alumnes");
        Directory.CreateDirectory(fotosDir);

        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Primer importem el JSON
        var jsonEntry = zip.GetEntry("backup.json");
        if (jsonEntry is null) return new ImportResult(false, "El ZIP no conté backup.json", 0, 0, 0);

        BackupDto? backup;
        try
        {
            await using var stream = jsonEntry.Open();
            backup = await JsonSerializer.DeserializeAsync<BackupDto>(stream, _json);
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"Error llegint backup.json: {ex.Message}", 0, 0, 0);
        }
        if (backup is null) return new ImportResult(false, "Format invàlid", 0, 0, 0);

        var result = await ImportAsync(backup);
        if (!result.Success) return result;

        // Després restaurem les fotos
        var fotosRestaurades = 0;
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("fotos/alumnes/") || !entry.Name.EndsWith(".jpg")) continue;
            var destPath = Path.Combine(fotosDir, entry.Name);
            try
            {
                await using var src  = entry.Open();
                await using var dest = File.Create(destPath);
                await src.CopyToAsync(dest);
                fotosRestaurades++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No s'ha pogut restaurar la foto {Nom}", entry.Name);
            }
        }
        logger.LogInformation("Backup ZIP restaurat: {Fotos} fotos", fotosRestaurades);
        return result;
    }

    private string? SafePath(string name)
    {
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..")) return null;
        if (!name.StartsWith("backup_")) return null;
        if (!name.EndsWith(".json") && !name.EndsWith(".zip")) return null;
        return Path.Combine(BackupsDir, name);
    }
}
