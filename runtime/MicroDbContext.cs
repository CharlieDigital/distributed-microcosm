using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;

public class MicroDbContext : DbContext
{
    public MicroDbContext(DbContextOptions<MicroDbContext> options)
        : base(options) { }

    public DbSet<ClusterRoleLease> ClusterRoleLeases => Set<ClusterRoleLease>();
}

public class ClusterRoleLease
{
    public Guid Id { get; set; }
    public required string RoleName { get; set; }
    public required long LeaseExpiresAt { get; set; }
}

public static class MicroDbContextExtensions
{
    public static async Task<bool> AcquireClusterRoleAsync(
        this MicroDbContext context,
        string roleName,
        TimeSpan leaseDuration,
        bool renewExisting = false
    )
    {
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        // Acquire the lock and check if there is an active least.  If so, exit.
        var key = new PostgresAdvisoryLockKey(roleName, true);
        await PostgresDistributedLock.AcquireWithTransactionAsync(key, transaction);

        var existingLease = await context
            .ClusterRoleLeases.Where(l => l.RoleName == roleName)
            .FirstOrDefaultAsync();

        if (existingLease == null)
        {
            // No existing lease, create one
            var newLease = new ClusterRoleLease
            {
                RoleName = roleName,
                LeaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration).UtcTicks,
            };

            context.ClusterRoleLeases.Add(newLease);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        // Existing lease found; acquired ownership if expired
        var now = DateTimeOffset.UtcNow.UtcTicks;

        if (existingLease.LeaseExpiresAt < now || renewExisting)
        {
            // Existing lease has expired or renewal requested, renew it with a buffer
            existingLease.LeaseExpiresAt = now + leaseDuration.Ticks + 1000;
            context.ClusterRoleLeases.Update(existingLease);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        await transaction.RollbackAsync();

        return false;
    }
}
