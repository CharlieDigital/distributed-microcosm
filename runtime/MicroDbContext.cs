using Medallion.Threading.Postgres;
using Medallion.Threading.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

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
        // Acquire the lock and check if there is an active lease.  If so, exit.
        var redis = await ConnectionMultiplexer.ConnectAsync(
            Environment.GetEnvironmentVariable("redis") ?? ""
        ); // uses StackExchange.Redis

        var @lock = new RedisDistributedLock("MyLockName", redis.GetDatabase());

        await using (var handle = await @lock.TryAcquireAsync())
        {
            if (handle == null)
            {
                return false;
            }
        }

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
            return true;
        }

        // Existing lease found; acquired ownership if expired
        var now = DateTimeOffset.UtcNow.UtcTicks;

        if (existingLease.LeaseExpiresAt < now || renewExisting)
        {
            Console.WriteLine(
                renewExisting ? "Renewing existing lease..." : "Acquiring expired lease..."
            );

            // Existing lease has expired or renewal requested
            existingLease.LeaseExpiresAt = now + leaseDuration.Ticks;
            context.ClusterRoleLeases.Update(existingLease);
            await context.SaveChangesAsync();
            return true;
        }

        return false;
    }
}
