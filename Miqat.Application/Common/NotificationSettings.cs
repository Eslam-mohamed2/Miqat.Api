namespace Miqat.Application.Common
{
    /// <summary>The "Notifications" section of the configuration.</summary>
    public class NotificationSettings
    {
        /// <summary>
        /// Server-wide switch for notification emails. Off by default: on a mail
        /// plan with a daily limit, notification emails would use up the quota
        /// that sign-in codes depend on. Each user also has their own switch.
        /// </summary>
        public bool EmailEnabled { get; set; }
    }
}
