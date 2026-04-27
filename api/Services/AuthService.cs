using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EntornExamen.Api.Data;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EntornExamen.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> ProfessorLoginAsync(ProfessorLoginRequest req);
}

public class AuthService(AppDbContext db, IConfiguration config) : IAuthService
{
    public async Task<LoginResponse?> ProfessorLoginAsync(ProfessorLoginRequest req)
    {
        var professor = await db.Professors
            .FirstOrDefaultAsync(p => p.Email == req.Email.Trim().ToLower());
        if (professor is null || !PasswordHelper.Verify(req.Password, professor.PasswordHash))
            return null;

        db.ProfessorLogins.Add(new EntornExamen.Api.Data.Models.ProfessorLogin { ProfessorId = professor.Id });
        await db.SaveChangesAsync();

        var role  = professor.IsAdmin ? "Admin" : "Professor";
        var token = GenerateToken(professor.Id.ToString(), professor.NomComplet, role);
        return new LoginResponse(token, professor.NomComplet, role, professor.Id);
    }

    private string GenerateToken(string userId, string nomComplet, string role, params Claim[] extraClaims)
    {
        var secret = config["JwtSettings:Secret"]!;
        var hours  = int.TryParse(config["JwtSettings:ExpiryHours"], out var h) ? h : 8;
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name,           nomComplet),
            new(ClaimTypes.Role,           role)
        };
        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            claims: claims, expires: DateTime.UtcNow.AddHours(hours),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
