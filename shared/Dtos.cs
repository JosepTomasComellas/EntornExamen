namespace EntornExamen.Shared.DTOs;

// ─── Autenticació ────────────────────────────────────────────────────────────
public record ProfessorLoginRequest(string Email, string Password);
public record LoginResponse(string Token, string NomComplet, string Role, int UserId);

// ─── Professors ──────────────────────────────────────────────────────────────
public record ProfessorDto(
    int Id, string Email, string Nom, string Cognoms, string NomComplet,
    bool IsAdmin, DateTime CreatedAt);

public record CreateProfessorRequest(
    string Email, string Nom, string Cognoms, bool IsAdmin, string? Password = null);

public record UpdateProfessorRequest(
    string Email, string Nom, string Cognoms, bool IsAdmin, string? NewPassword);

public record SendCredentialsResult(bool Sent, string? Reason);
public record SendAllResult(int Sent, int Skipped, List<string> Details);

// ─── Classes ─────────────────────────────────────────────────────────────────
public record ClassDto(
    int Id, string Name, string? AcademicYear, DateTime CreatedAt, int NumStudents);

public record CreateClassRequest(string Name, string? AcademicYear);
public record UpdateClassRequest(string Name, string? AcademicYear);

// ─── Alumnes ─────────────────────────────────────────────────────────────────
public record StudentDto(
    int Id, int ClassId, string Nom, string Cognoms, string NomComplet,
    int NumLlista, string Email, DateTime CreatedAt, string? FotoUrl = null);

public record CreateStudentRequest(
    string Nom, string Cognoms, int NumLlista, string Email);

public record UpdateStudentRequest(
    string Nom, string Cognoms, int NumLlista, string Email);

public record MoveStudentRequest(int TargetClassId);
public record BulkMoveStudentsRequest(List<int> StudentIds, int TargetClassId);

public record BulkCreateStudentsRequest(List<CreateStudentRequest> Students);
public record BulkCreateResult(int Created, int Skipped, List<string> Errors);

// ─── Backup / Restore ────────────────────────────────────────────────────────
public record BackupDto(
    string Version, DateTime CreatedAt,
    List<ProfessorBackupDto> Professors,
    List<ClassBackupDto>     Classes);

public record ProfessorBackupDto(
    int Id, string Email, string Nom, string Cognoms,
    bool IsAdmin, string? PasswordHash, DateTime CreatedAt);

public record ClassBackupDto(
    int Id, string Name, string? AcademicYear, DateTime CreatedAt,
    List<StudentBackupDto> Students);

public record StudentBackupDto(
    int Id, string Nom, string Cognoms, int NumLlista,
    string Email, string? Dni, DateTime CreatedAt);

public record BackupFileInfoDto(string Name, DateTime CreatedAt, long SizeBytes);

public record ImportResult(
    bool Success, string? Error,
    int Professors, int Classes, int Students);

// ─── Perfil professor (canvi propi) ──────────────────────────────────────────
public record UpdateOwnProfileRequest(string Nom, string Cognoms, string? CurrentPassword, string? NewPassword);

// ─── Reset de contrasenya (OTP per email) ────────────────────────────────────
public record PasswordResetRequestDto(string Email);
public record PasswordResetConfirmDto(string Email, string Code, string NewPassword);

// ─── Estadístiques d'administrador ───────────────────────────────────────────
public record AdminStatsDto(
    List<ProfessorStatsDto> Professors,
    List<MonthlyStatDto>    MonthlyLogins);

public record ProfessorStatsDto(
    int Id, string NomComplet, string Email, bool IsAdmin,
    int LoginsLast30,
    DateTime? LastAccess);

public record MonthlyStatDto(int Year, int Month, int Count);

// ═══════════════════════════════════════════════════════════════════════════
// ENTORN EXAMEN
// ═══════════════════════════════════════════════════════════════════════════

public enum EstatConnexioDto
{
    Connectat, SenseCheckin, Desconnectat, NoConnectat, Expulsat
}

