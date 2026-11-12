using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;

namespace Miqat.Application.Services
{
    /// <summary>
    /// The live "this task changed" message. Sent by everything that changes a
    /// task - the task itself, its checklist, its links - so every open board
    /// refreshes the same way.
    /// </summary>
    public class TaskBroadcaster
    {
        /// <summary>Name of the live (SignalR) event the boards listen for.</summary>
        public const string RealtimeEvent = "taskChanged";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IRealtimeNotifier _realtime;
        private readonly PeopleDirectory _people;

        public TaskBroadcaster(IUnitOfWork unitOfWork, IRealtimeNotifier realtime, PeopleDirectory people)
        {
            _unitOfWork = unitOfWork;
            _realtime = realtime;
            _people = people;
        }

        /// <summary>
        /// For a change to something that hangs off a task (a checklist line, a
        /// link): the boards showing that task refresh its progress.
        /// </summary>
        public async Task UpdatedAsync(Guid taskId)
        {
            var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
            if (task != null) await ChangedAsync(task, "updated");
        }

        /// <summary>
        /// Everyone whose board shows the task: its creator, its assignee, and
        /// the people on the project it belongs to. The person who made the
        /// change is included on purpose - their other tabs should update too.
        /// </summary>
        public async Task ChangedAsync(TaskItem task, string action)
        {
            var audience = new HashSet<Guid> { task.UserId };
            if (task.AssignedToUserId.HasValue) audience.Add(task.AssignedToUserId.Value);

            if (task.GroupId.HasValue)
                audience.UnionWith(await _people.GetProjectAudienceAsync(task.GroupId.Value));

            await _realtime.NotifyUsersAsync(audience, RealtimeEvent, new
            {
                taskId = task.Id,
                groupId = task.GroupId,
                action,
                title = task.Title
            });
        }
    }
}
