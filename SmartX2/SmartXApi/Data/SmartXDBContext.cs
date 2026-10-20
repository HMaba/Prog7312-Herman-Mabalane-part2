using Microsoft.EntityFrameworkCore;
using SmartXApi.Models;
using System.Reflection.Emit;

namespace SmartXApi.Data
{

    public class SmartXDbContext : DbContext
    {
        public SmartXDbContext(DbContextOptions<SmartXDbContext> options) : base(options)
        {
        }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<Sensor> Sensors => Set<Sensor>();
        public DbSet<TelemetryRecord> TelemetryRecords => Set<TelemetryRecord>();
        public DbSet<Attachment> Attachments => Set<Attachment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Device>()
                .HasIndex(d => d.DeviceIdentifier)
                .IsUnique();

            modelBuilder.Entity<Device>()
                .HasMany(d => d.Sensors)
                .WithOne(s => s.Device)
                .HasForeignKey(s => s.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Device>()
                .HasMany(d => d.Attachments)
                .WithOne(a => a.Device)
                .HasForeignKey(a => a.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TelemetryRecord>()
                .HasIndex(t => new { t.DeviceId, t.SensorId, t.Timestamp });
        }
    }
}