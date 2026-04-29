using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntornExamen.Shared.DTOs;

namespace EntornExamen.Web.Services;

public class ApiClient
{
    private readonly HttpClient          _http;
    private readonly UserStateService    _userState;
    private readonly ExamenCircuitState  _circuitState;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ApiClient(HttpClient http, UserStateService userState, ExamenCircuitState circuitState)
    {
        _http         = http;
        _userState    = userState;
        _circuitState = circuitState;
    }

    public void SetToken(string token) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    // ── Auth ──────────────────────────────────────────────────────────────────

    public Task<LoginResponse?> LoginProfessorAsync(string email, string password) =>
        PostLoginAsync<LoginResponse>("/api/auth/professor", new ProfessorLoginRequest(email, password));

    // ── Classes ───────────────────────────────────────────────────────────────

    public Task<List<ClassDto>?> GetClassesAsync() =>
        GetAsync<List<ClassDto>>("/api/classes");

    public Task<ClassDto?> GetClassAsync(int id) =>
        GetAsync<ClassDto>($"/api/classes/{id}");

    public Task<ClassDto?> CreateClassAsync(CreateClassRequest req) =>
        PostAsync<ClassDto>("/api/classes", req);

    public Task<ClassDto?> UpdateClassAsync(int id, UpdateClassRequest req) =>
        PutAsync<ClassDto>($"/api/classes/{id}", req);

    public Task<bool> DeleteClassAsync(int id) =>
        DeleteAsync($"/api/classes/{id}");

    // ── Alumnes ───────────────────────────────────────────────────────────────

    public Task<List<StudentDto>?> GetStudentsAsync(int classId) =>
        GetAsync<List<StudentDto>>($"/api/classes/{classId}/students");

    public Task<StudentDto?> AddStudentAsync(int classId, CreateStudentRequest req) =>
        PostAsync<StudentDto>($"/api/classes/{classId}/students", req);

    public Task<StudentDto?> UpdateStudentAsync(int classId, int studentId, UpdateStudentRequest req) =>
        PutAsync<StudentDto>($"/api/classes/{classId}/students/{studentId}", req);

    public Task<bool> DeleteStudentAsync(int classId, int studentId) =>
        DeleteAsync($"/api/classes/{classId}/students/{studentId}");

    public Task<BulkCreateResult?> BulkAddStudentsAsync(int classId, BulkCreateStudentsRequest req) =>
        PostAsync<BulkCreateResult>($"/api/classes/{classId}/students/bulk", req);

    public Task<StudentDto?> MoveStudentAsync(int classId, int studentId, int targetClassId) =>
        PostAsync<StudentDto>($"/api/classes/{classId}/students/{studentId}/move",
            new MoveStudentRequest(targetClassId));

    public Task<SendCredentialsResult?> SendProfessorCredentialsAsync(int professorId) =>
        PostAsync<SendCredentialsResult>($"/api/professors/{professorId}/send-credentials", null);

    public Task<SendAllResult?> SendAllProfessorCredentialsAsync() =>
        PostAsync<SendAllResult>("/api/professors/send-all-credentials", null);

    // ── Professors (admin) ────────────────────────────────────────────────────

    public Task<List<ProfessorDto>?> GetProfessorsAsync() =>
        GetAsync<List<ProfessorDto>>("/api/professors");

    public Task<ProfessorDto?> CreateProfessorAsync(CreateProfessorRequest req) =>
        PostAsync<ProfessorDto>("/api/professors", req);

    public Task<ProfessorDto?> UpdateProfessorAsync(int id, UpdateProfessorRequest req) =>
        PutAsync<ProfessorDto>($"/api/professors/{id}", req);

    public Task<bool> DeleteProfessorAsync(int id) =>
        DeleteAsync($"/api/professors/{id}");

    // ── Perfil professor ──────────────────────────────────────────────────────

    public Task<ProfessorDto?> GetOwnProfileAsync() =>
        GetAsync<ProfessorDto>("/api/professors/me");

