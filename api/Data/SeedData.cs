using EntornExamen.Api.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db, IConfiguration config)
    {
        var email    = config["Admin:Email"]    ?? "admin@autoco.cat";
        var password = config["Admin:Password"] ?? "Admin123!";
        var nom      = config["Admin:Nom"]      ?? "Administrador";
        var cognoms  = config["Admin:Cognoms"]  ?? "";

        var admin = await db.Professors.FirstOrDefaultAsync(p => p.IsAdmin);

        if (admin is null)
        {
            // Primera arrencada: crear l'admin amb les credencials del .env
            db.Professors.Add(new Professor
            {
                Email        = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Nom          = nom,
                Cognoms      = cognoms,
                IsAdmin      = true
            });
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Administrador creat: {email}");
        }
        else
        {
            var changed = false;

            // Si l'email del .env ha canviat, actualitzem-lo
            if (!string.Equals(admin.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                admin.Email = email;
                changed = true;
            }

            // Sempre verifiquem la contrasenya: si no coincideix amb el .env, la resetegem.
            // Això permet recuperar l'accés configurant el .env sense perdre canvis voluntaris
            // (si l'admin canvia la contrasenya via UI però el .env no canvia, en el pròxim
            // restart es tornarà a la del .env — comportament esperat per a comptes de servidor).
            if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
            {
                admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                changed = true;
            }

            if (admin.Nom != nom || admin.Cognoms != cognoms)
            {
                admin.Nom     = nom;
                admin.Cognoms = cognoms;
                changed = true;
            }

            if (changed)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"[Seed] Administrador sincronitzat: {email}");
            }
        }
    }
}
