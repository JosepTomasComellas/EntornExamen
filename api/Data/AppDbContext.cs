using EntornExamen.Api.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EntornExamen.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Professor>         Professors        => Set<Professor>();
    public DbSet<Class>             Classes           => Set<Class>();
    public DbSet<Student>           Students          => Set<Student>();
    public DbSet<ProfessorLogin>    ProfessorLogins   => Set<ProfessorLogin>();
    public DbSet<AlumneMac>         AlumneMacs        => Set<AlumneMac>();
    public DbSet<SessioExamen>      SessionsExamen    => Set<SessioExamen>();
    public DbSet<RegistreConnexio>  RegistresConnexio => Set<RegistreConnexio>();
    public DbSet<PeticioTdns>       PeticiosDns       => Set<PeticioTdns>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Professor>(e => {
            e.HasIndex(p => p.Email).IsUnique();
            e.Property(p => p.Email).HasMaxLength(200);
            e.Property(p => p.Nom).HasMaxLength(100);
            e.Property(p => p.Cognoms).HasMaxLength(200);
            e.Ignore(p => p.NomComplet);
        });

        b.Entity<Class>(e => {
            e.Property(c => c.Name).HasMaxLength(200);
        });

        b.Entity<Student>(e => {
            e.HasOne(s => s.Class)
             .WithMany(c => c.Students)
             .HasForeignKey(s => s.ClassId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.Email).IsUnique();
            e.Property(s => s.Email).HasMaxLength(200);
            e.Property(s => s.Nom).HasMaxLength(100);
            e.Property(s => s.Cognoms).HasMaxLength(200);
            e.Property(s => s.PasswordHash).HasMaxLength(100);
            e.Property(s => s.Dni).HasMaxLength(20);
            e.Ignore(s => s.NomComplet);
        });

        b.Entity<ProfessorLogin>(e => {
            e.HasOne<Professor>()
             .WithMany()
             .HasForeignKey(l => l.ProfessorId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.ProfessorId);
            e.HasIndex(l => l.CreatedAt);
        });

        b.Entity<AlumneMac>(e => {
            e.HasOne(m => m.Student)
             .WithMany(s => s.Macs)
             .HasForeignKey(m => m.StudentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.Mac).HasMaxLength(17);
            e.HasIndex(m => m.Mac).IsUnique();
            e.Property(m => m.Dispositiu).HasMaxLength(100);
        });

        b.Entity<SessioExamen>(e => {
            e.HasOne(s => s.Class)
             .WithMany()
             .HasForeignKey(s => s.ClassId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Professor)
             .WithMany()
             .HasForeignKey(s => s.ProfessorId)
             .OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.Titol).HasMaxLength(300);
            e.HasIndex(s => new { s.ClassId, s.Activa });
        });

        b.Entity<RegistreConnexio>(e => {
            e.HasOne(r => r.Sessio)
             .WithMany(s => s.Registres)
             .HasForeignKey(r => r.SessioId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Student)
             .WithMany(s => s.Registres)
             .HasForeignKey(r => r.StudentId)
             .OnDelete(DeleteBehavior.NoAction);
            e.Property(r => r.MacAddress).HasMaxLength(17);
            e.Property(r => r.IpAssignada).HasMaxLength(15);
            e.HasIndex(r => new { r.SessioId, r.MacAddress });
            e.HasIndex(r => r.IpAssignada);
        });

        b.Entity<PeticioTdns>(e => {
            e.HasOne(p => p.Registre)
             .WithMany(r => r.PeticiosDns)
             .HasForeignKey(p => p.RegistreId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Domini).HasMaxLength(253);
            e.HasIndex(p => new { p.RegistreId, p.Timestamp });
        });
    }
}