// ─── Sessions d'examen ───────────────────────────────────────────────────────
public record SessioExamenDto(
    int Id, int ClassId, string ClassName, int ProfessorId, string ProfessorNom,
    string? Titol, string? Descripcio, string? MissatgeActiu,
    DateTime IniciadaAt, DateTime? TancadaAt, bool Activa,
    int TotalAlumnes, int AlumnesConnectats,
    int IntervalSegons = 30);

public record CreateSessioRequest(int ClassId, string? Titol, string? Descripcio);

public record MissatgeRequest(string Text);

// ─── Registres de connexió ───────────────────────────────────────────────────
public record RegistreConnexioDto(
    int Id, int SessioId,
    int? StudentId, string? StudentNom, string? StudentCognoms,
    string? StudentEmail, int? StudentNumLlista, string? FotoUrl,
    string MacAddress, string? IpAssignada,
    DateTime ConnectatAt, DateTime? DesconnectatAt, DateTime? UltimCheckinAt,
    EstatConnexioDto Estat,
    List<PeticioTdnsDto> DnsRecents);

public record PeticioTdnsDto(
    int Id, string Domini, DateTime Timestamp, bool EsExterna);

// ─── Check-in alumne ─────────────────────────────────────────────────────────
public record CheckinRequest(string Email);

public record CheckinResponse(
    CheckinAlumneInfo Alumne,
    CheckinSessioInfo Sessio);

public record CheckinAlumneInfo(
    int StudentId, string Nom, string Cognoms, string Classe, string? FotoUrl);

public record CheckinSessioInfo(
    int SessioId, string? Titol, string? Descripcio, string? MissatgeActiu,
    int IntervalSegons = 30);

// ─── Esdeveniments DHCP / DNS ────────────────────────────────────────────────
public record DhcpEventRequest(string Mac, string? Ip, string Event);  // "connected" | "disconnected"

public record DnsEventRequest(string Ip, string Domini, DateTime Timestamp);

// ─── Dashboard professor ─────────────────────────────────────────────────────
public record ExamenDashboardDto(
    SessioExamenDto Sessio,
    List<ExamenAlumneDto> Alumnes);

public record ExamenAlumneDto(
    int RegistreId,
    int? StudentId, string? Nom, string? Cognoms, string? Email,
    int? NumLlista, string? FotoUrl,
    string MacAddress, string? IpAssignada,
    DateTime ConnectatAt, DateTime? UltimCheckinAt, EstatConnexioDto Estat,
    List<PeticioTdnsDto> DnsRecents,
    long? BytesEnviats = null, int? NumRequestes = null);

// ─── Importació alumnes ───────────────────────────────────────────────────────
public record ImportacioAlumnesResult(
    int Importats, int Actualitzats, int Saltats, List<string> Errors);

public record ImportacioFotosResult(
    int Importades, List<string> NoTrobades, List<string> Errors);

// ─── Dispositius MAC ─────────────────────────────────────────────────────────
public record AlumneMacDto(
    int Id, int StudentId,
    string StudentNom, string StudentCognoms, string StudentEmail,
    string ClassName, string Mac, string? Dispositiu,
    DateTime PrimerCopVist, string? FotoUrl);

// ─── Diagnòstic integració (admin) ───────────────────────────────────────────
public record DiagnosticDto(
    DiagnosticFitxer Dhcp,
    DiagnosticFitxer Dns,
    DiagnosticBd     Bd);

public record DiagnosticFitxer(
    string Path, bool Exists, long Bytes,
    DateTime? Modified, string? LastLine, string? Error);

public record DiagnosticBd(
    int SessionsActives, int RegistresActius, DateTime? UltimCheckin);

// ─── Events SignalR (publicats per Redis) ─────────────────────────────────────
public record ExamenEventAlumne(
    int? StudentId, string? Nom, string? Cognoms,
    string? Ip, string Mac, DateTime Timestamp);

public record ExamenEventDns(
    int? StudentId, string? Nom, string Domini, DateTime Timestamp);

public record ExamenEventMissatge(string Text);
