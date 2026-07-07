using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace DataConcentrator
{
    public class ContextClass : DbContext
    {
        private static readonly object syncRoot = new object();

        public static object SyncRoot => syncRoot;

        public DbSet<TagEntity> Tags { get; set; }
        public DbSet<AlarmEntity> Alarms { get; set; }
        public DbSet<ActivatedAlarmEntity> ActivatedAlarms { get; set; }

        public static string DatabasePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scada_data.db");

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TagEntity>(entity =>
            {
                entity.ToTable("Tags");
                entity.HasKey(tag => tag.Name);
                entity.Property(tag => tag.Name).IsRequired();
                entity.HasMany(tag => tag.Alarms)
                    .WithOne(alarm => alarm.Tag)
                    .HasForeignKey(alarm => alarm.TagName)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AlarmEntity>(entity =>
            {
                entity.ToTable("Alarms");
                entity.HasKey(alarm => alarm.Id);
                entity.Property(alarm => alarm.Id).ValueGeneratedNever();
                entity.Property(alarm => alarm.TagName).IsRequired();
                entity.Property(alarm => alarm.Message).IsRequired(false);
            });

            modelBuilder.Entity<ActivatedAlarmEntity>(entity =>
            {
                entity.ToTable("ActivatedAlarms");
                entity.HasKey(alarm => alarm.Id);
                entity.Property(alarm => alarm.Id).ValueGeneratedNever();
                entity.Property(alarm => alarm.TagName).IsRequired();
                entity.Property(alarm => alarm.Message).IsRequired(false);
            });
        }
    }
}
