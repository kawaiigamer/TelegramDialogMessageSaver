using Microsoft.EntityFrameworkCore;
using TelegamSaver;

namespace TelegramDialogMessageSaver
{
    internal class ApplicationStorageContext : DbContext
    {
        private readonly string database_path;
        public DbSet<InternalChannel> Channels { get; set; }
        public DbSet<InternalUser> Users { get; set; }
        public DbSet<InternalUserMessageMedia> Media { get; set; }
        public DbSet<InternalUserMessage> Messages { get; set; }

        public ApplicationStorageContext(string path)
        {
            database_path = path;
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={database_path}");
        }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<InternalUserMessageMedia>().HasKey(m => m.hash); 
            mb.Entity<InternalChannel>().HasKey(c => c.channel_id);
            mb.Entity<InternalUser>().HasKey(u => u.user_id);
            //mb.Entity<InternalUserMessage>().HasIndex(u => $"{u.from_channel.chnnel_id}{u.from_user.user_id}{u.direction}{u.dialog_id}").IsUnique(); 
        }

        public async Task<bool> IsMediaHashExists(long hash) => await Media.AsNoTracking().AnyAsync(m => m.hash == hash);
                
        public async Task<List<InternalUserMessage>> GetMessageVersions(InternalUserMessage msg) =>
            await Messages.AsNoTracking().Where(x => x.from_channel == msg.from_channel && x.from_user == msg.from_user && x.direction == msg.direction && x.dialog_id == msg.dialog_id).OrderBy(x => x.date).ToListAsync();
     }
}
