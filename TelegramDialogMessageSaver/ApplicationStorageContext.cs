using Microsoft.EntityFrameworkCore;
using TelegamSaver;

namespace TelegramDialogMessageSaver
{
    internal class ApplicationStorageContext : DbContext
    {
        public ApplicationStorageContext() => Database.EnsureCreated();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=TelegramChatMessages.db");
        }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<InternalUserMessageMedia>().HasKey(m => m.hash);
        }

        public DbSet<InternalUser> Users { get; set; }

        public DbSet<InternalUserMessageMedia> Media { get; set; }

        public bool IsMediaHashExists(long hash) => Media.Any((m) => m.hash == hash);

    }
}
