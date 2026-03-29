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

        public SeederRunner(
            UserSeeder userSeeder,
            GroupSeeder groupSeeder,
            TaskSeeder taskSeeder,
            NotificationSeeder notificationSeeder)
        {
            _userSeeder = userSeeder;
            _groupSeeder = groupSeeder;
            _taskSeeder = taskSeeder;
            _notificationSeeder = notificationSeeder;
        }

        public async Task RunAllAsync()
        {
            Console.WriteLine("[Seeder] Starting seed process...");
            await _userSeeder.SeedAsync();
            await _groupSeeder.SeedAsync();
            await _taskSeeder.SeedAsync();
            await _notificationSeeder.SeedAsync();
            Console.WriteLine("[Seeder] ✅ All seed data ready.");
        }
    }
}
