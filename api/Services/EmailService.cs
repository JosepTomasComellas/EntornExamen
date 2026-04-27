using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EntornExamen.Api.Services;

public interface IEmailService
{
    bool IsEnabled { get; }
    Task<bool> SendStudentPasswordAsync(string toEmail, string toName, string className, string password);
    Task<bool> SendProfessorCredentialsAsync(string toEmail, string toName, string password);
    Task<bool> SendReminderAsync(string toEmail, string toName, string activityName, string className);
    Task<bool> SendActivityCompletedAsync(string toEmail, string toName, string activityName, string className, int total);
    Task<bool> SendPasswordResetAsync(string toEmail, string toName, string code);
}

public class EmailService(IConfiguration config, ILogger<EmailService> logger) : IEmailService
{
    private readonly string _host        = config["Smtp:Host"]        ?? "";
    private readonly int    _port        = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
    private readonly string _username    = config["Smtp:Username"]    ?? "";
    private readonly string _password    = config["Smtp:Password"]    ?? "";
    private readonly string _fromAddress = config["Smtp:FromAddress"] ?? "";
    private readonly string _fromName    = config["Smtp:FromName"]    ?? "Salesians de Sarrià";
    private readonly string _webUrl      = config["App:WebUrl"]       ?? "";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_fromAddress);

    public async Task<bool> SendStudentPasswordAsync(string toEmail, string toName, string className, string password)
    {
        if (!IsEnabled) return false;
        var url = string.IsNullOrWhiteSpace(_webUrl) ? "(URL no configurada)" : $"{_webUrl}/auth/login-alumne";
        var body = $"""
            Hola, {toName}!

            T'enviem les teves credencials d'accés al sistema d'avaluació.

            ─────────────────────────────────────
             DADES D'ACCÉS
            ─────────────────────────────────────
             Classe:       {className}
             Correu:       {toEmail}
             Contrasenya:  {password}
            ─────────────────────────────────────

            Accedeix aquí: {url}

            Si tens qualsevol problema, contacta amb el teu professor/a.

            Departament d'Informàtica · Salesians de Sarrià
            """;
        return await SendAsync(toEmail, toName, $"Credencials d'accés – {className}", body);
    }

    public async Task<bool> SendProfessorCredentialsAsync(string toEmail, string toName, string password)
    {
        if (!IsEnabled) return false;
        var url = string.IsNullOrWhiteSpace(_webUrl) ? "(URL no configurada)" : $"{_webUrl}/auth/login-professor";
        var body = $"""
            Hola, {toName}!

            T'enviem les teves credencials d'accés al sistema d'avaluació.

            ─────────────────────────────────────
             DADES D'ACCÉS
            ─────────────────────────────────────
             Correu:       {toEmail}
             Contrasenya:  {password}
            ─────────────────────────────────────

            Accedeix aquí: {url}

            Es recomana canviar la contrasenya després del primer accés.

            Departament d'Informàtica · Salesians de Sarrià
            """;
        return await SendAsync(toEmail, toName, "Credencials d'accés – AutoCo", body);
    }

    public async Task<bool> SendReminderAsync(string toEmail, string toName, string activityName, string className)
    {
        if (!IsEnabled) return false;
        var url = string.IsNullOrWhiteSpace(_webUrl) ? "(URL no configurada)" : $"{_webUrl}/auth/login-alumne";
        var body = $"""
            Hola, {toName}!

            Recordeu que teniu pendent omplir l'avaluació de l'activitat «{activityName}» a la classe {className}.

            Accedeix aquí: {url}

            Si ja has completat l'avaluació, ignora aquest missatge.

            Departament d'Informàtica · Salesians de Sarrià
            """;
        return await SendAsync(toEmail, toName, $"Recordatori: avaluació pendent – {activityName}", body);
    }

    public async Task<bool> SendActivityCompletedAsync(string toEmail, string toName,
        string activityName, string className, int total)
    {
        if (!IsEnabled) return false;
        var url = string.IsNullOrWhiteSpace(_webUrl) ? "(URL no configurada)" : $"{_webUrl}/professor/resultats";
        var body = $"""
            Hola, {toName}!

            Tots els {total} alumnes de l'activitat «{activityName}» ({className}) han completat la seva avaluació.

            Podeu consultar els resultats aquí: {url}

            Departament d'Informàtica · Salesians de Sarrià
            """;
        return await SendAsync(toEmail, toName, $"Avaluació completada – {activityName}", body);
    }

    public async Task<bool> SendPasswordResetAsync(string toEmail, string toName, string code)
    {
        if (!IsEnabled) return false;
        var body = $"""
            Hola, {toName}!

            Has sol·licitat restablir la teva contrasenya d'AutoCo.

            ─────────────────────────────────────
             CODI DE VERIFICACIÓ
            ─────────────────────────────────────
             {code}
            ─────────────────────────────────────

            Aquest codi és vàlid durant 15 minuts.
            Si no has sol·licitat aquest canvi, ignora aquest missatge.

            Departament d'Informàtica · Salesians de Sarrià
            """;
        return await SendAsync(toEmail, toName, "Restabliment de contrasenya – AutoCo", body);
    }

    private async Task<bool> SendAsync(string toEmail, string toName, string subject, string body)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _fromAddress));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;
            message.Body    = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(_host, _port, SecureSocketOptions.StartTls);
            if (!string.IsNullOrWhiteSpace(_username))
                await client.AuthenticateAsync(_username, _password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enviant correu a {Email}", toEmail);
            return false;
        }
    }
}
