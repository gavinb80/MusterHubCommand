using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentOrganisationAccessor organisationAccessor) : DbContext(options)
{
    private static readonly MethodInfo ApplyTenantFilterMethod = typeof(ApplicationDbContext)
        .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<OrgUnitType> OrgUnitTypes => Set<OrgUnitType>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<CoreDirectorySyncConfig> CoreDirectorySyncConfigs => Set<CoreDirectorySyncConfig>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<CommandOperator> CommandOperators => Set<CommandOperator>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentAppliance> IncidentAppliances => Set<IncidentAppliance>();
    public DbSet<IncidentUpdate> IncidentUpdates => Set<IncidentUpdate>();
    public DbSet<IntegrationApiKey> IntegrationApiKeys => Set<IntegrationApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Backs the org hierarchy's materialized-path column (see Rota/Skills'
        // own docs/architecture-plan.md 1.1 for the shape this mirrors).
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<OrgUnitType>(e =>
        {
            e.HasOne(t => t.AllowedParentType)
                .WithMany()
                .HasForeignKey(t => t.AllowedParentTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrgUnit>(e =>
        {
            e.Property(u => u.Path).HasColumnType("ltree");
            e.HasIndex(u => u.Path);

            e.HasOne(u => u.OrgUnitType)
                .WithMany()
                .HasForeignKey(u => u.OrgUnitTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Restrict, not Cascade: deleting a unit with children should be
            // an explicit, deliberate decision, not something that silently
            // cascades. Command has no Setup UI for editing units by hand
            // (they're a passive directory-sync mirror), so this mostly
            // guards against ever wiring a delete endpoint carelessly later.
            e.HasOne(u => u.Parent)
                .WithMany()
                .HasForeignKey(u => u.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.HasIndex(d => d.TokenHash).IsUnique();
            e.HasOne(d => d.OrgUnit).WithMany().HasForeignKey(d => d.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommandOperator>(e =>
        {
            e.HasOne(o => o.Employee).WithMany().HasForeignKey(o => o.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(o => o.EmployeeId).IsUnique();
        });

        modelBuilder.Entity<Incident>(e =>
        {
            // Unique per organisation, not globally: two different services'
            // Vision instances could plausibly reuse the same incident
            // number scheme.
            e.HasIndex(i => new { i.OrganisationId, i.ExternalReference }).IsUnique();
            e.HasOne(i => i.OrgUnit).WithMany().HasForeignKey(i => i.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(i => i.Appliances).WithOne(a => a.Incident).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Updates).WithOne(u => u.Incident).HasForeignKey(u => u.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationApiKey>(e =>
        {
            e.HasIndex(k => k.KeyHash).IsUnique();
        });

        // Tenant isolation, default-deny: every ITenantScoped entity gets
        // this filter automatically just by implementing the interface --
        // nobody writing a new entity needs to remember to scope every
        // query by hand, and a missed filter fails closed rather than open.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantScoped
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OrganisationId == organisationAccessor.OrganisationId);
    }
}
