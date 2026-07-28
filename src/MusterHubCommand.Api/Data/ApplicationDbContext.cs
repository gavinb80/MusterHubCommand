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
    public DbSet<IncidentSector> IncidentSectors => Set<IncidentSector>();
    public DbSet<IncidentObjective> IncidentObjectives => Set<IncidentObjective>();
    public DbSet<IncidentRisk> IncidentRisks => Set<IncidentRisk>();
    public DbSet<BaEntryControlPoint> BaEntryControlPoints => Set<BaEntryControlPoint>();
    public DbSet<BaTeam> BaTeams => Set<BaTeam>();
    public DbSet<BaWearer> BaWearers => Set<BaWearer>();
    public DbSet<IncidentAction> IncidentActions => Set<IncidentAction>();
    public DbSet<IncidentCloseType> IncidentCloseTypes => Set<IncidentCloseType>();
    public DbSet<IncidentAttachment> IncidentAttachments => Set<IncidentAttachment>();
    public DbSet<IntegrationApiKey> IntegrationApiKeys => Set<IntegrationApiKey>();
    public DbSet<EmployeeStationAssignment> EmployeeStationAssignments => Set<EmployeeStationAssignment>();
    public DbSet<VehicleProfile> VehicleProfiles => Set<VehicleProfile>();
    public DbSet<OrganisationSettings> OrganisationSettings => Set<OrganisationSettings>();

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
            e.HasOne(d => d.VehicleProfile).WithMany().HasForeignKey(d => d.VehicleProfileId).OnDelete(DeleteBehavior.SetNull);
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
            // Restrict, not SetNull -- a retired close type shouldn't
            // silently rewrite the record of what an already-closed
            // incident was actually closed against.
            e.HasOne(i => i.CloseType).WithMany().HasForeignKey(i => i.CloseTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(i => i.Appliances).WithOne(a => a.Incident).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Updates).WithOne(u => u.Incident).HasForeignKey(u => u.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Sectors).WithOne(s => s.Incident).HasForeignKey(s => s.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Objectives).WithOne(o => o.Incident).HasForeignKey(o => o.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Risks).WithOne(r => r.Incident).HasForeignKey(r => r.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.BaEntryControlPoints).WithOne(b => b.Incident).HasForeignKey(b => b.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Actions).WithOne(a => a.Incident).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Attachments).WithOne(a => a.Incident).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentUpdate>(e =>
        {
            // Restrict, not Cascade or SetNull: nothing ever deletes an
            // IncidentUpdate (append-only, see the entity's own comment),
            // so this never actually fires -- Restrict is just the safe
            // default for a self-reference with no real delete path.
            e.HasOne(u => u.ReplyToUpdate).WithMany().HasForeignKey(u => u.ReplyToUpdateId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IncidentAction>(e =>
        {
            e.HasOne(a => a.AssignedToEmployee).WithMany().HasForeignKey(a => a.AssignedToEmployeeId).OnDelete(DeleteBehavior.SetNull);
            // SetNull, same reasoning as IncidentAppliance.SectorId -- a
            // deleted sector shouldn't block or cascade into removing a
            // task/request that happened to be tied to it.
            e.HasOne(a => a.Sector).WithMany().HasForeignKey(a => a.SectorId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IncidentObjective>(e =>
        {
            // SetNull, same reasoning as IncidentAction.AssignedToEmployee --
            // a deleted employee shouldn't block or cascade into removing
            // an objective. Audit-only: RaisedByEmployee is never
            // .Include()'d or resolved for display, RaisedByName already
            // is the string to show.
            e.HasOne(o => o.RaisedByEmployee).WithMany().HasForeignKey(o => o.RaisedByEmployeeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IncidentRisk>(e =>
        {
            // Same reasoning as IncidentObjective.RaisedByEmployee -- a
            // deleted employee shouldn't block or cascade into removing a
            // risk log entry, and RaisedByName is already the string to show.
            e.HasOne(r => r.RaisedByEmployee).WithMany().HasForeignKey(r => r.RaisedByEmployeeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BaEntryControlPoint>(e =>
        {
            e.HasMany(p => p.Teams).WithOne(t => t.EntryControlPoint).HasForeignKey(t => t.EntryControlPointId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.OwningDevice).WithMany().HasForeignKey(p => p.OwningDeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BaTeam>(e =>
        {
            e.HasMany(t => t.Wearers).WithOne(w => w.Team).HasForeignKey(w => w.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentAppliance>(e =>
        {
            // SetNull, not Restrict: deleting a sector should just drop
            // its appliances back to Unassigned, not block the delete or
            // cascade into removing attendance rows.
            e.HasOne(a => a.Sector).WithMany().HasForeignKey(a => a.SectorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.OfficerInChargeEmployee).WithMany().HasForeignKey(a => a.OfficerInChargeEmployeeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IncidentSector>(e =>
        {
            // Cascade: removing a node removes its descendant nodes too --
            // a sub-tree without its parent doesn't mean anything. This is
            // independent from IncidentAppliance.SectorId above, which
            // stays SetNull -- an appliance attached anywhere in a deleted
            // sub-tree goes back to Unassigned, it never gets silently
            // detached from the incident's attendance.
            e.HasOne(s => s.Parent).WithMany().HasForeignKey(s => s.ParentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.PersonInChargeEmployee).WithMany().HasForeignKey(s => s.PersonInChargeEmployeeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IncidentAttachment>(e =>
        {
            e.HasOne(a => a.UploadedByEmployee).WithMany().HasForeignKey(a => a.UploadedByEmployeeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.UploadedByDevice).WithMany().HasForeignKey(a => a.UploadedByDeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IntegrationApiKey>(e =>
        {
            e.HasIndex(k => k.KeyHash).IsUnique();
        });

        modelBuilder.Entity<EmployeeStationAssignment>(e =>
        {
            e.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.OrgUnit).WithMany().HasForeignKey(a => a.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.EmployeeId, a.OrgUnitId }).IsUnique();
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
