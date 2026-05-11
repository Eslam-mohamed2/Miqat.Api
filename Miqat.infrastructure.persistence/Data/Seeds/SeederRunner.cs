using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public class SeederRunner
    {
        private readonly UserSeeder _userSeeder;
        private readonly GroupSeeder _groupSeeder;
        private readonly TaskSeeder _taskSeeder;
        private readonly NotificationSeeder _notificationSeeder;
        private readonly FriendshipSeeder _friendshipSeeder;
        private readonly MentionSeeder _mentionSeeder;

        public SeederRunner(
            UserSeeder userSeeder,
            GroupSeeder groupSeeder,
            TaskSeeder taskSeeder,
            NotificationSeeder notificationSeeder,
            FriendshipSeeder friendshipSeeder,
            MentionSeeder mentionSeeder)
        {
            _userSeeder = userSeeder;
            _groupSeeder = groupSeeder;
            _taskSeeder = taskSeeder;
            _notificationSeeder = notificationSeeder;
            _friendshipSeeder = friendshipSeeder;
            _mentionSeeder = mentionSeeder;
        }

        public async Task RunAllAsync()
        {
            Console.WriteLine("[Seeder] Starting seed process...");
            await _userSeeder.SeedAsync();
            await _groupSeeder.SeedAsync();
            await _taskSeeder.SeedAsync();
            await _friendshipSeeder.SeedAsync();
            await _notificationSeeder.SeedAsync();
            await _mentionSeeder.SeedAsync();

            Console.WriteLine();
            Console.WriteLine("✅ Seeding complete!");
            Console.WriteLine("👤 20 Users created");
            Console.WriteLine("👥 10 Groups created");
            Console.WriteLine("✅ 100 Tasks created");
            Console.WriteLine("🤝 50 Friendships created");
            Console.WriteLine("🔔 100 Notifications created");
            Console.WriteLine("💬 80 Mentions created");
            Console.WriteLine("─────────────────────────────");
            Console.WriteLine("📧 Admin login: eslam@miqat.app");
            Console.WriteLine("🔑 Password: Test@1234");
        }
    }
}
