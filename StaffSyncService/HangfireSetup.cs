using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using StaffSyncService.Services;

namespace StaffSyncService
{
    public static class HangfireSetup
    {
        public static void AddHangfireJobs(this IServiceProvider serviceProvider)
        {            var staffOrchestrator = serviceProvider.GetRequiredService<StaffSyncOrchestrator>();
            var hqUserOrchestrator = serviceProvider.GetRequiredService<HQUserSyncOrchestrator>();
            var recurringJobManager = serviceProvider.GetRequiredService<IRecurringJobManager>();

            // Schedule recurring jobs using DI-based API
            recurringJobManager.AddOrUpdate(
                "StaffSyncJob",
                () => staffOrchestrator.SynchronizeStaffAsync(null),
                "0 */1 * * *"); // Every minute at 0 seconds (matching StreamSets cron: 0 0/1 * 1/1 * ? *)

            recurringJobManager.AddOrUpdate(
                "HQUserSyncJob",
                () => hqUserOrchestrator.SynchronizeHQUsersAsync(),
                "0 */2 * * *"); // Every 2 minutes (matching StreamSets cron: 0 0/2 * 1/1 * ? *)
        }
    }
}