    public Task<ProfessorDto?> UpdateOwnProfileAsync(UpdateOwnProfileRequest req) =>
        PutAsync<ProfessorDto>("/api/professors/me", req);

    // ── Reset de contrasenya ──────────────────────────────────────────────────

    public Task<bool> RequestPasswordResetAsync(string email) =>
        PostNoContentAsync("/api/auth/request-reset", new PasswordResetRequestDto(email));

    public async Task<(bool Success, string? Error)> ConfirmPasswordResetAsync(
        string email, string code, string newPassword)
    {
        var resp = await _http.PostAsync("/api/auth/confirm-reset",
            Json(new PasswordResetConfirmDto(email, code, newPassword)));
        if (resp.IsSuccessStatusCode) return (true, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (false, err ?? "Error desconegut.");
        }
        catch { return (false, "Error desconegut."); }
    }

    // ── Entorn Examen ─────────────────────────────────────────────────────────

    public Task<List<SessioExamenDto>?> GetExamenSessionsAsync() =>
        GetAsync<List<SessioExamenDto>>("/api/examen/sessions");

    public async Task<(SessioExamenDto? Sessio, string? Error)> CreateExamenSessioAsync(
        CreateSessioRequest req)
    {
        var resp = await _http.PostAsync("/api/examen/sessions", Json(req));
        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<SessioExamenDto>(_json), null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, err ?? "Error desconegut.");
        }
        catch { return (null, "Error desconegut."); }
    }

    public Task<ExamenDashboardDto?> GetExamenDashboardAsync(int sessioId) =>
        GetAsync<ExamenDashboardDto>($"/api/examen/sessions/{sessioId}/dashboard");

    public Task<bool> TancarExamenSessioAsync(int sessioId) =>
        PutNoContentAsync($"/api/examen/sessions/{sessioId}/tancar", null);

    public Task<bool> EliminarExamenSessioAsync(int sessioId) =>
        DeleteAsync($"/api/examen/sessions/{sessioId}");

    public Task<bool> ReobrirExamenSessioAsync(int sessioId) =>
        PutNoContentAsync($"/api/examen/sessions/{sessioId}/reobrir", null);

    public Task<bool> SetMissatgeExamenAsync(int sessioId, string text) =>
        PutNoContentAsync($"/api/examen/sessions/{sessioId}/missatge",
            new MissatgeRequest(text));

    public Task<bool> DeleteMissatgeExamenAsync(int sessioId) =>
        DeleteAsync($"/api/examen/sessions/{sessioId}/missatge");

    public async Task<(byte[] Content, string FileName)?> ExportarExamenCsvAsync(int sessioId)
    {
        var resp = await _http.GetAsync($"/api/examen/sessions/{sessioId}/exportar");
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return null; }
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName
            ?? $"examen_{sessioId}.csv";
        return (bytes, fileName.Trim('"'));
    }

    public Task<bool> SortirExamenAsync() =>
        PostWithClientIpAsync("/api/examen/sortida");

    public Task<bool> SortirCircuitAsync(int studentId) =>
        PostNoContentAsync($"/api/examen/sortida-circuit/{studentId}", null);

    public Task<bool> ExpulsarAlumneAsync(int sessioId, int studentId) =>
        PostNoContentAsync($"/api/examen/sessions/{sessioId}/alumnes/{studentId}/expulsar", null);

    public async Task<(CheckinResponse? Resp, string? Error)> ExamenCheckinAsync(CheckinRequest req)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/examen/checkin") { Content = Json(req) };
        AfegirCapcaleraIp(msg);
        var resp = await _http.SendAsync(msg);
        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<CheckinResponse>(_json), null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, err ?? "Error desconegut.");
        }
        catch { return (null, "Error desconegut."); }
    }

    public async Task<(ImportacioAlumnesResult? Result, string? Error)>
        ImportarAlumnesExamenAsync(MultipartFormDataContent form)
    {
        var resp = await _http.PostAsync("/api/examen/importar-alumnes", form);
        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<ImportacioAlumnesResult>(_json), null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, err ?? "Error desconegut.");
        }
        catch { return (null, "Error desconegut."); }
    }

    public async Task<(ImportacioAlumnesResult? Result, string? Error)>
        ImportarAlumnesXlsAsync(int classId, MultipartFormDataContent form)
    {
        var resp = await _http.PostAsync($"/api/examen/importar-alumnes-xls?classId={classId}", form);
        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<ImportacioAlumnesResult>(_json), null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, err ?? "Error desconegut.");
        }
        catch { return (null, "Error desconegut."); }
    }

    public async Task<string?> UploadStudentFotoAsync(int classId, int studentId, MultipartFormDataContent form)
    {
        var resp = await _http.PostAsync($"/api/classes/{classId}/students/{studentId}/foto", form);
        if (!resp.IsSuccessStatusCode) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
        }
        catch { return null; }
    }

    public Task<List<AlumneMacDto>?> GetExamenMacsAsync() =>
        GetAsync<List<AlumneMacDto>>("/api/examen/macs");

    public Task<bool> DeleteExamenMacAsync(int id) =>
        DeleteAsync($"/api/examen/macs/{id}");

    public async Task<(ImportacioFotosResult? Result, string? Error)>
        ImportarFotosExamenAsync(MultipartFormDataContent form)
    {
        var resp = await _http.PostAsync("/api/examen/importar-fotos", form);
        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<ImportacioFotosResult>(_json), null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, err ?? "Error desconegut.");
        }
        catch { return (null, "Error desconegut."); }
    }

    // ── Recursos examen (admin) ───────────────────────────────────────────────

    public Task<List<RecursExamenDto>?> GetRecursosExamenAsync() =>
        GetAsync<List<RecursExamenDto>>("/api/admin/recursos");

    public Task<RecursExamenDto?> CreateRecursExamenAsync(CreateRecursRequest req) =>
        PostAsync<RecursExamenDto>("/api/admin/recursos", req);

    public Task<RecursExamenDto?> UpdateRecursExamenAsync(int id, UpdateRecursRequest req) =>
        PutAsync<RecursExamenDto>($"/api/admin/recursos/{id}", req);

    public Task<bool> DeleteRecursExamenAsync(int id) =>
        DeleteAsync($"/api/admin/recursos/{id}");

    public Task<List<RecursExamenDto>?> GetSessioRecursosAsync(int sessioId) =>
        GetAsync<List<RecursExamenDto>>($"/api/examen/sessions/{sessioId}/recursos");

    public Task<bool> SetSessioRecursosAsync(int sessioId, List<int> recursIds) =>
        PutNoContentAsync($"/api/examen/sessions/{sessioId}/recursos",
            new SetSessioRecursosRequest(recursIds));

    // ── Diagnòstic (admin) ────────────────────────────────────────────────────

    public Task<DiagnosticDto?> GetDiagnosticAsync() =>
        GetAsync<DiagnosticDto>("/api/admin/diagnostic");

    // ── Estadístiques (admin) ─────────────────────────────────────────────────

    public Task<AdminStatsDto?> GetAdminStatsAsync() =>
        GetAsync<AdminStatsDto>("/api/admin/stats");

    public Task<bool> DeleteAdminLoginsAsync() =>
        DeleteAsync("/api/admin/stats/logins");

    // ── Backup / Restore (admin) ──────────────────────────────────────────────

    public async Task<(byte[] Content, string FileName)?> ExportBackupAsync()
    {
        var resp = await _http.GetAsync("/api/admin/backup/export");
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return null; }
        var bytes    = await resp.Content.ReadAsByteArrayAsync();
        var fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName
            ?? $"entornexamen_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        return (bytes, fileName.Trim('"'));
    }

    public Task<ImportResult?> ImportBackupAsync(BackupDto backup) =>
        PostAsync<ImportResult>("/api/admin/backup/import", backup);

    public Task<List<BackupFileInfoDto>?> ListBackupFilesAsync() =>
        GetAsync<List<BackupFileInfoDto>>("/api/admin/backup/files");

    public Task<BackupFileInfoDto?> CreateBackupFileAsync() =>
        PostAsync<BackupFileInfoDto>("/api/admin/backup/files", null);

    public async Task<(byte[] Content, string FileName)?> DownloadBackupFileAsync(string name)
    {
        var resp = await _http.GetAsync($"/api/admin/backup/files/{Uri.EscapeDataString(name)}");
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return null; }
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        return (bytes, name);
    }

    public Task<bool> DeleteBackupFileAsync(string name) =>
        DeleteAsync($"/api/admin/backup/files/{Uri.EscapeDataString(name)}");

    public Task<ImportResult?> RestoreBackupFileAsync(string name) =>
        PostAsync<ImportResult>($"/api/admin/backup/files/{Uri.EscapeDataString(name)}/restore", null);

    public async Task<(byte[] Content, string FileName)?> ExportBackupZipAsync()
    {
        var resp = await _http.GetAsync("/api/admin/backup/export-zip");
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return null; }
        var bytes    = await resp.Content.ReadAsByteArrayAsync();
        var fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName
            ?? $"backup_complet_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
        return (bytes, fileName.Trim('"'));
    }

    public async Task<(ImportResult? Result, string? Error)> ImportBackupZipAsync(MultipartFormDataContent form)
    {
        var resp = await _http.PostAsync("/api/admin/backup/import-zip", form);
        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<ImportResult>(_json), null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, err ?? "Error desconegut.");
        }
        catch { return (null, "Error desconegut."); }
    }

    // ── Privats ───────────────────────────────────────────────────────────────

    private void CheckUnauthorized(HttpResponseMessage resp)
    {
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            _userState.SessionExpired();
    }

    private async Task<T?> PostLoginAsync<T>(string url, object? body)
    {
        var resp = await _http.PostAsync(url, Json(body));
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(_json);
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return default; }
        return await resp.Content.ReadFromJsonAsync<T>(_json);
    }

    private async Task<T?> PostAsync<T>(string url, object? body)
    {
        var resp = await _http.PostAsync(url, Json(body));
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return default; }
        return await resp.Content.ReadFromJsonAsync<T>(_json);
    }

    private async Task<bool> PostNoContentAsync(string url, object? body)
    {
        var resp = await _http.PostAsync(url, Json(body));
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return false; }
        return true;
    }

    // Reenvia la IP real del client cap a l'API (capturada per App.razor des de X-Real-IP de nginx).
    // Necessari perquè les crides web→api van de contenidor a contenidor i l'API veuria la IP del web.
    private void AfegirCapcaleraIp(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_circuitState.ClientIp))
            req.Headers.TryAddWithoutValidation("X-Forwarded-For", _circuitState.ClientIp);
    }

    private async Task<bool> PostWithClientIpAsync(string url)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, url);
        AfegirCapcaleraIp(msg);
        var resp = await _http.SendAsync(msg);
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return false; }
        return true;
    }

    private async Task<T?> PutAsync<T>(string url, object? body)
    {
        var resp = await _http.PutAsync(url, Json(body));
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return default; }
        return await resp.Content.ReadFromJsonAsync<T>(_json);
    }

    private async Task<bool> PutNoContentAsync(string url, object? body)
    {
        var resp = await _http.PutAsync(url, Json(body));
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return false; }
        return true;
    }

    private async Task<bool> DeleteAsync(string url)
    {
        var resp = await _http.DeleteAsync(url);
        if (!resp.IsSuccessStatusCode) { CheckUnauthorized(resp); return false; }
        return true;
    }

    private static StringContent Json(object? body) =>
        new(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
}
